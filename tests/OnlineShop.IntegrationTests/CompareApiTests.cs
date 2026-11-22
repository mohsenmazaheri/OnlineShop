using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using OnlineShop.Application.Accounts;
using OnlineShop.Application.Catalog.Admin;
using OnlineShop.Application.Catalog.Compare;
using OnlineShop.Domain.Catalog;

namespace OnlineShop.IntegrationTests;

/// <summary>Product comparison on PostgreSQL: the public table and a signed-in customer's saved list.</summary>
public sealed class CompareApiTests(CatalogAdminApiTests.AdminFactory factory) : IClassFixture<CatalogAdminApiTests.AdminFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    [Fact]
    public async Task Products_are_compared_with_their_specs_and_a_customer_keeps_a_list()
    {
        using var admin = factory.CreateClient();
        (await admin.PostAsJsonAsync("/api/v1/account/login",
            new LoginRequest(CatalogAdminApiTests.AdminFactory.AdminEmail, CatalogAdminApiTests.AdminFactory.AdminPassword, false))).EnsureSuccessStatusCode();
        var word = Guid.NewGuid().ToString("N")[..6];
        var spec = await ReadAsync<SpecView>(await admin.PostAsJsonAsync("/api/v1/admin/specs", new SaveSpecRequest(
            $"cmp_{word}", "Number", true, true, false, [new SpecText("fa", "حافظه", "حافظه", "GB"), new SpecText("en", "Storage", "Memory", "GB")], [], SpecBetter.Higher)));
        Assert.Equal(SpecBetter.Higher, spec.Better);
        var category = await ReadAsync<CatalogNodeView>(await admin.PostAsJsonAsync("/api/v1/admin/catalog/categories",
            new SaveCatalogNodeRequest([new("fa", $"مقایسه {word}", null, null), new("en", $"Compare {word}", null, null)])));
        var sub = await ReadAsync<CatalogNodeView>(await admin.PostAsJsonAsync($"/api/v1/admin/catalog/categories/{category.Id}/subcategories",
            new SaveCatalogNodeRequest([new("fa", "زیر", null, null)])));
        var group = await ReadAsync<CatalogNodeView>(await admin.PostAsJsonAsync($"/api/v1/admin/catalog/subcategories/{sub.Id}/groups",
            new SaveCatalogNodeRequest([new("fa", "گروه", null, null)])));
        var numbers = new List<long>();
        foreach (var (sku, gb) in new[] { ($"CMP-A-{word}", 64m), ($"CMP-B-{word}", 128m) })
        {
            var product = await ReadAsync<ProductEditView>(await admin.PostAsJsonAsync("/api/v1/admin/products", new SaveProductRequest(
                sku, 10, null, 1, true, group.Id, [new("fa", $"گوشی {sku}", null, null), new("en", $"Phone {sku}", null, null)],
                [new ProductSpecInput(spec.Code, gb, null, null, null)])));
            numbers.Add(product.Number);
        }

        using var visitor = factory.CreateClient();
        var table = await ReadAsync<ProductComparison>(await visitor.GetAsync($"/api/v1/catalog/compare?numbers={numbers[0]},{numbers[1]}&locale=en"));
        Assert.Equal(numbers, table.Products.Select(p => p.Number));
        var row = table.Groups.SelectMany(g => g.Rows).Single(r => r.Code == spec.Code);
        Assert.Equal("Memory", table.Groups.Single(g => g.Rows.Contains(row)).Name);
        Assert.Equal([64m, 128m], row.Values.Select(v => v.Number!.Value));
        Assert.Equal([1], row.Best);

        var guest = await ReadAsync<CompareListView>(await visitor.GetAsync("/api/v1/account/compare"));
        Assert.False(guest.SignedIn);
        Assert.Empty(guest.Numbers);
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.PutAsJsonAsync("/api/v1/account/compare", new SaveCompareListRequest([numbers[0]]))).StatusCode);
        var saved = await ReadAsync<CompareListView>(await admin.PutAsJsonAsync("/api/v1/account/compare", new SaveCompareListRequest([numbers[1], numbers[0], numbers[1]])));
        Assert.Equal([numbers[1], numbers[0]], saved.Numbers);
        Assert.Equal(saved.Numbers, (await ReadAsync<CompareListView>(await admin.GetAsync("/api/v1/account/compare"))).Numbers);
    }
}
