using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnlineShopping.Application.Accounts;
using OnlineShopping.Application.Common;
using OnlineShopping.Application.Customers;
using OnlineShopping.Application.Inventory;
using OnlineShopping.Application.Notifications;
using OnlineShopping.Domain.Sales;

namespace OnlineShopping.Application.Sales;

/// <summary>
/// Checkout: what the page starts with, the totals for the customer's choices, and placing the order. Every amount
/// is computed here from the live cart (rule 5). Placing an order reserves its stock (all or nothing, see
/// <see cref="StockReservationService"/>), counts the discount code's use, copies everything into the order and
/// empties the cart, in one transaction. The order then waits for its payment (phase 8) until the reservation ends.
/// </summary>
public sealed class CheckoutService(
    IApplicationDbContext db,
    CartService carts,
    AddressService addresses,
    ShippingMethodService shipping,
    DiscountCodeService discounts,
    TaxSettingsService taxes,
    StockReservationService reservations,
    IAccountDataIdentity accounts,
    IPhoneNumberNormalizer phones,
    ISmsCapability sms,
    TimeProvider clock,
    IOptions<StoreOptions> store)
{
    public const long FirstOrderNumber = 1001;

    /// <summary>Currencies shown without minor units when the shop has not set them up (IRR, toman).</summary>
    private static readonly string[] WholeCurrencies = ["IRR", "IRT", "JPY", "KRW"];

    private sealed record Priced(CheckoutTotals Totals, IReadOnlyList<ShippingOption> Options, ShippingMethod? Method, DiscountCode? Discount, AppError? DiscountError);

    public async Task<CheckoutView> GetAsync(CartOwner owner, string locale, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var cart = await carts.GetForCheckoutAsync(owner, locale, cancellationToken);
        CheckoutContact? contact = null;
        IReadOnlyList<AddressView> book = [];
        if (owner.UserId is { } userId)
        {
            var account = await accounts.GetAccountDataAsync(userId);
            contact = account is null ? null : new CheckoutContact(account.DisplayName, account.Email, account.PhoneNumber);
            book = await addresses.ListAsync(userId, cancellationToken);
        }
        var priced = await PriceAsync(cart, null, null, null, new DiscountCustomer(owner.UserId, null, null), locale, cancellationToken);
        return new CheckoutView(
            cart.View, owner.UserId is not null, store.Value.GuestCheckout, owner.UserId is null && !sms.IsEnabled, contact, book,
            store.Value.ShippingCountryList, priced.Options, await taxes.GetAsync(cancellationToken));
    }

    public async Task<CheckoutQuote> QuoteAsync(CartOwner owner, CheckoutQuoteRequest request, string locale, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(request);
        var cart = await carts.GetForCheckoutAsync(owner, locale, cancellationToken);
        var country = string.IsNullOrWhiteSpace(request.CountryCode) ? null : request.CountryCode.Trim().ToUpperInvariant();
        var priced = await PriceAsync(cart, country, request.ShippingMethodId, request.DiscountCode, new DiscountCustomer(owner.UserId, null, null), locale, cancellationToken);
        return new CheckoutQuote(
            priced.Totals, store.Value.Currency, priced.Options, priced.Method?.Id, priced.Discount is not null, priced.DiscountError?.Code, cart.View.HasProblems);
    }

    public async Task<Result<PlacedOrderView>> PlaceAsync(CartOwner owner, PlaceOrderRequest request, string locale, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(request);
        var userId = owner.UserId;
        if (userId is null && !store.Value.GuestCheckout)
            return AppError.Forbidden(CheckoutErrors.SignInRequired, "Sign in to order.");

        var cart = await carts.GetForCheckoutAsync(owner, locale, cancellationToken);
        if (cart.Items.Count == 0 || cart.CartId is null)
            return AppError.Conflict(CheckoutErrors.CartEmpty, "The cart is empty.");
        if (cart.View.HasProblems)
            return AppError.Conflict(CheckoutErrors.CartChanged, "Some items in the cart changed; review the cart.");

        var method = (await shipping.ActiveAsync(cancellationToken)).FirstOrDefault(m => m.Id == request.ShippingMethodId);
        if (method is null)
            return AppError.Validation(CheckoutErrors.ShippingMethodInvalid, "Choose one of the shipping methods.");

        var account = userId is null ? null : await accounts.GetAccountDataAsync(userId);
        var destination = await DestinationAsync(method, request, userId, account, cancellationToken);
        if (!destination.IsSuccess)
            return destination.Error!;
        var (name, phone, address) = destination.Value;

        var email = string.IsNullOrWhiteSpace(request.Email) ? account?.Email : request.Email.Trim();
        if (email is null && userId is null && !sms.IsEnabled)
            return AppError.Validation(CheckoutErrors.ContactRequired, "Give an email address for the order confirmation.");

        var priced = await PriceAsync(cart, address?.CountryCode, method.Id, request.DiscountCode, new DiscountCustomer(userId, email, phone), locale, cancellationToken);
        if (priced.DiscountError is { } discountError)
            return discountError;
        if (priced.Totals.Total != request.ExpectedTotal)
            return AppError.Conflict(CheckoutErrors.TotalChanged, "The total changed; check the order again.");

        var option = priced.Options.Single(o => o.Id == method.Id);
        Result<PlacedOrderView> placed = AppError.Conflict(CheckoutErrors.NotAvailable, "The order could not be placed.");
        // Two orders placed at the same moment may pick the same number: the unique index refuses one, which tries again.
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                placed = await db.InTransactionAsync(
                    () => CreateAsync(cart, priced, option, userId, email, name, phone, address, request.Note, locale, cancellationToken),
                    cancellationToken);
                break;
            }
            catch (DbUpdateException) when (attempt < 3)
            {
                db.ChangeTracker.Clear();
            }
        }
        if (!placed.IsSuccess)
            return placed;

        await carts.ClearAsync(cart.CartId.Value, cancellationToken);
        if (request.SaveAddress && userId is not null && request.AddressId is null && request.Address is not null && !method.IsPickup)
            await addresses.CreateAsync(userId, request.Address with { IsDefault = false }, cancellationToken); // a full address book does not stop the order
        return placed;
    }

    private async Task<Result<PlacedOrderView>> CreateAsync(
        CheckoutCart cart, Priced priced, ShippingOption option, string? userId, string? email, string name, string phone, ShippingAddress? address,
        string? note, string locale, CancellationToken cancellationToken)
    {
        var last = await db.Orders.IgnoreQueryFilters().MaxAsync(o => (long?)o.Number, cancellationToken) ?? 0;
        var number = Math.Max(FirstOrderNumber, last + 1);
        var reserved = await reservations.ReserveAsync(
            $"order:{number}", cart.Items.Select(i => new ReservationInput(new StockItem(i.ProductId, i.VariantId), i.Line.Quantity)).ToList(),
            cancellationToken: cancellationToken);
        if (!reserved.IsSuccess)
            return AppError.Conflict(CheckoutErrors.NotAvailable, "Some items are no longer available in that quantity.");
        if (priced.Discount is { } discount && !await discounts.ClaimAsync(discount.Id, cancellationToken))
            return AppError.Conflict(CheckoutErrors.DiscountUsedUp, "The discount code has been used up.");

        var totals = priced.Totals;
        var order = new Order
        {
            Number = number,
            CustomerId = userId,
            OrderedUtc = clock.GetUtcNow().UtcDateTime,
            Locale = locale,
            Status = OrderStatus.Pending,
            PaymentDueUtc = reserved.Value.Min(r => r.ExpiresUtc),
            Email = email,
            RecipientName = name,
            PhoneNumber = phone,
            CountryCode = address?.CountryCode,
            Region = address?.Region,
            City = address?.City,
            Line1 = address?.Line1,
            Line2 = address?.Line2,
            PostalCode = address?.PostalCode,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            ShippingMethodId = option.Id,
            ShippingMethodName = option.Name,
            IsPickup = option.IsPickup,
            DiscountCodeId = priced.Discount?.Id,
            DiscountCodeText = priced.Discount?.Code,
            Currency = store.Value.Currency,
            ExchangeRate = 1,
            Subtotal = totals.Subtotal,
            Discount = totals.Discount,
            Shipping = totals.Shipping,
            Tax = totals.Tax,
            TaxRate = totals.TaxRate,
            PricesIncludeTax = totals.PricesIncludeTax,
            Total = totals.Total,
        };
        foreach (var item in cart.Items)
        {
            order.Items.Add(new OrderItem
            {
                ProductId = item.ProductId,
                VariantId = item.VariantId,
                ProductNumber = item.Line.Number,
                ProductName = item.Line.Name,
                Sku = item.Sku,
                VariantLabel = item.Line.VariantLabel,
                UnitPrice = item.Line.UnitPrice,
                Quantity = item.Line.Quantity,
            });
        }
        db.Orders.Add(order);
        await db.SaveChangesAsync(cancellationToken);
        return new PlacedOrderView(number, totals.Total, order.Currency, order.PaymentDueUtc.Value, email);
    }

    /// <summary>Who receives the order and where: the pickup contact, a saved address of the customer, or a typed one.</summary>
    private async Task<Result<(string Name, string Phone, ShippingAddress? Address)>> DestinationAsync(
        ShippingMethod method, PlaceOrderRequest request, string? userId, AccountData? account, CancellationToken cancellationToken)
    {
        if (method.IsPickup)
        {
            var name = string.IsNullOrWhiteSpace(request.PickupName) ? account?.DisplayName : request.PickupName.Trim();
            var typed = string.IsNullOrWhiteSpace(request.PickupPhone) ? account?.PhoneNumber : request.PickupPhone;
            var phone = typed is null ? null : phones.Normalize(typed, store.Value.PhoneRegion);
            if (string.IsNullOrWhiteSpace(name) || phone is null)
                return AppError.Validation(CheckoutErrors.PickupContactRequired, "Give a name and phone number for the pickup.");
            return (name, phone, (ShippingAddress?)null);
        }

        ShippingAddress? address;
        if (request.AddressId is { } addressId)
        {
            address = userId is null ? null : await addresses.FindAsync(userId, addressId, cancellationToken);
            if (address is null)
                return AppError.NotFound(CheckoutErrors.AddressNotFound, "Address not found.");
        }
        else if (request.Address is { } input)
        {
            var checkedAddress = addresses.Check(input);
            if (!checkedAddress.IsSuccess)
                return checkedAddress.Error!;
            address = checkedAddress.Value;
        }
        else
        {
            return AppError.Validation(CheckoutErrors.AddressRequired, "Give the shipping address.");
        }

        if (!method.ServesCountry(address.CountryCode))
            return AppError.Validation(CheckoutErrors.ShippingMethodInvalid, "The shipping method does not ship to this country.");
        return (address.RecipientName, address.PhoneNumber, address);
    }

    private async Task<Priced> PriceAsync(
        CheckoutCart cart, string? country, Guid? methodId, string? code, DiscountCustomer customer, string locale, CancellationToken cancellationToken)
    {
        var decimals = await DecimalsAsync(cancellationToken);
        var subtotal = cart.View.Subtotal;
        DiscountCode? discount = null;
        AppError? discountError = null;
        if (!string.IsNullOrWhiteSpace(code))
        {
            var checkedCode = await discounts.CheckAsync(code, subtotal, customer, cancellationToken);
            if (checkedCode.IsSuccess)
                discount = checkedCode.Value;
            else
                discountError = checkedCode.Error;
        }

        var discountAmount = CheckoutPricing.DiscountFor(discount, subtotal, decimals);
        var goods = subtotal - discountAmount;
        var weight = cart.Items.Where(i => i.Line.Problem == CartLineProblem.None).Sum(i => i.WeightGrams * i.Line.Quantity);
        var freeShipping = discount?.Kind == DiscountKind.FreeShipping;
        var methods = (await shipping.ActiveAsync(cancellationToken)).Where(m => country is null || m.ServesCountry(country)).ToList();
        var options = methods.Select(m => ShippingMethodService.Option(m, locale, goods, weight, freeShipping)).ToList();
        var method = methods.FirstOrDefault(m => m.Id == methodId);
        var shippingCost = method is null ? 0 : CheckoutPricing.ShippingCost(method, goods, weight, freeShipping);
        var totals = CheckoutPricing.Totals(subtotal, discountAmount, shippingCost, await taxes.GetAsync(cancellationToken), decimals);
        return new Priced(totals, options, method, discount, discountError);
    }

    /// <summary>Decimals of the base currency: as the admin set the currency up, else 0 for rial-like currencies and 2 otherwise.</summary>
    private async Task<int> DecimalsAsync(CancellationToken cancellationToken)
    {
        var code = store.Value.Currency;
        var set = await db.Currencies.AsNoTracking().Where(c => c.Code == code).Select(c => (int?)c.Decimals).FirstOrDefaultAsync(cancellationToken);
        return set ?? (WholeCurrencies.Contains(code, StringComparer.Ordinal) ? 0 : 2);
    }
}
