using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using OnlineShopping.Application.Accounts;
using OnlineShopping.Application.Catalog.Admin;
using OnlineShopping.IntegrationTests.TestSupport;

namespace OnlineShopping.IntegrationTests;

/// <summary>Catalog admin end to end on PostgreSQL: build a tree, create, edit, find and delete a product.</summary>
public sealed class CatalogAdminApiTests(CatalogAdminApiTests.AdminFactory factory) : IClassFixture<CatalogAdminApiTests.AdminFactory>
{
    public sealed class AdminFactory : ApiFactory
    {
        public const string AdminEmail = "catalog-owner@shop.test";
        public const string AdminPassword = "catalog pass 2026"; // test-only value for this throwaway database

        protected override IReadOnlyDictionary<string, string?> ConfigurationOverrides => new Dictionary<string, string?>
        {
            ["Database:AdminEmail"] = AdminEmail,
            ["Database:AdminPassword"] = AdminPassword,
        };
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private async Task<HttpClient> AdminAsync()
    {
        var client = factory.CreateClient();
        (await client.PostAsJsonAsync("/api/v1/account/login", new LoginRequest(AdminFactory.AdminEmail, AdminFactory.AdminPassword, false))).EnsureSuccessStatusCode();
        return client;
    }

    private static SaveCatalogNodeRequest Texts(string fa, string en = "") => new([new("fa", fa, null, null), new("en", en, null, null)]);

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    [Fact]
    public async Task An_admin_builds_the_tree_and_manages_a_product()
    {
        using var admin = await AdminAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var category = await ReadAsync<CatalogNodeView>(await admin.PostAsJsonAsync("/api/v1/admin/catalog/categories", Texts($"لوازم {suffix}", $"Supplies {suffix}")));
        var sub = await ReadAsync<CatalogNodeView>(await admin.PostAsJsonAsync($"/api/v1/admin/catalog/categories/{category.Id}/subcategories", Texts("دفتر")));
        var group = await ReadAsync<CatalogNodeView>(await admin.PostAsJsonAsync($"/api/v1/admin/catalog/subcategories/{sub.Id}/groups", Texts("دفتر سیمی")));

        var request = new SaveProductRequest($"SKU-{suffix}", 250_000, 300_000, 4, true, group.Id,
            [new("fa", "دفتر سیمی آزمایشی", null, "توضیح"), new("en", "Test notebook", null, "About")], []);
        var created = await ReadAsync<ProductEditView>(await admin.PostAsJsonAsync("/api/v1/admin/products", request));
        Assert.True(created.Number >= 1001);

        var updated = await ReadAsync<ProductEditView>(await admin.PutAsJsonAsync($"/api/v1/admin/products/{created.Number}", request with { Stock = 0, IsPublished = false }));
        Assert.Equal((0, false), (updated.Stock, updated.IsPublished));

        var page = await ReadAsync<ProductPage>(await admin.GetAsync($"/api/v1/admin/products?search={suffix}&locale=en"));
        Assert.Equal("Test notebook", Assert.Single(page.Items).Name);

        // An unpublished product is not in the shop.
        using (var anonymous = factory.CreateClient())
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/v1/catalog/products/{created.Number}")).StatusCode);

        var tree = await ReadAsync<CatalogTree>(await admin.GetAsync("/api/v1/admin/catalog"));
        Assert.Equal(1, tree.Categories.Single(c => c.Id == category.Id).SubCategories.Single().Groups.Single().ProductCount);

        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/v1/admin/catalog/groups/{group.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync($"/api/v1/admin/products/{created.Number}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync($"/api/v1/admin/catalog/groups/{group.Id}")).StatusCode);
    }

    [Fact]
    public async Task Customers_and_visitors_cannot_manage_the_catalog()
    {
        using var anonymous = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/admin/catalog")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/v1/admin/products", new { })).StatusCode);
    }
}
