using OnlineShop.Domain.Common;

namespace OnlineShop.Domain.Sales;

/// <summary>One product or variant of an order, with its name and price as they were when ordered (snapshots).</summary>
public class OrderItem : BaseEntity
{
    public Guid ProductId { get; set; }

    public Guid? VariantId { get; set; }

    public long ProductNumber { get; set; }

    public string ProductName { get; set; } = string.Empty; // snapshot

    public string Sku { get; set; } = string.Empty;          // snapshot

    /// <summary>"Beige / M" for a variant.</summary>
    public string? VariantLabel { get; set; }                // snapshot

    public decimal UnitPrice { get; set; }                   // snapshot

    public int Quantity { get; set; }

    public Guid OrderId { get; set; }
    public Order Order { get; set; } = default!;
}
