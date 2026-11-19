namespace OnlineShopping.Domain.Catalog;

/// <summary>
/// A catalog item's content in one more language (categories, sub-categories, product groups, products). The item's own
/// columns hold the shop's default language; a language without a row falls back to them. Product content is
/// translated manually only, never by an external service.
/// </summary>
public sealed class CatalogTranslation
{
    /// <summary>Locale code (fa, en, hy, de, ar).</summary>
    public required string Locale { get; set; }

    public required string Name { get; set; }

    /// <summary>URL slug in this language (categories, sub-categories and products); null where the item has none.</summary>
    public string? Slug { get; set; }

    public string? Description { get; set; }

    /// <summary>Search-result title in this language (empty: made from the name).</summary>
    public string? MetaTitle { get; set; }

    /// <summary>Search-result description in this language (empty: the start of the description).</summary>
    public string? MetaDescription { get; set; }

    /// <summary>Extra search words in this language, comma-separated ("phone, mobile").</summary>
    public string? Tags { get; set; }
}
