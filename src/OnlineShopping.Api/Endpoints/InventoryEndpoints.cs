using Microsoft.AspNetCore.Mvc;
using OnlineShopping.Api.Common;
using OnlineShopping.Application.Authorization;
using OnlineShopping.Application.Catalog.Transfer;
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

        view.MapGet("/stocktakes", async (StocktakeService stocktakes, CancellationToken ct) => TypedResults.Ok(await stocktakes.ListAsync(ct)))
            .WithName("ListStocktakes").WithSummary("Every stocktake, newest first, with how many items were counted.");
        view.MapGet("/stocktakes/{number:int}", async (int number, StocktakeLineFilter? filter, string? search, int? page, string? locale, StocktakeService stocktakes, CancellationToken ct) =>
                (await stocktakes.GetAsync(number, filter ?? StocktakeLineFilter.All, search, page ?? 1, locale, ct)).ToHttpResult())
            .WithName("GetStocktake").WithSummary("A stocktake with its totals and 50 lines a page (all, uncounted, counted or with a difference).").Produces<StocktakeView>();
        view.MapGet("/stocktakes/{number:int}/sheet", async (int number, string? format, string? locale, StocktakeService stocktakes, CancellationToken ct) =>
            {
                var sheet = await stocktakes.SheetAsync(number, format ?? "xlsx", locale, ct);
                return sheet.IsSuccess ? TypedResults.File(sheet.Value.Content, sheet.Value.ContentType, sheet.Value.FileName) : sheet.ToHttpResult();
            })
            .WithName("StocktakeSheet").WithSummary("The count sheet (SKU, name, an empty counted column) as CSV or Excel.");
        manage.MapPost("/stocktakes", async (OpenStocktakeRequest request, StocktakeService stocktakes, CancellationToken ct) =>
                (await stocktakes.OpenAsync(request, ct)).ToHttpResult())
            .Validate<OpenStocktakeRequest>().WithName("OpenStocktake")
            .WithSummary("Start counting a warehouse: every item with its stock now (one open count per warehouse).").Produces<StocktakeSummary>();
        manage.MapPost("/stocktakes/{number:int}/counts", async (int number, CountRequest request, string? locale, StocktakeService stocktakes, CancellationToken ct) =>
                (await stocktakes.CountAsync(number, request, locale, ct)).ToHttpResult())
            .Validate<CountRequest>().WithName("CountStocktake")
            .WithSummary("Record counts by SKU (add=true adds to the count so far); returns those lines.").Produces<List<StocktakeLineView>>();
        manage.MapPost("/stocktakes/{number:int}/counts/import", async (int number, IFormFile file, StocktakeService stocktakes, CancellationToken ct) =>
            {
                await using var stream = file.OpenReadStream();
                return (await stocktakes.ImportCountsAsync(number, stream, file.Length, ct)).ToHttpResult();
            })
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(ProductImportService.MaxBytes + 1024 * 1024))
            .WithName("ImportStocktakeCounts")
            .WithSummary("Record counts from a filled-in sheet (sku, counted); saved only when every row is valid.").Produces<ImportReport>();
        manage.MapPost("/stocktakes/{number:int}/post", async (int number, StocktakeService stocktakes, CancellationToken ct) =>
                (await stocktakes.PostAsync(number, ct)).ToHttpResult())
            .WithName("PostStocktake").WithSummary("Write the differences to the ledger as adjustments and close the stocktake.").Produces<StocktakeSummary>();
        manage.MapPost("/stocktakes/{number:int}/cancel", async (int number, StocktakeService stocktakes, CancellationToken ct) =>
                (await stocktakes.CancelAsync(number, ct)).ToHttpResult())
            .WithName("CancelStocktake").WithSummary("Drop an open stocktake without changing the stock.").Produces<StocktakeSummary>();

        return app;
    }
}
