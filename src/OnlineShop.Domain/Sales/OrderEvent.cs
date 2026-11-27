using OnlineShop.Domain.Common;

namespace OnlineShop.Domain.Sales;

public enum OrderEventKind
{
    /// <summary>The customer placed the order.</summary>
    Placed,

    /// <summary>The status changed to <see cref="OrderEvent.Status"/>.</summary>
    StatusChanged,

    /// <summary>A carrier and tracking code were set (<see cref="OrderEvent.Text"/>).</summary>
    TrackingSet,

    /// <summary>A note by staff (internal) or a reason shown to the customer.</summary>
    Note,

    /// <summary>The payment time ran out: the shop canceled the order and released its stock.</summary>
    PaymentExpired,

    /// <summary>The invoice was issued (<see cref="OrderEvent.Text"/> = its number).</summary>
    Invoiced,

    /// <summary>The customer asked for a return (<see cref="OrderEvent.Text"/> = its number, "R-3").</summary>
    ReturnRequested,

    ReturnApproved,

    ReturnRejected,

    /// <summary>The returned items arrived and went back into stock.</summary>
    ReturnReceived,

    /// <summary>Money was paid back (<see cref="OrderEvent.Text"/> = the amount).</summary>
    Refunded,
}

/// <summary>
/// One entry of an order's history, never changed afterwards. Its time is <see cref="BaseEntity.CreatedUtc"/>; the
/// acting staff member is <see cref="ActorUserId"/> (null: the customer or the shop itself, e.g. an expired payment).
/// Internal entries are shown to staff only.
/// </summary>
public sealed class OrderEvent : BaseEntity
{
    public Guid OrderId { get; set; }

    public OrderEventKind Kind { get; set; }

    /// <summary>The new status, for <see cref="OrderEventKind.StatusChanged"/>.</summary>
    public OrderStatus? Status { get; set; }

    public string? Text { get; set; }

    public string? ActorUserId { get; set; }

    public bool IsInternal { get; set; }
}
