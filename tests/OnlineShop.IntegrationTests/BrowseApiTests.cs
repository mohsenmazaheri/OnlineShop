using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using OnlineShop.Application.Accounts;
using OnlineShop.Application.Catalog.Admin;
using OnlineShop.Application.Catalog.Browse;
using OnlineShop.Application.Search;

namespace OnlineShop.IntegrationTests;

/// <summary>Category listings with filters and facets on PostgreSQL (translated slugs, decimals, the query string).</summary>
public sealed class BrowseApiTests(CatalogAdminApiTests.AdminFactory factory) : IClassFixture<CatalogAdminApiTests.AdminFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    /// <summary>The index worker runs every few seconds; here the queue is worked through at once.</summary>
    private async Task IndexAsync()
    {
        using var scope = factory.Services.CreateScope();
        var index = scope.ServiceProvider.GetRequiredService<SearchIndexService>();
        while (await index.ProcessOutboxAsync() > 0)
        {
        }
        Assert.Equal(0, (await index.StatusAsync()).Pending);
    }

    [Fact]
    public async Task A_category_lists_published_products_with_filters_facets_and_its_translated_address()
    {
        using var admin = factory.CreateClient();
        (await admin.PostAsJsonAsync("/api/v1/account/login",
            new LoginRequest(CatalogAdminApiTests.AdminFactory.AdminEmail, CatalogAdminApiTests.AdminFactory.AdminPassword, false))).EnsureSuccessStatusCode();
        // Digits only on purpose: PostgreSQL reads "b-12345678" as "b" and a negative number (the SKU search below).
        var suffix = Random.Shared.Next(10_000_000, 100_000_000).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var category = await ReadAsync<CatalogNodeView>(await admin.PostAsJsonAsync("/api/v1/admin/catalog/categories",
            new SaveCatalogNodeRequest([new("fa", $"ورزشی {suffix}", $"varzeshi-{suffix}", null), new("en", $"Sport {suffix}", $"sport-{suffix}", null)])));
        var sub = await ReadAsync<CatalogNodeView>(await admin.PostAsJsonAsync($"/api/v1/admin/catalog/categories/{category.Id}/subcategories",
            new SaveCatalogNodeRequest([new("fa", "توپ", "toop", null), new("en", "Balls", "balls", null)])));
        var group = await ReadAsync<CatalogNodeView>(await admin.PostAsJsonAsync($"/api/v1/admin/catalog/subcategories/{sub.Id}/groups",
            new SaveCatalogNodeRequest([new("fa", "توپ فوتبال", null, null)])));
        async Task AddAsync(string sku, decimal price, decimal? compareAt, int stock, bool published) =>
            await ReadAsync<ProductEditView>(await admin.PostAsJsonAsync("/api/v1/admin/products",
                new SaveProductRequest($"{sku}-{suffix}", price, compareAt, stock, published, group.Id, [new("fa", $"توپ {sku}", null, null), new("en", $"Ball {sku}", null, null)], [])));
        await AddAsync("A", 10.5m, 15m, 3, true);
        await AddAsync("B", 20m, null, 0, true);
        await AddAsync("C", 30m, null, 1, false);

        await IndexAsync();
        using var visitor = factory.CreateClient();
        var all = await ReadAsync<BrowseResult>(await visitor.GetAsync($"/api/v1/catalog/browse?c=sport-{suffix}&locale=en"));
        Assert.Equal(("category", $"Sport {suffix}", 2), (all.Scope.Kind, all.Scope.Title, all.Total));
        Assert.Contains(all.Scope.Alternates, a => a.Locale == "fa" && a.Path == $"c/varzeshi-{suffix}");
        Assert.Equal(new PriceRange(10.5m, 20m), all.Facets.Price);
        Assert.Equal((1, 1), (all.Facets.InStock, all.Facets.Discounted));
        Assert.Equal($"c/sport-{suffix}/balls", Assert.Single(all.Facets.SubCategories).Path);

        var filtered = await ReadAsync<BrowseResult>(await visitor.GetAsync($"/api/v1/catalog/browse?c=varzeshi-{suffix}&s=toop&stock=1&min=10&sort=price_desc&locale=en"));
        Assert.Equal(("subcategory", 1, BrowseSort.PriceDesc), (filtered.Scope.Kind, filtered.Total, filtered.Sort));
        Assert.Equal($"Ball A", Assert.Single(filtered.Items).Name);

        using var missing = await visitor.GetAsync("/api/v1/catalog/browse?c=no-such-category");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        var search = await ReadAsync<BrowseResult>(await visitor.GetAsync($"/api/v1/catalog/browse?q=ball%20b-{suffix}&locale=en"));
        Assert.Equal("search", search.Scope.Kind);
        Assert.Equal($"Ball B", Assert.Single(search.Items).Name);
    }

    [Fact]
    public async Task Full_text_search_ranks_names_tolerates_typos_and_suggests_while_typing()
    {
        using var admin = factory.CreateClient();
        (await admin.PostAsJsonAsync("/api/v1/account/login",
            new LoginRequest(CatalogAdminApiTests.AdminFactory.AdminEmail, CatalogAdminApiTests.AdminFactory.AdminPassword, false))).EnsureSuccessStatusCode();
        var word = $"zephyrion{Guid.NewGuid().ToString("N")[..6]}";
        var category = await ReadAsync<CatalogNodeView>(await admin.PostAsJsonAsync("/api/v1/admin/catalog/categories",
            new SaveCatalogNodeRequest([new("fa", $"جستجو {word}", null, null), new("en", $"Search {word}", null, null)])));
        var sub = await ReadAsync<CatalogNodeView>(await admin.PostAsJsonAsync($"/api/v1/admin/catalog/categories/{category.Id}/subcategories",
            new SaveCatalogNodeRequest([new("fa", "زیر", null, null)])));
        var group = await ReadAsync<CatalogNodeView>(await admin.PostAsJsonAsync($"/api/v1/admin/catalog/subcategories/{sub.Id}/groups",
            new SaveCatalogNodeRequest([new("fa", "گروه", null, null)])));
        await ReadAsync<ProductEditView>(await admin.PostAsJsonAsync("/api/v1/admin/products", new SaveProductRequest(
            $"FTS-A-{word}", 10, null, 1, true, group.Id, [new("fa", $"لامپ {word}", null, null), new("en", $"{word} lamp", null, null)], [])));
        await ReadAsync<ProductEditView>(await admin.PostAsJsonAsync("/api/v1/admin/products", new SaveProductRequest(
            $"FTS-B-{word}", 10, null, 1, true, group.Id, [new("fa", "پایه", null, $"برای {word}"), new("en", "Stand", null, $"Fits the {word} lamp")], [])));

        await IndexAsync();

        using var visitor = factory.CreateClient();
        var found = await ReadAsync<BrowseResult>(await visitor.GetAsync($"/api/v1/catalog/browse?q={word}&locale=en"));
        // Both match; the one with the word in its name ranks above the one with it in the description.
        Assert.Equal([$"{word} lamp", "Stand"], found.Items.Select(i => i.Name));
        var prefix = await ReadAsync<BrowseResult>(await visitor.GetAsync($"/api/v1/catalog/browse?q={word[..7]}&locale=en"));
        Assert.Equal(2, prefix.Total);

        var typo = word[..3] + word[4] + word[3] + word[5..];
        var tolerant = await ReadAsync<BrowseResult>(await visitor.GetAsync($"/api/v1/catalog/browse?q={typo}&locale=en"));
        Assert.Contains(tolerant.Items, i => i.Name == $"{word} lamp");

        var suggestions = await ReadAsync<SearchSuggestions>(await visitor.GetAsync($"/api/v1/catalog/suggest?q={word[..6]}&locale=en"));
        Assert.Contains(suggestions.Products, p => p.Name == $"{word} lamp");
        Assert.Contains(suggestions.Categories, c => c.Name == $"Search {word}");

        var status = await ReadAsync<SearchIndexStatus>(await admin.PostAsync("/api/v1/admin/seo/search-index/rebuild", null));
        Assert.True(status.Pending >= 1);
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.PostAsync("/api/v1/admin/seo/search-index/rebuild", null)).StatusCode);
    }
}
