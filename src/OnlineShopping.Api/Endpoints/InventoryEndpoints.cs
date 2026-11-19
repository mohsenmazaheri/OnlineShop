using OnlineShopping.Api.Common;
using OnlineShopping.Application.Authorization;
using OnlineShopping.Application.Inventory;
using OnlineShopping.Domain.Inventory;

namespace OnlineShopping.Api.Endpoints;

/// <summary>Warehouses, stock and the stock ledger: reading needs inventory.view, any change inventory.manage.</summary>
public static class InventoryEndpoints
{
    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder app)
    {
        var view = app.MapGroup("/api/v1/admin/inventory").WithTags("Admin: inventory")
            .RequireAuthorization(Permissions.Policy(Permissions.InventoryView));
        var manage = app.MapGroup("/api/v1/admin/inventory").WithTags("Admin: inventory")
            .RequireAuthorization(Permissions.Policy(Permissions.InventoryManage));

        view.MapGet("/warehouses", async (WarehouseService warehouses, CancellationToken ct) => TypedResults.Ok(await warehouses.ListAsync(ct)))
            .WithName("ListWarehouses").WithSummary("Every warehouse (the default first) with its stock in units and items.");
        manage.MapPost("/warehouses", async (SaveWarehouseRequest request, WarehouseService warehouses, CancellationToken ct) =>
                (await warehouses.CreateAsync(request, ct)).ToHttpResult())
            .Validate<SaveWarehouseRequest>().WithName("CreateWarehouse").WithSummary("Add a warehouse.").Produces<WarehouseView>();
        manage.MapPut("/warehouses/{id:guid}", async (Guid id, SaveWarehouseRequest request, WarehouseService warehouses, CancellationToken ct) =>
                (await warehouses.UpdateAsync(id, request, ct)).ToHttpResult())
            .Validate<SaveWarehouseRequest>().WithName("UpdateWarehouse")
            .WithSummary("Change a warehouse; make it the default, or deactivate one that holds no stock.").Produces<WarehouseView>();
        manage.MapDelete("/warehouses/{id:guid}", async (Guid id, WarehouseService warehouses, CancellationToken ct) =>
                (await warehouses.DeleteAsync(id, ct)).ToHttpResult())
            .WithName("DeleteWarehouse").WithSummary("Delete a warehouse that never had a movement.");

        view.MapGet("/stock", async (string? search, bool? onlyOut, int? page, string? locale, InventoryService inventory, CancellationToken ct) =>
                TypedResults.Ok(await inventory.StockAsync(search, onlyOut ?? false, page ?? 1, locale, ct)))
            .WithName("ListStock").WithSummary("Stock per item (a product, or each of its variants) and warehouse, 20 products a page.");
        view.MapGet("/movements", async (string? sku, Guid? warehouseId, StockMovementKind? kind, int? page, string? locale, InventoryService inventory, CancellationToken ct) =>
                TypedResults.Ok(await inventory.MovementsAsync(sku, warehouseId, kind, page ?? 1, locale, ct)))
            .WithName("ListStockMovements").WithSummary("The stock ledger, newest first, 50 lines a page.");
        manage.MapPost("/movements", async (PostMovementRequest request, string? locale, InventoryService inventory, CancellationToken ct) =>
                (await inventory.PostAsync(request, locale, ct)).ToHttpResult())
            .Validate<PostMovementRequest>().WithName("PostStockMovement")
            .WithSummary("Enter a receipt, an issue or an adjustment; returns the item's stock afterwards.").Produces<StockRow>();
        manage.MapPost("/transfers", async (TransferStockRequest request, string? locale, InventoryService inventory, CancellationToken ct) =>
                (await inventory.TransferAsync(request, locale, ct)).ToHttpResult())
            .Validate<TransferStockRequest>().WithName("TransferStock")
            .WithSummary("Move stock of one item between two warehouses; returns the item's stock afterwards.").Produces<StockRow>();

        return app;
    }
}
