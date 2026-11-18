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
}
