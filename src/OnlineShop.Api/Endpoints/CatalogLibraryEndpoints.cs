using Microsoft.AspNetCore.Mvc;
using OnlineShop.Api.Common;
using OnlineShop.Application.Authorization;
using OnlineShop.Application.Catalog.Admin;

namespace OnlineShop.Api.Endpoints;

/// <summary>Brands and the spec library for staff with catalog.manage.</summary>
public static class CatalogLibraryEndpoints
{
    public static IEndpointRouteBuilder MapCatalogLibraryEndpoints(this IEndpointRouteBuilder app)
    {
        var brands = app.MapGroup("/api/v1/admin/brands").WithTags("Admin: catalog")
            .RequireAuthorization(Permissions.Policy(Permissions.CatalogManage));

        brands.MapGet("/", async (BrandAdminService admin, CancellationToken ct) => TypedResults.Ok(await admin.ListAsync(ct)))
            .WithName("ListBrands").WithSummary("Every brand with its texts in every shop language and its product count.");
        brands.MapPost("/", async (SaveBrandRequest request, BrandAdminService admin, CancellationToken ct) => (await admin.CreateAsync(request, ct)).ToHttpResult())
            .Validate<SaveBrandRequest>().WithName("CreateBrand").WithSummary("Add a brand.").Produces<BrandView>();
        brands.MapPut("/{id:guid}", async (Guid id, SaveBrandRequest request, BrandAdminService admin, CancellationToken ct) =>
                (await admin.UpdateAsync(id, request, ct)).ToHttpResult())
            .Validate<SaveBrandRequest>().WithName("UpdateBrand").WithSummary("Change a brand's texts.").Produces<BrandView>();
        brands.MapDelete("/{id:guid}", async (Guid id, BrandAdminService admin, CancellationToken ct) => (await admin.DeleteAsync(id, ct)).ToHttpResult())
            .WithName("DeleteBrand").WithSummary("Delete a brand no product uses.");

        brands.MapPost("/{id:guid}/logo", async (Guid id, IFormFile file, BrandAdminService admin, CancellationToken ct) =>
            {
                await using var stream = file.OpenReadStream();
                return (await admin.SetLogoAsync(id, stream, file.Length, ct)).ToHttpResult();
            })
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(MediaEndpoints.MaxRequestBytes))
            .WithName("SetBrandLogo")
            .WithSummary("Upload a brand's logo (replaces the previous one).");
        brands.MapDelete("/{id:guid}/logo", async (Guid id, BrandAdminService admin, CancellationToken ct) => (await admin.RemoveLogoAsync(id, ct)).ToHttpResult())
            .WithName("RemoveBrandLogo").WithSummary("Remove a brand's logo.");

        var specs = app.MapGroup("/api/v1/admin/specs").WithTags("Admin: catalog")
            .RequireAuthorization(Permissions.Policy(Permissions.CatalogManage));

        specs.MapGet("/", async (SpecAdminService admin, CancellationToken ct) => TypedResults.Ok(await admin.ListAsync(ct)))
            .WithName("ListSpecs").WithSummary("The spec library: every spec with its texts, options and usage, in display order.");
        specs.MapPost("/", async (SaveSpecRequest request, SpecAdminService admin, CancellationToken ct) => (await admin.CreateAsync(request, ct)).ToHttpResult())
            .Validate<SaveSpecRequest>().WithName("CreateSpec").WithSummary("Add a spec (added at the end of the order).").Produces<SpecView>();
        specs.MapPut("/{id:guid}", async (Guid id, SaveSpecRequest request, SpecAdminService admin, CancellationToken ct) =>
                (await admin.UpdateAsync(id, request, ct)).ToHttpResult())
            .Validate<SaveSpecRequest>().WithName("UpdateSpec").WithSummary("Change a spec and its options.").Produces<SpecView>();
        specs.MapDelete("/{id:guid}", async (Guid id, SpecAdminService admin, CancellationToken ct) => (await admin.DeleteAsync(id, ct)).ToHttpResult())
            .WithName("DeleteSpec").WithSummary("Delete a spec no product or variant uses.");
        specs.MapPut("/order", async (ReorderSpecsRequest request, SpecAdminService admin, CancellationToken ct) => TypedResults.Ok(await admin.ReorderAsync(request, ct)))
            .WithName("ReorderSpecs").WithSummary("Set the display order of the specs.");

        return app;
    }
}
