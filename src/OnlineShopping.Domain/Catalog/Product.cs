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

    /// <summary>Barcode (GTIN-8/12/13/14, digits only, check digit valid); shopping feeds and structured data need it.</summary>
    public string? Gtin { get; set; }

    /// <summary>Manufacturer part number.</summary>
    public string? Mpn { get; set; }

    /// <summary>Shipping weight in grams.</summary>
    public int? WeightGrams { get; set; }

    /// <summary>Search-result title in the default language (empty: made from the name); other languages: <see cref="Translations"/>.</summary>
    public string? MetaTitle { get; set; }

    public string? MetaDescription { get; set; }

    /// <summary>Extra search words in the default language, comma-separated.</summary>
    public string? Tags { get; set; }

    public decimal Price { get; set; }
    public decimal? CompareAtPrice { get; set; }
    /// <summary>Stock over all warehouses, kept by the stock ledger (never set it directly).</summary>
    public int Stock { get; set; }

    /// <summary>Units held for checkouts that are not paid yet (active reservations). Available = Stock − Reserved.</summary>
    public int Reserved { get; set; }

    /// <summary>Average stars of the approved reviews (0 without any), kept by the review service.</summary>
    public decimal RatingAverage { get; set; }

    /// <summary>How many approved reviews there are.</summary>
    public int RatingCount { get; set; }
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
    public ICollection<ProductVideo> Videos { get; set; } = new List<ProductVideo>();
    public ICollection<CatalogTranslation> Translations { get; set; } = new List<CatalogTranslation>();
    public ICollection<ProductSpecValue> SpecValues { get; set; } = new List<ProductSpecValue>();
}
