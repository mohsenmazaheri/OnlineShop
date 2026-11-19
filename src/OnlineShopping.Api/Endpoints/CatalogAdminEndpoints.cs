using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OnlineShopping.Api.Common;
using OnlineShopping.Application.Authorization;
using OnlineShopping.Application.Catalog.Admin;
using OnlineShopping.Application.Common;

namespace OnlineShopping.Api.Endpoints;

/// <summary>Catalog management for staff with catalog.manage: the category tree and products.</summary>
public static class CatalogAdminEndpoints
{
    public static IEndpointRouteBuilder MapCatalogAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var tree = app.MapGroup("/api/v1/admin/catalog").WithTags("Admin: catalog")
            .RequireAuthorization(Permissions.Policy(Permissions.CatalogManage));

        tree.MapGet("/", async (CatalogTreeService catalog, CancellationToken ct) => TypedResults.Ok(await catalog.GetTreeAsync(ct)))
            .WithName("GetCatalogTree")
            .WithSummary("Categories, sub-categories and product groups with their texts in every shop language.");

        tree.MapPost("/categories", async (SaveCatalogNodeRequest request, CatalogTreeService catalog, CancellationToken ct) =>
                (await catalog.CreateCategoryAsync(request, ct)).ToHttpResult())
            .Validate<SaveCatalogNodeRequest>().WithName("CreateCategory").WithSummary("Add a category.").Produces<CatalogNodeView>();
        tree.MapPut("/categories/{id:guid}", async (Guid id, SaveCatalogNodeRequest request, CatalogTreeService catalog, CancellationToken ct) =>
                (await catalog.UpdateCategoryAsync(id, request, ct)).ToHttpResult())
            .Validate<SaveCatalogNodeRequest>().WithName("UpdateCategory").WithSummary("Rename a category.").Produces<CatalogNodeView>();
        tree.MapDelete("/categories/{id:guid}", async (Guid id, CatalogTreeService catalog, CancellationToken ct) =>
                (await catalog.DeleteCategoryAsync(id, ct)).ToHttpResult())
            .WithName("DeleteCategory").WithSummary("Delete an empty category.");

        tree.MapPost("/categories/{id:guid}/subcategories", async (Guid id, SaveCatalogNodeRequest request, CatalogTreeService catalog, CancellationToken ct) =>
                (await catalog.CreateSubCategoryAsync(id, request, ct)).ToHttpResult())
            .Validate<SaveCatalogNodeRequest>().WithName("CreateSubCategory").WithSummary("Add a sub-category.").Produces<CatalogNodeView>();
        tree.MapPut("/subcategories/{id:guid}", async (Guid id, SaveCatalogNodeRequest request, CatalogTreeService catalog, CancellationToken ct) =>
                (await catalog.UpdateSubCategoryAsync(id, request, ct)).ToHttpResult())
            .Validate<SaveCatalogNodeRequest>().WithName("UpdateSubCategory").WithSummary("Rename a sub-category.").Produces<CatalogNodeView>();
        tree.MapDelete("/subcategories/{id:guid}", async (Guid id, CatalogTreeService catalog, CancellationToken ct) =>
                (await catalog.DeleteSubCategoryAsync(id, ct)).ToHttpResult())
            .WithName("DeleteSubCategory").WithSummary("Delete an empty sub-category.");

        tree.MapPost("/subcategories/{id:guid}/groups", async (Guid id, SaveCatalogNodeRequest request, CatalogTreeService catalog, CancellationToken ct) =>
                (await catalog.CreateGroupAsync(id, request, ct)).ToHttpResult())
            .Validate<SaveCatalogNodeRequest>().WithName("CreateProductGroup").WithSummary("Add a product group.").Produces<CatalogNodeView>();
        tree.MapPut("/groups/{id:guid}", async (Guid id, SaveCatalogNodeRequest request, CatalogTreeService catalog, CancellationToken ct) =>
                (await catalog.UpdateGroupAsync(id, request, ct)).ToHttpResult())
            .Validate<SaveCatalogNodeRequest>().WithName("UpdateProductGroup").WithSummary("Rename a product group.").Produces<CatalogNodeView>();
        tree.MapDelete("/groups/{id:guid}", async (Guid id, CatalogTreeService catalog, CancellationToken ct) =>
                (await catalog.DeleteGroupAsync(id, ct)).ToHttpResult())
            .WithName("DeleteProductGroup").WithSummary("Delete a product group without products.");

        var products = app.MapGroup("/api/v1/admin/products").WithTags("Admin: catalog")
            .RequireAuthorization(Permissions.Policy(Permissions.CatalogManage));

        products.MapGet("/", async (string? search, Guid? groupId, int? page, string? locale, ProductAdminService admin, IOptions<StoreOptions> store, CancellationToken ct) =>
                TypedResults.Ok(await admin.ListAsync(search, groupId, page ?? 1, store.Value.ContentLocale(locale), ct)))
            .WithName("ListAdminProducts")
            .WithSummary("Products (published or not), newest first, with search by name, SKU or number and a group filter.");

        products.MapGet("/specs", async (string? locale, ProductAdminService admin, IOptions<StoreOptions> store, CancellationToken ct) =>
                TypedResults.Ok(await admin.GetSpecsAsync(store.Value.ContentLocale(locale), ct)))
            .WithName("GetProductSpecChoices")
            .WithSummary("The specs a product can have, with their options, for the product editor.");

        products.MapGet("/{number:long}", async (long number, ProductAdminService admin, CancellationToken ct) => (await admin.GetAsync(number, ct)).ToHttpResult())
            .WithName("GetAdminProduct").WithSummary("A product for editing (every language, spec values, images).").Produces<ProductEditView>();
        products.MapPost("/", async (SaveProductRequest request, ProductAdminService admin, CancellationToken ct) => (await admin.CreateAsync(request, ct)).ToHttpResult())
            .Validate<SaveProductRequest>().WithName("CreateProduct").WithSummary("Add a product (it gets the next public number).").Produces<ProductEditView>();
        products.MapPut("/{number:long}", async (long number, SaveProductRequest request, ProductAdminService admin, CancellationToken ct) =>
                (await admin.UpdateAsync(number, request, ct)).ToHttpResult())
            .Validate<SaveProductRequest>().WithName("UpdateProduct").WithSummary("Change a product.").Produces<ProductEditView>();
        products.MapDelete("/{number:long}", async (long number, ProductAdminService admin, CancellationToken ct) => (await admin.DeleteAsync(number, ct)).ToHttpResult())
            .WithName("DeleteProduct").WithSummary("Remove a product from the shop (its number is never reused).");

        // Uploads: multipart with one "file"; the Origin check (OriginCheckMiddleware) protects them against CSRF.
        products.MapPost("/{number:long}/images", async (long number, IFormFile file, CatalogImageService images, CancellationToken ct) =>
            {
                await using var stream = file.OpenReadStream();
                return (await images.AddProductImageAsync(number, stream, file.Length, ct)).ToHttpResult();
            })
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(MediaEndpoints.MaxRequestBytes))
            .WithName("AddProductImage")
            .WithSummary("Upload a product image (JPEG, PNG, WebP or GIF; checked, re-encoded to WebP in three sizes).")
            .Produces<ProductImageView>();

        products.MapPut("/{number:long}/images", async (long number, SaveProductImagesRequest request, CatalogImageService images, CancellationToken ct) =>
                (await images.SaveProductImagesAsync(number, request, ct)).ToHttpResult())
            .WithName("SaveProductImages")
            .WithSummary("Reorder a product's images (the first is the main one) and save their alternative texts.");

        products.MapDelete("/{number:long}/images/{id:guid}", async (long number, Guid id, CatalogImageService images, CancellationToken ct) =>
                (await images.DeleteProductImageAsync(number, id, ct)).ToHttpResult())
            .WithName("DeleteProductImage")
            .WithSummary("Remove a product image and its stored files.");

        tree.MapPost("/categories/{id:guid}/image", async (Guid id, IFormFile file, CatalogImageService images, CancellationToken ct) =>
            {
                await using var stream = file.OpenReadStream();
                return (await images.SetCategoryImageAsync(id, stream, file.Length, ct)).ToHttpResult();
            })
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(MediaEndpoints.MaxRequestBytes))
            .WithName("SetCategoryImage")
            .WithSummary("Upload a category's tile image (replaces the previous one).");

        tree.MapDelete("/categories/{id:guid}/image", async (Guid id, CatalogImageService images, CancellationToken ct) =>
                (await images.RemoveCategoryImageAsync(id, ct)).ToHttpResult())
            .WithName("RemoveCategoryImage")
            .WithSummary("Remove a category's tile image.");

        return app;
    }
}
