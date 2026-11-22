using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OnlineShop.Application.Catalog;
using OnlineShop.Application.Common;
using OnlineShop.Application.Inventory;
using OnlineShop.Domain.Inventory;
using OnlineShop.Infrastructure.Inventory;
using OnlineShop.Infrastructure.Persistence;
using OnlineShop.UnitTests.TestSupport;

namespace OnlineShop.UnitTests.Application;

public sealed class StockReservationServiceTests : InventoryTestBase
{
    private StockReservationService Service(AppDbContext db) =>
        new(db, Ledger(db), Database.Clock, Options.Create(new InventoryOptions { ReservationMinutes = 15 }), Options.Create(Store));

    private async Task<(int Stock, int Reserved)> CountsAsync(string sku)
    {
        await using var db = Database.CreateContext();
        var variant = await db.ProductVariants.Where(v => v.Sku == sku).Select(v => new { v.Stock, v.Reserved }).SingleOrDefaultAsync();
        if (variant is not null)
            return (variant.Stock, variant.Reserved);
        var product = await db.Products.Where(p => p.SKU == sku).Select(p => new { p.Stock, p.Reserved }).SingleAsync();
        return (product.Stock, product.Reserved);
    }

    /// <summary>The cached counts equal the active reservations, on both the product and the variant.</summary>
    private async Task AssertReservedAgreesAsync()
    {
        await using var db = Database.CreateContext();
        var active = await db.StockReservations.Where(r => r.Status == StockReservationStatus.Active).ToListAsync();
        foreach (var product in await db.Products.Select(p => new { p.Id, p.Reserved }).ToListAsync())
            Assert.Equal(active.Where(r => r.ProductId == product.Id).Sum(r => r.Quantity), product.Reserved);
        foreach (var variant in await db.ProductVariants.Select(v => new { v.Id, v.Reserved }).ToListAsync())
            Assert.Equal(active.Where(r => r.VariantId == variant.Id).Sum(r => r.Quantity), variant.Reserved);
    }

    [Fact]
    public async Task Reserving_holds_available_units_and_never_more_than_there_are()
    {
        await SeedDemoAsync();
        var shoes = await ItemAsync(Shoes);
        var hoodie = await ItemAsync(HoodieBeigeS);
        await using var db = Database.CreateContext();
        var service = Service(db);

        var first = (await service.ReserveAsync("order:1", [new(shoes, 30), new(hoodie, 3), new(hoodie, 2)])).Value;
        Assert.Equal([30, 5], first.Select(r => r.Quantity));
        Assert.All(first, r => Assert.Equal(Database.Clock.GetUtcNow().UtcDateTime.AddMinutes(15), r.ExpiresUtc));
        Assert.Equal((33, 30), await CountsAsync(Shoes));
        Assert.Equal((8, 5), await CountsAsync(HoodieBeigeS));
        Assert.Equal(5, (await db.Products.AsNoTracking().SingleAsync(p => p.SKU == "DEMO-FSH-003")).Reserved);

        // 3 shoes are left: a checkout asking for 4 gets nothing at all, its hoodie included.
        var second = await service.ReserveAsync("order:2", [new(hoodie, 1), new(shoes, 4)]);
        Assert.Equal(ReservationErrors.NotAvailable, second.Error!.Code);
        Assert.Equal((8, 5), await CountsAsync(HoodieBeigeS));
        Assert.True((await service.ReserveAsync("order:2", [new(shoes, 3)], TimeSpan.FromMinutes(1))).IsSuccess);

        Assert.Equal(ReservationErrors.ReferenceRequired, (await service.ReserveAsync(" ", [new(shoes, 1)])).Error!.Code);
        Assert.Equal(InventoryErrors.QuantityInvalid, (await service.ReserveAsync("x", [new(shoes, 0)])).Error!.Code);
        Assert.Equal(InventoryErrors.QuantityInvalid, (await service.ReserveAsync("x", [])).Error!.Code);
        await AssertReservedAgreesAsync();
    }

    [Fact]
    public async Task Releasing_and_expiry_put_the_units_back_on_sale_once()
    {
        await SeedDemoAsync();
        var shoes = await ItemAsync(Shoes);
        await using var db = Database.CreateContext();
        var service = Service(db);
        await service.ReserveAsync("order:1", [new(shoes, 10)]);
        await service.ReserveAsync("order:2", [new(shoes, 5)], TimeSpan.FromMinutes(1));

        Assert.Equal(1, await service.ReleaseAsync("order:1"));
        Assert.Equal(0, await service.ReleaseAsync("order:1"));
        Assert.Equal((33, 5), await CountsAsync(Shoes));

        Assert.Equal(0, await service.ReleaseExpiredAsync());
        Database.Clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(1, await service.ReleaseExpiredAsync());
        Assert.Equal((33, 0), await CountsAsync(Shoes));
        Assert.Equal(StockReservationStatus.Expired, (await db.StockReservations.AsNoTracking().SingleAsync(r => r.Reference == "order:2")).Status);
        // An expired checkout cannot be turned into a sale: it has to reserve again.
        Assert.Equal(ReservationErrors.NothingReserved, (await service.CommitAsync("order:2", "ORD-2")).Error!.Code);
        await AssertReservedAgreesAsync();
    }

    [Fact]
    public async Task Committing_sells_the_units_through_the_ledger_from_the_default_warehouse_first()
    {
        await SeedDemoAsync();
        var shoes = await ItemAsync(Shoes);
        var shop = await AddWarehouseAsync("Shop");
        await using (var db = Database.CreateContext())
            await Inventory(db).TransferAsync(new(Shoes, await Ledger(db).DefaultWarehouseIdAsync(), shop, 30, null, null), "en");

        await using (var db = Database.CreateContext())
        {
            var service = Service(db);
            await service.ReserveAsync("order:7", [new(shoes, 5)]);
            Assert.Equal(1, (await service.CommitAsync("order:7", "ORD-1007")).Value);
            Assert.Equal(ReservationErrors.NothingReserved, (await service.CommitAsync("order:7", "ORD-1007")).Error!.Code);
        }

        Assert.Equal((28, 0), await CountsAsync(Shoes));
        await using var check = Database.CreateContext();
        var sales = await check.StockMovements.Where(m => m.Kind == StockMovementKind.Sale).OrderBy(m => m.Quantity).ToListAsync();
        // The default warehouse had 3 left after the transfer; the other 2 come from the shop.
        Assert.Equal([(-3, "ORD-1007"), (-2, "ORD-1007")], sales.Select(m => (m.Quantity, m.Reference!)));
        Assert.Equal(StockReservationStatus.Committed, (await check.StockReservations.SingleAsync()).Status);
        await AssertLedgerAgreesAsync();
        await AssertReservedAgreesAsync();
    }

    [Fact]
    public async Task A_commit_that_finds_too_little_stock_changes_nothing()
    {
        await SeedDemoAsync();
        var shoes = await ItemAsync(Shoes);
        await using var db = Database.CreateContext();
        var service = Service(db);
        await service.ReserveAsync("order:9", [new(shoes, 30)]);
        // Staff wrote off 10 broken pairs after the reservation: 23 left for a checkout that holds 30.
        await Ledger(db).PostAsync(await Ledger(db).DefaultWarehouseIdAsync(), shoes, StockMovementKind.Issue, -10);

        Assert.Equal(InventoryErrors.Insufficient, (await service.CommitAsync("order:9", "ORD-9")).Error!.Code);
        Assert.Equal((23, 30), await CountsAsync(Shoes));
        Assert.Equal(StockReservationStatus.Active, (await db.StockReservations.AsNoTracking().SingleAsync()).Status);
        await AssertReservedAgreesAsync();
    }

    [Fact]
    public async Task The_shop_shows_only_unreserved_units_as_available()
    {
        await SeedDemoAsync();
        var number = await NumberOfAsync("DEMO-FSH-003");
        await using var db = Database.CreateContext();
        await Service(db).ReserveAsync("order:1", [new(await ItemAsync(HoodieBeigeS), 8)]);

        var detail = (await new ProductQueries(db).GetAsync(number, "en", "fa")).Value;
        Assert.False(detail.Variants.Single(v => v.Sku == HoodieBeigeS).InStock);
        Assert.Equal(52, detail.Stock);

        var row = (await Inventory(db).StockAsync("FSH-003", false, 1, "en")).Items.Single(r => r.Sku == HoodieBeigeS);
        Assert.Equal((8, 8, 0), (row.Total, row.Reserved, row.Available));
    }

    [Fact]
    public async Task Staff_see_active_reservations_with_item_names()
    {
        await SeedDemoAsync();
        await using var db = Database.CreateContext();
        var service = Service(db);
        await service.ReserveAsync("order:1", [new(await ItemAsync(HoodieBeigeS), 2)]);
        await service.ReserveAsync("order:2", [new(await ItemAsync(Shoes), 1)]);
        await service.ReleaseAsync("order:2");

        var active = Assert.Single(await service.ListActiveAsync("en"));
        Assert.Equal((HoodieBeigeS, 2, "order:1"), (active.Sku, active.Quantity, active.Reference));
        Assert.NotNull(active.VariantLabel);
    }

    [Fact]
    public void The_release_validator_needs_a_reference()
    {
        var validator = new ReleaseReservationsRequestValidator();
        Assert.True(validator.Validate(new ReleaseReservationsRequest("order:1")).IsValid);
        Assert.Equal([ReservationErrors.ReferenceRequired], validator.Validate(new ReleaseReservationsRequest("")).Errors.Select(e => e.ErrorCode));
    }
}

public sealed class ReservationExpiryWorkerTests : InventoryTestBase
{
    [Fact]
    public async Task Releases_expired_reservations_and_survives_failures()
    {
        await SeedDemoAsync();
        var services = new ServiceCollection();
        services.AddScoped(_ => Database.CreateContext());
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddSingleton<TimeProvider>(Database.Clock);
        services.AddSingleton<ICurrentUser>(new FixedCurrentUser(null));
        services.AddSingleton(Options.Create(Store));
        services.AddSingleton(Options.Create(new InventoryOptions()));
        services.AddScoped<StockLedger>();
        services.AddScoped<StockReservationService>();
        await using var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<StockReservationService>().ReserveAsync("order:1", [new(await ItemAsync(Shoes), 3)], TimeSpan.FromMinutes(1));
        Database.Clock.Advance(TimeSpan.FromMinutes(2));

        var worker = new ReservationExpiryWorker(provider.GetRequiredService<IServiceScopeFactory>(), Options.Create(new InventoryOptions()), Database.Clock, NullLogger<ReservationExpiryWorker>.Instance);
        await worker.RunOnceAsync(CancellationToken.None);

        await using var db = Database.CreateContext();
        Assert.Equal(StockReservationStatus.Expired, (await db.StockReservations.SingleAsync()).Status);
        Assert.Equal(0, (await db.Products.SingleAsync(p => p.SKU == Shoes)).Reserved);

        // Without the service the run logs and returns.
        await using var empty = new ServiceCollection().BuildServiceProvider();
        await new ReservationExpiryWorker(empty.GetRequiredService<IServiceScopeFactory>(), Options.Create(new InventoryOptions()), Database.Clock, NullLogger<ReservationExpiryWorker>.Instance)
            .RunOnceAsync(CancellationToken.None);
    }
}
