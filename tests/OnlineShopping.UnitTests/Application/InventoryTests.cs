using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnlineShopping.Application.Catalog.Admin;
using OnlineShopping.Application.Common;
using OnlineShopping.Application.Inventory;
using OnlineShopping.Domain.Inventory;
using OnlineShopping.Infrastructure.Identity;
using OnlineShopping.Infrastructure.Persistence;
using OnlineShopping.UnitTests.TestSupport;

namespace OnlineShopping.UnitTests.Application;

/// <summary>Shared checks: the ledger's sums must always agree with the levels and the cached totals.</summary>
public abstract class InventoryTestBase : DemoCatalogTestBase
{
    protected const string Shoes = "DEMO-FSH-001";
    protected const string HoodieBeigeS = "DEMO-FSH-003-BG-S";

    protected StockLedger Ledger(AppDbContext db, string? userId = null) => TestLedger.For(db, Store, userId);

    protected WarehouseService Warehouses(AppDbContext db) => new(db, Ledger(db));

    private protected InventoryService Inventory(AppDbContext db, FakeStaffIdentity? staff = null, string? userId = null)
    {
        var ledger = Ledger(db, userId);
        return new InventoryService(db, ledger, new WarehouseService(db, ledger), staff ?? new FakeStaffIdentity(), Options.Create(Store));
    }

    /// <summary>A staff account row, which movement lines point at.</summary>
    protected async Task AddUserAsync(string id)
    {
        await using var db = Database.CreateContext();
        db.Users.Add(new AppUser { Id = id, UserName = id, NormalizedUserName = id.ToUpperInvariant() });
        await db.SaveChangesAsync();
    }

    protected async Task<Guid> AddWarehouseAsync(string name)
    {
        await using var db = Database.CreateContext();
        return (await Warehouses(db).CreateAsync(new SaveWarehouseRequest(null, name, null, true, false))).Value.Id;
    }

    /// <summary>Stock = sum of lines (per warehouse and item), product = sum of its levels, variant = sum of its levels.</summary>
    protected async Task AssertLedgerAgreesAsync()
    {
        await using var db = Database.CreateContext();
        var lines = await db.StockMovements.GroupBy(m => new { m.WarehouseId, m.ItemId }).Select(g => new { g.Key.WarehouseId, g.Key.ItemId, Sum = g.Sum(m => m.Quantity) }).ToListAsync();
        var levels = await db.StockLevels.ToListAsync();
        foreach (var line in lines)
            Assert.Equal(line.Sum, levels.SingleOrDefault(l => l.WarehouseId == line.WarehouseId && l.ItemId == line.ItemId)?.Quantity ?? 0);
        foreach (var product in await db.Products.Select(p => new { p.Id, p.Stock }).ToListAsync())
            Assert.Equal(levels.Where(l => l.ProductId == product.Id).Sum(l => l.Quantity), product.Stock);
        foreach (var variant in await db.ProductVariants.Select(v => new { v.Id, v.Stock }).ToListAsync())
            Assert.Equal(levels.Where(l => l.VariantId == variant.Id).Sum(l => l.Quantity), variant.Stock);
    }

    protected async Task<StockItem> ItemAsync(string sku)
    {
        await using var db = Database.CreateContext();
        var variant = await db.ProductVariants.Where(v => v.Sku == sku).Select(v => new { v.Id, v.ProductId }).SingleOrDefaultAsync();
        return variant is not null ? new StockItem(variant.ProductId, variant.Id) : new StockItem(await db.Products.Where(p => p.SKU == sku).Select(p => p.Id).SingleAsync(), null);
    }
}

public sealed class StockLedgerTests : InventoryTestBase
{
    [Fact]
    public async Task The_demo_stock_opens_the_ledger_once_in_the_default_warehouse()
    {
        await SeedDemoAsync();
        await using var db = Database.CreateContext();
        var main = await db.Warehouses.SingleAsync();
        Assert.True(main.IsDefault);
        Assert.Equal("انبار اصلی", main.Name);
        Assert.All(await db.StockMovements.ToListAsync(), m => Assert.Equal((StockMovementKind.Opening, StockReferences.Opening), (m.Kind, m.Reference)));
        // The hoodie's sold-out variant has no line; its product has none of its own.
        var hoodie = await db.Products.SingleAsync(p => p.SKU == "DEMO-FSH-003");
        Assert.Equal(7, await db.StockMovements.CountAsync(m => m.ProductId == hoodie.Id));
        Assert.DoesNotContain(await db.StockMovements.ToListAsync(), m => m.ProductId == hoodie.Id && m.VariantId == null);

        Assert.Equal(0, await Ledger(db).OpenUntrackedAsync());
        await AssertLedgerAgreesAsync();
    }

    [Fact]
    public async Task Posting_changes_the_level_and_both_totals_and_never_goes_below_zero()
    {
        await SeedDemoAsync();
        var item = await ItemAsync(HoodieBeigeS);
        await AddUserAsync("staff-1");
        await using var db = Database.CreateContext();
        var ledger = Ledger(db, "staff-1");
        var main = await ledger.DefaultWarehouseIdAsync();

        Assert.Equal(13, (await ledger.PostAsync(main, item, StockMovementKind.Receipt, 5, "PO-7", "late delivery")).Value);
        Assert.Equal(InventoryErrors.Insufficient, (await ledger.PostAsync(main, item, StockMovementKind.Issue, -14)).Error!.Code);
        Assert.Equal(InventoryErrors.QuantityInvalid, (await ledger.PostAsync(main, item, StockMovementKind.Issue, 0)).Error!.Code);
        var elsewhere = await AddWarehouseAsync("Shop");
        Assert.Equal(InventoryErrors.Insufficient, (await ledger.PostAsync(elsewhere, item, StockMovementKind.Issue, -1)).Error!.Code);

        await using var check = Database.CreateContext();
        Assert.Equal(13, (await check.ProductVariants.SingleAsync(v => v.Sku == HoodieBeigeS)).Stock);
        Assert.Equal(65, (await check.Products.SingleAsync(p => p.SKU == "DEMO-FSH-003")).Stock);
        var line = await check.StockMovements.SingleAsync(m => m.Kind == StockMovementKind.Receipt);
        Assert.Equal((5, 13, "PO-7", "late delivery", "staff-1"), (line.Quantity, line.BalanceAfter, line.Reference, line.Note, line.UserId));
        await AssertLedgerAgreesAsync();
    }

    [Fact]
    public async Task Setting_a_total_adds_to_the_default_and_takes_from_it_first()
    {
        await SeedDemoAsync();
        var item = await ItemAsync(Shoes);
        var shop = await AddWarehouseAsync("Shop");
        await using (var db = Database.CreateContext())
            await Ledger(db).PostAsync(shop, item, StockMovementKind.Receipt, 10);

        await using (var db = Database.CreateContext())
        {
            // 33 in the default + 10 in the shop = 43; down to 5 empties the default first, then the shop gives 5.
            Assert.Equal(5, (await Ledger(db).SetTotalAsync(item, 5, StockReferences.ProductEditor)).Value);
            Assert.Equal(InventoryErrors.QuantityInvalid, (await Ledger(db).SetTotalAsync(item, -1, StockReferences.ProductEditor)).Error!.Code);
        }
        await using (var db = Database.CreateContext())
        {
            var levels = await db.StockLevels.Where(l => l.ItemId == item.ItemId).ToDictionaryAsync(l => l.WarehouseId, l => l.Quantity);
            Assert.Equal(5, levels[shop]);
            Assert.Equal(0, levels.Where(l => l.Key != shop).Sum(l => l.Value));
            Assert.Equal(9, (await Ledger(db).SetTotalAsync(item, 9, StockReferences.ProductEditor)).Value);
            Assert.Equal(4, await db.StockLevels.Where(l => l.ItemId == item.ItemId && l.WarehouseId != shop).SumAsync(l => l.Quantity));
            Assert.Equal(9, (await Ledger(db).SetTotalAsync(item, 9, StockReferences.ProductEditor)).Value);
        }
        await using var check = Database.CreateContext();
        // 43 → 5: the default gives 33, the shop 5 (two lines); 5 → 9: one line; 9 → 9: none.
        Assert.Equal(3, await check.StockMovements.CountAsync(m => m.ItemId == item.ItemId && m.Kind == StockMovementKind.Adjustment));
        await AssertLedgerAgreesAsync();
    }

    [Fact]
    public async Task A_new_shop_gets_its_default_warehouse_on_first_use()
    {
        await using var db = Database.CreateContext();
        var id = await TestLedger.For(db, new StoreOptions { Locales = "de,en", DefaultLocale = "de" }).DefaultWarehouseIdAsync();

        Assert.Equal(id, await Ledger(db).DefaultWarehouseIdAsync());
        Assert.Equal(("main", "Hauptlager"), await db.Warehouses.Select(w => ValueTuple.Create(w.Code, w.Name)).SingleAsync());
        Assert.Equal(0, await Ledger(db).OpenUntrackedAsync());
    }

    [Fact]
    public void Default_names_exist_for_every_language()
    {
        Assert.Equal(["ar", "de", "en", "fa", "hy"], StockLedger.DefaultWarehouseNames.Keys.Order());
        Assert.Equal(new StockItem(Guid.Empty, null).ItemId, Guid.Empty);
        // Outside a request (startup, background work) lines have no user.
        Assert.Null(new NoCurrentUser().UserId);
    }
}

public sealed class WarehouseServiceTests : InventoryTestBase
{
    [Fact]
    public async Task Creates_with_a_code_from_the_name_and_lists_the_default_first_with_its_stock()
    {
        await SeedDemoAsync();
        await using var db = Database.CreateContext();
        var service = Warehouses(db);

        var branch = (await service.CreateAsync(new SaveWarehouseRequest(null, " North Branch ", " 12 Main St ", true, false))).Value;
        Assert.Equal(("north_branch", "North Branch", "12 Main St", false), (branch.Code, branch.Name, branch.Address, branch.IsDefault));
        Assert.Equal("warehouse_1", (await service.CreateAsync(new SaveWarehouseRequest(null, "انبار شمال", null, true, false))).Value.Code);
        Assert.Equal(InventoryErrors.CodeTaken, (await service.CreateAsync(new SaveWarehouseRequest("north_branch", "Other", null, true, false))).Error!.Code);
        Assert.Equal(InventoryErrors.CodeInvalid, (await service.CreateAsync(new SaveWarehouseRequest("North!", "Other", null, true, false))).Error!.Code);

        var list = await service.ListAsync();
        Assert.True(list[0].IsDefault);
        Assert.True(list[0].Used);
        Assert.True(list[0].Units > 0);
        Assert.Equal(0, list.Single(w => w.Code == "north_branch").Units);
    }

    [Fact]
    public async Task There_is_always_one_active_default()
    {
        await SeedDemoAsync();
        var branch = await AddWarehouseAsync("Branch");
        await using var db = Database.CreateContext();
        var service = Warehouses(db);
        var main = (await service.ListAsync()).Single(w => w.IsDefault);

        Assert.Equal(InventoryErrors.DefaultRequired, (await service.UpdateAsync(main.Id, new(main.Code, main.Name, null, true, false))).Error!.Code);
        Assert.Equal(InventoryErrors.DefaultRequired, (await service.UpdateAsync(main.Id, new(main.Code, main.Name, null, false, true))).Error!.Code);
        Assert.Equal(InventoryErrors.WarehouseInactive, (await service.UpdateAsync(branch, new("branch", "Branch", null, false, true))).Error!.Code);
        Assert.Equal(InventoryErrors.DefaultRequired, (await service.DeleteAsync(main.Id)).Error!.Code);

        Assert.True((await service.UpdateAsync(branch, new("branch", "Branch", null, true, true))).Value.IsDefault);
        Assert.Equal([branch], (await service.ListAsync()).Where(w => w.IsDefault).Select(w => w.Id));
        Assert.Equal(InventoryErrors.WarehouseNotFound, (await service.UpdateAsync(Guid.NewGuid(), new(null, "x", null, true, false))).Error!.Code);
    }

    [Fact]
    public async Task Deactivates_only_without_stock_and_deletes_only_without_movements()
    {
        await SeedDemoAsync();
        var branch = await AddWarehouseAsync("Branch");
        var spare = await AddWarehouseAsync("Spare");
        await using (var db = Database.CreateContext())
            await Ledger(db).PostAsync(branch, await ItemAsync(Shoes), StockMovementKind.Receipt, 2);

        await using var context = Database.CreateContext();
        var service = Warehouses(context);
        Assert.Equal(InventoryErrors.WarehouseHasStock, (await service.UpdateAsync(branch, new("branch", "Branch", null, false, false))).Error!.Code);
        Assert.Equal(InventoryErrors.WarehouseUsed, (await service.DeleteAsync(branch)).Error!.Code);
        Assert.False((await service.UpdateAsync(spare, new("spare", "Spare", null, false, false))).Value.IsActive);
        Assert.True((await service.DeleteAsync(spare)).Value);
        Assert.DoesNotContain(await service.ListAsync(), w => w.Id == spare);
        Assert.Equal(InventoryErrors.WarehouseNotFound, (await service.DeleteAsync(spare)).Error!.Code);
    }

    [Fact]
    public void The_request_validator_needs_a_name_and_short_texts()
    {
        var validator = new SaveWarehouseRequestValidator();
        Assert.True(validator.Validate(new SaveWarehouseRequest(null, "Main", null, true, true)).IsValid);
        Assert.Equal([InventoryErrors.NameRequired], validator.Validate(new SaveWarehouseRequest(null, "", null, true, true)).Errors.Select(e => e.ErrorCode));
        Assert.Equal([InventoryErrors.TextTooLong], validator.Validate(new SaveWarehouseRequest(null, "Main", new string('x', 301), true, true)).Errors.Select(e => e.ErrorCode));
    }
}

public sealed class InventoryServiceTests : InventoryTestBase
{
    [Fact]
    public async Task Receipts_issues_and_adjustments_by_sku()
    {
        await SeedDemoAsync();
        await using var db = Database.CreateContext();
        var service = Inventory(db);
        var main = await Ledger(db).DefaultWarehouseIdAsync();

        var row = (await service.PostAsync(new(Shoes, main, StockMovementKind.Receipt, 7, " PO-1 ", " "), "en")).Value;
        Assert.Equal((40, "Ranna Men's Walking Shoes", (string?)null), (row.Total, row.Name, row.VariantLabel));
        Assert.Equal(37, (await service.PostAsync(new(Shoes, main, StockMovementKind.Issue, 3, null, "damaged"), "en")).Value.Total);
        Assert.Equal(35, (await service.PostAsync(new(Shoes, main, StockMovementKind.Adjustment, -2, null, null), "en")).Value.Total);
        var variant = (await service.PostAsync(new(HoodieBeigeS, main, StockMovementKind.Receipt, 1, null, null), "en")).Value;
        Assert.Equal((9, HoodieBeigeS), (variant.Total, variant.Sku));
        Assert.Contains("/", variant.VariantLabel);

        Assert.Equal(InventoryErrors.ItemHasVariants, (await service.PostAsync(new("DEMO-FSH-003", main, StockMovementKind.Receipt, 1, null, null), "en")).Error!.Code);
        Assert.Equal(InventoryErrors.ItemNotFound, (await service.PostAsync(new("NOPE", main, StockMovementKind.Receipt, 1, null, null), "en")).Error!.Code);
        Assert.Equal(InventoryErrors.WarehouseNotFound, (await service.PostAsync(new(Shoes, Guid.NewGuid(), StockMovementKind.Receipt, 1, null, null), "en")).Error!.Code);
        Assert.Equal(InventoryErrors.Insufficient, (await service.PostAsync(new(Shoes, main, StockMovementKind.Issue, 999, null, null), "en")).Error!.Code);

        var lines = (await service.MovementsAsync(Shoes, null, null, 1, "en")).Items;
        Assert.Equal([StockMovementKind.Adjustment, StockMovementKind.Issue, StockMovementKind.Receipt, StockMovementKind.Opening], lines.Select(l => l.Kind));
        Assert.Equal(("PO-1", (string?)null), (lines[2].Reference, lines[2].Note));
        await AssertLedgerAgreesAsync();
    }

    [Fact]
    public async Task A_transfer_moves_stock_in_two_lines_or_not_at_all()
    {
        await SeedDemoAsync();
        var shop = await AddWarehouseAsync("Shop");
        await using var db = Database.CreateContext();
        var service = Inventory(db);
        var main = await Ledger(db).DefaultWarehouseIdAsync();

        var row = (await service.TransferAsync(new(Shoes, main, shop, 30, "T-1", null), "en")).Value;
        Assert.Equal(33, row.Total);
        Assert.Equal(30, row.Levels.Single(l => l.WarehouseId == shop).Quantity);
        Assert.Equal(InventoryErrors.Insufficient, (await service.TransferAsync(new(Shoes, main, shop, 4, null, null), "en")).Error!.Code);
        Assert.Equal(InventoryErrors.SameWarehouse, (await service.TransferAsync(new(Shoes, shop, shop, 1, null, null), "en")).Error!.Code);

        var lines = (await service.MovementsAsync(null, shop, null, 1, "en")).Items;
        var transfer = Assert.Single(lines);
        Assert.Equal((StockMovementKind.TransferIn, 30, "Shop"), (transfer.Kind, transfer.Quantity, transfer.Warehouse));
        Assert.Equal(2, (await service.MovementsAsync(null, null, null, 1, "en")).Items.Count(l => l.TransferId == transfer.TransferId));

        await using (var deactivate = Database.CreateContext())
        {
            await Inventory(deactivate).TransferAsync(new(Shoes, shop, main, 30, null, null), "en");
            await Warehouses(deactivate).UpdateAsync(shop, new("shop", "Shop", null, false, false));
        }
        Assert.Equal(InventoryErrors.WarehouseInactive, (await Inventory(Database.CreateContext()).TransferAsync(new(Shoes, main, shop, 1, null, null), "en")).Error!.Code);
        await AssertLedgerAgreesAsync();
    }

    [Fact]
    public async Task Lists_stock_per_variant_with_search_and_the_out_of_stock_filter()
    {
        await SeedDemoAsync();
        await using var db = Database.CreateContext();
        var service = Inventory(db);

        var hoodie = await service.StockAsync("FSH-003", onlyOut: false, page: 1, "en");
        Assert.Equal(1, hoodie.Total);
        Assert.Equal(8, hoodie.Items.Count);
        Assert.All(hoodie.Items, r => Assert.NotNull(r.VariantLabel));
        Assert.Equal(8, hoodie.Items.Single(r => r.Sku == HoodieBeigeS).Levels.Single().Quantity);
        Assert.NotEmpty(hoodie.Warehouses);

        // The hoodie has a sold-out variant; the shoes do not show up.
        var outOfStock = await service.StockAsync(null, onlyOut: true, page: 1, "en");
        Assert.Contains(outOfStock.Items, r => r.Sku == "DEMO-FSH-003-GR-XL");
        Assert.DoesNotContain(outOfStock.Items, r => r.Sku == Shoes);
        Assert.Equal(InventoryService.StockPageSize, (await service.StockAsync(null, false, 1, "en")).PageSize);
    }

    [Fact]
    public async Task The_ledger_names_who_did_it_and_filters_by_kind_and_product()
    {
        await SeedDemoAsync();
        var staff = new FakeStaffIdentity();
        staff.Add("u-1", email: "keeper@shop.test", phone: null, "Warehouse");
        await AddUserAsync("u-1");
        await using var db = Database.CreateContext();
        var service = Inventory(db, staff, userId: "u-1");
        var main = await Ledger(db).DefaultWarehouseIdAsync();
        await service.PostAsync(new(HoodieBeigeS, main, StockMovementKind.Receipt, 2, null, null), "en");

        var byKind = (await service.MovementsAsync(null, null, StockMovementKind.Receipt, 1, "en")).Items;
        Assert.Equal("keeper@shop.test", Assert.Single(byKind).UserName);
        // A product's own SKU lists the lines of all its variants.
        Assert.Equal(8, (await service.MovementsAsync("DEMO-FSH-003", null, null, 1, "en")).Total);
        Assert.Equal(0, (await service.MovementsAsync("NOPE", null, null, 1, "en")).Total);
        Assert.Null((await service.MovementsAsync(Shoes, null, null, 1, "en")).Items.Single().UserName);
    }

    [Fact]
    public void The_request_validators_check_kind_quantity_and_warehouses()
    {
        var post = new PostMovementRequestValidator();
        var id = Guid.NewGuid();
        Assert.True(post.Validate(new PostMovementRequest("A", id, StockMovementKind.Adjustment, -3, null, null)).IsValid);
        Assert.Equal([InventoryErrors.QuantityInvalid], post.Validate(new PostMovementRequest("A", id, StockMovementKind.Issue, -3, null, null)).Errors.Select(e => e.ErrorCode));
        Assert.Equal([InventoryErrors.KindInvalid], post.Validate(new PostMovementRequest("A", id, StockMovementKind.Sale, 3, null, null)).Errors.Select(e => e.ErrorCode));
        Assert.Equal([InventoryErrors.ItemNotFound], post.Validate(new PostMovementRequest("", id, StockMovementKind.Receipt, 3, null, null)).Errors.Select(e => e.ErrorCode));

        var transfer = new TransferStockRequestValidator();
        Assert.Equal([InventoryErrors.SameWarehouse], transfer.Validate(new TransferStockRequest("A", id, id, 1, null, null)).Errors.Select(e => e.ErrorCode));
        Assert.Equal([InventoryErrors.QuantityInvalid], transfer.Validate(new TransferStockRequest("A", id, Guid.NewGuid(), 0, null, null)).Errors.Select(e => e.ErrorCode));
    }
}

public sealed class TransactionsTests : InventoryTestBase
{
    [Fact]
    public async Task Commits_on_success_rolls_back_on_failure_and_joins_an_open_transaction()
    {
        await using var db = Database.CreateContext();
        await db.InTransactionAsync(async () =>
        {
            db.Warehouses.Add(new Warehouse { Code = "kept", Name = "Kept" });
            await db.SaveChangesAsync();
            return Result.Success(true);
        });
        var failed = await db.InTransactionAsync<bool>(async () =>
        {
            db.Warehouses.Add(new Warehouse { Code = "gone", Name = "Gone" });
            await db.SaveChangesAsync();
            return AppError.Conflict("x", "no");
        });
        Assert.False(failed.IsSuccess);
        db.ChangeTracker.Clear();

        await using (var outer = await db.Database.BeginTransactionAsync())
        {
            await db.InTransactionAsync(async () =>
            {
                db.Warehouses.Add(new Warehouse { Code = "inner", Name = "Inner" });
                await db.SaveChangesAsync();
                return Result.Success(true);
            });
            await outer.RollbackAsync();
        }
        db.ChangeTracker.Clear();
        Assert.Equal(["kept"], await db.Warehouses.Select(w => w.Code).ToListAsync());
    }
}

public sealed class CatalogStockLinesTests : InventoryTestBase
{
    [Fact]
    public async Task The_product_editor_writes_an_adjustment_line_for_a_changed_stock()
    {
        await SeedDemoAsync();
        var number = await NumberOfAsync(Shoes);
        await using var db = Database.CreateContext();
        var admin = new ProductAdminService(db, Options.Create(Store), Ledger(db));
        var shoes = (await admin.GetAsync(number)).Value;
        var request = new SaveProductRequest(shoes.Sku, shoes.Price, shoes.CompareAtPrice, 40, true, shoes.ProductGroupId, shoes.Texts, shoes.Specs, shoes.BrandId);

        Assert.Equal(40, (await admin.UpdateAsync(number, request)).Value.Stock);
        Assert.Equal(40, (await admin.UpdateAsync(number, request)).Value.Stock);
        var created = (await admin.CreateAsync(request with { Sku = "NEW-1" })).Value;
        Assert.Equal(40, created.Stock);

        await using var check = Database.CreateContext();
        var lines = await check.StockMovements.Where(m => m.Reference == StockReferences.ProductEditor).Select(m => m.Quantity).ToListAsync();
        Assert.Equal([7, 40], lines.Order());
        await AssertLedgerAgreesAsync();
    }

    [Fact]
    public async Task Saving_variants_writes_off_removed_ones_and_sets_the_others()
    {
        await SeedDemoAsync();
        var number = await NumberOfAsync("DEMO-FSH-003");
        await using var db = Database.CreateContext();
        var hoodie = (await new ProductAdminService(db, Options.Create(Store), Ledger(db)).GetAsync(number)).Value;
        var keep = hoodie.Variants.Variants[0];

        await new ProductVariantService(db, Ledger(db)).SaveAsync(number, new(["color", "size"], [new VariantInput(keep.Id, keep.Sku, null, null, 2, keep.Options)]));

        await using var check = Database.CreateContext();
        Assert.Equal(2, (await check.Products.SingleAsync(p => p.PublicNumber == number)).Stock);
        // Six variants with stock are written off (the sold-out one has nothing to write off), the kept one goes 8 → 2.
        Assert.Equal(7, await check.StockMovements.CountAsync(m => m.Reference == StockReferences.Variants));
        await AssertLedgerAgreesAsync();
    }
}
