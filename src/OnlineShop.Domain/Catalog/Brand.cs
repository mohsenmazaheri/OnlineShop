using OnlineShop.Domain.Common;

namespace OnlineShop.Domain.Catalog;

/// <summary>
/// A product brand ("Orvia"). Name, slug and description are in the default language; <see cref="Translations"/> holds
/// the others (a brand name often reads the same everywhere, so a translation is optional).
/// </summary>
public sealed class Brand : BaseEntity
{
    public required string Name { get; set; }

    /// <summary>URL slug in the default language (a later brand page: <c>/{locale}/brand/{slug}</c>).</summary>
    public string Slug { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>The brand's logo (a media URL, 300 px); optional.</summary>
    public string? LogoUrl { get; set; }

    public ICollection<CatalogTranslation> Translations { get; set; } = new List<CatalogTranslation>();
}
