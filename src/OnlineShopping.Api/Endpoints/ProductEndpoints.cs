using Microsoft.Extensions.Options;
using OnlineShopping.Api.Common;
using OnlineShopping.Application.Catalog;
using OnlineShopping.Application.Common;

namespace OnlineShopping.Api.Endpoints;

/// <summary>The product's slug in the requested language (the storefront redirects any other slug to it).</summary>
public sealed record ProductSlugResponse(string Slug);

/// <summary>Public product endpoints (no authentication). Only published products are visible.</summary>
public static class ProductEndpoints
{
    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/catalog/products").WithTags("Storefront");

        group.MapGet("/", async (ProductList list, string? locale, int? take, IProductQueries queries, IOptions<StoreOptions> store, CancellationToken ct) =>
                TypedResults.Ok(await queries.ListAsync(list, store.Value.ContentLocale(locale), take ?? 12, ct)))
            .WithName("ListProducts")
            .WithSummary("Published products for a list (discounted, newest), named in the requested locale.");

        group.MapGet("/{number:long}", async (long number, string? locale, IProductQueries queries, IOptions<StoreOptions> store, CancellationToken ct) =>
                (await queries.GetAsync(number, store.Value.ContentLocale(locale), store.Value.DefaultLocale, ct)).ToHttpResult())
            .WithName("GetProduct")
            .WithSummary("One published product by its public number, with images, specs and its slug in every translated language.")
            .Produces<ProductDetail>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{number:long}/slug", async (long number, string? locale, IProductQueries queries, IOptions<StoreOptions> store, CancellationToken ct) =>
            {
                var result = await queries.GetSlugAsync(number, store.Value.ContentLocale(locale), ct);
                return result.IsSuccess ? TypedResults.Ok(new ProductSlugResponse(result.Value)) : ResultExtensions.ToProblem(result.Error!);
            })
            .WithName("GetProductSlug")
            .WithSummary("The product's slug in the requested locale, for redirecting old or wrong product addresses.")
            .Produces<ProductSlugResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
