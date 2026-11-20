using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnlineShopping.Application.Catalog;
using OnlineShopping.Application.Catalog.Browse;
using OnlineShopping.Application.Inventory;
using OnlineShopping.Application.Search;
using OnlineShopping.Infrastructure.Persistence;

namespace OnlineShopping.UnitTests.Application;

public sealed class BrowseQueryParserTests
{
    private static BrowseQuery Parse(params (string Key, string? Value)[] pairs) =>
        BrowseQueryParser.Parse(pairs.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value)));

    [Fact]
    public void Reads_scope_filters_sort_and_page()
    {
        var id = Guid.NewGuid();
        var query = Parse(("c", "electronics"), ("s", "phones"), ("brand", "orvia, ranna,orvia"), ("group", $"{id},bad"), ("min", "10.5"), ("max", "200"),
            ("stock", "1"), ("sale", "1"), ("o.color", "black,white"), ("n.storage", "128-"), ("n.ram", "-8"), ("y.network_5g", "1"), ("sort", "price_desc"), ("page", "3"));

        Assert.Equal(("electronics", "phones"), (query.Category, query.SubCategory));
        Assert.Equal(["orvia", "ranna"], query.Brands);
        Assert.Equal(["a", "b"], Parse(("brand", "a"), ("brand", "b")).Brands);
        Assert.Equal([id], query.Groups);
        Assert.Equal((10.5m, 200m, true, true), (query.MinPrice, query.MaxPrice, query.InStock, query.Discounted));
        Assert.Equal(["black", "white"], query.Options["color"]);
        Assert.Equal(new NumberRange(128, null), query.Ranges["storage"]);
        Assert.Equal(new NumberRange(null, 8), query.Ranges["ram"]);
        Assert.Equal(["network_5g"], query.Flags);
        Assert.Equal((BrowseSort.PriceDesc, 3, true), (query.Sort, query.Page, query.HasFilters));
    }

    [Fact]
    public void Ignores_what_it_does_not_understand()
    {
        var query = Parse(("min", "-5"), ("max", "abc"), ("stock", "yes"), ("n.ram", "x-y"), ("n.", "1-2"), ("y.wifi", "0"), ("sort", "random"), ("page", "0"), ("q", new string('a', 300)), ("brand", " "));

        Assert.Equal((null, null, false, null, 1), (query.MinPrice, query.MaxPrice, query.InStock, query.Sort, query.Page));
        Assert.Empty(query.Ranges);
        Assert.Empty(query.Flags);
        Assert.Empty(query.Brands);
        Assert.Equal(200, query.Text!.Length);
        Assert.False(query.HasFilters);
        Assert.True(new NumberRange(1, 2).Contains(1.5m));
        Assert.False(new NumberRange(1, null).Contains(0.5m));
    }
}

public sealed class ProductBrowseServiceTests : DemoCatalogTestBase
{
    private ProductBrowseService Service(AppDbContext db) =>
        new(db, new ProductQueries(db), new ProductTextSearch(db, new SimpleProductSearch(db)), new SearchTermLog(db, Database.Clock), Options.Create(Store));

    private async Task<BrowseResult> BrowseAsync(BrowseQuery query, string locale = "en")
    {
        await using var db = Database.CreateContext();
        return (await Service(db).BrowseAsync(query, locale)).Value;
    }

    private async Task<string> CategorySlugAsync(string sku)
    {
        await using var db = Database.CreateContext();
        return await db.Products.Where(p => p.SKU == sku).Select(p => p.ProductGroup.SubCategory.Category.Slug).SingleAsync();
    }

    [Fact]
    public async Task A_category_lists_its_products_newest_first_with_its_addresses_and_sub_categories()
    {
        await SeedDemoAsync();
        var slug = await CategorySlugAsync("DEMO-PHN-001");

        var result = await BrowseAsync(new BrowseQuery { Category = slug });

        Assert.Equal(("category", "Electronics", BrowseSort.Newest, false), (result.Scope.Kind, result.Scope.Title, result.Sort, result.Filtered));
        Assert.Contains(result.Scope.Alternates, a => a.Locale == "fa" && a.Path == $"c/{slug}");
        Assert.Equal(5, result.Scope.Alternates.Count);
        Assert.True(result.Total >= 8);
        Assert.Contains(result.Facets.SubCategories, s => s.Name == "Mobile phones" && s.Count == 3 && s.Path.StartsWith($"c/", StringComparison.Ordinal));
        Assert.Equal(result.Total, result.Facets.SubCategories.Sum(s => s.Count));

        var phones = result.Facets.SubCategories.Single(s => s.Name == "Mobile phones").Path.Split('/');
        var sub = await BrowseAsync(new BrowseQuery { Category = phones[1], SubCategory = phones[2] });
        Assert.Equal(("subcategory", 3, 2), (sub.Scope.Kind, sub.Total, sub.Scope.Breadcrumb.Count));
        Assert.Empty(sub.Facets.SubCategories);

        await using var db = Database.CreateContext();
        Assert.Equal(ProductBrowseService.NotFoundCode, (await Service(db).BrowseAsync(new BrowseQuery { Category = "nope" }, "en")).Error!.Code);
        Assert.Equal(ProductBrowseService.NotFoundCode, (await Service(db).BrowseAsync(new BrowseQuery { Category = slug, SubCategory = "nope" }, "en")).Error!.Code);
    }

    [Fact]
    public async Task Facets_count_with_the_other_filters_applied()
    {
        await SeedDemoAsync();
        var slug = await CategorySlugAsync("DEMO-PHN-001");
        var all = await BrowseAsync(new BrowseQuery { Category = slug });
        var orvia = all.Facets.Brands.Single(b => b.Label == "Orvia");

        var filtered = await BrowseAsync(new BrowseQuery { Category = slug, Brands = [orvia.Value] });

        Assert.Equal(orvia.Count, filtered.Total);
        Assert.True(filtered.Filtered);
        // The brand facet still counts every brand (its own filter is left out), and marks the chosen one.
        Assert.Equal(all.Facets.Brands.Select(b => (b.Label, b.Count)), filtered.Facets.Brands.Select(b => (b.Label, b.Count)));
        Assert.True(filtered.Facets.Brands.Single(b => b.Label == "Orvia").Selected);
        // Other facets narrow to Orvia's products.
        Assert.Equal(filtered.Total, filtered.Facets.SubCategories.Sum(s => s.Count));
    }

    [Fact]
    public async Task Spec_options_include_variant_options_and_numbers_and_flags_filter()
    {
        await SeedDemoAsync();
        var fashion = await CategorySlugAsync("DEMO-FSH-003");
        var colours = (await BrowseAsync(new BrowseQuery { Category = fashion })).Facets.Specs.Single(s => s.Code == "color");
        // The hoodie has no colour of its own: its variants are beige and grey.
        Assert.Contains(colours.Options, o => o.Value == "beige" && o.Count >= 1);

        var beige = await BrowseAsync(new BrowseQuery { Category = fashion, Options = new Dictionary<string, IReadOnlyList<string>> { ["color"] = ["beige"] } });
        Assert.Contains(beige.Items, p => p.Name.Contains("Hoodie", StringComparison.OrdinalIgnoreCase));
        Assert.True(beige.Facets.Specs.Single(s => s.Code == "color").Options.Single(o => o.Value == "beige").Selected);

        var electronics = await CategorySlugAsync("DEMO-PHN-001");
        var screens = (await BrowseAsync(new BrowseQuery { Category = electronics })).Facets.Specs.Single(s => s.Code == "screen_size");
        Assert.Equal(("Number", "Screen size"), (screens.Kind, screens.Name));
        Assert.True(screens.Min < screens.Max);
        var big = await BrowseAsync(new BrowseQuery { Category = electronics, Ranges = new Dictionary<string, NumberRange> { ["screen_size"] = new(screens.Max, null) } });
        Assert.True(big.Total >= 1);
        Assert.Equal(screens.Max, big.Facets.Specs.Single(s => s.Code == "screen_size").SelectedMin);

        var fiveG = (await BrowseAsync(new BrowseQuery { Category = electronics })).Facets.Specs.Single(s => s.Code == "network_5g");
        var only5G = await BrowseAsync(new BrowseQuery { Category = electronics, Flags = ["network_5g"] });
        Assert.Equal(fiveG.YesCount, only5G.Total);
        Assert.True(only5G.Facets.Specs.Single(s => s.Code == "network_5g").YesSelected);
    }

    [Fact]
    public async Task Price_stock_and_sale_filters_and_sorting()
    {
        await SeedDemoAsync();
        var all = await BrowseAsync(new BrowseQuery());
        Assert.Equal("all", all.Scope.Kind);
        var price = all.Facets.Price!;

        var cheap = await BrowseAsync(new BrowseQuery { MaxPrice = price.Min });
        Assert.All(cheap.Items, p => Assert.Equal(price.Min, p.Price));
        // The price facet ignores its own filter: the full range stays visible.
        Assert.Equal(price, cheap.Facets.Price);
        Assert.Equal(price.Min, cheap.Facets.SelectedMax);

        var sale = await BrowseAsync(new BrowseQuery { Discounted = true, Sort = BrowseSort.Discount });
        Assert.Equal(all.Facets.Discounted, sale.Total);
        Assert.All(sale.Items, p => Assert.True(p.CompareAtPrice > p.Price));
        var shares = sale.Items.Select(p => (p.CompareAtPrice!.Value - p.Price) / p.CompareAtPrice.Value).ToList();
        Assert.Equal(shares.OrderByDescending(s => s), shares);

        var byPrice = await BrowseAsync(new BrowseQuery { Sort = BrowseSort.PriceAsc });
        Assert.Equal(byPrice.Items.Select(p => p.Price).Order(), byPrice.Items.Select(p => p.Price));
        Assert.Equal(Math.Min(byPrice.Total, ProductBrowseService.PageSize), byPrice.Items.Count);
        var second = await BrowseAsync(new BrowseQuery { Sort = BrowseSort.PriceAsc, Page = 2 });
        Assert.Equal(Math.Max(0, byPrice.Total - ProductBrowseService.PageSize), second.Items.Count);
        Assert.Equal(2, second.Page);

        // A product whose stock is all reserved is not "in stock".
        await using (var db = Database.CreateContext())
        {
            var item = await StockItemLookup.FindAsync(db, "DEMO-FSH-001");
            var reservations = new StockReservationService(db, OnlineShopping.UnitTests.TestSupport.TestLedger.For(db, Store), Database.Clock,
                Options.Create(new InventoryOptions()), Options.Create(Store));
            await reservations.ReserveAsync("order:1", [new(item.Value, 33)]);
        }
        var inStock = await BrowseAsync(new BrowseQuery { InStock = true });
        Assert.DoesNotContain(inStock.Items, p => p.Name.Contains("Walking Shoes", StringComparison.Ordinal));
        Assert.Equal(all.Facets.InStock - 1, inStock.Total);
    }

    [Fact]
    public async Task Text_search_matches_any_language_brand_and_sku_best_name_first()
    {
        await SeedDemoAsync();
        await IndexSearchAsync();

        var hoodie = await BrowseAsync(new BrowseQuery { Text = "هودی" }, "fa");
        Assert.Equal(("search", "هودی", BrowseSort.Relevance), (hoodie.Scope.Kind, hoodie.Scope.Title, hoodie.Sort));
        Assert.Single(hoodie.Items);

        var brand = await BrowseAsync(new BrowseQuery { Text = "casana" });
        Assert.Equal(6, brand.Total);
        Assert.Single((await BrowseAsync(new BrowseQuery { Text = "demo-fsh-003-bg-s" })).Items);
        Assert.Empty((await BrowseAsync(new BrowseQuery { Text = "zzqx" })).Items);
        // Every word must match.
        Assert.Empty((await BrowseAsync(new BrowseQuery { Text = "casana zzqx" })).Items);
    }

    [Fact]
    public async Task A_brand_page_lists_the_brand_with_its_description_and_addresses()
    {
        await SeedDemoAsync();
        await using var db = Database.CreateContext();
        var slug = await db.Brands.Where(b => b.Name == "Orvia").Select(b => b.Slug).SingleAsync();

        var result = (await Service(db).BrowseAsync(new BrowseQuery { Brand = slug }, "en")).Value;

        Assert.Equal(("brand", "Orvia", 9), (result.Scope.Kind, result.Scope.Title, result.Total));
        Assert.StartsWith("b/", result.Scope.Path, StringComparison.Ordinal);
        Assert.Contains(result.Scope.Alternates, a => a.Locale == "fa");
        Assert.Equal(ProductBrowseService.NotFoundCode, (await Service(db).BrowseAsync(new BrowseQuery { Brand = "nope" }, "en")).Error!.Code);
    }

    [Fact]
    public async Task Cards_come_back_in_the_order_asked()
    {
        await SeedDemoAsync();
        await using var db = Database.CreateContext();
        var ids = await db.Products.OrderByDescending(p => p.PublicNumber).Select(p => p.Id).Take(3).ToListAsync();

        var cards = await new ProductQueries(db).CardsAsync([.. ids, Guid.NewGuid()], "en");

        var numbers = await db.Products.Where(p => ids.Contains(p.Id)).OrderByDescending(p => p.PublicNumber).Select(p => p.PublicNumber).ToListAsync();
        Assert.Equal(numbers, cards.Select(c => c.Number));
        Assert.Empty(await new ProductQueries(db).CardsAsync([], "en"));
    }
}
