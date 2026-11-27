using Microsoft.Extensions.Options;
using OnlineShop.Api.Common;
using OnlineShop.Application.Authorization;
using OnlineShop.Application.Common;
using OnlineShop.Application.Sales;
using OnlineShop.Application.Accounts;

namespace OnlineShop.Api.Endpoints;

/// <summary>
/// Checkout (the cart's owner as in <see cref="CartEndpoints"/>: the signed-in customer or the guest cookie), and the
/// admin's shipping methods, tax rate and discount codes.
/// </summary>
public static class CheckoutEndpoints
{
    public const string CheckoutRateLimitPolicy = "checkout";

    public static IEndpointRouteBuilder MapCheckoutEndpoints(this IEndpointRouteBuilder app)
    {
        var checkout = app.MapGroup("/api/v1/checkout").WithTags("Checkout");

        checkout.MapGet("/", async (string? locale, HttpContext http, CheckoutService service, CartService carts, IOptions<StoreOptions> store, IOptions<AuthOptions> auth, CancellationToken ct) =>
            {
                var owner = await CartEndpoints.OwnerAsync(http, carts, auth.Value, create: false, ct);
                return TypedResults.Ok(await service.GetAsync(owner, store.Value.ContentLocale(locale), ct));
            })
            .WithName("GetCheckout").WithSummary("What checkout starts with: the cart, saved addresses, countries, shipping methods and tax.");

        checkout.MapPost("/quote", async (CheckoutQuoteRequest request, string? locale, HttpContext http, CheckoutService service, CartService carts, IOptions<StoreOptions> store, IOptions<AuthOptions> auth, CancellationToken ct) =>
            {
                var owner = await CartEndpoints.OwnerAsync(http, carts, auth.Value, create: false, ct);
                return TypedResults.Ok(await service.QuoteAsync(owner, request, store.Value.ContentLocale(locale), ct));
            })
            .WithName("QuoteCheckout").WithSummary("The totals for a country, shipping method and discount code (a refused code is reported, not failed).");

        checkout.MapPost("/orders", async (PlaceOrderRequest request, string? locale, HttpContext http, CheckoutService service, CartService carts, IOptions<StoreOptions> store, IOptions<AuthOptions> auth, CancellationToken ct) =>
            {
                var owner = await CartEndpoints.OwnerAsync(http, carts, auth.Value, create: false, ct);
                var result = await service.PlaceAsync(owner, request, store.Value.ContentLocale(locale), ct);
                if (result.IsSuccess)
                    CartEndpoints.Forget(http, auth.Value);
                return result.ToHttpResult();
            })
            .Validate<PlaceOrderRequest>()
            .RequireRateLimiting(CheckoutRateLimitPolicy)
            .WithName("PlaceOrder").WithSummary("Place the order: reserves the stock, counts the discount code and empties the cart. Payment follows (phase 8).")
            .Produces<PlacedOrderView>();

        var shipping = app.MapGroup("/api/v1/admin/shipping").WithTags("Admin: shipping and tax")
            .RequireAuthorization(Permissions.Policy(Permissions.SettingsManage));
        shipping.MapGet("/", async (ShippingMethodService methods, CancellationToken ct) => TypedResults.Ok(await methods.ListAsync(ct)))
            .WithName("ListShippingMethods").WithSummary("All shipping methods, active or not, in checkout order.");
        shipping.MapPost("/", async (SaveShippingMethodRequest request, ShippingMethodService methods, CancellationToken ct) => (await methods.CreateAsync(request, ct)).ToHttpResult())
            .Validate<SaveShippingMethodRequest>()
            .WithName("CreateShippingMethod").WithSummary("Add a shipping method.").Produces<ShippingMethodAdminView>();
        shipping.MapPut("/{id:guid}", async (Guid id, SaveShippingMethodRequest request, ShippingMethodService methods, CancellationToken ct) => (await methods.UpdateAsync(id, request, ct)).ToHttpResult())
            .Validate<SaveShippingMethodRequest>()
            .WithName("UpdateShippingMethod").WithSummary("Change a shipping method (placed orders keep their copy).").Produces<ShippingMethodAdminView>();
        shipping.MapDelete("/{id:guid}", async (Guid id, ShippingMethodService methods, CancellationToken ct) => (await methods.DeleteAsync(id, ct)).ToHttpResult())
            .WithName("DeleteShippingMethod").WithSummary("Delete a shipping method (placed orders keep their copy).");
        shipping.MapGet("/tax", async (TaxSettingsService taxes, CancellationToken ct) => TypedResults.Ok(await taxes.GetAsync(ct)))
            .WithName("GetTax").WithSummary("The shop's tax rate and whether prices include it.");
        shipping.MapGet("/sales", async (SalesSettingsService sales, CancellationToken ct) => TypedResults.Ok(await sales.GetAsync(ct)))
            .WithName("GetSalesSettings").WithSummary("The seller's details printed on invoices and the return period.");
        shipping.MapPut("/sales", async (SalesSettingsView request, SalesSettingsService sales, CancellationToken ct) => TypedResults.Ok(await sales.SaveAsync(request, ct)))
            .Validate<SalesSettingsView>()
            .WithName("SaveSalesSettings").WithSummary("Change the invoice details (issued invoices keep theirs) and the return period.");
        shipping.MapPut("/tax", async (SaveTaxRequest request, TaxSettingsService taxes, CancellationToken ct) => TypedResults.Ok(await taxes.SaveAsync(request, ct)))
            .Validate<SaveTaxRequest>()
            .WithName("SaveTax").WithSummary("Change the tax rate (placed orders keep theirs).");

        var discounts = app.MapGroup("/api/v1/admin/discounts").WithTags("Admin: discount codes")
            .RequireAuthorization(Permissions.Policy(Permissions.DiscountsManage));
        discounts.MapGet("/", async (DiscountCodeService codes, CancellationToken ct) => TypedResults.Ok(await codes.ListAsync(ct)))
            .WithName("ListDiscountCodes").WithSummary("All discount codes, the newest first, with their use counts.");
        discounts.MapPost("/", async (SaveDiscountCodeRequest request, DiscountCodeService codes, CancellationToken ct) => (await codes.CreateAsync(request, ct)).ToHttpResult())
            .Validate<SaveDiscountCodeRequest>()
            .WithName("CreateDiscountCode").WithSummary("Add a discount code.").Produces<DiscountCodeView>();
        discounts.MapPut("/{id:guid}", async (Guid id, SaveDiscountCodeRequest request, DiscountCodeService codes, CancellationToken ct) => (await codes.UpdateAsync(id, request, ct)).ToHttpResult())
            .Validate<SaveDiscountCodeRequest>()
            .WithName("UpdateDiscountCode").WithSummary("Change a discount code (its use count stays).").Produces<DiscountCodeView>();
        discounts.MapDelete("/{id:guid}", async (Guid id, DiscountCodeService codes, CancellationToken ct) => (await codes.DeleteAsync(id, ct)).ToHttpResult())
            .WithName("DeleteDiscountCode").WithSummary("Delete a discount code (orders keep their copy).");

        return app;
    }
}
