using OnlineShop.Domain.Common;

namespace OnlineShop.Domain.Catalog;

/// <summary>
/// A landing page for a much-searched combination ("Orvia phones under 300"): its own clean address
/// (/{locale}/l/{slug}), title and text per language, and the product list of a fixed browse query (the query string
/// of a filtered listing, e.g. <c>c=mobile-phones&amp;brand=orvia&amp;max=300</c>). Unlike filtered listings it is indexed.
/// The default language is in the own columns, the others in <see cref="Translations"/> (name = title, description =
/// the text above the products).
/// </summary>
public sealed class LandingPage : BaseEntity
{
    public required string Title { get; set; }

    public required string Slug { get; set; }

    /// <summary>The text above the products (plain text, paragraphs separated by blank lines).</summary>
    public string? Intro { get; set; }

    public string? MetaTitle { get; set; }

    public string? MetaDescription { get; set; }

    /// <summary>The browse query whose products the page lists (validated on save).</summary>
    public required string Query { get; set; }

    public bool IsPublished { get; set; }

    public ICollection<CatalogTranslation> Translations { get; set; } = new List<CatalogTranslation>();
}
