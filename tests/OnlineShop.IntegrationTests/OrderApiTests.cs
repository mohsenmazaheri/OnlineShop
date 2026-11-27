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
using OnlineShop.Domain.Sales;
using OnlineShop.Infrastructure.Persistence;

namespace OnlineShop.IntegrationTests;

/// <summary>The order life on PostgreSQL: a guest's order, its signed link, and staff paying, shipping and completing it.</summary>
public sealed class OrderApiTests(CatalogAdminApiTests.AdminFactory factory) : IClassFixture<CatalogAdminApiTests.AdminFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    [Fact]
    public async Task A_guest_order_is_paid_shipped_and_delivered_by_staff()
    {
        using var admin = factory.CreateClient();
        (await admin.PostAsJsonAsync("/api/v1/account/login",
            new LoginRequest(CatalogAdminApiTests.AdminFactory.AdminEmail, CatalogAdminApiTests.AdminFactory.AdminPassword, false))).EnsureSuccessStatusCode();
        var word = Guid.NewGuid().ToString("N")[..6];
        var category = await ReadAsync<CatalogNodeView>(await admin.PostAsJsonAsync("/api/v1/admin/catalog/categories",
            new SaveCatalogNodeRequest([new("fa", $"سفارش {word}", null, null)])));
        var sub = await ReadAsync<CatalogNodeView>(await admin.PostAsJsonAsync($"/api/v1/admin/catalog/categories/{category.Id}/subcategories",
            new SaveCatalogNodeRequest([new("fa", "زیر", null, null)])));
        var group = await ReadAsync<CatalogNodeView>(await admin.PostAsJsonAsync($"/api/v1/admin/catalog/subcategories/{sub.Id}/groups",
            new SaveCatalogNodeRequest([new("fa", "گروه", null, null)])));
        var product = await ReadAsync<ProductEditView>(await admin.PostAsJsonAsync("/api/v1/admin/products", new SaveProductRequest(
            $"ORD-{word}", 100, null, 5, true, group.Id, [new("fa", $"کتری {word}", null, null)], [])));
        var post = await ReadAsync<ShippingMethodAdminView>(await admin.PostAsJsonAsync("/api/v1/admin/shipping",
            new SaveShippingMethodRequest([new("fa", "پست", null, null)], false, 10, 0, null, [], null, null, true, 0)));

        // A guest orders two.
        using var guest = factory.CreateClient();
        (await guest.PostAsJsonAsync("/api/v1/cart/items", new AddToCartRequest(product.Number, null, 2))).EnsureSuccessStatusCode();
        var email = $"guest-{word}@example.com";
        var placed = await ReadAsync<PlacedOrderView>(await guest.PostAsJsonAsync("/api/v1/checkout/orders?locale=fa", new PlaceOrderRequest(
            post.Id, null, new AddressInput(null, "Sara", "09121234567", "IR", null, "Tehran", "Valiasr 10", null, null, false), false,
            email, null, null, null, null, 210)));
        Assert.StartsWith($"order/{placed.Number}?key=", placed.Path, StringComparison.Ordinal);
        var confirmation = Assert.Single(factory.Emails.Sent, m => m.To == email);
        Assert.Contains($"/fa/order/{placed.Number}?key=", confirmation.TextBody, StringComparison.Ordinal);

        // The signed link opens it, for anyone holding it; a changed key or another number does not.
        var link = $"/api/v1/orders/{placed.Number}?key={placed.Path[(placed.Path.IndexOf("key=", StringComparison.Ordinal) + 4)..]}";
        using var anyone = factory.CreateClient();
        var view = await ReadAsync<OrderView>(await anyone.GetAsync(link));
        Assert.Equal((OrderStatus.Pending, 210m, (string?)null), (view.Status, view.Total, view.CustomerId));
        Assert.Equal(HttpStatusCode.NotFound, (await anyone.GetAsync(link + "x")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anyone.GetAsync(link.Replace($"/{placed.Number}?", $"/{placed.Number + 1}?", StringComparison.Ordinal))).StatusCode);
        // Not signed in: no account orders, no staff list.
        Assert.Equal(HttpStatusCode.Unauthorized, (await anyone.GetAsync("/api/v1/account/orders")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anyone.GetAsync("/api/v1/admin/orders")).StatusCode);

        // Staff find it, record the payment (the stock is sold), ship it and complete it.
        var list = await ReadAsync<OrderPage>(await admin.GetAsync($"/api/v1/admin/orders?status=Pending&q={Uri.EscapeDataString(email)}"));
        Assert.Equal(placed.Number, Assert.Single(list.Items).Number);
        Assert.Equal(OrderStatus.Paid, await ReadAsync<OrderStatus>(await admin.PostAsJsonAsync($"/api/v1/admin/orders/{placed.Number}/paid", new OrderActionRequest("Transfer"))));
        var refused = await admin.PostAsJsonAsync($"/api/v1/admin/orders/{placed.Number}/cancel", new OrderActionRequest(null));
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/v1/admin/orders/{placed.Number}/ship", new ShipOrderRequest("", ""))).StatusCode);
        await ReadAsync<OrderStatus>(await admin.PostAsJsonAsync($"/api/v1/admin/orders/{placed.Number}/ship", new ShipOrderRequest("Post", "RR123IR")));
        await ReadAsync<OrderStatus>(await admin.PostAsJsonAsync($"/api/v1/admin/orders/{placed.Number}/complete", new OrderActionRequest(null)));
        (await admin.PostAsJsonAsync($"/api/v1/admin/orders/{placed.Number}/notes", new OrderNoteRequest("Left with the neighbour"))).EnsureSuccessStatusCode();

        var staff = await ReadAsync<OrderView>(await admin.GetAsync($"/api/v1/admin/orders/{placed.Number}"));
        Assert.Equal((OrderStatus.Completed, "RR123IR"), (staff.Status, staff.TrackingCode));
        Assert.Contains(staff.History, h => h.IsInternal && h.Text == "Left with the neighbour");
        var customer = await ReadAsync<OrderView>(await anyone.GetAsync(link));
        Assert.DoesNotContain(customer.History, h => h.IsInternal);
        Assert.Contains(factory.Emails.Sent, m => m.To == email && m.TextBody.Contains("RR123IR", StringComparison.Ordinal));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal((3, 0), await db.Products.Where(p => p.PublicNumber == product.Number).Select(p => new ValueTuple<int, int>(p.Stock, p.Reserved)).SingleAsync());
    }
}
