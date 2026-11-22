using OnlineShop.Domain.Common;

namespace OnlineShop.Domain.Inventory;

/// <summary>Why stock changed.</summary>
public enum StockMovementKind
{
    /// <summary>The stock an item already had when the ledger started.</summary>
    Opening,

    /// <summary>Goods came in (a delivery, a purchase).</summary>
    Receipt,

    /// <summary>Goods went out for any reason other than a sale (damage, own use, a sample).</summary>
    Issue,

    /// <summary>Sold through an order.</summary>
    Sale,

    /// <summary>Came back from a customer.</summary>
    Return,

    /// <summary>A correction (the product editor, an import, a stocktake).</summary>
    Adjustment,

    /// <summary>Left this warehouse for another one (paired with a <see cref="TransferIn"/>).</summary>
    TransferOut,

    /// <summary>Arrived from another warehouse.</summary>
    TransferIn
}

/// <summary>
/// One line of the stock ledger: a signed quantity of one item (a product without variants, or a variant) in one
/// warehouse. Lines are never changed or deleted; a mistake is corrected by another line. The stock of an item in a
/// warehouse is the sum of its lines, kept in <see cref="StockLevel"/>.
/// </summary>
public sealed class StockMovement : BaseEntity
{
    public Guid WarehouseId { get; set; }

    public Guid ProductId { get; set; }

    /// <summary>The variant, for a product with variants.</summary>
    public Guid? VariantId { get; set; }

    /// <summary>The variant's id, else the product's: the item the line counts.</summary>
    public Guid ItemId { get; set; }

    public StockMovementKind Kind { get; set; }

    /// <summary>Positive in, negative out.</summary>
    public int Quantity { get; set; }

    /// <summary>The item's stock in this warehouse right after this line.</summary>
    public int BalanceAfter { get; set; }

    /// <summary>A document number or the source of an automatic line ("auto:editor").</summary>
    public string? Reference { get; set; }

    public string? Note { get; set; }

    /// <summary>
    /// The cost of one unit in the base currency: the purchase price on a receipt, otherwise the item's average cost at the
    /// time (null while it has none). The accounts value stock movements with it.
    /// </summary>
    public decimal? UnitCost { get; set; }

    /// <summary>The two lines of one transfer share this id.</summary>
    public Guid? TransferId { get; set; }

    /// <summary>The staff member who made it; null for automatic lines.</summary>
    public string? UserId { get; set; }
}

/// <summary>
/// The current stock of one item in one warehouse: the sum of its <see cref="StockMovement"/> lines, never below zero.
/// Only the stock ledger changes it, always together with a movement.
/// </summary>
public sealed class StockLevel
{
    public Guid WarehouseId { get; set; }

    public Guid ItemId { get; set; }

    public Guid ProductId { get; set; }

    public Guid? VariantId { get; set; }

    public int Quantity { get; set; }
}
