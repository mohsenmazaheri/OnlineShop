using System.Security.Claims;
using Microsoft.Extensions.Options;
using OnlineShop.Api.Common;
using OnlineShop.Api.Security;
using OnlineShop.Application.Catalog.Compare;
using OnlineShop.Application.Common;

namespace OnlineShop.Api.Endpoints;

/// <summary>Product comparison: the public table, and a signed-in customer's saved list (guests keep theirs in the browser).</summary>
public static class CompareEndpoints
{
    public static IEndpointRouteBuilder MapCompareEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/catalog/compare", async (string? numbers, string? locale, ProductComparisonService comparison, IOptions<StoreOptions> store, CancellationToken ct) =>
                TypedResults.Ok(await comparison.CompareAsync(ProductComparisonService.ParseNumbers(numbers), store.Value.ContentLocale(locale), ct)))
            .WithTags("Storefront")
            .WithName("CompareProducts")
            .WithSummary("Up to four published products (numbers=12,15) side by side: current price and stock, and their comparable specs by group.");

        var mine = app.MapGroup("/api/v1/account/compare").WithTags("Account");
        // A guest gets an empty answer instead of 401: the browser asks on page views, and a 401 would be logged as an error.
        mine.MapGet("/", async (ClaimsPrincipal user, CompareListService lists, CancellationToken ct) =>
                TypedResults.Ok(user.Identity?.IsAuthenticated == true ? await lists.GetAsync(user.GetUserId(), ct) : new CompareListView([], SignedIn: false)))
            .WithName("GetCompareList")
            .WithSummary("The signed-in customer's comparison list (product numbers in the order they were added); signedIn=false for a guest.");
        mine.MapPut("/", async (SaveCompareListRequest request, ClaimsPrincipal user, CompareListService lists, CancellationToken ct) =>
                TypedResults.Ok(await lists.SaveAsync(user.GetUserId(), request.Numbers, ct)))
            .RequireAuthorization()
            .Validate<SaveCompareListRequest>()
            .WithName("SaveCompareList")
            .WithSummary("Replace the signed-in customer's comparison list (published products only, at most four).");

        return app;
    }
}
