using System.Security.Claims;
using Microsoft.Extensions.Options;
using OnlineShopping.Api.Common;
using OnlineShopping.Api.Security;
using OnlineShopping.Application.Authorization;
using OnlineShopping.Application.Catalog.Reviews;
using OnlineShopping.Application.Common;
using OnlineShopping.Application.Customers;
using OnlineShopping.Domain.Catalog;

namespace OnlineShopping.Api.Endpoints;

/// <summary>Product reviews (public list, the customer's own, moderation) and the customer's wishlist.</summary>
public static class ReviewEndpoints
{
    public const string ReviewRateLimitPolicy = "reviews";

    public static IEndpointRouteBuilder MapReviewEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/catalog/products/{number:long}/reviews", async (long number, int? page, ReviewService reviews, CancellationToken ct) =>
                (await reviews.ListAsync(number, page ?? 1, ct)).ToHttpResult())
            .WithTags("Storefront")
            .WithName("ListProductReviews")
            .WithSummary("A page of the product's approved reviews (newest first) with its rating summary.")
            .Produces<ProductReviews>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        var mine = app.MapGroup("/api/v1/account/reviews").WithTags("Account");
        // A guest gets signedIn=false instead of 401 (the review form asks when opened).
        mine.MapGet("/{number:long}", async (long number, ClaimsPrincipal user, ReviewService reviews, CancellationToken ct) =>
                user.Identity?.IsAuthenticated == true
                    ? (await reviews.MineAsync(user.GetUserId(), number, ct)).ToHttpResult()
                    : TypedResults.Ok(new MyReviewState(false, false, null)))
            .WithName("GetMyReview")
            .WithSummary("Whether the visitor may review the product, and their own review if any.")
            .Produces<MyReviewState>();
        mine.MapPut("/{number:long}", async (long number, string? locale, ReviewInput input, ClaimsPrincipal user, ReviewService reviews, IOptions<StoreOptions> store, CancellationToken ct) =>
                (await reviews.SubmitAsync(user.GetUserId(), number, input, store.Value.ContentLocale(locale), ct)).ToHttpResult())
            .RequireAuthorization()
            .RequireRateLimiting(ReviewRateLimitPolicy)
            .Validate<ReviewInput>()
            .WithName("SubmitReview")
            .WithSummary("Write or replace the customer's review of the product (it waits for moderation).")
            .Produces<MyReview>();
        mine.MapDelete("/{number:long}", async (long number, ClaimsPrincipal user, ReviewService reviews, CancellationToken ct) =>
                (await reviews.DeleteMineAsync(user.GetUserId(), number, ct)).ToHttpResult())
            .RequireAuthorization()
            .WithName("DeleteMyReview")
            .WithSummary("Remove the customer's own review of the product.");

        var admin = app.MapGroup("/api/v1/admin/reviews").WithTags("Admin: reviews")
            .RequireAuthorization(Permissions.Policy(Permissions.ReviewsModerate));
        admin.MapGet("/", async (ReviewStatus? status, int? page, ReviewService reviews, CancellationToken ct) =>
                TypedResults.Ok(await reviews.AdminListAsync(status, page ?? 1, ct)))
            .WithName("ListReviewsForModeration")
            .WithSummary("Reviews of one status (all when empty) for moderation, with the number waiting.");
        admin.MapPut("/{id:guid}", async (Guid id, ModerateReviewRequest request, ReviewService reviews, CancellationToken ct) =>
                (await reviews.ModerateAsync(id, request.Status, ct)).ToHttpResult())
            .WithName("ModerateReview")
            .WithSummary("Approve or reject a review; the product's rating follows.")
            .Produces<ReviewAdminRow>();

        var wishlist = app.MapGroup("/api/v1/account/wishlist").WithTags("Account");
        wishlist.MapGet("/ids", async (ClaimsPrincipal user, WishlistService lists, CancellationToken ct) =>
                TypedResults.Ok(user.Identity?.IsAuthenticated == true ? await lists.IdsAsync(user.GetUserId(), ct) : new WishlistIds([], SignedIn: false)))
            .WithName("GetWishlistIds")
            .WithSummary("Numbers of the products on the visitor's wishlist (signedIn=false for a guest).");
        wishlist.MapGet("/", async (string? locale, ClaimsPrincipal user, WishlistService lists, IOptions<StoreOptions> store, CancellationToken ct) =>
                TypedResults.Ok(await lists.ListAsync(user.GetUserId(), store.Value.ContentLocale(locale), ct)))
            .RequireAuthorization()
            .WithName("ListWishlist")
            .WithSummary("The signed-in customer's wishlist as product cards, the most recently added first.");
        wishlist.MapPut("/{number:long}", async (long number, ClaimsPrincipal user, WishlistService lists, CancellationToken ct) =>
                (await lists.AddAsync(user.GetUserId(), number, ct)).ToHttpResult())
            .RequireAuthorization()
            .WithName("AddToWishlist")
            .WithSummary("Add a product to the wishlist (no change when it is there already).");
        wishlist.MapDelete("/{number:long}", async (long number, ClaimsPrincipal user, WishlistService lists, CancellationToken ct) =>
                TypedResults.Ok(await lists.RemoveAsync(user.GetUserId(), number, ct)))
            .RequireAuthorization()
            .WithName("RemoveFromWishlist")
            .WithSummary("Take a product off the wishlist.");

        return app;
    }
}
