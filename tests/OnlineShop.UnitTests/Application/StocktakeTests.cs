using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnlineShop.Application.Catalog.Transfer;
using OnlineShop.Application.Inventory;
using OnlineShop.Domain.Inventory;
using OnlineShop.Infrastructure.Persistence;
using OnlineShop.Infrastructure.Transfer;
using OnlineShop.UnitTests.TestSupport;

namespace OnlineShop.UnitTests.Application;

public sealed class StocktakeServiceTests : InventoryTestBase
{
    private static readonly TableFiles Files = new([new CsvTableFile(), new XlsxTableFile()]);
    private readonly FakeStaffIdentity _staff = new();

    private StocktakeService Service(AppDbContext db, string? userId = null) =>
        new(db, Ledger(db, userId), new FixedCurrentUser(userId), Database.Clock, Files, _staff, Options.Create(Store));

    private static MemoryStream Csv(string text) => new(Encoding.UTF8.GetBytes(text));

    private async Task<(Guid Main, int Number)> OpenMainAsync()
    {
        await SeedDemoAsync();
        await using var db = Database.CreateContext();
        var main = await Ledger(db).DefaultWarehouseIdAsync();
        return (main, (await Service(db).OpenAsync(new OpenStocktakeRequest(main, " yearly "))).Value.Number);
    }

    private async Task<StocktakeView> ViewAsync(int number, StocktakeLineFilter filter = StocktakeLineFilter.All, string? search = null)
    {
        await using var db = Database.CreateContext();
        return (await Service(db).GetAsync(number, filter, search, 1, "en")).Value;
    }

    [Fact]
    public async Task Opening_lists_every_item_with_its_stock_and_allows_one_open_count_per_warehouse()
    {
        var (main, number) = await OpenMainAsync();
        await using var db = Database.CreateContext();
        var service = Service(db);

        var view = (await service.GetAsync(number, StocktakeLineFilter.All, null, 1, "en")).Value;
        var items = await db.Products.CountAsync(p => !p.Variants.Any()) + await db.ProductVariants.CountAsync();
        Assert.Equal((1, StocktakeStatus.Open, "yearly", items, 0), (view.Number, view.Status, view.Note, view.Totals.Lines, view.Totals.Counted));
        var shoes = (await service.GetAsync(number, StocktakeLineFilter.All, Shoes, 1, "en")).Value.Lines.Single();
        Assert.Equal((33, 33, (int?)null), (shoes.Snapshot, shoes.Expected, shoes.Counted));
        // The sold-out hoodie variant is listed too: it may still be found on a shelf.
        Assert.Equal(0, (await service.GetAsync(number, StocktakeLineFilter.All, "DEMO-FSH-003-GR-XL", 1, "en")).Value.Lines.Single().Snapshot);

        Assert.Equal(StocktakeErrors.AlreadyOpen, (await service.OpenAsync(new(main, null))).Error!.Code);
        Assert.Equal(InventoryErrors.WarehouseNotFound, (await service.OpenAsync(new(Guid.NewGuid(), null))).Error!.Code);
        var spare = await AddWarehouseAsync("Spare");
        await Warehouses(db).UpdateAsync(spare, new("spare", "Spare", null, false, false));
        Assert.Equal(InventoryErrors.WarehouseInactive, (await service.OpenAsync(new(spare, null))).Error!.Code);
        Assert.Equal(2, (await service.OpenAsync(new(await AddWarehouseAsync("Shop"), null))).Value.Number);
        Assert.Equal([2, 1], (await service.ListAsync()).Select(s => s.Number));
    }

    [Fact]
    public async Task Counts_are_set_or_added_by_sku_and_show_their_difference()
    {
        var (_, number) = await OpenMainAsync();
        await using var db = Database.CreateContext();
        var service = Service(db);

        var lines = (await service.CountAsync(number, new([new(Shoes, 30), new(HoodieBeigeS, 8)]), "en")).Value;
        Assert.Equal([(Shoes, -3), (HoodieBeigeS, 0)], lines.OrderBy(l => l.Sku, StringComparer.Ordinal).Select(l => (l.Sku, l.Difference!.Value)));
        Assert.Equal(32, (await service.CountAsync(number, new([new(Shoes, 2)], Add: true), "en")).Value.Single().Counted);

        Assert.Equal(InventoryErrors.ItemHasVariants, (await service.CountAsync(number, new([new("DEMO-FSH-003", 1)]), "en")).Error!.Code);
        Assert.Equal(InventoryErrors.ItemNotFound, (await service.CountAsync(number, new([new("NOPE", 1)]), "en")).Error!.Code);
        Assert.Equal(StocktakeErrors.NotFound, (await service.CountAsync(99, new([new(Shoes, 1)]), "en")).Error!.Code);

        var view = await ViewAsync(number, StocktakeLineFilter.Differences);
        Assert.Equal((2, 1, 0, 1), (view.Totals.Counted, view.Totals.WithDifference, view.Totals.UnitsOver, view.Totals.UnitsShort));
        Assert.Equal(Shoes, view.Lines.Single().Sku);
        Assert.Equal(2, (await ViewAsync(number, StocktakeLineFilter.Counted)).Total);
        Assert.Equal(view.Totals.Lines - 2, (await ViewAsync(number, StocktakeLineFilter.Uncounted)).Total);
    }

    [Fact]
    public async Task Sales_and_deliveries_during_the_count_are_not_mistaken_for_differences()
    {
        var (main, number) = await OpenMainAsync();
        var shoes = await ItemAsync(Shoes);
        var hoodie = await ItemAsync(HoodieBeigeS);

        await using (var db = Database.CreateContext())
        {
            // Before the hoodie's count a delivery of 5 arrives (8 → 13); the count finds 13: no difference.
            Database.Clock.Advance(TimeSpan.FromMinutes(1));
            await Ledger(db).PostAsync(main, hoodie, StockMovementKind.Receipt, 5);
            Database.Clock.Advance(TimeSpan.FromMinutes(1));
            await Service(db).CountAsync(number, new([new(Shoes, 30), new(HoodieBeigeS, 13)]), "en");
            // After the count two shoes are sold (33 → 31): the count still means 3 missing.
            Database.Clock.Advance(TimeSpan.FromMinutes(1));
            await Ledger(db).PostAsync(main, shoes, StockMovementKind.Issue, -2);
        }

        var view = await ViewAsync(number, StocktakeLineFilter.Counted);
        Assert.Equal([(33, -3), (13, 0)], view.Lines.OrderBy(l => l.Sku, StringComparer.Ordinal).Select(l => (l.Expected, l.Difference!.Value)));

        await using (var db = Database.CreateContext())
        {
            Database.Clock.Advance(TimeSpan.FromMinutes(1));
            Assert.Equal(StocktakeStatus.Posted, (await Service(db).PostAsync(number)).Value.Status);
        }
        await using var check = Database.CreateContext();
        Assert.Equal(28, (await check.Products.SingleAsync(p => p.SKU == Shoes)).Stock);
        var line = await check.StockMovements.SingleAsync(m => m.Reference == "ST-1");
        Assert.Equal((StockMovementKind.Adjustment, -3, shoes.ItemId), (line.Kind, line.Quantity, line.ItemId));
        var after = await ViewAsync(number, StocktakeLineFilter.Counted);
        Assert.Equal([(33, -3), (13, 0)], after.Lines.OrderBy(l => l.Sku, StringComparer.Ordinal).Select(l => (l.Expected, l.Difference!.Value)));
        await AssertLedgerAgreesAsync();
    }

    [Fact]
    public async Task Uncounted_items_stay_and_a_closed_stocktake_cannot_change()
    {
        var (main, number) = await OpenMainAsync();
        await using var db = Database.CreateContext();
        var service = Service(db);

        Assert.Equal(StocktakeErrors.NothingCounted, (await service.PostAsync(number)).Error!.Code);
        await service.CountAsync(number, new([new(Shoes, 40)]), "en");
        Assert.Equal(StocktakeStatus.Posted, (await service.PostAsync(number)).Value.Status);
        Assert.Equal(StocktakeErrors.NotOpen, (await service.PostAsync(number)).Error!.Code);
        Assert.Equal(StocktakeErrors.NotOpen, (await service.CountAsync(number, new([new(Shoes, 1)]), "en")).Error!.Code);
        Assert.Equal(StocktakeErrors.NotOpen, (await service.CancelAsync(number)).Error!.Code);
        Assert.Equal(1, await db.StockMovements.CountAsync(m => m.Reference == "ST-1"));
        Assert.Equal(40, (await db.Products.AsNoTracking().SingleAsync(p => p.SKU == Shoes)).Stock);

        // A new count of the same warehouse can start, and cancelling it changes nothing.
        var second = (await service.OpenAsync(new(main, null))).Value.Number;
        await service.CountAsync(second, new([new(Shoes, 1)]), "en");
        Assert.Equal(StocktakeStatus.Cancelled, (await service.CancelAsync(second)).Value.Status);
        Assert.Equal(40, (await db.Products.AsNoTracking().SingleAsync(p => p.SKU == Shoes)).Stock);
        Assert.Equal(StocktakeErrors.NotFound, (await service.CancelAsync(99)).Error!.Code);
        await AssertLedgerAgreesAsync();
    }

    [Fact]
    public async Task The_count_sheet_goes_out_blind_and_comes_back_with_counts()
    {
        var (_, number) = await OpenMainAsync();
        await using var db = Database.CreateContext();
        var service = Service(db);

        var sheet = (await service.SheetAsync(number, "csv", "en")).Value;
        Assert.Equal("ST-1.csv", sheet.FileName);
        var table = new CsvTableFile().Read(new MemoryStream(sheet.Content), 5000);
        Assert.Equal(["sku", "name", "counted"], table.Headers);
        Assert.Contains(table.Rows, r => r[0] == HoodieBeigeS && r[1].Contains(" – ", StringComparison.Ordinal) && r[2] == "");
        Assert.Equal(TransferErrors.FormatUnknown, (await service.SheetAsync(number, "pdf", "en")).Error!.Code);

        var bad = Csv("sku,counted\nDEMO-FSH-001,x\nNOPE,1\nDEMO-FSH-001,2\n");
        Assert.Equal(
            [new ImportProblem(2, "counted", TransferErrors.NumberInvalid), new ImportProblem(3, "sku", TransferErrors.SkuNotFound), new ImportProblem(4, "sku", TransferErrors.SkuRepeated)],
            (await service.ImportCountsAsync(number, bad, bad.Length)).Value.Problems);
        Assert.Equal(0, (await ViewAsync(number)).Totals.Counted);
        var missing = Csv("sku,stock\nA,1\n");
        Assert.Equal(TransferErrors.ColumnMissing, (await service.ImportCountsAsync(number, missing, missing.Length)).Value.Problems.Single().Code);

        var good = Csv("sku,name,counted\nDEMO-FSH-001,shoes,۳۱\nDEMO-FSH-003-BG-S,,\n");
        var report = (await service.ImportCountsAsync(number, good, good.Length)).Value;
        Assert.Equal((true, 1), (report.Applied, report.Updated));
        Assert.Equal(-2, (await ViewAsync(number, StocktakeLineFilter.Counted)).Lines.Single().Difference);
    }

    [Fact]
    public async Task Names_who_opened_and_closed_it()
    {
        await SeedDemoAsync();
        _staff.Add("u-7", email: "counter@shop.test", phone: null, "Warehouse");
        await AddUserAsync("u-7");
        await using var db = Database.CreateContext();
        var main = await Ledger(db).DefaultWarehouseIdAsync();
        var number = (await Service(db, "u-7").OpenAsync(new(main, null))).Value.Number;
        await Service(db, "u-7").CancelAsync(number);

        var view = await ViewAsync(number);
        Assert.Equal(("counter@shop.test", "counter@shop.test", StocktakeStatus.Cancelled), (view.OpenedBy, view.ClosedBy, view.Status));
        Assert.NotNull(view.ClosedUtc);
    }

    [Fact]
    public void The_request_validators_check_counts_and_notes()
    {
        var count = new CountRequestValidator();
        Assert.True(count.Validate(new CountRequest([new("A", 0)])).IsValid);
        Assert.Equal([InventoryErrors.QuantityInvalid], count.Validate(new CountRequest([new("A", -1)])).Errors.Select(e => e.ErrorCode));
        Assert.Equal([InventoryErrors.ItemNotFound], count.Validate(new CountRequest([new(" ", 1)])).Errors.Select(e => e.ErrorCode));
        Assert.False(count.Validate(new CountRequest([])).IsValid);
        Assert.False(count.Validate(new CountRequest(Enumerable.Repeat(new CountInput("A", 1), CountRequestValidator.MaxCounts + 1).ToList())).IsValid);

        var open = new OpenStocktakeRequestValidator();
        Assert.True(open.Validate(new OpenStocktakeRequest(Guid.NewGuid(), null)).IsValid);
        Assert.Equal([InventoryErrors.WarehouseNotFound], open.Validate(new OpenStocktakeRequest(Guid.Empty, null)).Errors.Select(e => e.ErrorCode));
        Assert.Equal("ST-12", StocktakeService.Reference(12));
    }
}

public sealed class ItemNamesTests : InventoryTestBase
{
    [Fact]
    public async Task Names_products_and_variants_in_the_language_with_option_labels()
    {
        await SeedDemoAsync();
        var shoes = await ItemAsync(Shoes);
        var hoodie = await ItemAsync(HoodieBeigeS);
        await using var db = Database.CreateContext();

        var names = await ItemNames.LoadAsync(db, [shoes, hoodie, new StockItem(Guid.NewGuid(), null)], "en");

        Assert.Equal(2, names.Count);
        Assert.Equal(("Ranna Men's Walking Shoes", (string?)null, Shoes), (names[shoes.ItemId].Name, names[shoes.ItemId].VariantLabel, names[shoes.ItemId].Sku));
        Assert.Equal(HoodieBeigeS, names[hoodie.ItemId].Sku);
        Assert.Contains(" / ", names[hoodie.ItemId].VariantLabel, StringComparison.Ordinal);
        Assert.NotEqual(names[hoodie.ItemId].Name, (await ItemNames.LoadAsync(db, [hoodie], "fa"))[hoodie.ItemId].Name);
    }
}
