using OnlineShop.Domain.Common;

namespace OnlineShop.Domain.Sales;

/// <summary>
/// The invoice of a paid order. Its number is the next in one unbroken sequence (1, 2, 3 …), given in the same
/// transaction as the payment, so a failed payment leaves no gap. The seller's details are copied when it is issued:
/// changing them later never changes an issued invoice. The invoice is shown as a printable page (saved as PDF by the
/// browser); the amounts are the order's.
/// </summary>
public sealed class Invoice : BaseEntity
{
    public long Number { get; set; }

    public Guid OrderId { get; set; }

    public DateTime IssuedUtc { get; set; }

    public string SellerName { get; set; } = string.Empty;

    public string? SellerAddress { get; set; }

    public string? SellerTaxNumber { get; set; }

    public string? Note { get; set; }
}

public enum ReturnStatus
{
    /// <summary>The customer asked; staff decide.</summary>
    Requested,

    /// <summary>Staff agreed; the customer sends the items back.</summary>
    Approved,

    Rejected,

    /// <summary>The items arrived and went back into stock; the refund can be recorded.</summary>
    Received,
}

/// <summary>A customer's request to send items of a delivered order back, within the shop's return period.</summary>
public sealed class ReturnRequest : BaseEntity
{
    /// <summary>The return number customers and staff use (shown as R-1, R-2 …).</summary>
    public long Number { get; set; }

    public Guid OrderId { get; set; }

    public ReturnStatus Status { get; set; } = ReturnStatus.Requested;

    /// <summary>The customer's reason.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Staff's answer, shown to the customer (why it was rejected, how to send the items).</summary>
    public string? Answer { get; set; }

    public ICollection<ReturnLine> Lines { get; set; } = new List<ReturnLine>();
}

/// <summary>An order line and how many of it go back.</summary>
public sealed class ReturnLine : BaseEntity
{
    public Guid ReturnRequestId { get; set; }

    public Guid OrderItemId { get; set; }

    public int Quantity { get; set; }
}

/// <summary>
/// Money paid back to the customer, recorded by staff (by hand until the payment gateways can refund, phase 8):
/// for a return, or for a paid order canceled before shipping. All refunds of an order never exceed what was paid.
/// </summary>
public sealed class Refund : BaseEntity
{
    public Guid OrderId { get; set; }

    public Guid? ReturnRequestId { get; set; }

    public decimal Amount { get; set; }

    /// <summary>How it was paid back ("Bank transfer", "Card").</summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>A note for staff (a transfer reference).</summary>
    public string? Note { get; set; }

    public string? ActorUserId { get; set; }
}
