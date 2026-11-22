using OnlineShop.Domain.Common;

namespace OnlineShop.Domain.Inventory;

public enum StocktakeStatus
{
    /// <summary>Counting: counts can be entered and changed.</summary>
    Open,

    /// <summary>Done: the differences went into the ledger as adjustment lines; nothing changes any more.</summary>
    Posted,

    /// <summary>Dropped without touching the stock.</summary>
    Cancelled
}

/// <summary>
/// A physical count of one warehouse. Opening it lists every item with the warehouse's stock at that moment;
/// staff then enter what they counted (any order, any number of times); posting turns each difference into one
/// adjustment line. At most one open stocktake per warehouse.
/// </summary>
public sealed class Stocktake : BaseEntity
{
    /// <summary>1, 2, 3… for the whole shop; shown as "ST-12" and used as the ledger reference.</summary>
    public int Number { get; set; }

    public Guid WarehouseId { get; set; }

    public StocktakeStatus Status { get; set; }

    public string? Note { get; set; }

    public string? OpenedByUserId { get; set; }

    /// <summary>When it was posted or cancelled.</summary>
    public DateTime? ClosedUtc { get; set; }

    public string? ClosedByUserId { get; set; }

    public ICollection<StocktakeLine> Lines { get; set; } = new List<StocktakeLine>();
}

/// <summary>One item of a stocktake: its stock when the count opened, and what was counted.</summary>
public sealed class StocktakeLine
{
    public Guid StocktakeId { get; set; }

    /// <summary>The variant's id, else the product's (as in <see cref="StockLevel"/>).</summary>
    public Guid ItemId { get; set; }

    public Guid ProductId { get; set; }

    public Guid? VariantId { get; set; }

    /// <summary>The warehouse's stock of the item when the stocktake opened (or when the item was added to it).</summary>
    public int SnapshotQuantity { get; set; }

    /// <summary>What staff counted; null while not counted (an uncounted item is left as it is).</summary>
    public int? CountedQuantity { get; set; }

    /// <summary>When it was counted (last time): the stock the count is compared with is the stock at that moment.</summary>
    public DateTime? CountedUtc { get; set; }

    /// <summary>On posting: the stock at the moment of the count.</summary>
    public int? ExpectedQuantity { get; set; }

    /// <summary>On posting: the adjustment written to the ledger (counted − expected, 0 when they match).</summary>
    public int? PostedDifference { get; set; }
}
