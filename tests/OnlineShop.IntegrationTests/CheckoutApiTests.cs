using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OnlineShop.Application.Accounts;
using OnlineShop.Application.Catalog.Admin;
using OnlineShop.Application.Customers;
using OnlineShop.Application.Sales;
using OnlineShop.Domain.Inventory;
using OnlineShop.Domain.Sales;
using OnlineShop.Infrastructure.Persistence;

namespace OnlineShop.IntegrationTests;

/// <summary>Checkout on PostgreSQL: the admin's shipping, tax and codes, a guest's quote and order, and its stock hold.</summary>
public sealed class CheckoutApiTests(CatalogAdminApiTests.AdminFactory factory) : IClassFixture<CatalogAdminApiTests.AdminFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private static async Task<string> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()!;

    [Fact]
    public async Task A_guest_checks_out_with_shipping_tax_and_a_discount_code()
    {
        using var admin = factory.CreateClient();
        (await admin.PostAsJsonAsync("/api/v1/account/login",
            new LoginRequest(CatalogAdminApiTests.AdminFactory.AdminEmail, CatalogAdminApiTests.AdminFactory.AdminPassword, false))).EnsureSuccessStatusCode();
        var word = Guid.NewGuid().ToString("N")[..6];
        var category = await ReadAsync<CatalogNodeView>(await admin.PostAsJsonAsync("/api/v1/admin/catalog/categories",
            new SaveCatalogNodeRequest([new("fa", $"تسویه {word}", null, null), new("en", $"Checkout {word}", null, null)])));
        var sub = await ReadAsync<CatalogNodeView>(await admin.PostAsJsonAsync($"/api/v1/admin/catalog/categories/{category.Id}/subcategories",
            new SaveCatalogNodeRequest([new("fa", "زیر", null, null)])));
        var group = await ReadAsync<CatalogNodeView>(await admin.PostAsJsonAsync($"/api/v1/admin/catalog/subcategories/{sub.Id}/groups",
            new SaveCatalogNodeRequest([new("fa", "گروه", null, null)])));
        var product = await ReadAsync<ProductEditView>(await admin.PostAsJsonAsync("/api/v1/admin/products", new SaveProductRequest(
            $"CHK-{word}", 100, null, 3, true, group.Id, [new("fa", $"چراغ {word}", null, null), new("en", $"Lamp {word}", null, null)], [])));

        // The admin side: tax inside the prices, one method, one code. Visitors may not touch it.
        using var guest = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync("/api/v1/admin/shipping")).StatusCode);
        await ReadAsync<TaxView>(await admin.PutAsJsonAsync("/api/v1/admin/shipping/tax", new SaveTaxRequest(10, true)));
        var post = await ReadAsync<ShippingMethodAdminView>(await admin.PostAsJsonAsync("/api/v1/admin/shipping",
            new SaveShippingMethodRequest([new("fa", "پست", null, null), new("en", "Post", null, "Tracked")], false, 10, 0, null, [], 2, 4, true, 0)));
        var discount = await ReadAsync<DiscountCodeView>(await admin.PostAsJsonAsync("/api/v1/admin/discounts",
            new SaveDiscountCodeRequest($"save-{word}", DiscountKind.Amount, 20, null, null, null, null, 1, false, true, null)));
        Assert.Equal($"SAVE-{word}".ToUpperInvariant(), discount.Code);

        (await guest.PostAsJsonAsync("/api/v1/cart/items", new AddToCartRequest(product.Number, null, 2))).EnsureSuccessStatusCode();
        var view = await ReadAsync<CheckoutView>(await guest.GetAsync("/api/v1/checkout?locale=en"));
        Assert.Equal((false, true, 200m), (view.SignedIn, view.EmailRequired, view.Cart.Subtotal));
        Assert.Equal(("Post", "Tracked", 10m), (view.ShippingMethods.Single().Name, view.ShippingMethods.Single().Description, view.ShippingMethods.Single().Cost));

        var quote = await ReadAsync<CheckoutQuote>(await guest.PostAsJsonAsync("/api/v1/checkout/quote?locale=en", new CheckoutQuoteRequest("IR", post.Id, discount.Code.ToLowerInvariant())));
        // 200 − 20 + 10 = 190, of which 10/110 is tax, rounded to whole rials (the test shop sells in IRR).
        Assert.Equal(new CheckoutTotals(200, 20, 10, 17, 190, 10, true), quote.Totals);

        var address = new AddressInput(null, "Sara Ahmadi", "09121234567", "IR", null, "Tehran", "Valiasr 10", null, null, false);
        var order = new PlaceOrderRequest(post.Id, null, address, false, "guest@example.com", null, null, discount.Code, null, 190);
        Assert.Equal(HttpStatusCode.BadRequest, (await guest.PostAsJsonAsync("/api/v1/checkout/orders", order with { Email = "not-an-email" })).StatusCode);
        var changed = await guest.PostAsJsonAsync("/api/v1/checkout/orders", order with { ExpectedTotal = 200 });
        Assert.Equal((HttpStatusCode.Conflict, CheckoutErrors.TotalChanged), (changed.StatusCode, await CodeAsync(changed)));

        var response = await guest.PostAsJsonAsync("/api/v1/checkout/orders?locale=en", order);
        var placed = await ReadAsync<PlacedOrderView>(response);
        Assert.Equal((190m, "guest@example.com"), (placed.Total, placed.Email));
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("os_cart_n=;", StringComparison.Ordinal));
        Assert.Empty((await ReadAsync<CartView>(await guest.GetAsync("/api/v1/cart"))).Lines);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await db.Orders.AsNoTracking().Include(o => o.Items).SingleAsync(o => o.Number == placed.Number);
        Assert.Equal((OrderStatus.Pending, (string?)null, "+989121234567", 17m, discount.Code), (saved.Status, saved.CustomerId, saved.PhoneNumber, saved.Tax, saved.DiscountCodeText));
        Assert.Equal(($"Lamp {word}", 100m, 2), (saved.Items.Single().ProductName, saved.Items.Single().UnitPrice, saved.Items.Single().Quantity));
        Assert.Equal(2, await db.Products.Where(p => p.PublicNumber == product.Number).Select(p => p.Reserved).SingleAsync());
        Assert.Equal(2, await db.StockReservations.Where(r => r.Reference == $"order:{placed.Number}" && r.Status == StockReservationStatus.Active).SumAsync(r => r.Quantity));

        // The code allowed one use; the next guest sees it used up, and only one lamp is left to order.
        using var next = factory.CreateClient();
        (await next.PostAsJsonAsync("/api/v1/cart/items", new AddToCartRequest(product.Number, null, 1))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await next.PostAsJsonAsync("/api/v1/cart/items", new AddToCartRequest(product.Number, null, 1))).StatusCode);
        var usedUp = await ReadAsync<CheckoutQuote>(await next.PostAsJsonAsync("/api/v1/checkout/quote", new CheckoutQuoteRequest("IR", post.Id, discount.Code)));
        Assert.Equal(CheckoutErrors.DiscountUsedUp, usedUp.DiscountError);
    }
}
