using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OnlineShop.Application.Accounts;
using OnlineShop.Application.Catalog.Admin;
using OnlineShop.Application.Catalog.Browse;
using OnlineShop.Application.Search;
using OnlineShop.Infrastructure.Search;
using OnlineShop.IntegrationTests.TestSupport;

namespace OnlineShop.IntegrationTests;

/// <summary>
/// The API with an external search engine. Runs only where one is reachable: <c>IntegrationTests:OpenSearchUrl</c> /
/// <c>IntegrationTests:ElasticsearchUrl</c> (CI starts both as service containers); without it each test returns at once.
/// Every factory uses its own index prefix and removes its indexes afterwards.
/// </summary>
public abstract class SearchEngineFactory(SearchProvider provider, string urlKey) : ApiFactory
{
    public const string AdminEmail = "search-owner@shop.test";
    public const string AdminPassword = "search pass 2026"; // test-only value for this throwaway database

    public SearchProvider Provider { get; } = provider;

    public string? EngineUrl { get; } = new ConfigurationBuilder().AddUserSecrets<ApiFactory>(optional: true).AddEnvironmentVariables().Build()[urlKey];

    public string IndexPrefix { get; } = $"it-{Guid.NewGuid():N}"[..20];

    protected override IReadOnlyDictionary<string, string?> ConfigurationOverrides => new Dictionary<string, string?>
    {
        ["Database:AdminEmail"] = AdminEmail,
        ["Database:AdminPassword"] = AdminPassword,
        ["Search:Provider"] = Provider.ToString(),
        ["Search:Engine:Url"] = EngineUrl ?? "http://localhost:9",
        ["Search:Engine:IndexPrefix"] = IndexPrefix,
        ["Search:Engine:QueryTimeoutSeconds"] = "10",
        // The worker is not hosted in tests (ApiFactory); the scenario runs its steps itself.
        ["Search:IndexIntervalSeconds"] = "3600",
    };

    /// <summary>The engine's REST API, for checks and clean-up.</summary>
    public HttpClient Engine() => new() { BaseAddress = new Uri(EngineUrl!.TrimEnd('/') + "/") };
}

public sealed class OpenSearchFactory() : SearchEngineFactory(SearchProvider.OpenSearch, "IntegrationTests:OpenSearchUrl");

public sealed class ElasticsearchFactory() : SearchEngineFactory(SearchProvider.Elasticsearch, "IntegrationTests:ElasticsearchUrl");

public sealed class OpenSearchApiTests(OpenSearchFactory factory) : IClassFixture<OpenSearchFactory>
{
    [Fact]
    public Task Products_are_found_through_the_engine_and_changes_and_rebuilds_reach_it() => SearchEngineScenario.RunAsync(factory);
}

public sealed class ElasticsearchApiTests(ElasticsearchFactory factory) : IClassFixture<ElasticsearchFactory>
{
    [Fact]
    public Task Products_are_found_through_the_engine_and_changes_and_rebuilds_reach_it() => SearchEngineScenario.RunAsync(factory);
}

/// <summary>The same scenario for every engine (the database engines run it in BrowseApiTests and the unit tests).</summary>
public static class SearchEngineScenario
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    /// <summary>One run of the index worker (not hosted in tests): the database documents, then the engine.</summary>
    private static Task RunWorkerAsync(SearchEngineFactory factory) =>
        ActivatorUtilities.CreateInstance<SearchIndexWorker>(factory.Services).RunAsync(ensureBuilt: false, CancellationToken.None);

    public static async Task RunAsync(SearchEngineFactory factory)
    {
        if (string.IsNullOrEmpty(factory.EngineUrl))
            return;
        try
        {
            await ScenarioAsync(factory);
        }
        finally
        {
            await CleanUpAsync(factory);
        }
    }

    private static async Task ScenarioAsync(SearchEngineFactory factory)
    {
        using var admin = factory.CreateClient();
        (await admin.PostAsJsonAsync("/api/v1/account/login", new LoginRequest(SearchEngineFactory.AdminEmail, SearchEngineFactory.AdminPassword, false))).EnsureSuccessStatusCode();
        var word = $"quillmore{Guid.NewGuid().ToString("N")[..6]}";
        var category = await ReadAsync<CatalogNodeView>(await admin.PostAsJsonAsync("/api/v1/admin/catalog/categories",
            new SaveCatalogNodeRequest([new("fa", "دسته", null, null), new("en", "Lights", null, null)])));
        var sub = await ReadAsync<CatalogNodeView>(await admin.PostAsJsonAsync($"/api/v1/admin/catalog/categories/{category.Id}/subcategories",
            new SaveCatalogNodeRequest([new("fa", "زیر", null, null)])));
        var group = await ReadAsync<CatalogNodeView>(await admin.PostAsJsonAsync($"/api/v1/admin/catalog/subcategories/{sub.Id}/groups",
            new SaveCatalogNodeRequest([new("fa", "گروه", null, null)])));
        await ReadAsync<ProductEditView>(await admin.PostAsJsonAsync("/api/v1/admin/products", new SaveProductRequest(
            $"ENG-A-{word}", 10, null, 1, true, group.Id, [new("fa", $"چراغ {word}", null, null), new("en", $"{word} lamp", null, null)], [])));
        var stand = await ReadAsync<ProductEditView>(await admin.PostAsJsonAsync("/api/v1/admin/products", new SaveProductRequest(
            $"ENG-B-{word}", 10, null, 1, true, group.Id, [new("fa", "پایه", null, $"برای {word}"), new("en", "Stand", null, $"Fits the {word} lamp")], [])));

        // First run: the documents, then the engine's indexes are built (the alias is missing).
        await RunWorkerAsync(factory);
        await RunWorkerAsync(factory);
        var status = await ReadAsync<SearchIndexStatus>(await admin.GetAsync("/api/v1/admin/seo/search-index"));
        Assert.Equal(new SearchEngineStatus(factory.Provider, true, 0), status.Engine);
        using (var engine = factory.Engine())
        {
            var count = JsonNode.Parse(await engine.GetStringAsync($"{factory.IndexPrefix}-en/_count"))!["count"]!.GetValue<int>();
            Assert.True(count >= 2);
        }

        using var visitor = factory.CreateClient();
        var found = await ReadAsync<BrowseResult>(await visitor.GetAsync($"/api/v1/catalog/browse?q={word}&locale=en"));
        Assert.Equal([$"{word} lamp", "Stand"], found.Items.Select(i => i.Name));
        Assert.Equal(2, (await ReadAsync<BrowseResult>(await visitor.GetAsync($"/api/v1/catalog/browse?q={word[..7]}&locale=en"))).Total);
        var typo = word[..3] + word[4] + word[3] + word[5..];
        Assert.Contains((await ReadAsync<BrowseResult>(await visitor.GetAsync($"/api/v1/catalog/browse?q={typo}&locale=en"))).Items, i => i.Name == $"{word} lamp");

        // A change reaches the engine on the next run.
        var renamed = $"pedestal{word}";
        var edit = await ReadAsync<ProductEditView>(await admin.GetAsync($"/api/v1/admin/products/{stand.Number}"));
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/v1/admin/products/{stand.Number}", new SaveProductRequest(
            edit.Sku, 10, null, 1, true, group.Id, [new("fa", "پایه", null, null), new("en", renamed, null, null)], []))).StatusCode);
        await RunWorkerAsync(factory);
        Assert.Equal([renamed], (await ReadAsync<BrowseResult>(await visitor.GetAsync($"/api/v1/catalog/browse?q={renamed}&locale=en"))).Items.Select(i => i.Name));

        // A full rebuild switches to new indexes without losing anything. The renamed stand keeps the word only in its
        // SKU (ENG-B-{word}, whose parts are indexed), so it now ranks below the lamp, which has the word in its name.
        await ReadAsync<SearchIndexStatus>(await admin.PostAsync("/api/v1/admin/seo/search-index/rebuild", null));
        await RunWorkerAsync(factory);
        Assert.Equal([$"{word} lamp", renamed], (await ReadAsync<BrowseResult>(await visitor.GetAsync($"/api/v1/catalog/browse?q={word[..7]}&locale=en"))).Items.Select(i => i.Name));
        Assert.Equal([renamed], (await ReadAsync<BrowseResult>(await visitor.GetAsync($"/api/v1/catalog/browse?q={renamed}&locale=en"))).Items.Select(i => i.Name));
        Assert.Equal(new SearchEngineStatus(factory.Provider, true, 0), (await ReadAsync<SearchIndexStatus>(await admin.GetAsync("/api/v1/admin/seo/search-index"))).Engine);
    }

    private static async Task CleanUpAsync(SearchEngineFactory factory)
    {
        using var engine = factory.Engine();
        var indexes = JsonNode.Parse(await engine.GetStringAsync($"_cat/indices/{factory.IndexPrefix}-*?format=json&h=index")) as JsonArray ?? [];
        foreach (var name in indexes.Select(i => i?["index"]?.GetValue<string>()).OfType<string>())
            await engine.DeleteAsync(name);
    }
}
