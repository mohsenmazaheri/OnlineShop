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
    public const string NoInvoice = "order.no_invoice";
    public const string ReturnNotAllowed = "order.return_not_allowed";
    public const string ReturnQuantityInvalid = "order.return_quantity_invalid";
    public const string ReturnReasonRequired = "order.return_reason_required";
    public const string ReturnNotFound = "order.return_not_found";
    public const string ReturnStatusInvalid = "order.return_status_invalid";
    public const string RefundInvalid = "order.refund_invalid";
    public const string RefundTooMuch = "order.refund_too_much";
}

/// <param name="Returnable">How many of it may still be asked back (ordered − already in a return that was not rejected).</param>
public sealed record OrderLineView(Guid Id, long Number, string Name, string Sku, string? VariantLabel, string? ImageUrl, decimal UnitPrice, int Quantity, decimal LineTotal, int Returnable);

public sealed record InvoiceSummary(long Number, DateTime IssuedUtc);

public sealed record ReturnLineView(Guid OrderItemId, string Name, string? VariantLabel, string Sku, int Quantity);

/// <summary>A return with what staff may do with it now and the refund it suggests (its lines' share of the paid goods).</summary>
public sealed record ReturnView(
    Guid Id, long Number, ReturnStatus Status, DateTime CreatedUtc, string Reason, string? Answer, IReadOnlyList<ReturnLineView> Lines,
    decimal SuggestedRefund, bool CanApprove, bool CanReject, bool CanReceive);

/// <summary>Money paid back; <see cref="Note"/> is for staff only (null in customer views).</summary>
public sealed record RefundView(DateTime AtUtc, decimal Amount, string Method, string? Note, long? ReturnNumber);

/// <summary>An address as the order copied it (no address for pickup orders).</summary>
public sealed record OrderAddressView(string? CountryCode, string? Region, string? City, string? Line1, string? Line2, string? PostalCode);

/// <summary>An entry of the history; <see cref="ByStaff"/>: a staff member made it (else the customer or the shop itself).</summary>
public sealed record OrderEventView(DateTime AtUtc, OrderEventKind Kind, OrderStatus? Status, string? Text, bool IsInternal, bool ByStaff);

/// <summary>What changes are possible now (the admin's buttons and the customer's view follow it).</summary>
public sealed record OrderActions(bool CanMarkPaid, bool CanShip, bool CanComplete, bool CanCancel, bool CanRequestReturn = false, bool CanRefund = false);

/// <summary>One order with its lines, amounts, delivery, history and the possible next steps.</summary>
/// <param name="CustomerId">For staff: the ordering account (null: a guest; always null in customer views).</param>
/// <param name="Refundable">What may still be paid back: the paid total minus the refunds (0 before payment).</param>
/// <param name="ReturnableUntilUtc">The customer may ask for a return until then (null: not delivered, period over or returns off).</param>
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
    string? CustomerId,
    InvoiceSummary? Invoice,
    IReadOnlyList<ReturnView> Returns,
    IReadOnlyList<RefundView> Refunds,
    decimal RefundedTotal,
    decimal Refundable,
    DateTime? ReturnableUntilUtc);

/// <summary>A row of an order list.</summary>
public sealed record OrderSummary(long Number, OrderStatus Status, DateTime OrderedUtc, string RecipientName, string? Email, int ItemCount, decimal Total, string Currency, string? ImageUrl);

public sealed record OrderPage(IReadOnlyList<OrderSummary> Items, int Total, int Page, int PageSize);

/// <summary>The staff list's filters: a status, and a search over number, name, email and phone.</summary>
public sealed record OrderListQuery(OrderStatus? Status, string? Search, int Page = 1);

/// <summary>Staff changing an order; <see cref="Text"/> is the reason shown to the customer for a cancellation.</summary>
public sealed record OrderActionRequest(string? Text);

public sealed record ShipOrderRequest(string Carrier, string TrackingCode);

public sealed record OrderNoteRequest(string Text);

/// <summary>An invoice page: the invoice's own facts and its order.</summary>
public sealed record InvoiceView(long Number, DateTime IssuedUtc, string SellerName, string? SellerAddress, string? SellerTaxNumber, string? Note, OrderView Order);

public sealed record ReturnLineInput(Guid OrderItemId, int Quantity);

public sealed record RequestReturnRequest(IReadOnlyList<ReturnLineInput> Lines, string Reason);

/// <summary>Staff's answer to a return (shown to the customer).</summary>
public sealed record ReturnAnswerRequest(string? Text);

public sealed record RecordRefundRequest(decimal Amount, string Method, string? Note, Guid? ReturnId);

public sealed class RequestReturnRequestValidator : AbstractValidator<RequestReturnRequest>
{
    public RequestReturnRequestValidator()
    {
        RuleFor(r => r.Lines).NotNull().Must(l => l.Count is > 0 and <= 100 && l.All(x => x.Quantity > 0)).WithErrorCode(OrderErrors.ReturnQuantityInvalid);
        RuleFor(r => r.Reason).NotEmpty().WithErrorCode(OrderErrors.ReturnReasonRequired).MaximumLength(1000).WithErrorCode(OrderErrors.TextTooLong);
    }
}

public sealed class ReturnAnswerRequestValidator : AbstractValidator<ReturnAnswerRequest>
{
    public ReturnAnswerRequestValidator() =>
        RuleFor(r => r.Text!).MaximumLength(1000).WithErrorCode(OrderErrors.TextTooLong).When(r => r.Text is not null);
}

public sealed class RecordRefundRequestValidator : AbstractValidator<RecordRefundRequest>
{
    public RecordRefundRequestValidator()
    {
        RuleFor(r => r.Amount).GreaterThan(0).WithErrorCode(OrderErrors.RefundInvalid);
        RuleFor(r => r.Method).NotEmpty().WithErrorCode(OrderErrors.RefundInvalid).MaximumLength(100).WithErrorCode(OrderErrors.TextTooLong);
        RuleFor(r => r.Note!).MaximumLength(500).WithErrorCode(OrderErrors.TextTooLong).When(r => r.Note is not null);
    }
}

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
        // A paid order can still be canceled until it leaves: its stock comes back, the refund is recorded apart.
        CanCancel: status is OrderStatus.Pending or OrderStatus.Paid);

    /// <summary>The steps of a return in its status.</summary>
    public static (bool Approve, bool Reject, bool Receive) ForReturn(ReturnStatus status) =>
        (status == ReturnStatus.Requested, status is ReturnStatus.Requested or ReturnStatus.Approved, status == ReturnStatus.Approved);
}
