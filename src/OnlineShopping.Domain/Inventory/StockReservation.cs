using OnlineShopping.Domain.Common;

namespace OnlineShopping.Domain.Inventory;

public enum StockReservationStatus
{
    /// <summary>Holding stock until it is committed, released or runs out of time.</summary>
    Active,

    /// <summary>Turned into a sale: the units left the stock through the ledger.</summary>
    Committed,

    /// <summary>Given back (checkout abandoned, payment failed, released by staff).</summary>
    Released,

    /// <summary>Its time ran out before it was committed.</summary>
    Expired
}

/// <summary>
/// Units of one item held for a checkout that is not paid yet, so two customers cannot buy the last piece twice.
/// Reservations hold against the item's stock over all warehouses; the cached <c>Reserved</c> counts of the product
/// and the variant are the sum of the active ones. All reservations of one checkout share a <see cref="Reference"/>.
/// </summary>
public sealed class StockReservation : BaseEntity
{
    public Guid ItemId { get; set; }

    public Guid ProductId { get; set; }

    public Guid? VariantId { get; set; }

    public int Quantity { get; set; }

    /// <summary>The checkout or order it belongs to ("order:1042").</summary>
    public required string Reference { get; set; }

    public StockReservationStatus Status { get; set; }

    public DateTime ExpiresUtc { get; set; }

    public DateTime? ClosedUtc { get; set; }
}
