using OnlineShopping.Domain.Common;

namespace OnlineShopping.Domain.Catalog;

public class Category : BaseEntity
{
    public required string Name { get; set; }
    public string Slug { get; set; } = string.Empty;

    /// <summary>Tile image for menus and the home page (a site-relative path or an absolute URL); optional.</summary>
    public string? ImageUrl { get; set; }

    public ICollection<SubCategory> SubCategories { get; set; } = new List<SubCategory>();
    public ICollection<CatalogTranslation> Translations { get; set; } = new List<CatalogTranslation>();
}
