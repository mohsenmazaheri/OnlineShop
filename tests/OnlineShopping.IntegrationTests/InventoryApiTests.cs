using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    public async Task Visitors_cannot_see_or_change_stock()
    {
        using var anonymous = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/admin/inventory/stock")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/v1/admin/inventory/movements", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/admin/inventory/warehouses")).StatusCode);
    }
}
