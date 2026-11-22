using OnlineShop.Domain.Catalog;
using OnlineShop.Domain.Common;

namespace OnlineShop.Domain.Sales;

/// <summary>A product (or one of its variants) in a cart, with how many. One line per item.</summary>
public class CartItem : BaseEntity
{
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = default!;

    /// <summary>The chosen variant, for products that have variants.</summary>
    public Guid? VariantId { get; set; }

    /// <summary>What the line is: the variant's id, else the product's (one line per item, as stock levels are keyed).</summary>
    public Guid ItemId { get; set; }

    public int Quantity { get; set; }
    public Guid CartId { get; set; }
    public Cart Cart { get; set; } = default!;
}
