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

/// <summary>Invoices, returns and refunds on PostgreSQL, through the API as a guest and as staff.</summary>
public sealed class ReturnApiTests(CatalogAdminApiTests.AdminFactory factory) : IClassFixture<CatalogAdminApiTests.AdminFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    [Fact]
    public async Task A_paid_order_is_invoiced_and_a_return_is_received_and_refunded()
    {
        using var admin = factory.CreateClient();
        (await admin.PostAsJsonAsync("/api/v1/account/login",
            new LoginRequest(CatalogAdminApiTests.AdminFactory.AdminEmail, CatalogAdminApiTests.AdminFactory.AdminPassword, false))).EnsureSuccessStatusCode();
        var word = Guid.NewGuid().ToString("N")[..6];
        var category = await ReadAsync<CatalogNodeView>(await admin.PostAsJsonAsync("/api/v1/admin/catalog/categories", new SaveCatalogNodeRequest([new("fa", $"مرجوعی {word}", null, null)])));
        var sub = await ReadAsync<CatalogNodeView>(await admin.PostAsJsonAsync($"/api/v1/admin/catalog/categories/{category.Id}/subcategories", new SaveCatalogNodeRequest([new("fa", "زیر", null, null)])));
        var group = await ReadAsync<CatalogNodeView>(await admin.PostAsJsonAsync($"/api/v1/admin/catalog/subcategories/{sub.Id}/groups", new SaveCatalogNodeRequest([new("fa", "گروه", null, null)])));
        var product = await ReadAsync<ProductEditView>(await admin.PostAsJsonAsync("/api/v1/admin/products", new SaveProductRequest(
            $"RET-{word}", 100, null, 5, true, group.Id, [new("fa", $"لیوان {word}", null, null)], [])));
        var post = await ReadAsync<ShippingMethodAdminView>(await admin.PostAsJsonAsync("/api/v1/admin/shipping",
            new SaveShippingMethodRequest([new("fa", "پست", null, null)], false, 0, 0, null, [], null, null, true, 0)));
        await ReadAsync<SalesSettingsView>(await admin.PutAsJsonAsync("/api/v1/admin/shipping/sales", new SalesSettingsView("Sara Trading", "Valiasr 1", "IR-9", null, 14)));

        using var guest = factory.CreateClient();
        (await guest.PostAsJsonAsync("/api/v1/cart/items", new AddToCartRequest(product.Number, null, 2))).EnsureSuccessStatusCode();
        var placed = await ReadAsync<PlacedOrderView>(await guest.PostAsJsonAsync("/api/v1/checkout/orders", new PlaceOrderRequest(
            post.Id, null, new AddressInput(null, "Sara", "09121234567", "IR", null, "Tehran", "Valiasr 10", null, null, false), false,
            $"ret-{word}@example.com", null, null, null, null, 200)));
        var key = placed.Path[(placed.Path.IndexOf("key=", StringComparison.Ordinal) + 4)..];
        var orderUrl = $"/api/v1/orders/{placed.Number}";

        // No invoice before payment; staff pay, ship and deliver.
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"{orderUrl}/invoice?key={key}")).StatusCode);
        foreach (var (step, body) in new (string, object)[] { ("paid", new OrderActionRequest(null)), ("ship", new ShipOrderRequest("Post", "RR1")), ("complete", new OrderActionRequest(null)) })
            (await admin.PostAsJsonAsync($"/api/v1/admin/orders/{placed.Number}/{step}", body)).EnsureSuccessStatusCode();
        var invoice = await ReadAsync<InvoiceView>(await guest.GetAsync($"{orderUrl}/invoice?key={key}"));
        Assert.Equal(("Sara Trading", "IR-9", 200m), (invoice.SellerName, invoice.SellerTaxNumber, invoice.Order.Total));
        Assert.Equal(invoice.Number, (await ReadAsync<InvoiceView>(await admin.GetAsync($"/api/v1/admin/orders/{placed.Number}/invoice"))).Number);

        // The guest asks to return one, through the link.
        var view = await ReadAsync<OrderView>(await guest.GetAsync($"{orderUrl}?key={key}"));
        Assert.True(view.Actions.CanRequestReturn);
        var line = Assert.Single(view.Lines);
        Assert.Equal(HttpStatusCode.NotFound, (await guest.PostAsJsonAsync($"{orderUrl}/returns?key=x", new RequestReturnRequest([new(line.Id, 1)], "Too big"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await guest.PostAsJsonAsync($"{orderUrl}/returns?key={key}", new RequestReturnRequest([new(line.Id, 1)], ""))).StatusCode);
        await ReadAsync<long>(await guest.PostAsJsonAsync($"{orderUrl}/returns?key={key}", new RequestReturnRequest([new(line.Id, 1)], "Too big")));

        // Staff approve, receive (back into stock) and refund it; more than was paid is refused.
        var staff = await ReadAsync<OrderView>(await admin.GetAsync($"/api/v1/admin/orders/{placed.Number}"));
        var request = Assert.Single(staff.Returns);
        Assert.Equal(100m, request.SuggestedRefund);
        await ReadAsync<ReturnStatus>(await admin.PostAsJsonAsync($"/api/v1/admin/orders/{placed.Number}/returns/{request.Id}/approve", new ReturnAnswerRequest("Send it back")));
        await ReadAsync<ReturnStatus>(await admin.PostAsJsonAsync($"/api/v1/admin/orders/{placed.Number}/returns/{request.Id}/receive", new ReturnAnswerRequest(null)));
        var tooMuch = await admin.PostAsJsonAsync($"/api/v1/admin/orders/{placed.Number}/refunds", new RecordRefundRequest(201, "Card", null, request.Id));
        Assert.Equal(HttpStatusCode.BadRequest, tooMuch.StatusCode);
        Assert.Equal(100m, await ReadAsync<decimal>(await admin.PostAsJsonAsync($"/api/v1/admin/orders/{placed.Number}/refunds", new RecordRefundRequest(100, "Card", "Ref 7", request.Id))));

        var after = await ReadAsync<OrderView>(await guest.GetAsync($"{orderUrl}?key={key}"));
        Assert.Equal((100m, 100m, ReturnStatus.Received), (after.RefundedTotal, after.Refundable, after.Returns[0].Status));
        Assert.Null(after.Refunds[0].Note);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(4, await db.Products.Where(p => p.PublicNumber == product.Number).Select(p => p.Stock).SingleAsync());
    }
}
