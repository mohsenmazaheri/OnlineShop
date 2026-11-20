using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using OnlineShopping.Application.Accounts;
using OnlineShopping.Application.Catalog.Admin;
using OnlineShopping.Application.Inventory;
using OnlineShopping.Domain.Inventory;

namespace OnlineShopping.IntegrationTests;

/// <summary>Warehouses and the stock ledger end to end on PostgreSQL (atomic updates, check constraints, the user link).</summary>
public sealed class InventoryApiTests(CatalogAdminApiTests.AdminFactory factory) : IClassFixture<CatalogAdminApiTests.AdminFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private async Task<HttpClient> AdminAsync()
    {
        var client = factory.CreateClient();
        var login = new LoginRequest(CatalogAdminApiTests.AdminFactory.AdminEmail, CatalogAdminApiTests.AdminFactory.AdminPassword, false);
        (await client.PostAsJsonAsync("/api/v1/account/login", login)).EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    [Fact]
    public async Task Stock_moves_through_the_ledger_from_the_editor_to_a_receipt_and_a_transfer()
    {
        using var admin = await AdminAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await ReadAsync<CatalogNodeView>(await admin.PostAsJsonAsync("/api/v1/admin/catalog/categories", new SaveCatalogNodeRequest([new("fa", $"انبار {suffix}", null, null)])));
        var sub = await ReadAsync<CatalogNodeView>(await admin.PostAsJsonAsync($"/api/v1/admin/catalog/categories/{category.Id}/subcategories", new SaveCatalogNodeRequest([new("fa", "زیر", null, null)])));
        var group = await ReadAsync<CatalogNodeView>(await admin.PostAsJsonAsync($"/api/v1/admin/catalog/subcategories/{sub.Id}/groups", new SaveCatalogNodeRequest([new("fa", "گروه", null, null)])));
        var sku = $"INV-{suffix}";
        var product = await ReadAsync<ProductEditView>(await admin.PostAsJsonAsync("/api/v1/admin/products",
            new SaveProductRequest(sku, 10, null, 5, true, group.Id, [new("fa", "کالای انبار", null, null)], [])));
        Assert.Equal(5, product.Stock);

        var warehouses = await ReadAsync<List<WarehouseView>>(await admin.GetAsync("/api/v1/admin/inventory/warehouses"));
        var main = warehouses.Single(w => w.IsDefault);
        var shop = await ReadAsync<WarehouseView>(await admin.PostAsJsonAsync("/api/v1/admin/inventory/warehouses",
            new SaveWarehouseRequest($"shop_{suffix}", "فروشگاه", null, true, false)));

        var received = await ReadAsync<StockRow>(await admin.PostAsJsonAsync("/api/v1/admin/inventory/movements",
            new PostMovementRequest(sku, main.Id, StockMovementKind.Receipt, 10, "PO-1", null)));
        Assert.Equal(15, received.Total);
        var moved = await ReadAsync<StockRow>(await admin.PostAsJsonAsync("/api/v1/admin/inventory/transfers",
            new TransferStockRequest(sku, main.Id, shop.Id, 12, null, "for the window")));
        Assert.Equal((15, 3, 12), (moved.Total, moved.Levels.Single(l => l.WarehouseId == main.Id).Quantity, moved.Levels.Single(l => l.WarehouseId == shop.Id).Quantity));

        using var tooMuch = await admin.PostAsJsonAsync("/api/v1/admin/inventory/movements", new PostMovementRequest(sku, main.Id, StockMovementKind.Issue, 4, null, null));
        Assert.Equal(HttpStatusCode.Conflict, tooMuch.StatusCode);
        Assert.Equal(InventoryErrors.Insufficient, await CodeAsync(tooMuch));
        using var badKind = await admin.PostAsJsonAsync("/api/v1/admin/inventory/movements", new PostMovementRequest(sku, main.Id, StockMovementKind.Sale, 1, null, null));
        Assert.Equal(HttpStatusCode.BadRequest, badKind.StatusCode);
        using var withStock = await admin.PutAsJsonAsync($"/api/v1/admin/inventory/warehouses/{shop.Id}", new SaveWarehouseRequest(shop.Code, shop.Name, null, false, false));
        Assert.Equal(InventoryErrors.WarehouseHasStock, await CodeAsync(withStock));

        // The editor's number is the total over all warehouses; lowering it takes from the default first.
        var edited = await ReadAsync<ProductEditView>(await admin.PutAsJsonAsync($"/api/v1/admin/products/{product.Number}",
            new SaveProductRequest(sku, 10, null, 13, true, group.Id, [new("fa", "کالای انبار", null, null)], [])));
        Assert.Equal(13, edited.Stock);

        var ledger = await ReadAsync<MovementPage>(await admin.GetAsync($"/api/v1/admin/inventory/movements?sku={sku}"));
        Assert.Equal(
            [StockMovementKind.Adjustment, StockMovementKind.TransferIn, StockMovementKind.TransferOut, StockMovementKind.Receipt, StockMovementKind.Adjustment],
            ledger.Items.Select(m => m.Kind));
        Assert.Equal((-2, 1, StockReferences.ProductEditor), (ledger.Items[0].Quantity, ledger.Items[0].BalanceAfter, ledger.Items[0].Reference));
        Assert.All(ledger.Items, m => Assert.Equal("Admin", m.UserName));

        var stock = await ReadAsync<StockPage>(await admin.GetAsync($"/api/v1/admin/inventory/stock?search={sku}"));
        Assert.Equal(13, Assert.Single(stock.Items).Total);
    }

    [Fact]
    public async Task A_stocktake_counts_a_warehouse_and_posts_the_differences()
    {
        using var admin = await AdminAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await ReadAsync<CatalogNodeView>(await admin.PostAsJsonAsync("/api/v1/admin/catalog/categories", new SaveCatalogNodeRequest([new("fa", $"شمارش {suffix}", null, null)])));
        var sub = await ReadAsync<CatalogNodeView>(await admin.PostAsJsonAsync($"/api/v1/admin/catalog/categories/{category.Id}/subcategories", new SaveCatalogNodeRequest([new("fa", "زیر", null, null)])));
        var group = await ReadAsync<CatalogNodeView>(await admin.PostAsJsonAsync($"/api/v1/admin/catalog/subcategories/{sub.Id}/groups", new SaveCatalogNodeRequest([new("fa", "گروه", null, null)])));
        var sku = $"CNT-{suffix}";
        await ReadAsync<ProductEditView>(await admin.PostAsJsonAsync("/api/v1/admin/products",
            new SaveProductRequest(sku, 10, null, 9, true, group.Id, [new("fa", "کالای شمارش", null, null)], [])));
        var warehouse = await ReadAsync<WarehouseView>(await admin.PostAsJsonAsync("/api/v1/admin/inventory/warehouses",
            new SaveWarehouseRequest($"count_{suffix}", "انبار شمارش", null, true, false)));
        var main = (await ReadAsync<List<WarehouseView>>(await admin.GetAsync("/api/v1/admin/inventory/warehouses"))).Single(w => w.IsDefault);
        await ReadAsync<StockRow>(await admin.PostAsJsonAsync("/api/v1/admin/inventory/transfers", new TransferStockRequest(sku, main.Id, warehouse.Id, 9, null, null)));

        var opened = await ReadAsync<StocktakeSummary>(await admin.PostAsJsonAsync("/api/v1/admin/inventory/stocktakes", new OpenStocktakeRequest(warehouse.Id, "test")));
        using var again = await admin.PostAsJsonAsync("/api/v1/admin/inventory/stocktakes", new OpenStocktakeRequest(warehouse.Id, null));
        Assert.Equal(StocktakeErrors.AlreadyOpen, await CodeAsync(again));

        var counted = await ReadAsync<List<StocktakeLineView>>(await admin.PostAsJsonAsync($"/api/v1/admin/inventory/stocktakes/{opened.Number}/counts", new CountRequest([new(sku, 7)])));
        Assert.Equal((9, 7, -2), (counted.Single().Expected, counted.Single().Counted, counted.Single().Difference));
        using var sheet = await admin.GetAsync($"/api/v1/admin/inventory/stocktakes/{opened.Number}/sheet?format=csv");
        Assert.Equal(HttpStatusCode.OK, sheet.StatusCode);
        Assert.Contains(sku, await sheet.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var view = await ReadAsync<StocktakeView>(await admin.GetAsync($"/api/v1/admin/inventory/stocktakes/{opened.Number}?filter=Differences"));
        Assert.Equal((1, 2), (view.Totals.WithDifference, view.Totals.UnitsShort));
        var posted = await ReadAsync<StocktakeSummary>(await admin.PostAsync($"/api/v1/admin/inventory/stocktakes/{opened.Number}/post", null));
        Assert.Equal(StocktakeStatus.Posted, posted.Status);

        var ledger = await ReadAsync<MovementPage>(await admin.GetAsync($"/api/v1/admin/inventory/movements?sku={sku}&kind=Adjustment&warehouseId={warehouse.Id}"));
        Assert.Equal((-2, StocktakeService.Reference(opened.Number)), (ledger.Items.Single().Quantity, ledger.Items.Single().Reference));
        var stock = await ReadAsync<StockPage>(await admin.GetAsync($"/api/v1/admin/inventory/stock?search={sku}"));
        Assert.Equal(7, stock.Items.Single().Total);
    }

    [Fact]
    public async Task A_purchase_receipt_brings_goods_in_at_cost_and_a_minimum_flags_low_stock()
    {
        using var admin = await AdminAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await ReadAsync<CatalogNodeView>(await admin.PostAsJsonAsync("/api/v1/admin/catalog/categories", new SaveCatalogNodeRequest([new("fa", $"خرید {suffix}", null, null)])));
        var sub = await ReadAsync<CatalogNodeView>(await admin.PostAsJsonAsync($"/api/v1/admin/catalog/categories/{category.Id}/subcategories", new SaveCatalogNodeRequest([new("fa", "زیر", null, null)])));
        var group = await ReadAsync<CatalogNodeView>(await admin.PostAsJsonAsync($"/api/v1/admin/catalog/subcategories/{sub.Id}/groups", new SaveCatalogNodeRequest([new("fa", "گروه", null, null)])));
        var sku = $"BUY-{suffix}";
        await ReadAsync<ProductEditView>(await admin.PostAsJsonAsync("/api/v1/admin/products",
            new SaveProductRequest(sku, 10, null, 0, true, group.Id, [new("fa", "کالای خرید", null, null)], [])));
        var main = (await ReadAsync<List<WarehouseView>>(await admin.GetAsync("/api/v1/admin/inventory/warehouses"))).Single(w => w.IsDefault);
        var supplier = await ReadAsync<SupplierView>(await admin.PostAsJsonAsync("/api/v1/admin/inventory/suppliers",
            new SaveSupplierRequest($"تأمین {suffix}", null, null, "sales@supplier.test", null, null, true)));

        var draft = await ReadAsync<ReceiptView>(await admin.PostAsJsonAsync("/api/v1/admin/inventory/purchases",
            new SaveReceiptRequest(supplier.Id, main.Id, "INV-9", null, [new(sku, 4, 2.5m)])));
        Assert.Equal((PurchaseReceiptStatus.Draft, 10m), (draft.Summary.Status, draft.Summary.Total));
        var changed = await ReadAsync<ReceiptView>(await admin.PutAsJsonAsync($"/api/v1/admin/inventory/purchases/{draft.Summary.Number}",
            new SaveReceiptRequest(supplier.Id, main.Id, "INV-9", null, [new(sku, 6, 2.5m)])));
        Assert.Equal(15m, changed.Summary.Total);
        var posted = await ReadAsync<ReceiptView>(await admin.PostAsync($"/api/v1/admin/inventory/purchases/{draft.Summary.Number}/post", null));
        Assert.Equal(PurchaseReceiptStatus.Posted, posted.Summary.Status);
        using var again = await admin.PostAsync($"/api/v1/admin/inventory/purchases/{draft.Summary.Number}/post", null);
        Assert.Equal(PurchasingErrors.ReceiptNotDraft, await CodeAsync(again));

        var settings = await ReadAsync<ItemSettingsView>(await admin.PutAsJsonAsync("/api/v1/admin/inventory/items/settings", new SaveItemSettingsRequest(sku, 6, null)));
        Assert.Equal((6, 2.5m), (settings.MinimumStock, settings.AverageCost));
        var low = await ReadAsync<StockPage>(await admin.GetAsync($"/api/v1/admin/inventory/stock?search={sku}&onlyLow=true"));
        var row = Assert.Single(low.Items);
        Assert.Equal((6, true, 2.5m), (row.Total, row.Low, row.AverageCost));
        var ledger = await ReadAsync<MovementPage>(await admin.GetAsync($"/api/v1/admin/inventory/movements?sku={sku}&kind=Receipt"));
        Assert.Equal((6, PurchaseService.Reference(draft.Summary.Number)), (ledger.Items.Single().Quantity, ledger.Items.Single().Reference));
    }

    [Fact]
    public async Task Two_checkouts_racing_for_the_last_units_never_both_get_them()
    {
        using var admin = await AdminAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await ReadAsync<CatalogNodeView>(await admin.PostAsJsonAsync("/api/v1/admin/catalog/categories", new SaveCatalogNodeRequest([new("fa", $"رزرو {suffix}", null, null)])));
        var sub = await ReadAsync<CatalogNodeView>(await admin.PostAsJsonAsync($"/api/v1/admin/catalog/categories/{category.Id}/subcategories", new SaveCatalogNodeRequest([new("fa", "زیر", null, null)])));
        var group = await ReadAsync<CatalogNodeView>(await admin.PostAsJsonAsync($"/api/v1/admin/catalog/subcategories/{sub.Id}/groups", new SaveCatalogNodeRequest([new("fa", "گروه", null, null)])));
        var sku = $"RES-{suffix}";
        await ReadAsync<ProductEditView>(await admin.PostAsJsonAsync("/api/v1/admin/products",
            new SaveProductRequest(sku, 10, null, 5, true, group.Id, [new("fa", "کالای رزرو", null, null)], [])));

        StockItem item;
        using (var scope = factory.Services.CreateScope())
            item = (await StockItemLookup.FindAsync(scope.ServiceProvider.GetRequiredService<OnlineShopping.Application.Common.IApplicationDbContext>(), sku)).Value;

        // Ten checkouts at once each want 3 of the 5: exactly one can have them.
        var attempts = await Task.WhenAll(Enumerable.Range(0, 10).Select(async i =>
        {
            using var scope = factory.Services.CreateScope();
            try
            {
                return (await scope.ServiceProvider.GetRequiredService<StockReservationService>().ReserveAsync($"race-{suffix}-{i}", [new(item, 3)])).IsSuccess;
            }
            catch (Microsoft.EntityFrameworkCore.DbUpdateException)
            {
                return false; // a lock conflict is a refusal too
            }
        }));
        Assert.Equal(1, attempts.Count(ok => ok));

        var stock = await ReadAsync<StockPage>(await admin.GetAsync($"/api/v1/admin/inventory/stock?search={sku}"));
        Assert.Equal((5, 3, 2), (stock.Items.Single().Total, stock.Items.Single().Reserved, stock.Items.Single().Available));
        var active = await ReadAsync<List<ActiveReservationView>>(await admin.GetAsync("/api/v1/admin/inventory/reservations"));
        var held = active.Single(r => r.Sku == sku);
        var released = await ReadAsync<int>(await admin.PostAsJsonAsync("/api/v1/admin/inventory/reservations/release", new ReleaseReservationsRequest(held.Reference)));
        Assert.Equal(1, released);
        Assert.Equal(0, (await ReadAsync<StockPage>(await admin.GetAsync($"/api/v1/admin/inventory/stock?search={sku}"))).Items.Single().Reserved);
    }

    [Fact]
    public async Task Visitors_cannot_see_or_change_stock()
    {
        using var anonymous = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/admin/inventory/stock")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/v1/admin/inventory/movements", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/admin/inventory/warehouses")).StatusCode);
    }
}
