using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using OnlineShop.Application.Accounts;
using OnlineShop.Application.Catalog.Admin;

namespace OnlineShop.IntegrationTests;

/// <summary>
/// Brands, the spec library, spec templates and variants end to end on PostgreSQL, ending with the public product
/// page that shows the brand and the variants.
/// </summary>
public sealed class CatalogLibraryApiTests(CatalogAdminApiTests.AdminFactory factory) : IClassFixture<CatalogAdminApiTests.AdminFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private async Task<HttpClient> AdminAsync()
    {
        var client = factory.CreateClient();
        var login = new LoginRequest(CatalogAdminApiTests.AdminFactory.AdminEmail, CatalogAdminApiTests.AdminFactory.AdminPassword, false);
        (await client.PostAsJsonAsync("/api/v1/account/login", login)).EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static SaveCatalogNodeRequest Node(string fa) => new([new("fa", fa, null, null)]);

    [Fact]
    public async Task An_admin_defines_a_brand_specs_a_template_and_variants_shown_on_the_product_page()
    {
        using var admin = await AdminAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var brand = await ReadAsync<BrandView>(await admin.PostAsJsonAsync("/api/v1/admin/brands",
            new SaveBrandRequest([new("fa", $"برند {suffix}", null, null), new("en", $"Brand {suffix}", null, null)])));

        var size = await ReadAsync<SpecView>(await admin.PostAsJsonAsync("/api/v1/admin/specs", new SaveSpecRequest(
            $"size_{suffix}", "Option", true, true, false,
            [new("fa", "سایز", null, null), new("en", "Size", null, null)],
            [new("m", [new("fa", "M"), new("en", "M")]), new("l", [new("fa", "L"), new("en", "L")])])));
        var weight = await ReadAsync<SpecView>(await admin.PostAsJsonAsync("/api/v1/admin/specs", new SaveSpecRequest(
            null, "Number", false, true, true, [new("fa", "وزن", "ابعاد", "گرم"), new("en", "Weight", "Size", "g")], [])));
        Assert.Equal("spec_1", weight.Code);

        var category = await ReadAsync<CatalogNodeView>(await admin.PostAsJsonAsync("/api/v1/admin/catalog/categories", Node($"پوشاک {suffix}")));
        var sub = await ReadAsync<CatalogNodeView>(await admin.PostAsJsonAsync($"/api/v1/admin/catalog/categories/{category.Id}/subcategories", Node("تی‌شرت")));
        var group = await ReadAsync<CatalogNodeView>(await admin.PostAsJsonAsync($"/api/v1/admin/catalog/subcategories/{sub.Id}/groups", Node("تی‌شرت نخی")));
        var template = await ReadAsync<List<string>>(await admin.PutAsJsonAsync($"/api/v1/admin/catalog/categories/{category.Id}/template", new SaveTemplateRequest([weight.Code])));
        Assert.Equal([weight.Code], template);

        var product = await ReadAsync<ProductEditView>(await admin.PostAsJsonAsync("/api/v1/admin/products", new SaveProductRequest(
            $"TEE-{suffix}", 20, null, 0, true, group.Id, [new("fa", "تی‌شرت آزمایشی", null, null), new("en", "Test tee", null, null)],
            [new(weight.Code, 180, null, null, null)], brand.Id)));
        Assert.Equal(brand.Id, product.BrandId);

        var variants = await ReadAsync<ProductVariantsView>(await admin.PutAsJsonAsync($"/api/v1/admin/products/{product.Number}/variants", new SaveVariantsRequest(
            [size.Code],
            [new(null, $"TEE-{suffix}-M", null, null, 3, [new(size.Code, "m")]), new(null, $"TEE-{suffix}-L", 22, null, 0, [new(size.Code, "l")])])));
        Assert.Equal(2, variants.Variants.Count);

        // Rules are enforced over HTTP too: a used option and a used brand stay.
        var withoutL = new SaveSpecRequest(size.Code, "Option", true, true, false, [new("fa", "سایز", null, null)], [new("m", [new("fa", "M")])]);
        var refused = await admin.PutAsJsonAsync($"/api/v1/admin/specs/{size.Id}", withoutL);
        Assert.Equal((HttpStatusCode.Conflict, CatalogErrors.OptionInUse), (refused.StatusCode, await CodeAsync(refused)));
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/v1/admin/brands/{brand.Id}")).StatusCode);
        var invalid = await admin.PutAsJsonAsync($"/api/v1/admin/products/{product.Number}/variants",
            new SaveVariantsRequest([size.Code], [new(null, "X", null, null, 1, [new(size.Code, "xxl")])]));
        Assert.Equal((HttpStatusCode.BadRequest, CatalogErrors.VariantOptionsInvalid), (invalid.StatusCode, await CodeAsync(invalid)));

        // The storefront: brand, axes and variants with their effective prices; the stock is the variants' sum.
        using var visitor = factory.CreateClient();
        using var page = JsonDocument.Parse(await visitor.GetStringAsync($"/api/v1/catalog/products/{product.Number}?locale=en"));
        var root = page.RootElement;
        Assert.Equal($"Brand {suffix}", root.GetProperty("brand").GetProperty("name").GetString());
        Assert.Equal(3, root.GetProperty("stock").GetInt32());
        Assert.Equal("Size", root.GetProperty("variantAxes")[0].GetProperty("name").GetString());
        var offers = root.GetProperty("variants");
        Assert.Equal((20m, true), (offers[0].GetProperty("price").GetDecimal(), offers[0].GetProperty("inStock").GetBoolean()));
        Assert.Equal((22m, false), (offers[1].GetProperty("price").GetDecimal(), offers[1].GetProperty("inStock").GetBoolean()));

        // The tree carries the template; the spec list counts usage.
        var tree = await ReadAsync<CatalogTree>(await admin.GetAsync("/api/v1/admin/catalog"));
        Assert.Equal([weight.Code], tree.Categories.Single(c => c.Id == category.Id).TemplateSpecs);
        var specs = await ReadAsync<SpecList>(await admin.GetAsync("/api/v1/admin/specs"));
        Assert.Equal(2, specs.Specs.Single(s => s.Id == size.Id).UsageCount);
    }

    [Fact]
    public async Task Customers_and_visitors_cannot_reach_brands_or_specs()
    {
        using var visitor = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.GetAsync("/api/v1/admin/brands")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.GetAsync("/api/v1/admin/specs")).StatusCode);
    }
}
