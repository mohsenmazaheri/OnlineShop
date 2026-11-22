using Microsoft.Extensions.Options;
using OnlineShopping.Api.Common;
using OnlineShopping.Api.Security;
using OnlineShopping.Application.Accounts;
using OnlineShopping.Application.Common;
using OnlineShopping.Application.Sales;

namespace OnlineShopping.Api.Endpoints;

/// <summary>
/// The visitor's cart. A guest gets a random cart token in an HttpOnly cookie on the first add; after signing in, the
/// next cart request merges the guest cart into the account's and drops that cookie. Every answer also sets a small
/// readable cookie with the number of pieces, so the shop's header shows the count without asking the API.
/// </summary>
public static class CartEndpoints
{
    public const string GuestCookie = "os_cart";
    public const string CountCookie = "os_cart_n";

    public static IEndpointRouteBuilder MapCartEndpoints(this IEndpointRouteBuilder app)
    {
        var cart = app.MapGroup("/api/v1/cart").WithTags("Cart");

        cart.MapGet("/", async (string? locale, HttpContext http, CartService carts, IOptions<StoreOptions> store, IOptions<AuthOptions> auth, CancellationToken ct) =>
            {
                var owner = await OwnerAsync(http, carts, auth.Value, create: false, ct);
                return Answer(http, auth.Value, owner.IsEmpty ? EmptyCart(store.Value) : await carts.GetAsync(owner, store.Value.ContentLocale(locale), ct));
            })
            .WithName("GetCart").WithSummary("The visitor's cart with current prices and stock (a guest's merges into the account after signing in).");

        cart.MapPost("/items", async (AddToCartRequest request, string? locale, HttpContext http, CartService carts, IOptions<StoreOptions> store, IOptions<AuthOptions> auth, CancellationToken ct) =>
            {
                var owner = await OwnerAsync(http, carts, auth.Value, create: true, ct);
                var result = await carts.AddAsync(owner, request, store.Value.ContentLocale(locale), ct);
                return result.IsSuccess ? Answer(http, auth.Value, result.Value) : ResultExtensions.ToProblem(result.Error!);
            })
            .Validate<AddToCartRequest>()
            .WithName("AddToCart").WithSummary("Add a product (number) or a variant (SKU) to the cart; never more than is available.")
            .Produces<CartView>();

        cart.MapPut("/items/{id:guid}", async (Guid id, SetQuantityRequest request, string? locale, HttpContext http, CartService carts, IOptions<StoreOptions> store, IOptions<AuthOptions> auth, CancellationToken ct) =>
            {
                var owner = await OwnerAsync(http, carts, auth.Value, create: false, ct);
                var result = await carts.SetQuantityAsync(owner, id, request.Quantity, store.Value.ContentLocale(locale), ct);
                return result.IsSuccess ? Answer(http, auth.Value, result.Value) : ResultExtensions.ToProblem(result.Error!);
            })
            .Validate<SetQuantityRequest>()
            .WithName("SetCartQuantity").WithSummary("Change a line's quantity (0 removes it).")
            .Produces<CartView>();

        cart.MapDelete("/items/{id:guid}", async (Guid id, string? locale, HttpContext http, CartService carts, IOptions<StoreOptions> store, IOptions<AuthOptions> auth, CancellationToken ct) =>
            {
                var owner = await OwnerAsync(http, carts, auth.Value, create: false, ct);
                var result = await carts.SetQuantityAsync(owner, id, 0, store.Value.ContentLocale(locale), ct);
                return result.IsSuccess ? Answer(http, auth.Value, result.Value) : ResultExtensions.ToProblem(result.Error!);
            })
            .WithName("RemoveFromCart").WithSummary("Remove a line from the cart.")
            .Produces<CartView>();

        return app;
    }

    private static CartView EmptyCart(StoreOptions store) => new([], 0, 0, store.Currency, false);

    /// <summary>
    /// Whose cart this request is about: the signed-in customer's (merging a guest cart left in the cookie), else the
    /// guest's from the cookie, created on the first add.
    /// </summary>
    private static async Task<CartOwner> OwnerAsync(HttpContext http, CartService carts, AuthOptions auth, bool create, CancellationToken ct)
    {
        var userId = http.User.Identity?.IsAuthenticated == true ? http.User.GetUserId() : null;
        var token = http.Request.Cookies[GuestCookie];
        if (token is { Length: > 64 })
            token = null;
        if (userId is not null)
        {
            if (!string.IsNullOrEmpty(token))
            {
                await carts.MergeAsync(userId, token, ct);
                http.Response.Cookies.Delete(GuestCookie, Options(auth, httpOnly: true));
            }
            return new CartOwner(userId, null);
        }
        if (string.IsNullOrEmpty(token) && create)
        {
            token = CartService.NewGuestToken();
            http.Response.Cookies.Append(GuestCookie, token, Options(auth, httpOnly: true));
        }
        return new CartOwner(null, string.IsNullOrEmpty(token) ? null : token);
    }

    private static Microsoft.AspNetCore.Http.HttpResults.Ok<CartView> Answer(HttpContext http, AuthOptions auth, CartView view)
    {
        if (view.ItemCount > 0)
            http.Response.Cookies.Append(CountCookie, view.ItemCount.ToString(System.Globalization.CultureInfo.InvariantCulture), Options(auth, httpOnly: false));
        else
            http.Response.Cookies.Delete(CountCookie, Options(auth, httpOnly: false));
        return TypedResults.Ok(view);
    }

    private static CookieOptions Options(AuthOptions auth, bool httpOnly) => new()
    {
        HttpOnly = httpOnly,
        Secure = auth.SecureCookies,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        MaxAge = TimeSpan.FromDays(60),
        IsEssential = true,
    };
}
