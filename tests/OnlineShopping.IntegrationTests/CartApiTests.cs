using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using OnlineShopping.Application.Accounts;
using OnlineShopping.Application.Catalog.Admin;
using OnlineShopping.Application.Sales;

namespace OnlineShopping.IntegrationTests;

/// <summary>The cart on PostgreSQL: a guest cart behind its cookie, live stock checks and the move into the account.</summary>
public sealed class CartApiTests(CatalogAdminApiTests.AdminFactory factory) : IClassFixture<CatalogAdminApiTests.AdminFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private static string? SetCookie(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) ? values.FirstOrDefault(v => v.StartsWith(name + "=", StringComparison.Ordinal)) : null;

    private static async Task<string> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()!;

    [Fact]
    public async Task A_guest_fills_a_cart_that_moves_into_the_account_after_signing_in()
    {
        using var admin = factory.CreateClient();
        (await admin.PostAsJsonAsync("/api/v1/account/login",
            new LoginRequest(CatalogAdminApiTests.AdminFactory.AdminEmail, CatalogAdminApiTests.AdminFactory.AdminPassword, false))).EnsureSuccessStatusCode();
        var word = Guid.NewGuid().ToString("N")[..6];
        var category = await ReadAsync<CatalogNodeView>(await admin.PostAsJsonAsync("/api/v1/admin/catalog/categories",
            new SaveCatalogNodeRequest([new("fa", $"سبد {word}", null, null), new("en", $"Cart {word}", null, null)])));
        var sub = await ReadAsync<CatalogNodeView>(await admin.PostAsJsonAsync($"/api/v1/admin/catalog/categories/{category.Id}/subcategories",
            new SaveCatalogNodeRequest([new("fa", "زیر", null, null)])));
        var group = await ReadAsync<CatalogNodeView>(await admin.PostAsJsonAsync($"/api/v1/admin/catalog/subcategories/{sub.Id}/groups",
            new SaveCatalogNodeRequest([new("fa", "گروه", null, null)])));
        var product = await ReadAsync<ProductEditView>(await admin.PostAsJsonAsync("/api/v1/admin/products", new SaveProductRequest(
            $"CART-{word}", 25, 30, 3, true, group.Id, [new("fa", $"لیوان {word}", null, null), new("en", $"Mug {word}", null, null)], [])));

        // A guest: the first add sets the HttpOnly cart cookie and the readable count.
        using var guest = factory.CreateClient();
        var empty = await ReadAsync<CartView>(await guest.GetAsync("/api/v1/cart?locale=en"));
        Assert.Empty(empty.Lines);
        var added = await guest.PostAsJsonAsync("/api/v1/cart/items?locale=en", new AddToCartRequest(product.Number, null, 2));
        Assert.Contains("httponly", SetCookie(added, "os_cart")!, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("os_cart_n=2", SetCookie(added, "os_cart_n"));
        Assert.DoesNotContain("httponly", SetCookie(added, "os_cart_n")!, StringComparison.OrdinalIgnoreCase);
        var cart = await ReadAsync<CartView>(added);
        var line = Assert.Single(cart.Lines);
        Assert.Equal(($"Mug {word}", 25m, 30m, 50m), (line.Name, line.UnitPrice, line.CompareAtPrice, cart.Subtotal));

        // Stock is checked on the server; the request is validated.
        var tooMany = await guest.PutAsJsonAsync($"/api/v1/cart/items/{line.Id}", new SetQuantityRequest(4));
        Assert.Equal(HttpStatusCode.Conflict, tooMany.StatusCode);
        Assert.Equal(CartErrors.NotEnoughStock, await CodeAsync(tooMany));
        Assert.Equal(HttpStatusCode.BadRequest, (await guest.PostAsJsonAsync("/api/v1/cart/items", new AddToCartRequest(product.Number, null, 0))).StatusCode);
        Assert.Equal(3, (await ReadAsync<CartView>(await guest.PutAsJsonAsync($"/api/v1/cart/items/{line.Id}", new SetQuantityRequest(3)))).ItemCount);

        // Another visitor cannot touch the line.
        using var stranger = factory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync($"/api/v1/cart/items/{line.Id}")).StatusCode);

        // Signing in: the guest cart becomes the account's and its cookie is removed.
        (await guest.PostAsJsonAsync("/api/v1/account/login",
            new LoginRequest(CatalogAdminApiTests.AdminFactory.AdminEmail, CatalogAdminApiTests.AdminFactory.AdminPassword, false))).EnsureSuccessStatusCode();
        var merged = await guest.GetAsync("/api/v1/cart?locale=en");
        Assert.Contains("expires=Thu, 01 Jan 1970", SetCookie(merged, "os_cart")!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, Assert.Single((await ReadAsync<CartView>(merged)).Lines, l => l.Number == product.Number).Quantity);
        // The same account on another device sees it too.
        var mine = await ReadAsync<CartView>(await admin.GetAsync("/api/v1/cart?locale=en"));
        var mug = Assert.Single(mine.Lines, l => l.Number == product.Number);

        Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync($"/api/v1/cart/items/{mug.Id}")).StatusCode);
        Assert.DoesNotContain((await ReadAsync<CartView>(await guest.GetAsync("/api/v1/cart"))).Lines, l => l.Number == product.Number);
    }
}
