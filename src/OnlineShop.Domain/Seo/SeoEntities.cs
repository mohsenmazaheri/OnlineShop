using OnlineShop.Domain.Common;

namespace OnlineShop.Domain.Seo;

/// <summary>
/// Words that mean the same in one language ("گوشی", "موبایل", "تلفن همراه"): a search for any of them finds the
/// others (used by the product search of phase 6).
/// </summary>
public sealed class SearchSynonym : BaseEntity
{
    public required string Locale { get; set; }

    /// <summary>The words, normalized and comma-separated (at least two).</summary>
    public required string Terms { get; set; }
}

/// <summary>How a redirect answers.</summary>
public enum RedirectKind
{
    /// <summary>301: moved for good (search engines move the ranking).</summary>
    Permanent,

    /// <summary>302: moved for now.</summary>
    Temporary,

    /// <summary>410: removed for good, no replacement.</summary>
    Gone,
}

/// <summary>
/// An old storefront address that answers with a redirect (for example the pages of a shop's previous website), so
/// links and search rankings are not lost. Product addresses never need one: they carry a stable number.
/// </summary>
public sealed class Redirect : BaseEntity
{
    /// <summary>The old path, starting with "/" (no host, no query).</summary>
    public required string FromPath { get; set; }

    /// <summary>The new address: a path on this site or an absolute http(s) URL; empty for <see cref="RedirectKind.Gone"/>.</summary>
    public string? ToPath { get; set; }

    public RedirectKind Kind { get; set; }

    /// <summary>How often it was used (counted by the storefront).</summary>
    public long HitCount { get; set; }

    public DateTime? LastHitUtc { get; set; }
}
