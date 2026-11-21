using System.ComponentModel.DataAnnotations;

namespace OnlineShopping.Application.Search;

/// <summary>Which engine matches search text (<c>Search:Provider</c>).</summary>
public enum SearchProvider
{
    /// <summary>The database's own search: PostgreSQL full-text and trigrams (the simple engine on other databases).</summary>
    Postgres,

    /// <summary>A self-hosted OpenSearch cluster (Apache 2.0, language analyzers).</summary>
    OpenSearch,

    /// <summary>A self-hosted Elasticsearch cluster (its licence is the operator's responsibility).</summary>
    Elasticsearch
}

/// <summary>Settings of the product search (section "Search").</summary>
public sealed class SearchOptions : IValidatableObject
{
    public const string SectionName = "Search";

    /// <summary>How often the index worker looks for queued catalog changes.</summary>
    [Range(1, 3600)]
    public int IndexIntervalSeconds { get; set; } = 5;

    public SearchProvider Provider { get; set; } = SearchProvider.Postgres;

    /// <summary>The external engine (only read when <see cref="Provider"/> is not Postgres).</summary>
    public SearchEngineOptions Engine { get; set; } = new();

    /// <summary>Whether an external engine matches the text (the database stays the fallback).</summary>
    public bool UsesEngine => Provider != SearchProvider.Postgres;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!UsesEngine)
            yield break;
        if (!Uri.TryCreate(Engine.Url, UriKind.Absolute, out var url) || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
            yield return new ValidationResult($"Search:Engine:Url must be an http(s) address when Search:Provider is {Provider}.", ["Engine.Url"]);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(Engine, new ValidationContext(Engine), results, validateAllProperties: true);
        foreach (var result in results)
            yield return new ValidationResult($"Search:Engine: {result.ErrorMessage}", result.MemberNames.Select(m => $"Engine.{m}"));
    }
}

/// <summary>Where the external engine is and how the shop uses it (section "Search:Engine").</summary>
public sealed class SearchEngineOptions
{
    /// <summary>The cluster address, e.g. http://search:9200.</summary>
    public string? Url { get; set; }

    /// <summary>Basic authentication (OpenSearch security plugin, Elasticsearch users).</summary>
    public string? Username { get; set; }

    public string? Password { get; set; }

    /// <summary>An Elasticsearch API key (sent as "ApiKey …"); used instead of the user name when set.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Index names start with it ({prefix}-{locale}), so several shops can share one cluster.</summary>
    [RegularExpression("^[a-z0-9][a-z0-9_-]{0,40}$")]
    public string IndexPrefix { get; set; } = "onlineshop";

    /// <summary>A search waits this long for the engine before the database answers instead.</summary>
    [Range(1, 60)]
    public int QueryTimeoutSeconds { get; set; } = 3;

    /// <summary>Time allowed for one write (a batch of documents, an index creation).</summary>
    [Range(5, 600)]
    public int WriteTimeoutSeconds { get; set; } = 60;

    /// <summary>Replica shards per index (0 for a single-node cluster).</summary>
    [Range(0, 5)]
    public int Replicas { get; set; }

    /// <summary>When a change has waited longer than this to reach the engine, the database answers searches.</summary>
    [Range(10, 86400)]
    public int MaxLagSeconds { get; set; } = 300;

    /// <summary>After a failure, the engine is left alone (the database answers) for this long.</summary>
    [Range(5, 3600)]
    public int RetrySeconds { get; set; } = 30;
}
