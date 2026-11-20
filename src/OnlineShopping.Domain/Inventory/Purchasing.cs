using OnlineShopping.Domain.Common;

namespace OnlineShopping.Domain.Inventory;

/// <summary>Someone the shop buys goods from. One that has receipts is deactivated rather than deleted.</summary>
public sealed class Supplier : BaseEntity
{
    public required string Name { get; set; }

    public string? ContactName { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public string? Address { get; set; }

    public string? Note { get; set; }

    public bool IsActive { get; set; } = true;
}

public enum PurchaseReceiptStatus
{
    /// <summary>Being written: lines can change; the stock is not touched yet.</summary>
    Draft,

    /// <summary>The goods are in: every line went into the ledger as a receipt with its cost. Read-only.</summary>
    Posted,

    /// <summary>Dropped while still a draft.</summary>
    Cancelled
}

/// <summary>
/// Goods bought from a supplier into one warehouse ("PR-12"). Costs are in the shop's base currency, which the receipt
/// keeps. Posting adds the quantities to the stock and updates each item's weighted average cost.
/// </summary>
public sealed class PurchaseReceipt : BaseEntity
{
    public int Number { get; set; }

    public Guid SupplierId { get; set; }

    public Guid WarehouseId { get; set; }

    public PurchaseReceiptStatus Status { get; set; }

    /// <summary>The supplier's own invoice or delivery note number.</summary>
    public string? SupplierReference { get; set; }

    public string? Note { get; set; }

    /// <summary>ISO 4217 code of the costs (the base currency when the receipt was written).</summary>
    public required string Currency { get; set; }

    public string? CreatedByUserId { get; set; }

    /// <summary>When it was posted or cancelled.</summary>
    public DateTime? ClosedUtc { get; set; }

    public string? ClosedByUserId { get; set; }

    public ICollection<PurchaseReceiptLine> Lines { get; set; } = new List<PurchaseReceiptLine>();
}

/// <summary>One item of a purchase receipt: how many came in and what one cost.</summary>
public sealed class PurchaseReceiptLine
{
    public Guid ReceiptId { get; set; }

    /// <summary>The variant's id, else the product's; an item appears once per receipt.</summary>
    public Guid ItemId { get; set; }

    public Guid ProductId { get; set; }

    public Guid? VariantId { get; set; }

    public int SortOrder { get; set; }

    public int Quantity { get; set; }

    public decimal UnitCost { get; set; }
}

/// <summary>
/// What the shop knows about one stock item beyond its quantities: its weighted average cost (base currency, updated
/// by every receipt with a cost) and the minimum stock below which it is reported as running low.
/// </summary>
public sealed class StockItemInfo
{
    public Guid ItemId { get; set; }

    public Guid ProductId { get; set; }

    public Guid? VariantId { get; set; }

    public decimal? AverageCost { get; set; }

    /// <summary>At or below this total the item is "running low"; null = no alert.</summary>
    public int? MinimumStock { get; set; }
}
