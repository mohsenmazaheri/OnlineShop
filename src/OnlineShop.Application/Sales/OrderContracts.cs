using FluentValidation;
using OnlineShop.Domain.Sales;

namespace OnlineShop.Application.Sales;

/// <summary>Stable error codes of orders (translated by the web app under orders.errors).</summary>
public static class OrderErrors
{
    public const string NotFound = "order.not_found";
    public const string StatusInvalid = "order.status_invalid";
    public const string NotAvailable = "order.not_available";
    public const string TrackingRequired = "order.tracking_required";
    public const string TextTooLong = "order.text_too_long";
}

public sealed record OrderLineView(long Number, string Name, string Sku, string? VariantLabel, string? ImageUrl, decimal UnitPrice, int Quantity, decimal LineTotal);

/// <summary>An address as the order copied it (no address for pickup orders).</summary>
public sealed record OrderAddressView(string? CountryCode, string? Region, string? City, string? Line1, string? Line2, string? PostalCode);

/// <summary>An entry of the history; <see cref="ByStaff"/>: a staff member made it (else the customer or the shop itself).</summary>
public sealed record OrderEventView(DateTime AtUtc, OrderEventKind Kind, OrderStatus? Status, string? Text, bool IsInternal, bool ByStaff);

/// <summary>What changes are possible now (the admin's buttons and the customer's view follow it).</summary>
public sealed record OrderActions(bool CanMarkPaid, bool CanShip, bool CanComplete, bool CanCancel);

/// <summary>One order with its lines, amounts, delivery, history and the possible next steps.</summary>
/// <param name="CustomerId">For staff: the ordering account (null: a guest; always null in customer views).</param>
public sealed record OrderView(
    long Number,
    OrderStatus Status,
    DateTime OrderedUtc,
    DateTime? PaymentDueUtc,
    string Locale,
    string? Email,
    string RecipientName,
    string PhoneNumber,
    OrderAddressView? Address,
    bool IsPickup,
    string ShippingMethodName,
    string? TrackingCarrier,
    string? TrackingCode,
    string? Note,
    string? DiscountCode,
    string Currency,
    decimal Subtotal,
    decimal Discount,
    decimal Shipping,
    decimal Tax,
    decimal TaxRate,
    bool PricesIncludeTax,
    decimal Total,
    IReadOnlyList<OrderLineView> Lines,
    IReadOnlyList<OrderEventView> History,
    OrderActions Actions,
    string? CustomerId);

/// <summary>A row of an order list.</summary>
public sealed record OrderSummary(long Number, OrderStatus Status, DateTime OrderedUtc, string RecipientName, string? Email, int ItemCount, decimal Total, string Currency, string? ImageUrl);

public sealed record OrderPage(IReadOnlyList<OrderSummary> Items, int Total, int Page, int PageSize);

/// <summary>The staff list's filters: a status, and a search over number, name, email and phone.</summary>
public sealed record OrderListQuery(OrderStatus? Status, string? Search, int Page = 1);

/// <summary>Staff changing an order; <see cref="Text"/> is the reason shown to the customer for a cancellation.</summary>
public sealed record OrderActionRequest(string? Text);

public sealed record ShipOrderRequest(string Carrier, string TrackingCode);

public sealed record OrderNoteRequest(string Text);

public sealed class OrderActionRequestValidator : AbstractValidator<OrderActionRequest>
{
    public OrderActionRequestValidator() =>
        RuleFor(r => r.Text!).MaximumLength(1000).WithErrorCode(OrderErrors.TextTooLong).When(r => r.Text is not null);
}

public sealed class ShipOrderRequestValidator : AbstractValidator<ShipOrderRequest>
{
    public ShipOrderRequestValidator()
    {
        RuleFor(r => r.Carrier).NotEmpty().WithErrorCode(OrderErrors.TrackingRequired).MaximumLength(100).WithErrorCode(OrderErrors.TextTooLong);
        RuleFor(r => r.TrackingCode).NotEmpty().WithErrorCode(OrderErrors.TrackingRequired).MaximumLength(100).WithErrorCode(OrderErrors.TextTooLong);
    }
}

public sealed class OrderNoteRequestValidator : AbstractValidator<OrderNoteRequest>
{
    public OrderNoteRequestValidator() =>
        RuleFor(r => r.Text).NotEmpty().WithErrorCode(OrderErrors.TextTooLong).MaximumLength(1000).WithErrorCode(OrderErrors.TextTooLong);
}

/// <summary>
/// Signed links to a guest's order (the order's own page needs no account): the token proves the link came from the
/// shop. Implemented with ASP.NET Data Protection, so nothing is stored and old links keep working after key rotation.
/// </summary>
public interface IOrderLinks
{
    string TokenFor(Guid orderId);

    bool IsValid(Guid orderId, string token);
}

/// <summary>Which status changes are allowed (one place, used by the service and the views).</summary>
public static class OrderTransitions
{
    public static OrderActions For(OrderStatus status, bool isPickup) => new(
        CanMarkPaid: status == OrderStatus.Pending,
        CanShip: status == OrderStatus.Paid && !isPickup,
        // Pickup orders are handed over at the shop: paid → completed.
        CanComplete: status == OrderStatus.Shipped || (status == OrderStatus.Paid && isPickup),
        CanCancel: status == OrderStatus.Pending);
}
