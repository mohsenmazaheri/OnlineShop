using OnlineShopping.Domain.Common;

namespace OnlineShopping.Domain.Catalog;

/// <summary>
/// One buyable version of a product ("Beige, size M"): its own SKU and stock, and optionally its own price. Each variant
/// has exactly one option for every variant spec of its product (colour, size), and no two variants share the same
/// combination.
/// </summary>
public sealed class ProductVariant : BaseEntity
{
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = default!;

    public required string Sku { get; set; }

    /// <summary>Its own price; null means the product's price.</summary>
    public decimal? Price { get; set; }

    /// <summary>Its own "old price"; null means the product's (only when <see cref="Price"/> is null too).</summary>
    public decimal? CompareAtPrice { get; set; }

    public int Stock { get; set; }

    public int SortOrder { get; set; }

    public ICollection<VariantOption> Options { get; set; } = new List<VariantOption>();
}

/// <summary>A variant's choice for one variant spec (spec "Colour" → option "Beige").</summary>
public sealed class VariantOption
{
    public Guid SpecId { get; set; }
    public Guid OptionId { get; set; }
}
