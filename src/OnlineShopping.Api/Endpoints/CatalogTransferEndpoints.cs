using Microsoft.AspNetCore.Mvc;
using OnlineShopping.Api.Common;
using OnlineShopping.Application.Authorization;
using OnlineShopping.Application.Catalog.Transfer;

namespace OnlineShopping.Api.Endpoints;

/// <summary>Bulk export and import of products and stock as CSV or Excel (catalog.manage).</summary>
public static class CatalogTransferEndpoints
{
    /// <summary>The 10 MB file plus the multipart envelope.</summary>
    private const long MaxRequestBytes = ProductImportService.MaxBytes + 1024 * 1024;

    public static IEndpointRouteBuilder MapCatalogTransferEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/catalog").WithTags("Admin: catalog")
            .RequireAuthorization(Permissions.Policy(Permissions.CatalogManage));

        group.MapGet("/export/products", async (string? format, ProductExportService export, CancellationToken ct) =>
                ToFile(await export.ProductsAsync(format ?? "xlsx", ct)))
            .WithName("ExportProducts").WithSummary("Every product as CSV or Excel (format=csv|xlsx), in the layout the import reads.");
        group.MapGet("/export/stock", async (string? format, ProductExportService export, CancellationToken ct) =>
                ToFile(await export.StockAsync(format ?? "xlsx", ct)))
            .WithName("ExportStock").WithSummary("SKU and stock of every product without variants and of every variant.");

        group.MapPost("/import/products", async (IFormFile file, bool? dryRun, ProductImportService import, CancellationToken ct) =>
            {
                await using var stream = file.OpenReadStream();
                return (await import.ProductsAsync(stream, file.Length, dryRun ?? false, ct)).ToHttpResult();
            })
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            .WithName("ImportProducts")
            .WithSummary("Create or update products from CSV or Excel; with dryRun=true only check. Saved only when every row is valid.")
            .Produces<ImportReport>();
        group.MapPost("/import/stock", async (IFormFile file, bool? dryRun, ProductImportService import, CancellationToken ct) =>
            {
                await using var stream = file.OpenReadStream();
                return (await import.StockAsync(stream, file.Length, dryRun ?? false, ct)).ToHttpResult();
            })
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            .WithName("ImportStock")
            .WithSummary("Set stock by SKU (products and variants) from CSV or Excel; with dryRun=true only check.")
            .Produces<ImportReport>();

        return app;
    }

    private static IResult ToFile(Application.Common.Result<ExportFile> result) =>
        result.IsSuccess ? TypedResults.File(result.Value.Content, result.Value.ContentType, result.Value.FileName) : result.ToHttpResult();
}
