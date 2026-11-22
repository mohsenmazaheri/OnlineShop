using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnlineShopping.Application.Catalog.Admin;
using OnlineShopping.Application.Common;
using OnlineShopping.Application.Customers;
using OnlineShopping.Application.Inventory;
using OnlineShopping.Application.Notifications;
using OnlineShopping.Application.Sales;
using OnlineShopping.Domain.Inventory;
using OnlineShopping.Domain.Sales;
using OnlineShopping.Infrastructure.Identity;
using OnlineShopping.Infrastructure.Persistence;
using OnlineShopping.UnitTests.TestSupport;

namespace OnlineShopping.UnitTests.Application;

public sealed class CheckoutPricingTests
{
    private static ShippingMethod Method(decimal cost, decimal perKg = 0, decimal? freeOver = null) => new() { Name = "Post", Cost = cost, CostPerKg = perKg, FreeOver = freeOver };

    [Fact]
    public void Discounts_take_a_capped_percent_or_an_amount_never_more_than_the_goods()
    {
        Assert.Equal(12.35m, CheckoutPricing.DiscountFor(new DiscountCode { Code = "A", Kind = DiscountKind.Percent, Value = 10 }, 123.45m, 2));
        Assert.Equal(5m, CheckoutPricing.DiscountFor(new DiscountCode { Code = "A", Kind = DiscountKind.Percent, Value = 10, MaxDiscount = 5 }, 123.45m, 2));
        Assert.Equal(12m, CheckoutPricing.DiscountFor(new DiscountCode { Code = "A", Kind = DiscountKind.Percent, Value = 10 }, 123m, 0));
        Assert.Equal(30m, CheckoutPricing.DiscountFor(new DiscountCode { Code = "A", Kind = DiscountKind.Amount, Value = 50 }, 30m, 2));
        Assert.Equal(0m, CheckoutPricing.DiscountFor(new DiscountCode { Code = "A", Kind = DiscountKind.FreeShipping }, 30m, 2));
        Assert.Equal(0m, CheckoutPricing.DiscountFor(null, 30m, 2));
    }

    [Fact]
    public void Shipping_costs_a_fixed_part_plus_every_started_kilogram_unless_free()
    {
        Assert.Equal(5m, CheckoutPricing.ShippingCost(Method(5, 2), 100, 0));
        Assert.Equal(7m, CheckoutPricing.ShippingCost(Method(5, 2), 100, 1));
        Assert.Equal(9m, CheckoutPricing.ShippingCost(Method(5, 2), 100, 1001));
        Assert.Equal(0m, CheckoutPricing.ShippingCost(Method(5, 2, freeOver: 100), 100, 1001));
        Assert.Equal(9m, CheckoutPricing.ShippingCost(Method(5, 2, freeOver: 100), 99.99m, 1001));
        Assert.Equal(0m, CheckoutPricing.ShippingCost(Method(5, 2), 1, 5000, freeShippingCode: true));
    }

    [Fact]
    public void Tax_is_taken_out_of_inclusive_prices_or_added_to_exclusive_ones()
    {
        // 100 − 10 + 10 = 100 with 10 % inside: 9.09.
        Assert.Equal(new CheckoutTotals(100, 10, 10, 9.09m, 100, 10, true), CheckoutPricing.Totals(100, 10, 10, new TaxView(10, true), 2));
        Assert.Equal(new CheckoutTotals(100, 10, 10, 10, 110, 10, false), CheckoutPricing.Totals(100, 10, 10, new TaxView(10, false), 2));
        Assert.Equal(new CheckoutTotals(1_000_000, 0, 0, 90_909, 1_000_000, 10, true), CheckoutPricing.Totals(1_000_000, 0, 0, new TaxView(10, true), 0));
        Assert.Equal(new CheckoutTotals(50, 0, 5, 0, 55, 0, true), CheckoutPricing.Totals(50, 0, 5, new TaxView(0, true), 2));
    }
}

public sealed class ShippingAndDiscountAdminTests : DemoCatalogTestBase
{
    private ShippingMethodService Shipping(AppDbContext db, StoreOptions? store = null) => new(db, Options.Create(store ?? Store));

    internal static SaveShippingMethodRequest Request(string name, decimal cost = 5, bool pickup = false, string[]? countries = null, string en = "") =>
        new([new LocalizedText("fa", name, null, "۲ تا ۴ روز"), new LocalizedText("en", en, null, en.Length > 0 ? "2–4 days" : null)],
            pickup, cost, 0, null, countries ?? [], 2, 4, true, 0);

    [Fact]
    public async Task Shipping_methods_are_created_translated_changed_and_deleted()
    {
        await using var db = Database.CreateContext();
        var store = new StoreOptions { Locales = "fa,en", DefaultLocale = "fa", Currency = "EUR", ShippingCountries = "IR,DE" };
        var shipping = Shipping(db, store);

        var post = (await shipping.CreateAsync(Request("پست", 5, countries: ["de"], en: "Post"))).Value;
        Assert.Equal(["DE"], post.Countries);
        Assert.Equal(("Post", "2–4 days"), (post.Texts[1].Name, post.Texts[1].Description));
        Assert.Equal(ShippingErrors.CountryInvalid, (await shipping.CreateAsync(Request("x", countries: ["FR"]))).Error!.Code);
        Assert.Equal(CatalogErrors.DefaultTextRequired, (await shipping.CreateAsync(Request(""))).Error!.Code);
        var pickup = (await shipping.UpdateAsync(post.Id, Request("تحویل حضوری", 0, pickup: true, countries: ["DE"]))).Value;
        // Pickup needs no countries and has no weight cost.
        Assert.Equal((true, 0, ""), (pickup.IsPickup, pickup.Countries.Count, pickup.Texts[1].Name));

        var method = (await shipping.ActiveAsync()).Single();
        Assert.Equal(("تحویل حضوری", 0m), (ShippingMethodService.Option(method, "en", 10, 0).Name, ShippingMethodService.Option(method, "en", 10, 0).Cost));
        Assert.True((await shipping.DeleteAsync(post.Id)).Value);
        Assert.Empty(await shipping.ListAsync());
        Assert.Equal(ShippingErrors.NotFound, (await shipping.DeleteAsync(post.Id)).Error!.Code);
    }

    [Fact]
    public async Task Tax_defaults_to_none_and_is_saved_once_per_shop()
    {
        await using var db = Database.CreateContext();
        var taxes = new TaxSettingsService(db);
        Assert.Equal(new TaxView(0, true), await taxes.GetAsync());
        await taxes.SaveAsync(new SaveTaxRequest(9, false));
        Assert.Equal(new TaxView(10, true), await taxes.SaveAsync(new SaveTaxRequest(10, true)));
        Assert.Equal(1, await db.TaxSettings.CountAsync());
        Assert.False(new SaveTaxRequestValidator().Validate(new SaveTaxRequest(100.5m, true)).IsValid);
        Assert.False(new SaveTaxRequestValidator().Validate(new SaveTaxRequest(9.125m, true)).IsValid);
    }

    [Fact]
    public async Task Discount_codes_are_unique_and_checked_for_dates_minimum_limit_and_repeat_use()
    {
        await using var db = Database.CreateContext();
        var codes = new DiscountCodeService(db, Database.Clock);
        var now = Database.Clock.GetUtcNow().UtcDateTime;
        SaveDiscountCodeRequest Request(string code, int? limit = null, bool once = false, DateTime? starts = null, DateTime? ends = null, decimal? minimum = null) =>
            new(code, DiscountKind.Percent, 10, minimum, null, starts, ends, limit, once, true, null);

        var spring = (await codes.CreateAsync(Request(" spring-10 ", limit: 1, minimum: 50))).Value;
        Assert.Equal("SPRING-10", spring.Code);
        Assert.Equal(DiscountErrors.CodeTaken, (await codes.CreateAsync(Request("Spring-10"))).Error!.Code);
        await codes.CreateAsync(Request("LATER", starts: now.AddDays(1)));
        await codes.CreateAsync(Request("ENDED", ends: now));
        await codes.CreateAsync(Request("ONCE", once: true));

        var nobody = new DiscountCustomer(null, null, null);
        Assert.Equal(DiscountKind.Percent, (await codes.CheckAsync("spring-10", 50, nobody)).Value.Kind);
        Assert.Equal(CheckoutErrors.DiscountMinSubtotal, (await codes.CheckAsync("SPRING-10", 49.99m, nobody)).Error!.Code);
        Assert.Equal(CheckoutErrors.DiscountInvalid, (await codes.CheckAsync("LATER", 100, nobody)).Error!.Code);
        Assert.Equal(CheckoutErrors.DiscountInvalid, (await codes.CheckAsync("ENDED", 100, nobody)).Error!.Code);
        Assert.Equal(CheckoutErrors.DiscountInvalid, (await codes.CheckAsync("NOPE", 100, nobody)).Error!.Code);

        // The last use is claimed once only.
        Assert.True(await codes.ClaimAsync(spring.Id));
        Assert.False(await codes.ClaimAsync(spring.Id));
        Assert.Equal(CheckoutErrors.DiscountUsedUp, (await codes.CheckAsync("SPRING-10", 100, nobody)).Error!.Code);

        // "Once per customer" looks at earlier orders by account, email or phone; canceled orders do not count.
        var once = await db.DiscountCodes.SingleAsync(d => d.Code == "ONCE");
        db.Orders.Add(new Order { Number = 1001, Email = "a@example.com", PhoneNumber = "+491701234567", DiscountCodeId = once.Id, Status = OrderStatus.Canceled });
        await db.SaveChangesAsync();
        Assert.True((await codes.CheckAsync("ONCE", 10, new DiscountCustomer(null, "a@example.com", null))).IsSuccess);
        await db.Orders.ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.Pending));
        Assert.Equal(CheckoutErrors.DiscountAlreadyUsed, (await codes.CheckAsync("ONCE", 10, new DiscountCustomer(null, "a@example.com", null))).Error!.Code);
        Assert.Equal(CheckoutErrors.DiscountAlreadyUsed, (await codes.CheckAsync("ONCE", 10, new DiscountCustomer(null, null, "+491701234567"))).Error!.Code);
        Assert.True((await codes.CheckAsync("ONCE", 10, new DiscountCustomer("someone", "b@example.com", null))).IsSuccess);

        db.ChangeTracker.Clear();
        var updated = (await codes.UpdateAsync(spring.Id, Request("SPRING-11") with { Kind = DiscountKind.FreeShipping, Value = 99 })).Value;
        Assert.Equal(("SPRING-11", 0m, 1), (updated.Code, updated.Value, updated.UsedCount));
        Assert.True((await codes.DeleteAsync(spring.Id)).Value);
        Assert.Equal(["ENDED", "LATER", "ONCE"], (await codes.ListAsync()).Select(c => c.Code).Order());
    }

    [Fact]
    public void Admin_requests_are_validated()
    {
        var discount = new SaveDiscountCodeRequestValidator();
        var ok = new SaveDiscountCodeRequest("WELCOME", DiscountKind.Percent, 15, null, null, null, null, null, false, true, null);
        Assert.True(discount.Validate(ok).IsValid);
        Assert.False(discount.Validate(ok with { Code = "a b" }).IsValid);
        Assert.False(discount.Validate(ok with { Value = 120 }).IsValid);
        Assert.True(discount.Validate(ok with { Kind = DiscountKind.FreeShipping, Value = 0 }).IsValid);
        Assert.False(discount.Validate(ok with { StartsUtc = DateTime.UtcNow, EndsUtc = DateTime.UtcNow.AddDays(-1) }).IsValid);
        Assert.False(discount.Validate(ok with { UsageLimit = 0 }).IsValid);

        var shipping = new SaveShippingMethodRequestValidator();
        Assert.True(shipping.Validate(Request("پست")).IsValid);
        Assert.False(shipping.Validate(Request("پست", cost: -1)).IsValid);
        Assert.False(shipping.Validate(Request("پست") with { MinDays = 5, MaxDays = 2 }).IsValid);
    }
}

public sealed class CheckoutServiceTests : DemoCatalogTestBase
{
    private readonly FakeAccountIdentity _identity = new();

    private CheckoutService Service(AppDbContext db, StoreOptions? store = null)
    {
        var options = Options.Create(store ?? Store);
        var phones = new PhoneNumberNormalizer();
        return new CheckoutService(
            db, new CartService(db, options), new AddressService(db, phones, options), new ShippingMethodService(db, options),
            new DiscountCodeService(db, Database.Clock), new TaxSettingsService(db),
            new StockReservationService(db, TestLedger.For(db, store ?? Store), Database.Clock, Options.Create(new InventoryOptions()), options),
            new FakeAccountDataIdentity(_identity), phones, new NoSmsCapability(), Database.Clock, options);
    }

    private static AddressInput Address(string city = "Tehran", string country = "IR") =>
        new(null, "Sara Ahmadi", "0912 123 4567", country, null, city, "Valiasr St. 10", null, null, false);

    private async Task AddUserAsync(string id, string email)
    {
        await using var db = Database.CreateContext();
        db.Users.Add(new AppUser { Id = id, UserName = id, NormalizedUserName = id.ToUpperInvariant(), Email = email });
        await db.SaveChangesAsync();
        _identity.Users.Add(new FakeAccountIdentity.User(id, email, null, "x", "fa"));
    }

    private async Task<Guid> MethodAsync(AppDbContext db, string name, decimal cost, decimal perKg = 0, bool pickup = false, string[]? countries = null, StoreOptions? store = null) =>
        (await new ShippingMethodService(db, Options.Create(store ?? Store)).CreateAsync(
            ShippingAndDiscountAdminTests.Request(name, cost, pickup, countries) with { CostPerKg = perKg })).Value.Id;

    private static PlaceOrderRequest Order(Guid method, decimal expected, AddressInput? address = null, string? email = "guest@example.com", string? code = null) =>
        new(method, null, address ?? Address(), false, email, null, null, code, "Please call first", expected);

    [Fact]
    public async Task A_guest_places_an_order_that_reserves_the_stock_and_empties_the_cart()
    {
        await SeedDemoAsync();
        var phone = await NumberOfAsync("DEMO-PHN-001");
        await using var db = Database.CreateContext();
        var guest = new CartOwner(null, CartService.NewGuestToken());
        await new CartService(db, Options.Create(Store)).AddAsync(guest, new AddToCartRequest(phone, null, 2), "en");
        await new TaxSettingsService(db).SaveAsync(new SaveTaxRequest(10, true));
        var post = await MethodAsync(db, "پست", 5, perKg: 2);
        var product = await db.Products.AsNoTracking().SingleAsync(p => p.PublicNumber == phone);
        var checkout = Service(db);

        var view = await checkout.GetAsync(guest, "en");
        Assert.Equal((false, true, true), (view.SignedIn, view.GuestCheckout, view.EmailRequired));
        Assert.Equal(["IR"], view.Countries);
        var quote = await checkout.QuoteAsync(guest, new CheckoutQuoteRequest("IR", post, null), "en");
        var kilograms = (int)Math.Ceiling((product.WeightGrams ?? 0) * 2 / 1000m);
        var shipping = 5 + (2m * kilograms);
        var total = (product.Price * 2) + shipping;
        Assert.Equal(new CheckoutTotals(product.Price * 2, 0, shipping, Math.Round(total * 10 / 110, 2, MidpointRounding.AwayFromZero), total, 10, true), quote.Totals);
        Assert.Equal((post, "EUR"), (quote.ShippingMethodId, quote.Currency));

        // A total other than the one shown is refused.
        Assert.Equal(CheckoutErrors.TotalChanged, (await checkout.PlaceAsync(guest, Order(post, total - 1), "en")).Error!.Code);
        var placed = (await checkout.PlaceAsync(guest, Order(post, total), "en")).Value;
        Assert.Equal((CheckoutService.FirstOrderNumber, total, "guest@example.com"), (placed.Number, placed.Total, placed.Email));
        Assert.Equal(Database.Clock.GetUtcNow().UtcDateTime.AddMinutes(15), placed.PaymentDueUtc);

        var order = await db.Orders.AsNoTracking().Include(o => o.Items).SingleAsync();
        Assert.Equal((OrderStatus.Pending, (string?)null, "+989121234567", "Tehran", "Please call first", "پست"),
            (order.Status, order.CustomerId, order.PhoneNumber, order.City, order.Note, order.ShippingMethodName));
        var item = Assert.Single(order.Items);
        Assert.Equal(("DEMO-PHN-001", product.Price, 2, phone), (item.Sku, item.UnitPrice, item.Quantity, item.ProductNumber));
        Assert.Equal(2, await db.Products.Where(p => p.Id == product.Id).Select(p => p.Reserved).SingleAsync());
        Assert.Equal("order:1001", (await db.StockReservations.SingleAsync(r => r.Status == StockReservationStatus.Active)).Reference);
        Assert.Empty((await new CartService(db, Options.Create(Store)).GetAsync(guest, "en")).Lines);
        Assert.Equal(CheckoutErrors.CartEmpty, (await checkout.PlaceAsync(guest, Order(post, total), "en")).Error!.Code);

        // The next order gets the next number.
        var other = new CartOwner(null, CartService.NewGuestToken());
        await new CartService(db, Options.Create(Store)).AddAsync(other, new AddToCartRequest(phone, null, 1), "en");
        var next = (await checkout.QuoteAsync(other, new CheckoutQuoteRequest("IR", post, null), "en")).Totals.Total;
        Assert.Equal(1002, (await checkout.PlaceAsync(other, Order(post, next), "en")).Value.Number);
    }

    [Fact]
    public async Task A_customer_uses_a_saved_address_and_a_discount_code_once()
    {
        await SeedDemoAsync();
        await AddUserAsync("u-1", "sara@example.com");
        await AddUserAsync("u-2", "omid@example.com");
        var laptop = await NumberOfAsync("DEMO-LAP-001");
        await using var db = Database.CreateContext();
        var carts = new CartService(db, Options.Create(Store));
        var me = new CartOwner("u-1", null);
        await carts.AddAsync(me, new AddToCartRequest(laptop, null, 1), "en");
        var addresses = new AddressService(db, new PhoneNumberNormalizer(), Options.Create(Store));
        var mine = (await addresses.CreateAsync("u-1", Address("Shiraz"))).Value;
        var theirs = (await addresses.CreateAsync("u-2", Address("Tabriz"))).Value;
        var post = await MethodAsync(db, "پست", 3);
        await new DiscountCodeService(db, Database.Clock).CreateAsync(
            new SaveDiscountCodeRequest("WELCOME", DiscountKind.Percent, 10, null, 20, null, null, 5, true, true, null));
        var checkout = Service(db);
        var price = await db.Products.Where(p => p.PublicNumber == laptop).Select(p => p.Price).SingleAsync();

        var view = await checkout.GetAsync(me, "en");
        Assert.Equal((true, false, "sara@example.com", "Shiraz"), (view.SignedIn, view.EmailRequired, view.Contact!.Email, Assert.Single(view.Addresses).City));
        var refused = await checkout.QuoteAsync(me, new CheckoutQuoteRequest("IR", post, "nope"), "en");
        Assert.Equal((false, CheckoutErrors.DiscountInvalid, 0m), (refused.DiscountApplied, refused.DiscountError, refused.Totals.Discount));
        var quote = await checkout.QuoteAsync(me, new CheckoutQuoteRequest("IR", post, "welcome"), "en");
        Assert.Equal((true, Math.Min(20m, Math.Round(price / 10, 2)), price - quote.Totals.Discount + 3), (quote.DiscountApplied, quote.Totals.Discount, quote.Totals.Total));

        // Someone else's address is not found (checked in the query).
        var request = new PlaceOrderRequest(post, theirs.Id, null, false, null, null, null, "welcome", null, quote.Totals.Total);
        Assert.Equal(CheckoutErrors.AddressNotFound, (await checkout.PlaceAsync(me, request, "en")).Error!.Code);
        var placed = (await checkout.PlaceAsync(me, request with { AddressId = mine.Id }, "en")).Value;
        Assert.Equal("sara@example.com", placed.Email);
        var order = await db.Orders.AsNoTracking().SingleAsync();
        Assert.Equal(("u-1", "Shiraz", "WELCOME", quote.Totals.Discount), (order.CustomerId, order.City, order.DiscountCodeText, order.Discount));
        Assert.Equal(1, await db.DiscountCodes.Select(d => d.UsedCount).SingleAsync());

        // Once per customer: the second order cannot use it.
        await carts.AddAsync(me, new AddToCartRequest(laptop, null, 1), "en");
        Assert.Equal(CheckoutErrors.DiscountAlreadyUsed, (await checkout.QuoteAsync(me, new CheckoutQuoteRequest("IR", post, "WELCOME"), "en")).DiscountError);
    }

    [Fact]
    public async Task Checkout_refuses_what_it_cannot_deliver()
    {
        await SeedDemoAsync();
        var phone = await NumberOfAsync("DEMO-PHN-001");
        await using var db = Database.CreateContext();
        var carts = new CartService(db, Options.Create(Store));
        var guest = new CartOwner(null, CartService.NewGuestToken());
        var checkout = Service(db);
        var post = await MethodAsync(db, "پست", 0);
        Assert.Equal(CheckoutErrors.CartEmpty, (await checkout.PlaceAsync(guest, Order(post, 0), "en")).Error!.Code);
        await carts.AddAsync(guest, new AddToCartRequest(phone, null, 2), "en");
        var price = await db.Products.Where(p => p.PublicNumber == phone).Select(p => p.Price).SingleAsync();
        var total = price * 2;

        var noGuests = new StoreOptions { Locales = Store.Locales, DefaultLocale = "fa", Currency = "EUR", GuestCheckout = false };
        Assert.Equal(CheckoutErrors.SignInRequired, (await Service(db, noGuests).PlaceAsync(guest, Order(post, total), "en")).Error!.Code);
        Assert.Equal(CheckoutErrors.ContactRequired, (await checkout.PlaceAsync(guest, Order(post, total, email: null), "en")).Error!.Code);
        Assert.Equal(CheckoutErrors.ShippingMethodInvalid, (await checkout.PlaceAsync(guest, Order(Guid.NewGuid(), total), "en")).Error!.Code);
        Assert.Equal(CheckoutErrors.AddressRequired, (await checkout.PlaceAsync(guest, Order(post, total) with { Address = null }, "en")).Error!.Code);
        Assert.Equal(AddressErrors.CountryNotSupported, (await checkout.PlaceAsync(guest, Order(post, total, Address(country: "FR")), "en")).Error!.Code);

        // A method for other countries is not offered for the address.
        var germany = new StoreOptions { Locales = Store.Locales, DefaultLocale = "fa", Currency = "EUR", ShippingCountries = "IR,DE" };
        var dhl = await MethodAsync(db, "DHL", 9, countries: ["DE"], store: germany);
        Assert.Equal(CheckoutErrors.ShippingMethodInvalid, (await Service(db, germany).PlaceAsync(guest, Order(dhl, total + 9), "en")).Error!.Code);
        Assert.DoesNotContain((await checkout.QuoteAsync(guest, new CheckoutQuoteRequest("IR", dhl, null), "en")).ShippingMethods, m => m.Id == dhl);

        // Pickup needs a name and a phone number, no address.
        var pickup = await MethodAsync(db, "حضوری", 0, pickup: true);
        var atShop = Order(pickup, total) with { Address = null };
        Assert.Equal(CheckoutErrors.PickupContactRequired, (await checkout.PlaceAsync(guest, atShop, "en")).Error!.Code);
        var stockBefore = await db.Products.Where(p => p.PublicNumber == phone).Select(p => p.Stock).SingleAsync();

        // Stock changed meanwhile: the cart is to be reviewed first.
        await db.Products.Where(p => p.PublicNumber == phone).ExecuteUpdateAsync(s => s.SetProperty(p => p.Reserved, p => p.Stock - 1));
        Assert.Equal(CheckoutErrors.CartChanged, (await checkout.PlaceAsync(guest, atShop with { PickupName = "Sara", PickupPhone = "09121234567" }, "en")).Error!.Code);
        await db.Products.Where(p => p.PublicNumber == phone).ExecuteUpdateAsync(s => s.SetProperty(p => p.Reserved, 0));

        var placed = await checkout.PlaceAsync(guest, atShop with { PickupName = "Sara", PickupPhone = "09121234567" }, "en");
        var order = await db.Orders.AsNoTracking().SingleAsync(o => o.Number == placed.Value.Number);
        Assert.Equal((true, "Sara", "+989121234567", (string?)null), (order.IsPickup, order.RecipientName, order.PhoneNumber, order.City));
        Assert.Equal(stockBefore, await db.Products.Where(p => p.PublicNumber == phone).Select(p => p.Stock).SingleAsync());
    }

    [Fact]
    public async Task A_free_shipping_code_zeroes_the_shipping()
    {
        await SeedDemoAsync();
        var phone = await NumberOfAsync("DEMO-PHN-001");
        await using var db = Database.CreateContext();
        var guest = new CartOwner(null, CartService.NewGuestToken());
        await new CartService(db, Options.Create(Store)).AddAsync(guest, new AddToCartRequest(phone, null, 1), "en");
        var post = await MethodAsync(db, "پست", 7);
        await new DiscountCodeService(db, Database.Clock).CreateAsync(
            new SaveDiscountCodeRequest("SHIPFREE", DiscountKind.FreeShipping, 0, null, null, null, null, null, false, true, null));

        var quote = await Service(db).QuoteAsync(guest, new CheckoutQuoteRequest("IR", post, "shipfree"), "en");
        Assert.Equal((0m, 0m, 0m), (quote.Totals.Shipping, quote.Totals.Discount, Assert.Single(quote.ShippingMethods).Cost));
    }

    [Fact]
    public void Order_requests_are_validated()
    {
        var validator = new PlaceOrderRequestValidator();
        var ok = new PlaceOrderRequest(Guid.NewGuid(), null, Address(), false, "a@example.com", null, null, null, null, 10);
        Assert.True(validator.Validate(ok).IsValid);
        Assert.False(validator.Validate(ok with { Email = "not an email" }).IsValid);
        Assert.False(validator.Validate(ok with { ShippingMethodId = Guid.Empty }).IsValid);
        Assert.False(validator.Validate(ok with { Note = new string('x', 1001) }).IsValid);
        Assert.False(validator.Validate(ok with { Address = Address() with { City = "" } }).IsValid);
    }
}
