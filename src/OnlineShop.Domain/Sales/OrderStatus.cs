namespace OnlineShop.Domain.Sales;

/// <summary>
/// Where an order is: Pending (waiting for its payment, stock held) → Paid (stock sold, invoiced) → Shipped → Completed;
/// or Canceled: before payment (by staff, or when the payment time ran out) or paid but not shipped (the stock comes
/// back; the refund is recorded separately).
/// </summary>
public enum OrderStatus
{
    Pending,
    Paid,
    Shipped,
    Completed,
    Canceled
}
