using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using OnlineShop.Application;
using OnlineShop.Application.Search;
using OnlineShop.Domain.Search;
using OnlineShop.Infrastructure;
using OnlineShop.Infrastructure.Search;

namespace OnlineShop.UnitTests.Infrastructure;

public sealed class SearchEngineRequestsTests
{
    private static readonly Guid Id = Guid.Parse("0190a3c2-0000-7000-8000-000000000001");

    [Fact]
    public void Each_language_gets_its_analyzer_and_a_strict_mapping()
    {
        Assert.Equal("persian", SearchEngineRequests.LanguageAnalyzer("fa"));
        Assert.Equal("armenian", SearchEngineRequests.LanguageAnalyzer("hy"));
        Assert.Equal("german", SearchEngineRequests.LanguageAnalyzer("de-AT"));
        Assert.Equal("standard", SearchEngineRequests.LanguageAnalyzer("xx"));

        var definition = SearchEngineRequests.IndexDefinition("ar", 1);
        Assert.Equal(1, definition["settings"]!["number_of_replicas"]!.GetValue<int>());
        var properties = definition["mappings"]!["properties"]!;
        Assert.Equal("strict", definition["mappings"]!["dynamic"]!.GetValue<string>());
        Assert.Equal("arabic", properties["title"]!["analyzer"]!.GetValue<string>());
        Assert.Equal("words", properties["keywords"]!["copy_to"]!.GetValue<string>());
        Assert.Equal("plain", properties["codes"]!["analyzer"]!.GetValue<string>());
    }

    [Fact]
    public void Every_word_must_match_through_one_alternative_as_a_prefix_or_with_typos()
    {
        var body = SearchEngineRequests.Match(SearchTerms.From("phone red", ["phone", "red"], [["phone", "mobile"]]), 50);

        Assert.Equal(50, body["size"]!.GetValue<int>());
        Assert.False(body["_source"]!.GetValue<bool>());
        var must = body["query"]!["bool"]!["must"]!.AsArray();
        Assert.Equal(2, must.Count);
        var first = must[0]!["bool"]!["should"]!.AsArray();
        // Two alternatives, each as prefix and as fuzzy match.
        Assert.Equal(4, first.Count);
        Assert.Equal("bool_prefix", first[0]!["multi_match"]!["type"]!.GetValue<string>());
        Assert.Equal("AUTO", first[1]!["multi_match"]!["fuzziness"]!.GetValue<string>());
        Assert.Equal("mobile", first[2]!["multi_match"]!["query"]!.GetValue<string>());
        Assert.Equal("title^4", first[0]!["multi_match"]!["fields"]![0]!.GetValue<string>());
    }

    [Fact]
    public void Bulk_bodies_are_one_json_line_per_action_and_document()
    {
        var document = new SearchDocument { ProductId = Id, Locale = "en", Title = "lamp", Codes = "l-1", UpdatedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
        var lines = SearchEngineRequests.Bulk("shop-en", [document], [Guid.Empty]).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(3, lines.Length);
        Assert.Equal(Id.ToString(), JsonNode.Parse(lines[0])!["index"]!["_id"]!.GetValue<string>());
        Assert.Equal("lamp", JsonNode.Parse(lines[1])!["title"]!.GetValue<string>());
        Assert.Equal("", JsonNode.Parse(lines[1])!["keywords"]!.GetValue<string>());
        Assert.Equal("shop-en", JsonNode.Parse(lines[2])!["delete"]!["_index"]!.GetValue<string>());
    }

    [Fact]
    public void Publishing_moves_the_alias_and_drops_older_indexes_of_that_language_only()
    {
        var cat = JsonNode.Parse("""[{"index":"shop-en-20260101000000000"},{"index":"shop-en-20260102000000000"},{"index":"shop-en-gb-20260101000000000"},{"index":"shop-en"}]""");
        var older = SearchEngineRequests.ReadIndexNames(cat, "shop-en");
        Assert.Equal(["shop-en-20260101000000000", "shop-en-20260102000000000"], older);

        var actions = SearchEngineRequests.Publish("shop-en", "shop-en-20260102000000000", older)["actions"]!.AsArray();
        Assert.Equal(2, actions.Count);
        Assert.Equal("shop-en", actions[0]!["add"]!["alias"]!.GetValue<string>());
        Assert.Equal("shop-en-20260101000000000", actions[1]!["remove_index"]!["index"]!.GetValue<string>());
        Assert.True(SearchEngineRequests.IsPhysicalIndex("shop-en-20260102000000000"));
        Assert.False(SearchEngineRequests.IsPhysicalIndex("shop-en"));
    }

    [Fact]
    public void Answers_are_read_and_refused_items_throw()
    {
        var hits = SearchEngineRequests.ReadHits(JsonNode.Parse($$$"""{"hits":{"hits":[{"_id":"{{{Id}}}","_score":2.5},{"_id":"not-a-guid","_score":1}]}}"""));
        Assert.Equal(2.5, hits[Id]);
        Assert.Single(hits);
        Assert.Throws<SearchEngineException>(() => SearchEngineRequests.ReadHits(JsonNode.Parse("{}")));

        var suggest = JsonNode.Parse("""{"suggest":{"word":[{"text":"hodie","options":[{"text":"hoodie","score":0.8}]}]}}""");
        Assert.Equal("hoodie", SearchEngineRequests.ReadSuggestion(suggest, "hodie"));
        Assert.Null(SearchEngineRequests.ReadSuggestion(JsonNode.Parse("""{"suggest":{"word":[{"options":[]}]}}"""), "x"));
        Assert.Equal(2, SearchEngineRequests.SimilarWord("plasticity")["suggest"]!["word"]!["term"]!["max_edits"]!.GetValue<int>());

        SearchEngineRequests.EnsureBulkSucceeded(JsonNode.Parse("""{"errors":false,"items":[]}"""));
        SearchEngineRequests.EnsureBulkSucceeded(JsonNode.Parse("""{"errors":true,"items":[{"delete":{"status":404}}]}"""));
        var refused = Assert.Throws<SearchEngineException>(() => SearchEngineRequests.EnsureBulkSucceeded(
            JsonNode.Parse("""{"errors":true,"items":[{"index":{"status":201}},{"index":{"status":400,"error":{"type":"strict_dynamic_mapping_exception","reason":"no"}}}]}""")));
        Assert.Contains("strict_dynamic_mapping_exception", refused.Message, StringComparison.Ordinal);
    }
}

public sealed class HttpSearchEngineTests
{
    /// <summary>Answers each request from a queue of canned responses and records what was sent.</summary>
    private sealed class FakeHandler : HttpMessageHandler
    {
        public Queue<(HttpStatusCode Status, string Body)> Answers { get; } = new();

        public List<(HttpMethod Method, string Path, string? Body, string? Auth)> Requests { get; } = [];

        public TimeSpan Delay { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.Method, request.RequestUri!.PathAndQuery, request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken),
                request.Headers.Authorization?.ToString()));
            if (Delay > TimeSpan.Zero)
                await Task.Delay(Delay, cancellationToken);
            var (status, body) = Answers.Count > 0 ? Answers.Dequeue() : (HttpStatusCode.OK, "{}");
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private readonly FakeHandler _handler = new();

    private HttpSearchEngine Engine(Action<SearchEngineOptions>? change = null)
    {
        var options = new SearchOptions { Provider = SearchProvider.OpenSearch, Engine = { Url = "http://search:9200/base", IndexPrefix = "shop", Username = "shop", Password = "test-only", QueryTimeoutSeconds = 1 } };
        change?.Invoke(options.Engine);
        var client = new HttpClient(_handler);
        HttpSearchEngine.Configure(client, options.Engine);
        return new HttpSearchEngine(client, Options.Create(options), new FakeTimeProvider(new DateTimeOffset(2026, 3, 4, 5, 6, 7, 8, TimeSpan.Zero)));
    }

    [Fact]
    public async Task Searches_go_to_the_language_alias_with_basic_credentials()
    {
        var id = Guid.NewGuid();
        _handler.Answers.Enqueue((HttpStatusCode.OK, $$$"""{"hits":{"hits":[{"_id":"{{{id}}}","_score":1.5}]}}"""));

        var hits = await Engine().MatchAsync(SearchTerms.From("lamp", ["lamp"], []), "EN", 10);

        Assert.Equal(1.5, hits[id]);
        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/base/shop-en/_search", request.Path);
        Assert.StartsWith("Basic ", request.Auth, StringComparison.Ordinal);
        Assert.Empty(await Engine().MatchAsync(SearchTerms.From("", [], []), "en", 10));
        Assert.Null(await Engine().SimilarWordAsync("ab", "en"));
        Assert.Single(_handler.Requests);
    }

    [Fact]
    public async Task Indexes_are_created_filled_and_published_by_name()
    {
        _handler.Answers.Enqueue((HttpStatusCode.NotFound, "{}"));
        Assert.False(await Engine().IndexExistsAsync("fa"));
        Assert.Equal("/base/_alias/shop-fa", _handler.Requests[^1].Path);

        var name = await Engine().CreateIndexAsync("fa");
        Assert.Equal("shop-fa-20260304050607008", name);
        Assert.Equal(HttpMethod.Put, _handler.Requests[^1].Method);
        Assert.Contains("persian", _handler.Requests[^1].Body, StringComparison.Ordinal);

        _handler.Answers.Enqueue((HttpStatusCode.OK, """{"errors":false,"items":[]}"""));
        await Engine().WriteAsync(name, [new SearchDocument { ProductId = Guid.NewGuid(), Locale = "fa", Title = "چراغ" }], []);
        Assert.Equal($"/base/{name}/_bulk?refresh=wait_for", _handler.Requests[^1].Path);
        _handler.Answers.Enqueue((HttpStatusCode.OK, """{"errors":false,"items":[]}"""));
        await Engine().WriteAsync("shop-fa", [], [Guid.NewGuid()]);
        Assert.EndsWith("require_alias=true", _handler.Requests[^1].Path, StringComparison.Ordinal);
        var requests = _handler.Requests.Count;
        await Engine().WriteAsync("shop-fa", [], []);
        Assert.Equal(requests, _handler.Requests.Count);

        _handler.Answers.Enqueue((HttpStatusCode.OK, """[{"index":"shop-fa-20260101000000000"}]"""));
        await Engine().PublishAsync("fa", name);
        Assert.Equal("/base/_aliases", _handler.Requests[^1].Path);
        Assert.Contains("shop-fa-20260101000000000", _handler.Requests[^1].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Error_answers_timeouts_and_a_missing_address_become_engine_exceptions()
    {
        _handler.Answers.Enqueue((HttpStatusCode.InternalServerError, "broken"));
        var error = await Assert.ThrowsAsync<SearchEngineException>(() => Engine().IndexExistsAsync("en"));
        Assert.Contains("500", error.Message, StringComparison.Ordinal);

        _handler.Answers.Enqueue((HttpStatusCode.OK, "not json"));
        await Assert.ThrowsAsync<SearchEngineException>(() => Engine().IndexExistsAsync("en"));

        _handler.Delay = TimeSpan.FromSeconds(5);
        var slow = await Assert.ThrowsAsync<SearchEngineException>(() => Engine().SimilarWordAsync("lamp", "en"));
        Assert.Contains("longer than 1 s", slow.Message, StringComparison.Ordinal);

        await Assert.ThrowsAsync<SearchEngineException>(() => Engine(e => e.Url = null).IndexExistsAsync("en"));
    }

    [Fact]
    public void An_api_key_replaces_the_user_name()
    {
        using var client = new HttpClient();
        HttpSearchEngine.Configure(client, new SearchEngineOptions { Url = "https://es.example:9243", Username = "x", ApiKey = "test-key" });
        Assert.Equal("ApiKey", client.DefaultRequestHeaders.Authorization!.Scheme);
        Assert.Equal(new Uri("https://es.example:9243/"), client.BaseAddress);
        Assert.Equal(Timeout.InfiniteTimeSpan, client.Timeout);
    }
}

public sealed class SearchRegistrationTests
{
    private static ServiceProvider Build(params (string Key, string Value)[] settings)
    {
        var values = new Dictionary<string, string?>
        {
            ["Database:Provider"] = "PostgreSql",
            ["ConnectionStrings:Default"] = "Host=localhost;Database=never_opened;Username=test",
        };
        foreach (var (key, value) in settings)
            values[key] = value;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddApplication(configuration);
        services.AddInfrastructure(configuration, persistDataProtectionKeys: false);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void PostgreSQL_searches_by_default_and_a_configured_engine_answers_with_the_database_behind_it()
    {
        using (var services = Build())
        {
            using var scope = services.CreateScope();
            Assert.IsType<PostgresProductSearch>(scope.ServiceProvider.GetRequiredService<IProductSearch>());
        }

        using var engine = Build(("Search:Provider", "OpenSearch"), ("Search:Engine:Url", "http://search:9200"));
        using var engineScope = engine.CreateScope();
        Assert.IsType<EngineProductSearch>(engineScope.ServiceProvider.GetRequiredService<IProductSearch>());
        Assert.IsType<PostgresProductSearch>(engineScope.ServiceProvider.GetRequiredService<IDatabaseProductSearch>());
        Assert.Equal("onlineshop-fa", engineScope.ServiceProvider.GetRequiredService<ISearchEngine>().AliasFor("fa"));
        Assert.Same(engine.GetRequiredService<SearchEngineHealth>(), engineScope.ServiceProvider.GetRequiredService<SearchEngineHealth>());
    }
}
