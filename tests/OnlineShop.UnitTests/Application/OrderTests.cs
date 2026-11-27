using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OnlineShop.Application.Catalog.Admin;
using OnlineShop.Application.Common;
using OnlineShop.Application.Customers;
using OnlineShop.Application.Inventory;
using OnlineShop.Application.Notifications;
using OnlineShop.Application.Sales;
using OnlineShop.Domain.Inventory;
using OnlineShop.Domain.Sales;
using OnlineShop.Infrastructure.Identity;
using OnlineShop.Infrastructure.Persistence;
using OnlineShop.Infrastructure.Sales;
using OnlineShop.UnitTests.TestSupport;

namespace OnlineShop.UnitTests.Application;

public sealed class OrderLifecycleTests : DemoCatalogTestBase
{
    private SalesKit? _kit;

    private SalesKit Kit => _kit ??= new SalesKit(Database, Store);

    private const string Phone = "DEMO-PHN-001";

    private async Task AddUserAsync(string id, string email)
    {
        await using var db = Database.CreateContext();
        db.Users.Add(new AppUser { Id = id, UserName = id, NormalizedUserName = id.ToUpperInvariant(), Email = email });
        await db.SaveChangesAsync();
        Kit.Identity.Users.Add(new FakeAccountIdentity.User(id, email, null, "x", "en"));
    }

    /// <summary>Places an order of <paramref name="quantity"/> phones (as a guest unless a user is given); returns it.</summary>
    private async Task<PlacedOrderView> PlaceAsync(int quantity = 1, string? userId = null, bool pickup = false, string? code = null)
    {
        var number = await NumberOfAsync(Phone);
        await using var db = Database.CreateContext();
        var owner = new CartOwner(userId, userId is null ? CartService.NewGuestToken() : null);
        await new CartService(db, Options.Create(Store)).AddAsync(owner, new AddToCartRequest(number, null, quantity), "en");
        var name = pickup ? "حضوری" : "پست";
        var method = await db.ShippingMethods.Where(m => m.Name == name).Select(m => (Guid?)m.Id).FirstOrDefaultAsync()
            ?? (await new ShippingMethodService(db, Options.Create(Store)).CreateAsync(
                new SaveShippingMethodRequest([new LocalizedText("fa", name, null, null)], pickup, pickup ? 0 : 5, 0, null, [], null, null, true, 0))).Value.Id;
        var checkout = Kit.Checkout(db);
        var total = (await checkout.QuoteAsync(owner, new CheckoutQuoteRequest("IR", method, code), "en")).Totals.Total;
        var address = pickup ? null : new AddressInput(null, "Sara", "09121234567", "IR", null, "Tehran", "Valiasr 10", null, null, false);
        var request = new PlaceOrderRequest(method, null, address, false, "sara@example.com", pickup ? "Sara" : null, pickup ? "09121234567" : null, code, null, total);
        return (await checkout.PlaceAsync(owner, request, "en")).Value;
    }

    private async Task<(int Stock, int Reserved)> PhoneStockAsync()
    {
        await using var db = Database.CreateContext();
        return await db.Products.Where(p => p.SKU == Phone).Select(p => new ValueTuple<int, int>(p.Stock, p.Reserved)).SingleAsync();
    }

    [Fact]
    public async Task A_paid_order_sells_its_stock_ships_with_tracking_and_completes()
    {
        await SeedDemoAsync();
        var (stockBefore, _) = await PhoneStockAsync();
        var order = await PlaceAsync(2);
        Kit.StaffUserId = "staff-1";
        Kit.SmsEnabled = true;
        await using var db = Database.CreateContext();
        var lifecycle = Kit.Lifecycle(db);

        // Not paid yet: it cannot ship or complete.
        Assert.Equal(OrderErrors.StatusInvalid, (await lifecycle.ShipAsync(order.Number, new ShipOrderRequest("Post", "RR123"))).Error!.Code);
        Assert.Equal(OrderErrors.StatusInvalid, (await lifecycle.CompleteAsync(order.Number, new OrderActionRequest(null))).Error!.Code);
        Assert.Equal(OrderErrors.NotFound, (await lifecycle.MarkPaidAsync(999_999, new OrderActionRequest(null))).Error!.Code);

        Database.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(OrderStatus.Paid, (await lifecycle.MarkPaidAsync(order.Number, new OrderActionRequest("Bank transfer 4711"))).Value);
        Assert.Equal((stockBefore - 2, 0), await PhoneStockAsync());
        Assert.Equal(OrderErrors.StatusInvalid, (await lifecycle.MarkPaidAsync(order.Number, new OrderActionRequest(null))).Error!.Code);

        Database.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(OrderStatus.Shipped, (await lifecycle.ShipAsync(order.Number, new ShipOrderRequest(" Post ", " RR123 "))).Value);
        // Once it left, it cannot be canceled any more.
        Assert.Equal(OrderErrors.StatusInvalid, (await lifecycle.CancelAsync(order.Number, new OrderActionRequest(null))).Error!.Code);
        Database.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(OrderStatus.Completed, (await lifecycle.CompleteAsync(order.Number, new OrderActionRequest(null))).Value);
        Database.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True((await lifecycle.AddNoteAsync(order.Number, new OrderNoteRequest("Customer called"))).Value);

        var staff = (await Kit.Queries(db).GetForStaffAsync(order.Number))!;
        Assert.Equal(("Post", "RR123", OrderStatus.Completed), (staff.TrackingCarrier, staff.TrackingCode, staff.Status));
        Assert.Equal(
            [OrderEventKind.Placed, OrderEventKind.StatusChanged, OrderEventKind.Invoiced, OrderEventKind.Note, OrderEventKind.TrackingSet, OrderEventKind.StatusChanged, OrderEventKind.StatusChanged, OrderEventKind.Note],
            staff.History.Select(h => h.Kind));
        Assert.True(staff.History[1].ByStaff);
        // The payment reference is for staff only: the customer's view leaves it out.
        Assert.True(staff.History[3].IsInternal);
        var customerView = (await Kit.Queries(db).GetWithTokenAsync(order.Number, Uri.UnescapeDataString(order.Path[(order.Path.IndexOf("key=", StringComparison.Ordinal) + 4)..])))!;
        Assert.DoesNotContain(customerView.History, h => h.Text == "Bank transfer 4711");
        Assert.False(staff.Actions.CanCancel || staff.Actions.CanShip);

        // The customer is told: placed, paid, shipped by email; placed and shipped by SMS too (SMS was on from payment).
        Assert.Equal(["Your order 1001 at Test Shop", "Payment received for order 1001 at Test Shop", "Your order 1001 from Test Shop is on its way"],
            Kit.Emails.Sent.Select(m => m.Subject.Replace(Store.Name, "Test Shop", StringComparison.Ordinal)));
        Assert.Contains("RR123", Kit.Emails.Sent[2].TextBody, StringComparison.Ordinal);
        var sms = Assert.Single(Kit.Sms.OrderUpdates);
        Assert.Equal(("+989121234567", OrderNotice.Shipped, "RR123"), (sms.Phone, sms.Notice, sms.Data.TrackingCode));
    }

    [Fact]
    public async Task Customers_see_their_own_orders_and_guests_theirs_through_the_link()
    {
        await SeedDemoAsync();
        await AddUserAsync("u-1", "sara@example.com");
        var mine = await PlaceAsync(1, "u-1");
        var guest = await PlaceAsync(1);
        Kit.StaffUserId = "staff-1";
        await using var db = Database.CreateContext();
        await Kit.Lifecycle(db).AddNoteAsync(mine.Number, new OrderNoteRequest("Internal only"));
        var queries = Kit.Queries(db);

        var list = await queries.ListForCustomerAsync("u-1", 1);
        Assert.Equal([mine.Number], list.Items.Select(o => o.Number));
        Assert.Equal((1, 1), (list.Total, list.Items[0].ItemCount));
        var view = (await queries.GetForCustomerAsync("u-1", mine.Number))!;
        Assert.Equal((OrderStatus.Pending, "Tehran", (string?)null), (view.Status, view.Address!.City, view.CustomerId));
        Assert.NotNull(view.PaymentDueUtc);
        Assert.DoesNotContain(view.History, h => h.IsInternal);
        Assert.Null(await queries.GetForCustomerAsync("u-2", mine.Number));
        Assert.Null(await queries.GetForCustomerAsync("u-1", guest.Number));

        var key = Uri.UnescapeDataString(guest.Path[(guest.Path.IndexOf("key=", StringComparison.Ordinal) + 4)..]);
        Assert.Equal(guest.Number, (await queries.GetWithTokenAsync(guest.Number, key))!.Number);
        Assert.Null(await queries.GetWithTokenAsync(mine.Number, key));
        Assert.Null(await queries.GetWithTokenAsync(guest.Number, "forged"));

        // Staff see everything, by status and search.
        Assert.Equal(2, (await queries.ListAsync(new OrderListQuery(OrderStatus.Pending, null))).Total);
        Assert.Equal([mine.Number], (await queries.ListAsync(new OrderListQuery(null, mine.Number.ToString(System.Globalization.CultureInfo.InvariantCulture)))).Items.Select(o => o.Number));
        Assert.Equal(2, (await queries.ListAsync(new OrderListQuery(null, "SARA@"))).Total);
        Assert.Equal(2, (await queries.ListAsync(new OrderListQuery(null, "912 123 4567"))).Total);
        Assert.Equal(0, (await queries.ListAsync(new OrderListQuery(OrderStatus.Shipped, null))).Total);
        Assert.Equal("u-1", (await queries.GetForStaffAsync(mine.Number))!.CustomerId);
        Assert.Contains((await queries.GetForStaffAsync(mine.Number))!.History, h => h.IsInternal && h.Text == "Internal only");
    }

    [Fact]
    public async Task Canceling_gives_back_the_stock_and_the_discount_code()
    {
        await SeedDemoAsync();
        await using (var setup = Database.CreateContext())
            await new DiscountCodeService(setup, Database.Clock).CreateAsync(new SaveDiscountCodeRequest("TEN", DiscountKind.Percent, 10, null, null, null, null, 1, false, true, null));
        var order = await PlaceAsync(2, code: "TEN");
        Assert.Equal(2, (await PhoneStockAsync()).Reserved);
        await using var db = Database.CreateContext();
        Assert.Equal(1, await db.DiscountCodes.Select(d => d.UsedCount).SingleAsync());

        Assert.Equal(OrderStatus.Canceled, (await Kit.Lifecycle(db).CancelAsync(order.Number, new OrderActionRequest("Out of stock at the supplier"))).Value);
        Assert.Equal(0, (await PhoneStockAsync()).Reserved);
        Assert.Equal(0, await db.DiscountCodes.Select(d => d.UsedCount).SingleAsync());
        var view = (await Kit.Queries(db).GetForStaffAsync(order.Number))!;
        Assert.Contains(view.History, h => h.Kind == OrderEventKind.Note && h.Text == "Out of stock at the supplier" && !h.IsInternal);
        Assert.Equal("Order 1001 at Test Shop is canceled", Kit.Emails.Sent[^1].Subject.Replace(Store.Name, "Test Shop", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Unpaid_orders_are_canceled_when_their_time_runs_out()
    {
        await SeedDemoAsync();
        var order = await PlaceAsync(1);
        await using var db = Database.CreateContext();
        Assert.Equal(0, await Kit.Lifecycle(db).CancelExpiredAsync());

        Database.Clock.Advance(TimeSpan.FromMinutes(16));
        Assert.Equal(1, await Kit.Lifecycle(db).CancelExpiredAsync());
        Assert.Equal(0, await Kit.Lifecycle(db).CancelExpiredAsync());
        Assert.Equal(0, (await PhoneStockAsync()).Reserved);
        var view = (await Kit.Queries(db).GetForStaffAsync(order.Number))!;
        Assert.Equal(OrderStatus.Canceled, view.Status);
        Assert.Null(view.PaymentDueUtc);
        Assert.Contains(view.History, h => h.Kind == OrderEventKind.PaymentExpired && !h.ByStaff);
        Assert.Contains("payment time", Kit.Emails.Sent[^1].Subject, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_late_payment_holds_the_stock_again_or_is_refused_when_it_is_gone()
    {
        await SeedDemoAsync();
        var first = await PlaceAsync(1);
        var second = await PlaceAsync(1);
        // Both holds ran out, the expiry jobs have not run yet.
        Database.Clock.Advance(TimeSpan.FromMinutes(20));
        var (stock, _) = await PhoneStockAsync();
        await using var db = Database.CreateContext();
        var lifecycle = Kit.Lifecycle(db);

        Assert.Equal(OrderStatus.Paid, (await lifecycle.MarkPaidAsync(first.Number, new OrderActionRequest(null))).Value);
        Assert.Equal((stock - 1, 1), await PhoneStockAsync());

        // Meanwhile others hold everything that is left: the second payment cannot be booked.
        await db.Products.Where(p => p.SKU == Phone).ExecuteUpdateAsync(s => s.SetProperty(p => p.Reserved, p => p.Stock));
        await db.StockReservations.Where(r => r.Reference == OrderLifecycleService.ReservationReference(second.Number))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, StockReservationStatus.Expired));
        Assert.Equal(OrderErrors.NotAvailable, (await lifecycle.MarkPaidAsync(second.Number, new OrderActionRequest(null))).Error!.Code);
        Assert.Equal(OrderStatus.Pending, (await Kit.Queries(db).GetForStaffAsync(second.Number))!.Status);
    }

    [Fact]
    public async Task Pickup_orders_complete_without_shipping()
    {
        await SeedDemoAsync();
        var order = await PlaceAsync(1, pickup: true);
        await using var db = Database.CreateContext();
        var lifecycle = Kit.Lifecycle(db);
        await lifecycle.MarkPaidAsync(order.Number, new OrderActionRequest(null));
        Assert.Equal(OrderErrors.StatusInvalid, (await lifecycle.ShipAsync(order.Number, new ShipOrderRequest("Post", "X"))).Error!.Code);
        Assert.Equal(OrderStatus.Completed, (await lifecycle.CompleteAsync(order.Number, new OrderActionRequest(null))).Value);
        Assert.Null((await Kit.Queries(db).GetForStaffAsync(order.Number))!.Address);
    }

    [Fact]
    public async Task The_maintenance_job_cancels_expired_orders_and_deletes_abandoned_guest_carts()
    {
        await SeedDemoAsync();
        var order = await PlaceAsync(1);
        await AddUserAsync("u-1", "sara@example.com");
        var number = await NumberOfAsync(Phone);
        await using (var db = Database.CreateContext())
        {
            var carts = new CartService(db, Options.Create(Store));
            await carts.AddAsync(new CartOwner(null, CartService.NewGuestToken()), new AddToCartRequest(number, null, 1), "en");
            await carts.AddAsync(new CartOwner("u-1", null), new AddToCartRequest(number, null, 1), "en");
        }
        var services = new ServiceCollection();
        services.AddScoped(_ => Database.CreateContext());
        services.AddScoped(sp => Kit.Lifecycle(sp.GetRequiredService<AppDbContext>()));
        services.AddScoped(sp => new CartService(sp.GetRequiredService<AppDbContext>(), Options.Create(Store)));
        await using var provider = services.BuildServiceProvider();
        var worker = new SalesMaintenanceWorker(provider.GetRequiredService<IServiceScopeFactory>(), Database.Clock, NullLogger<SalesMaintenanceWorker>.Instance);

        Database.Clock.Advance(TimeSpan.FromDays(CartService.AbandonedGuestCartDays + 1));
        await worker.RunOnceAsync(CancellationToken.None);

        await using var check = Database.CreateContext();
        Assert.Equal(OrderStatus.Canceled, await check.Orders.Where(o => o.Number == order.Number).Select(o => o.Status).SingleAsync());
        Assert.Equal(["u-1"], await check.Carts.Select(c => c.CustomerId).ToListAsync());

        // Without the services the run logs and returns.
        await using var empty = new ServiceCollection().BuildServiceProvider();
        await new SalesMaintenanceWorker(empty.GetRequiredService<IServiceScopeFactory>(), Database.Clock, NullLogger<SalesMaintenanceWorker>.Instance).RunOnceAsync(CancellationToken.None);
    }
}

public sealed class OrderPartsTests
{
    private static readonly OrderMessageData Data = new(1001, "1,234 IRR", "Post", "RR123", new Uri("http://shop.test/fa/order/1001?key=abc"));

    [Fact]
    public void Messages_are_in_the_order_language_with_its_link()
    {
        var fa = OrderMessages.Email(OrderNotice.Shipped, "fa", "a@example.com", "فروشگاه", Data);
        Assert.Contains("dir=\"rtl\"", fa.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("RR123", fa.TextBody, StringComparison.Ordinal);
        Assert.Contains("http://shop.test/fa/order/1001?key=abc", fa.TextBody, StringComparison.Ordinal);
        var unknown = OrderMessages.Email(OrderNotice.Placed, "xx", "a@example.com", "Shop", Data);
        Assert.Equal("Your order 1001 at Shop", unknown.Subject);
        Assert.Equal("Shop: order 1001 has shipped. Tracking code: RR123", OrderMessages.SmsText(OrderNotice.Shipped, "en", "Shop", Data));
        Assert.True(OrderMessages.HasSms(OrderNotice.Placed));
        Assert.False(OrderMessages.HasSms(OrderNotice.Paid));
        Assert.Equal("1,047,900 IRR", OrderNotifier.FormatTotal(1_047_900m, "IRR", "en"));
        Assert.Equal("12.50 EUR", OrderNotifier.FormatTotal(12.5m, "EUR", "en"));
    }

    [Fact]
    public void Transitions_follow_the_order_life()
    {
        Assert.Equal(new OrderActions(true, false, false, true), OrderTransitions.For(OrderStatus.Pending, false));
        Assert.Equal(new OrderActions(false, true, false, true), OrderTransitions.For(OrderStatus.Paid, false));
        Assert.Equal(new OrderActions(false, false, true, true), OrderTransitions.For(OrderStatus.Paid, true));
        Assert.Equal(new OrderActions(false, false, true, false), OrderTransitions.For(OrderStatus.Shipped, false));
        Assert.Equal(new OrderActions(false, false, false, false), OrderTransitions.For(OrderStatus.Canceled, false));
    }

    [Fact]
    public void Order_links_open_only_their_own_order()
    {
        var links = new DataProtectionOrderLinks(new EphemeralDataProtectionProvider());
        var id = Guid.NewGuid();
        var token = links.TokenFor(id);
        Assert.True(links.IsValid(id, token));
        Assert.False(links.IsValid(Guid.NewGuid(), token));
        Assert.False(links.IsValid(id, "nonsense"));
        Assert.False(links.IsValid(id, ""));
    }

    [Fact]
    public void Requests_are_validated()
    {
        Assert.False(new ShipOrderRequestValidator().Validate(new ShipOrderRequest("", "X")).IsValid);
        Assert.False(new ShipOrderRequestValidator().Validate(new ShipOrderRequest("Post", new string('1', 101))).IsValid);
        Assert.True(new ShipOrderRequestValidator().Validate(new ShipOrderRequest("Post", "RR123")).IsValid);
        Assert.False(new OrderNoteRequestValidator().Validate(new OrderNoteRequest("")).IsValid);
        Assert.False(new OrderActionRequestValidator().Validate(new OrderActionRequest(new string('x', 1001))).IsValid);
        Assert.True(new OrderActionRequestValidator().Validate(new OrderActionRequest(null)).IsValid);
    }
}
