namespace OnlineShop.Domain.Search;

/// <summary>
/// What search reads for one product in one shop language, every part already in one spelling (see SearchText):
/// <see cref="Title"/> weighs most, then <see cref="Keywords"/>, then <see cref="Details"/> and <see cref="Codes"/>, then
/// <see cref="Description"/>. Built from the catalog by the search index; only published products have one. On
/// PostgreSQL the table also has a full-text vector computed from these columns (see its migration).
/// </summary>
public sealed class SearchDocument
{
    public Guid ProductId { get; set; }

    public required string Locale { get; set; }

    /// <summary>The name in this language (the default language's when not translated).</summary>
    public required string Title { get; set; }

    /// <summary>The default-language name (when translated), brand and category names.</summary>
    public string? Keywords { get; set; }

    /// <summary>Search words, spec values and variant options.</summary>
    public string? Details { get; set; }

    /// <summary>SKUs, barcodes and the public number.</summary>
    public string? Codes { get; set; }

    public string? Description { get; set; }

    public DateTime UpdatedUtc { get; set; }
}

/// <summary>What a queued change touches: its documents are rebuilt.</summary>
public enum SearchTarget
{
    Product,
    Brand,
    Category,
    SubCategory,
    ProductGroup,
    Spec,
    SpecOption,
    /// <summary>Every product (a full rebuild).</summary>
    All
}

/// <summary>
/// A change waiting to reach the search index (the outbox): written in the same transaction as the change, so the index
/// can never miss one; processed and removed by the index worker.
/// </summary>
public sealed class SearchOutboxEntry
{
    public long Id { get; set; }

    public SearchTarget Target { get; set; }

    public Guid TargetId { get; set; }

    public DateTime EnqueuedUtc { get; set; }
}

/// <summary>How often a phrase was searched in a language, and how often it found nothing (for suggestions and reports).</summary>
public sealed class SearchTermStat
{
    public required string Locale { get; set; }

    /// <summary>The phrase in one spelling.</summary>
    public required string Term { get; set; }

    public int Searches { get; set; }

    public int ZeroResults { get; set; }

    public DateTime LastSearchedUtc { get; set; }
}

/// <summary>
/// A product whose search documents changed and must reach the external search engine (OpenSearch/Elasticsearch): the
/// second outbox, written in the same transaction as the documents and removed once the engine has them.
/// <see cref="Guid.Empty"/> asks for a full rebuild of the engine's indexes.
/// </summary>
public sealed class SearchEngineOutboxEntry
{
    public long Id { get; set; }

    public Guid ProductId { get; set; }

    public DateTime EnqueuedUtc { get; set; }
}
