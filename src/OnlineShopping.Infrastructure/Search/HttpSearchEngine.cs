using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using OnlineShopping.Application.Search;
using OnlineShopping.Domain.Search;

namespace OnlineShopping.Infrastructure.Search;

/// <summary>
/// OpenSearch or Elasticsearch over their REST API (the same calls work on both, so no engine-specific client library
/// is needed): one index per language named {prefix}-{locale}-{timestamp}, found through the alias {prefix}-{locale}.
/// Every call has its own time limit; a timeout or an error answer becomes a <see cref="SearchEngineException"/>, so
/// callers fall back to the database.
/// </summary>
public sealed class HttpSearchEngine(HttpClient http, IOptions<SearchOptions> options, TimeProvider clock) : ISearchEngine
{
    private SearchEngineOptions Settings => options.Value.Engine;

    /// <summary>Base address (always ending in "/"), no client-wide timeout, and the credentials.</summary>
    public static void Configure(HttpClient client, SearchEngineOptions engine)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(engine);
        if (Uri.TryCreate(engine.Url, UriKind.Absolute, out var url))
            client.BaseAddress = url.AbsoluteUri.EndsWith('/') ? url : new Uri(url.AbsoluteUri + "/");
        client.Timeout = Timeout.InfiniteTimeSpan;
        if (!string.IsNullOrEmpty(engine.ApiKey))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("ApiKey", engine.ApiKey);
        else if (!string.IsNullOrEmpty(engine.Username))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{engine.Username}:{engine.Password}")));
    }

    public string AliasFor(string locale)
    {
        ArgumentNullException.ThrowIfNull(locale);
        return $"{Settings.IndexPrefix}-{locale.ToLowerInvariant()}";
    }

    public async Task<IReadOnlyDictionary<Guid, double>> MatchAsync(SearchTerms terms, string locale, int limit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(terms);
        if (terms.IsEmpty)
            return new Dictionary<Guid, double>();
        var answer = await SendAsync(HttpMethod.Post, $"{AliasFor(locale)}/_search", Json(SearchEngineRequests.Match(terms, limit)), Settings.QueryTimeoutSeconds, cancellationToken);
        return SearchEngineRequests.ReadHits(answer);
    }

    public async Task<string?> SimilarWordAsync(string word, string locale, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(word);
        if (word.Length < 3)
            return null;
        var answer = await SendAsync(HttpMethod.Post, $"{AliasFor(locale)}/_search", Json(SearchEngineRequests.SimilarWord(word)), Settings.QueryTimeoutSeconds, cancellationToken);
        return SearchEngineRequests.ReadSuggestion(answer, word);
    }

    public async Task<bool> IndexExistsAsync(string locale, CancellationToken cancellationToken = default)
    {
        var answer = await SendAsync(HttpMethod.Get, $"_alias/{AliasFor(locale)}", null, Settings.QueryTimeoutSeconds, cancellationToken, allowNotFound: true);
        return answer is not null;
    }

    public async Task<string> CreateIndexAsync(string locale, CancellationToken cancellationToken = default)
    {
        var name = $"{AliasFor(locale)}-{clock.GetUtcNow().UtcDateTime.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture)}";
        await SendAsync(HttpMethod.Put, name, Json(SearchEngineRequests.IndexDefinition(locale, Settings.Replicas)), Settings.WriteTimeoutSeconds, cancellationToken);
        return name;
    }

    public async Task WriteAsync(string index, IReadOnlyCollection<SearchDocument> upserts, IReadOnlyCollection<Guid> deletes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(upserts);
        ArgumentNullException.ThrowIfNull(deletes);
        if (upserts.Count + deletes.Count == 0)
            return;
        // wait_for: once written, searches see the change (the database already does). require_alias: a write to a
        // missing alias fails instead of creating an index of that name with guessed field types.
        var content = new StringContent(SearchEngineRequests.Bulk(index, upserts, deletes), Encoding.UTF8, "application/x-ndjson");
        var path = SearchEngineRequests.IsPhysicalIndex(index) ? $"{index}/_bulk?refresh=wait_for" : $"{index}/_bulk?refresh=wait_for&require_alias=true";
        var answer = await SendAsync(HttpMethod.Post, path, content, Settings.WriteTimeoutSeconds, cancellationToken);
        SearchEngineRequests.EnsureBulkSucceeded(answer);
    }

    public async Task PublishAsync(string locale, string index, CancellationToken cancellationToken = default)
    {
        var alias = AliasFor(locale);
        var existing = await SendAsync(HttpMethod.Get, $"_cat/indices/{alias}-*?format=json&h=index", null, Settings.WriteTimeoutSeconds, cancellationToken, allowNotFound: true);
        var older = SearchEngineRequests.ReadIndexNames(existing, alias);
        await SendAsync(HttpMethod.Post, "_aliases", Json(SearchEngineRequests.Publish(alias, index, older)), Settings.WriteTimeoutSeconds, cancellationToken);
    }

    private static StringContent Json(JsonNode body) => new(body.ToJsonString(), Encoding.UTF8, "application/json");

    /// <summary>Sends a request within its time limit and returns the parsed answer (null for a 404 when allowed).</summary>
    private async Task<JsonNode?> SendAsync(HttpMethod method, string path, HttpContent? content, int timeoutSeconds, CancellationToken cancellationToken, bool allowNotFound = false)
    {
        if (http.BaseAddress is null)
            throw new SearchEngineException("Search:Engine:Url is not set.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative)) { Content = content };
        try
        {
            using var response = await http.SendAsync(request, timeout.Token);
            if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
                return null;
            var text = await response.Content.ReadAsStringAsync(timeout.Token);
            if (!response.IsSuccessStatusCode)
                throw new SearchEngineException($"{method} {path} answered {(int)response.StatusCode}: {(text.Length > 300 ? text[..300] : text)}");
            return text.Length == 0 ? new JsonObject() : JsonNode.Parse(text);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SearchEngineException($"{method} {path} took longer than {timeoutSeconds} s.");
        }
        catch (HttpRequestException ex)
        {
            throw new SearchEngineException($"{method} {path} failed: {ex.Message}");
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new SearchEngineException($"{method} {path} answered something that is not JSON: {ex.Message}");
        }
    }
}
