using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using OnlineShop.Application.Accounts;
using OnlineShop.Application.Catalog.Admin;
using OnlineShop.Application.Seo;
using OnlineShop.Domain.Seo;

namespace OnlineShop.IntegrationTests;

/// <summary>Synonyms, redirects (admin, import, storefront rules and hit counting) and product SEO fields on PostgreSQL.</summary>
public sealed class SeoApiTests(CatalogAdminApiTests.AdminFactory factory) : IClassFixture<CatalogAdminApiTests.AdminFactory>
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

    [Fact]
    public async Task Synonyms_and_redirects_are_managed_and_the_storefront_reads_and_counts_redirects()
    {
        using var admin = await AdminAsync();

        var group = await ReadAsync<SynonymView>(await admin.PostAsJsonAsync("/api/v1/admin/seo/synonyms", new SaveSynonymRequest("fa", ["گوشي", "موبایل"]), Json));
        Assert.Equal(["گوشی", "موبایل"], group.Terms);
        var clash = await admin.PostAsJsonAsync("/api/v1/admin/seo/synonyms", new SaveSynonymRequest("fa", ["موبایل", "سلولی"]), Json);
        Assert.Equal(HttpStatusCode.Conflict, clash.StatusCode);

        await ReadAsync<RedirectView>(await admin.PostAsJsonAsync("/api/v1/admin/seo/redirects", new SaveRedirectRequest("/old-page", "/fa", RedirectKind.Permanent), Json));
        var import = await ReadAsync<ImportRedirectsResult>(await admin.PostAsJsonAsync("/api/v1/admin/seo/redirects/import", new ImportRedirectsRequest("/a,/fa\n/b,,410\nnope,/x"), Json));
        Assert.Equal((2, 0, 1), (import.Added, import.Updated, import.Errors.Count));

        using var visitor = factory.CreateClient();
        var rules = await ReadAsync<List<RedirectRule>>(await visitor.GetAsync("/api/v1/redirects"));
        Assert.Equal(["/a", "/b", "/old-page"], rules.Select(r => r.FromPath));
        Assert.Equal(HttpStatusCode.NoContent, (await visitor.PostAsJsonAsync("/api/v1/redirects/hits", new RedirectHitRequest("/old-page"))).StatusCode);

        var list = await ReadAsync<List<RedirectView>>(await admin.GetAsync("/api/v1/admin/seo/redirects"));
        Assert.Equal(1, list.Single(r => r.FromPath == "/old-page").HitCount);
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.GetAsync("/api/v1/admin/seo/redirects")).StatusCode);
    }

    [Fact]
    public async Task Product_seo_fields_reach_the_storefront()
    {
        using var admin = await AdminAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await ReadAsync<CatalogNodeView>(await admin.PostAsJsonAsync("/api/v1/admin/catalog/categories", new SaveCatalogNodeRequest([new("fa", $"دسته {suffix}", null, null)])));
        var sub = await ReadAsync<CatalogNodeView>(await admin.PostAsJsonAsync($"/api/v1/admin/catalog/categories/{category.Id}/subcategories", new SaveCatalogNodeRequest([new("fa", "زیر", null, null)])));
        var group = await ReadAsync<CatalogNodeView>(await admin.PostAsJsonAsync($"/api/v1/admin/catalog/subcategories/{sub.Id}/groups", new SaveCatalogNodeRequest([new("fa", "گروه", null, null)])));

        var product = await ReadAsync<ProductEditView>(await admin.PostAsJsonAsync("/api/v1/admin/products", new SaveProductRequest(
            $"SEO-{suffix}", 10, null, 1, true, group.Id, [new("fa", "کالای سئو", null, null), new("en", "SEO item", null, null)], [],
            Gtin: "4006381333931", Mpn: "M-1", WeightGrams: 250,
            Seo: [new("fa", "عنوان جستجو", "توضیح جستجو", "برچسب"), new("en", "Search title", "Search description", null)]), Json));
        Assert.Equal("4006381333931", product.Gtin);

        using var visitor = factory.CreateClient();
        using var page = JsonDocument.Parse(await visitor.GetStringAsync($"/api/v1/catalog/products/{product.Number}?locale=en"));
        var root = page.RootElement;
        Assert.Equal(("Search title", "Search description", "4006381333931", 250),
            (root.GetProperty("metaTitle").GetString(), root.GetProperty("metaDescription").GetString(), root.GetProperty("gtin").GetString(), root.GetProperty("weightGrams").GetInt32()));
    }
}
