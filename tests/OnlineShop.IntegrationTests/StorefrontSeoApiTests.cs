using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using OnlineShop.Application.Accounts;
using OnlineShop.Application.Catalog;
using OnlineShop.Application.Catalog.Admin;

namespace OnlineShop.IntegrationTests;

/// <summary>Landing pages, sitemap data and special offers on PostgreSQL.</summary>
public sealed class StorefrontSeoApiTests(CatalogAdminApiTests.AdminFactory factory) : IClassFixture<CatalogAdminApiTests.AdminFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    [Fact]
    public async Task A_landing_page_lists_a_fixed_query_and_the_sitemaps_list_translated_pages()
    {
        using var admin = factory.CreateClient();
        (await admin.PostAsJsonAsync("/api/v1/account/login",
            new LoginRequest(CatalogAdminApiTests.AdminFactory.AdminEmail, CatalogAdminApiTests.AdminFactory.AdminPassword, false))).EnsureSuccessStatusCode();
        var word = Guid.NewGuid().ToString("N")[..6];
        var category = await ReadAsync<CatalogNodeView>(await admin.PostAsJsonAsync("/api/v1/admin/catalog/categories",
            new SaveCatalogNodeRequest([new("fa", $"فرود {word}", $"farood-{word}", null), new("en", $"Landing {word}", $"landing-{word}", null)])));
        var sub = await ReadAsync<CatalogNodeView>(await admin.PostAsJsonAsync($"/api/v1/admin/catalog/categories/{category.Id}/subcategories",
            new SaveCatalogNodeRequest([new("fa", "زیر", "zir", null)])));
        var group = await ReadAsync<CatalogNodeView>(await admin.PostAsJsonAsync($"/api/v1/admin/catalog/subcategories/{sub.Id}/groups",
            new SaveCatalogNodeRequest([new("fa", "گروه", null, null)])));
        var ends = DateTime.UtcNow.AddDays(2);
        var product = await ReadAsync<ProductEditView>(await admin.PostAsJsonAsync("/api/v1/admin/products", new SaveProductRequest(
            $"SEO-{word}", 80, 100, 1, true, group.Id, [new("fa", $"کالا {word}", null, null), new("en", $"Item {word}", null, null)], [], SaleEndsUtc: ends)));
        Assert.Equal(ends, product.SaleEndsUtc!.Value, TimeSpan.FromSeconds(1));

        var created = await ReadAsync<LandingPageView>(await admin.PostAsJsonAsync("/api/v1/admin/landing-pages", new SaveLandingPageRequest(
            [new("fa", $"پیشنهاد {word}", null, "متن"), new("en", $"Offers {word}", null, "Text")], null, $"https://shop.test/en/c/landing-{word}?sale=1&page=2", true)));
        Assert.Equal($"c=landing-{word}&sale=1", created.Query);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/v1/admin/landing-pages", new SaveLandingPageRequest(
            [new("fa", "دیگر", null, null)], null, "c=no-such-category", true))).StatusCode);

        using var visitor = factory.CreateClient();
        var page = await ReadAsync<LandingPageContent>(await visitor.GetAsync($"/api/v1/catalog/landing/offers-{word}?locale=en"));
        Assert.Equal(($"Offers {word}", $"offers-{word}", "Text"), (page.Title, page.Slug, page.Intro));
        Assert.Equal(["fa", "en"], page.Alternates.Select(a => a.Locale));
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.GetAsync("/api/v1/admin/landing-pages")).StatusCode);

        var products = await ReadAsync<List<SitemapEntry>>(await visitor.GetAsync("/api/v1/catalog/sitemap/Products"));
        var entry = Assert.Single(products, e => e.Links[0].Path.StartsWith($"p/{product.Number}/", StringComparison.Ordinal));
        Assert.Equal(["fa", "en"], entry.Links.Select(l => l.Locale));
        var categories = await ReadAsync<List<SitemapEntry>>(await visitor.GetAsync("/api/v1/catalog/sitemap/Categories"));
        // The sub-category has no English text: only its default-language (fa) address.
        Assert.Contains(categories, c => c.Links.Count == 1 && c.Links[0].Path == $"c/farood-{word}/zir");
        Assert.Contains(await ReadAsync<List<SitemapEntry>>(await visitor.GetAsync("/api/v1/catalog/sitemap/Landing")), l => l.Links[0].Path == $"l/پیشنهاد-{word}");

        var offers = await ReadAsync<List<ProductCard>>(await visitor.GetAsync("/api/v1/catalog/products?list=SpecialOffers&locale=en"));
        Assert.Contains(offers, o => o.Number == product.Number && o.SaleEndsUtc is not null);
    }
}
