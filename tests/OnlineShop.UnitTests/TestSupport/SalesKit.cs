using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OnlineShop.Application.Common;
using OnlineShop.Application.Customers;
using OnlineShop.Application.Inventory;
using OnlineShop.Application.Sales;
using OnlineShop.Infrastructure.Identity;
using OnlineShop.Infrastructure.Persistence;
using OnlineShop.Infrastructure.Sales;

namespace OnlineShop.UnitTests.TestSupport;

/// <summary>Checkout and order services over one test database, with captured emails and SMS.</summary>
internal sealed class SalesKit(SqliteTestDatabase database, StoreOptions store, FakeAccountIdentity? identity = null)
{
    public FakeAccountIdentity Identity { get; } = identity ?? new FakeAccountIdentity();
    public CapturingEmailSender Emails { get; } = new();
    public CapturingSmsSender Sms { get; } = new();
    public bool SmsEnabled { get; set; }
    public IOrderLinks Links { get; } = new DataProtectionOrderLinks(new EphemeralDataProtectionProvider());

    /// <summary>The staff member acting (null: the shop itself).</summary>
    public string? StaffUserId { get; set; }

    public OrderNotifier Notifier(AppDbContext db, StoreOptions? options = null) =>
        new(db, Emails, Sms, new FixedSmsCapability(SmsEnabled), Links, Options.Create(options ?? store), NullLogger<OrderNotifier>.Instance);

    public StockReservationService Reservations(AppDbContext db, StoreOptions? options = null) =>
        new(db, TestLedger.For(db, options ?? store), database.Clock, Options.Create(new InventoryOptions()), Options.Create(options ?? store));

    public CheckoutService Checkout(AppDbContext db, StoreOptions? options = null)
    {
        var o = Options.Create(options ?? store);
        var phones = new PhoneNumberNormalizer();
        return new CheckoutService(
            db, new CartService(db, o), new AddressService(db, phones, o), new ShippingMethodService(db, o),
            new DiscountCodeService(db, database.Clock), new TaxSettingsService(db), Reservations(db, options),
            new FakeAccountDataIdentity(Identity), phones, new FixedSmsCapability(SmsEnabled), Notifier(db, options), database.Clock, o);
    }

    public OrderLifecycleService Lifecycle(AppDbContext db) =>
        new(db, Reservations(db), new DiscountCodeService(db, database.Clock), TestLedger.For(db, store), new SalesSettingsService(db), Notifier(db),
            new FixedCurrentUser(StaffUserId), Options.Create(store), database.Clock);

    public ReturnService Returns(AppDbContext db) =>
        new(db, TestLedger.For(db, store), new SalesSettingsService(db), Notifier(db), new FixedCurrentUser(StaffUserId), database.Clock);

    public OrderQueries Queries(AppDbContext db) => new(db, Links, new SalesSettingsService(db), database.Clock);
}
