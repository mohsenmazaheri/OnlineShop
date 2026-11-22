using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnlineShop.Application.Inventory;
using OnlineShop.Domain.Inventory;
using OnlineShop.Infrastructure.Persistence;
using OnlineShop.UnitTests.TestSupport;

namespace OnlineShop.UnitTests.Application;

public sealed class AverageCostTests
{
    [Theory]
    [InlineData(null, 10, 5, 7.5, 7.5)]        // no average yet: the purchase price
    [InlineData(10.0, 0, 5, 7.5, 7.5)]         // nothing left to average with
    [InlineData(10.0, 10, 10, 20.0, 15.0)]     // half at 10, half at 20
    [InlineData(10.0, 3, 1, 11.0, 10.25)]
    [InlineData(1.0, 2, 1, 2.0, 1.3333)]       // four decimals
    public void Weighs_the_stock_before_with_the_new_purchase(double? average, int before, int quantity, double cost, double expected)
    {
        Assert.Equal((decimal)expected, StockLedger.AverageCost((decimal?)average, before, quantity, (decimal)cost));
    }
}

public sealed class SupplierServiceTests : InventoryTestBase
{
    private static SaveSupplierRequest Supplier(string name, bool active = true) => new($" {name} ", " Sara ", " 0912 ", null, null, " ", active);

    [Fact]
    public async Task Adds_changes_and_deletes_only_suppliers_without_receipts()
    {
        await using var db = Database.CreateContext();
        var service = new SupplierService(db);

        var rana = (await service.CreateAsync(Supplier("Rana Trading"))).Value;
        Assert.Equal(("Rana Trading", "Sara", "0912", (string?)null), (rana.Name, rana.ContactName, rana.Phone, rana.Note));
        var old = (await service.CreateAsync(Supplier("Old Co"))).Value;
        Assert.False((await service.UpdateAsync(old.Id, Supplier("Old Co", active: false))).Value.IsActive);
        Assert.Equal(["Rana Trading", "Old Co"], (await service.ListAsync()).Select(s => s.Name));

        Assert.True((await service.DeleteAsync(old.Id)).Value);
        Assert.Equal(PurchasingErrors.SupplierNotFound, (await service.DeleteAsync(old.Id)).Error!.Code);
        Assert.Equal(PurchasingErrors.SupplierNotFound, (await service.UpdateAsync(Guid.NewGuid(), Supplier("x"))).Error!.Code);
    }

    [Fact]
    public void The_validator_needs_a_name_and_a_valid_email()
    {
        var validator = new SaveSupplierRequestValidator();
        Assert.True(validator.Validate(new SaveSupplierRequest("Rana", null, null, "sales@rana.test", null, null, true)).IsValid);
        Assert.Equal([PurchasingErrors.SupplierNameRequired], validator.Validate(new SaveSupplierRequest(" ", null, null, null, null, null, true)).Errors.Select(e => e.ErrorCode));
        Assert.Equal([PurchasingErrors.EmailInvalid], validator.Validate(new SaveSupplierRequest("Rana", null, null, "not-an-email", null, null, true)).Errors.Select(e => e.ErrorCode));
    }
}

public sealed class PurchaseServiceTests : InventoryTestBase
{
    private PurchaseService Service(AppDbContext db, string? userId = null) =>
        new(db, Ledger(db, userId), new FixedCurrentUser(userId), Database.Clock, new FakeStaffIdentity(), Options.Create(Store));

    private async Task<(Guid Supplier, Guid Main)> PrepareAsync()
    {
        await SeedDemoAsync();
        await using var db = Database.CreateContext();
        var supplier = (await new SupplierService(db).CreateAsync(new("Rana Trading", null, null, null, null, null, true))).Value.Id;
        return (supplier, await Ledger(db).DefaultWarehouseIdAsync());
    }

    [Fact]
    public async Task A_draft_is_written_changed_and_totalled_on_the_server()
    {
        var (supplier, main) = await PrepareAsync();
        await using var db = Database.CreateContext();
        var service = Service(db);

        var draft = (await service.CreateAsync(new(supplier, main, " INV-77 ", null, [new(Shoes, 10, 20.5m), new(HoodieBeigeS, 3, 12.12345m)]), "en")).Value;
        Assert.Equal((1, PurchaseReceiptStatus.Draft, "INV-77", "EUR", 2, 13), (draft.Summary.Number, draft.Summary.Status, draft.Summary.SupplierReference, draft.Summary.Currency, draft.Summary.Lines, draft.Summary.Units));
        // 10 × 20.5 + 3 × 12.1235 (costs keep four decimals, totals two).
        Assert.Equal(241.37m, draft.Summary.Total);
        Assert.Equal([Shoes, HoodieBeigeS], draft.Lines.Select(l => l.Sku));

        var changed = (await service.UpdateAsync(1, new(supplier, main, null, "late", [new(HoodieBeigeS, 4, 10m)]), "en")).Value;
        Assert.Equal((40m, "late", 1), (changed.Summary.Total, changed.Note, changed.Lines.Count));
        Assert.Equal(PurchasingErrors.ItemRepeated, (await service.UpdateAsync(1, new(supplier, main, null, null, [new(Shoes, 1, 1m), new(" DEMO-FSH-001", 2, 1m)]), "en")).Error!.Code);
        Assert.Equal(InventoryErrors.ItemHasVariants, (await service.UpdateAsync(1, new(supplier, main, null, null, [new("DEMO-FSH-003", 1, 1m)]), "en")).Error!.Code);
        Assert.Equal(PurchasingErrors.SupplierNotFound, (await service.CreateAsync(new(Guid.NewGuid(), main, null, null, []), "en")).Error!.Code);
        Assert.Equal(InventoryErrors.WarehouseNotFound, (await service.CreateAsync(new(supplier, Guid.NewGuid(), null, null, []), "en")).Error!.Code);
        Assert.Equal(PurchasingErrors.ReceiptNotFound, (await service.GetAsync(9, "en")).Error!.Code);
        Assert.Single(await service.ListAsync(PurchaseReceiptStatus.Draft));
        Assert.Empty(await service.ListAsync(PurchaseReceiptStatus.Posted));
    }

    [Fact]
    public async Task Posting_brings_the_goods_in_with_their_cost_and_moves_the_average()
    {
        var (supplier, main) = await PrepareAsync();
        var shoes = await ItemAsync(Shoes);
        await using (var db = Database.CreateContext())
        {
            // The shoes had no cost yet: the first purchase sets it, the second averages with the 43 then in stock.
            var service = Service(db);
            await service.CreateAsync(new(supplier, main, "INV-1", null, [new(Shoes, 10, 20m)]), "en");
            Assert.Equal(PurchaseReceiptStatus.Posted, (await service.PostAsync(1, "en")).Value.Summary.Status);
            await service.CreateAsync(new(supplier, main, null, null, [new(Shoes, 7, 30m)]), "en");
            await service.PostAsync(2, "en");

            Assert.Equal(PurchasingErrors.ReceiptNotDraft, (await service.PostAsync(1, "en")).Error!.Code);
            Assert.Equal(PurchasingErrors.ReceiptNotDraft, (await service.UpdateAsync(1, new(supplier, main, null, null, []), "en")).Error!.Code);
            Assert.Equal(PurchasingErrors.ReceiptNotDraft, (await service.CancelAsync(1, "en")).Error!.Code);
        }

        await using var check = Database.CreateContext();
        Assert.Equal(50, (await check.Products.SingleAsync(p => p.SKU == Shoes)).Stock);
        // (43 × 20 + 7 × 30) ÷ 50 = 21.4
        Assert.Equal(21.4m, (await check.StockItems.SingleAsync(i => i.ItemId == shoes.ItemId)).AverageCost);
        var lines = await check.StockMovements.Where(m => m.Kind == StockMovementKind.Receipt).OrderBy(m => m.CreatedUtc).ThenBy(m => m.Id).ToListAsync();
        Assert.Equal([("PR-1", "INV-1", 20m), ("PR-2", null, 30m)], lines.Select(l => (l.Reference!, l.Note, l.UnitCost!.Value)));
        await AssertLedgerAgreesAsync();

        // Other lines record the average at their moment.
        await using var later = Database.CreateContext();
        await Ledger(later).PostAsync(main, shoes, StockMovementKind.Issue, -1);
        Assert.Equal(21.4m, (await later.StockMovements.SingleAsync(m => m.Kind == StockMovementKind.Issue)).UnitCost);
    }

    [Fact]
    public async Task A_receipt_needs_lines_an_active_supplier_and_can_be_cancelled_without_touching_stock()
    {
        var (supplier, main) = await PrepareAsync();
        await using var db = Database.CreateContext();
        var service = Service(db);

        await service.CreateAsync(new(supplier, main, null, null, []), "en");
        Assert.Equal(PurchasingErrors.ReceiptEmpty, (await service.PostAsync(1, "en")).Error!.Code);
        await service.UpdateAsync(1, new(supplier, main, null, null, [new(Shoes, 5, 1m)]), "en");
        await new SupplierService(db).UpdateAsync(supplier, new("Rana Trading", null, null, null, null, null, false));
        Assert.Equal(PurchasingErrors.SupplierInactive, (await service.PostAsync(1, "en")).Error!.Code);
        Assert.Equal(PurchasingErrors.SupplierInactive, (await service.CreateAsync(new(supplier, main, null, null, []), "en")).Error!.Code);
        Assert.Equal(PurchasingErrors.SupplierUsed, (await new SupplierService(db).DeleteAsync(supplier)).Error!.Code);

        Assert.Equal(PurchaseReceiptStatus.Cancelled, (await service.CancelAsync(1, "en")).Value.Summary.Status);
        Assert.Equal(33, (await db.Products.AsNoTracking().SingleAsync(p => p.SKU == Shoes)).Stock);
        Assert.Equal("PR-3", PurchaseService.Reference(3));
    }

    [Fact]
    public void The_receipt_validator_checks_quantities_and_costs()
    {
        var validator = new SaveReceiptRequestValidator();
        var ids = (Guid.NewGuid(), Guid.NewGuid());
        Assert.True(validator.Validate(new SaveReceiptRequest(ids.Item1, ids.Item2, null, null, [new("A", 1, 0m)])).IsValid);
        Assert.Equal([InventoryErrors.QuantityInvalid], validator.Validate(new SaveReceiptRequest(ids.Item1, ids.Item2, null, null, [new("A", 0, 1m)])).Errors.Select(e => e.ErrorCode));
        Assert.Equal([InventoryErrors.CostInvalid], validator.Validate(new SaveReceiptRequest(ids.Item1, ids.Item2, null, null, [new("A", 1, -1m)])).Errors.Select(e => e.ErrorCode));
        Assert.Equal([PurchasingErrors.SupplierNotFound], validator.Validate(new SaveReceiptRequest(Guid.Empty, ids.Item2, null, null, [])).Errors.Select(e => e.ErrorCode));
    }
}

public sealed class ItemSettingsTests : InventoryTestBase
{
    [Fact]
    public async Task A_minimum_marks_items_running_low_and_an_average_cost_can_be_set_by_hand()
    {
        await SeedDemoAsync();
        await using var db = Database.CreateContext();
        var service = Inventory(db);

        var saved = (await service.SaveItemSettingsAsync(new(Shoes, 33, 18.123456m))).Value;
        Assert.Equal((33, 18.1235m), (saved.MinimumStock, saved.AverageCost));
        await service.SaveItemSettingsAsync(new(HoodieBeigeS, 2, null));
        Assert.Equal(InventoryErrors.ItemNotFound, (await service.SaveItemSettingsAsync(new("NOPE", 1, null))).Error!.Code);

        // Shoes: 33 ≤ 33 → low; hoodie beige S: 8 > 2 → fine.
        var page = await service.StockAsync(null, false, 1, "en", onlyLow: true);
        var row = Assert.Single(page.Items);
        Assert.Equal((Shoes, true, 18.1235m, 33, 598.08m), (row.Sku, row.Low, row.AverageCost, row.MinimumStock, row.Value));
        Assert.Equal((1, "EUR"), (page.LowCount, page.Currency));
        Assert.False((await service.StockAsync("FSH-003", false, 1, "en")).Items.Single(r => r.Sku == HoodieBeigeS).Low);

        // Clearing the minimum ends the alert but keeps the cost.
        Assert.Null((await service.SaveItemSettingsAsync(new(Shoes, null, null))).Value.MinimumStock);
        Assert.Equal(0, await service.LowStockCountAsync());
        Assert.Equal(18.1235m, (await db.StockItems.AsNoTracking().SingleAsync(i => i.MinimumStock == null && i.AverageCost != null)).AverageCost);
    }

    [Fact]
    public void The_settings_validator_refuses_negative_numbers()
    {
        var validator = new SaveItemSettingsRequestValidator();
        Assert.True(validator.Validate(new SaveItemSettingsRequest("A", null, null)).IsValid);
        Assert.Equal([InventoryErrors.MinimumInvalid], validator.Validate(new SaveItemSettingsRequest("A", -1, null)).Errors.Select(e => e.ErrorCode));
        Assert.Equal([InventoryErrors.CostInvalid], validator.Validate(new SaveItemSettingsRequest("A", null, -1m)).Errors.Select(e => e.ErrorCode));
    }
}

public sealed class StockItemLookupTests : InventoryTestBase
{
    [Fact]
    public async Task Finds_variants_and_products_without_variants_by_sku()
    {
        await SeedDemoAsync();
        await using var db = Database.CreateContext();

        Assert.NotNull((await StockItemLookup.FindAsync(db, $" {HoodieBeigeS} ")).Value.VariantId);
        Assert.Null((await StockItemLookup.FindAsync(db, Shoes)).Value.VariantId);
        Assert.Equal(InventoryErrors.ItemHasVariants, (await StockItemLookup.FindAsync(db, "DEMO-FSH-003")).Error!.Code);
        Assert.Equal(InventoryErrors.ItemNotFound, (await StockItemLookup.FindAsync(db, "NOPE")).Error!.Code);
    }
}
