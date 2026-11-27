using FluentValidation;
using OnlineShop.Application.Customers;

namespace OnlineShop.Application.Sales;

/// <summary>Stable error codes of checkout (translated by the web app under checkout.errors).</summary>
public static class CheckoutErrors
{
    public const string CartEmpty = "checkout.cart_empty";
    public const string CartChanged = "checkout.cart_changed";
    public const string SignInRequired = "checkout.sign_in_required";
    public const string ShippingMethodInvalid = "checkout.shipping_method_invalid";
    public const string AddressRequired = "checkout.address_required";
    public const string AddressNotFound = "checkout.address_not_found";
    public const string ContactRequired = "checkout.contact_required";
    public const string EmailInvalid = "checkout.email_invalid";
    public const string PickupContactRequired = "checkout.pickup_contact_required";
    public const string NoteTooLong = "checkout.note_too_long";
    public const string TotalChanged = "checkout.total_changed";
    public const string NotAvailable = "checkout.not_available";
    public const string DiscountInvalid = "checkout.discount_invalid";
    public const string DiscountMinSubtotal = "checkout.discount_min_subtotal";
    public const string DiscountUsedUp = "checkout.discount_used_up";
    public const string DiscountAlreadyUsed = "checkout.discount_already_used";
}

/// <summary>The signed-in customer's contact details, to fill the form.</summary>
public sealed record CheckoutContact(string? Name, string? Email, string? PhoneNumber);

/// <summary>Everything the checkout page starts with.</summary>
/// <param name="EmailRequired">A guest must give an email address (without SMS it is the only way to reach them).</param>
public sealed record CheckoutView(
    CartView Cart,
    bool SignedIn,
    bool GuestCheckout,
    bool EmailRequired,
    CheckoutContact? Contact,
    IReadOnlyList<AddressView> Addresses,
    IReadOnlyList<string> Countries,
    IReadOnlyList<ShippingOption> ShippingMethods,
    TaxView Tax);

/// <summary>What the totals depend on: the destination country, the chosen method and a discount code.</summary>
public sealed record CheckoutQuoteRequest(string? CountryCode, Guid? ShippingMethodId, string? DiscountCode);

/// <summary>
/// The totals for the current cart and choices. A refused discount code does not fail the quote: it is reported in
/// <see cref="DiscountError"/> and left out. <see cref="ShippingMethodId"/> is null when the chosen method does not
/// serve the country (no shipping cost is counted then).
/// </summary>
public sealed record CheckoutQuote(
    CheckoutTotals Totals,
    string Currency,
    IReadOnlyList<ShippingOption> ShippingMethods,
    Guid? ShippingMethodId,
    bool DiscountApplied,
    string? DiscountError,
    bool HasProblems);

/// <summary>
/// Places the order. The address is a saved one (<see cref="AddressId"/>, signed-in customers) or a typed one
/// (<see cref="Address"/>; <see cref="SaveAddress"/> adds it to the address book); pickup orders give a name and
/// phone instead. <see cref="ExpectedTotal"/> is the total the customer saw: if prices changed meanwhile the order is
/// refused (checkout.total_changed) rather than charged differently.
/// </summary>
public sealed record PlaceOrderRequest(
    Guid ShippingMethodId,
    Guid? AddressId,
    AddressInput? Address,
    bool SaveAddress,
    string? Email,
    string? PickupName,
    string? PickupPhone,
    string? DiscountCode,
    string? Note,
    decimal ExpectedTotal);

public sealed class PlaceOrderRequestValidator : AbstractValidator<PlaceOrderRequest>
{
    public PlaceOrderRequestValidator()
    {
        RuleFor(r => r.ShippingMethodId).NotEmpty().WithErrorCode(CheckoutErrors.ShippingMethodInvalid);
        RuleFor(r => r.Address!).SetValidator(new AddressInputValidator()).When(r => r.Address is not null);
        RuleFor(r => r.Email!).MaximumLength(256).WithErrorCode(CheckoutErrors.EmailInvalid)
            .EmailAddress().WithErrorCode(CheckoutErrors.EmailInvalid)
            .When(r => !string.IsNullOrWhiteSpace(r.Email));
        RuleFor(r => r.PickupName!).MaximumLength(100).WithErrorCode(CheckoutErrors.PickupContactRequired).When(r => r.PickupName is not null);
        RuleFor(r => r.PickupPhone!).MaximumLength(32).WithErrorCode(CheckoutErrors.PickupContactRequired).When(r => r.PickupPhone is not null);
        RuleFor(r => r.DiscountCode!).MaximumLength(40).WithErrorCode(CheckoutErrors.DiscountInvalid).When(r => r.DiscountCode is not null);
        RuleFor(r => r.Note!).MaximumLength(1000).WithErrorCode(CheckoutErrors.NoteTooLong).When(r => r.Note is not null);
        RuleFor(r => r.ExpectedTotal).GreaterThanOrEqualTo(0);
    }
}

/// <summary>
/// The placed order: its number, what is to be paid and until when the stock is held for it, and its page below the
/// language ("account/orders/1001", or for guests the signed "order/1001?key=…"). Payment itself arrives with the
/// gateways (phase 8).
/// </summary>
public sealed record PlacedOrderView(long Number, decimal Total, string Currency, DateTime PaymentDueUtc, string? Email, string Path);
