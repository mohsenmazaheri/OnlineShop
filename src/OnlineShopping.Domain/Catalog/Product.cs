using OnlineShopping.Domain.Common;

namespace OnlineShopping.Domain.Catalog;

public class Product : BaseEntity
{
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string SKU { get; set; } = string.Empty;

    /// <summary>
    /// Stable public product number used in URLs (<c>/{locale}/p/{number}/{slug}</c>); never reused or changed, so a
    /// renamed product keeps its address (docs/SEO-MARKETING.md).
    /// </summary>
    public long PublicNumber { get; set; }

    /// <summary>URL slug in the default language (other languages: <see cref="Translations"/>).</summary>
    public string Slug { get; set; } = string.Empty;

    public decimal Price { get; set; }
    public decimal? CompareAtPrice { get; set; }
    public int Stock { get; set; }
    public bool IsPublished { get; set; }
    public Guid ProductGroupId { get; set; }
    public ProductGroup ProductGroup { get; set; } = default!;
    public Guid? BrandId { get; set; }
    public Brand? Brand { get; set; }

    /// <summary>
    /// Buyable versions (colour, size). With variants, <see cref="Stock"/> is the sum of their stock (kept by the
    /// admin service) and <see cref="Price"/> is the price of every variant without its own.
    /// </summary>
    public ICollection<ProductVariant> Variants { get; set; } = new List<ProductVariant>();

    public ICollection<ProductImage> Images { get; set; } = new List<ProductImage>();
    public ICollection<CatalogTranslation> Translations { get; set; } = new List<CatalogTranslation>();
    public ICollection<ProductSpecValue> SpecValues { get; set; } = new List<ProductSpecValue>();
}
