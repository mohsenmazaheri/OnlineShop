using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OnlineShopping.Application.Catalog;
using OnlineShopping.Application.Common;
using OnlineShopping.Domain.Catalog;
using OnlineShopping.Infrastructure.Persistence.Seeding.Demo;
using OnlineShopping.UnitTests.TestSupport;

namespace OnlineShopping.UnitTests.Application;

public sealed class ProductQueriesTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    /// <summary>The demo catalog (default language fa, prices in EUR) as realistic test data.</summary>
    private async Task SeedDemoAsync()
    {
        await using var db = _database.CreateContext();
        await new DemoCatalogSeeder(
            db,
            Options.Create(new StoreOptions { Locales = "fa,en,hy,de,ar", DefaultLocale = "fa", Currency = "EUR" }),
            Options.Create(new DemoCatalogOptions { Enabled = true }),
            NullLogger<DemoCatalogSeeder>.Instance).SeedAsync();
    }

    private ProductQueries Queries(out OnlineShopping.Infrastructure.Persistence.AppDbContext db)
    {
        db = _database.CreateContext();
        return new ProductQueries(db);
    }

    [Fact]
    public async Task Discounted_lists_only_products_with_an_old_price_largest_discount_first()
    {
        await SeedDemoAsync();
        var queries = Queries(out var db);
        await using var _ = db;

        var list = await queries.ListAsync(ProductList.Discounted, "en", 48);

        Assert.NotEmpty(list);
        Assert.All(list, p => Assert.True(p.CompareAtPrice > p.Price));
        var shares = list.Select(p => (p.CompareAtPrice!.Value - p.Price) / p.CompareAtPrice.Value).ToList();
        Assert.Equal(shares.OrderByDescending(s => s), shares);
        Assert.Equal(DemoCatalogData.AllProducts.Count(p => p.CompareAtEur is not null), list.Count);
    }

    [Fact]
    public async Task Lists_are_translated_and_limited()
    {
        await SeedDemoAsync();
        var queries = Queries(out var db);
        await using var _ = db;

        var english = await queries.ListAsync(ProductList.Newest, "en", 5);
        var persian = await queries.ListAsync(ProductList.Newest, "fa", 500);

        Assert.Equal(5, english.Count);
        Assert.All(english, p => Assert.Matches("^[\\x20-\\x7E]+$", p.Name));
        // A larger request is capped at MaxListSize (48); the demo has 22 products.
        Assert.Equal(DemoCatalogData.AllProducts.Count(), persian.Count);
        Assert.All(persian, p => Assert.StartsWith("/demo/products/", p.ImageUrl));
    }

    [Fact]
    public async Task Detail_has_translated_specs_breadcrumb_and_every_alternate_language()
    {
        await SeedDemoAsync();
        var queries = Queries(out var db);
        await using var _ = db;
        var number = (await queries.ListAsync(ProductList.Newest, "fa", 48)).Single(p => p.Name.Contains("X1 پرو", StringComparison.Ordinal)).Number;

        var result = await queries.GetAsync(number, "de", "fa");

        var phone = result.Value;
        Assert.Equal("Orvia X1 Pro Smartphone, 256 GB", phone.Name);
        Assert.Equal("orvia-x1-pro-smartphone-256-gb", phone.Slug);
        Assert.Equal("Elektronik", phone.Category.Name);
        Assert.Equal("Handys", phone.SubCategory.Name);
        Assert.Equal(["/demo/products/phone-x1-pro.svg"], phone.Images);
        Assert.True(phone.InStock);

        var color = phone.Specs.Single(s => s.Code == "color");
        Assert.Equal(("Farbe", "Schwarz", SpecKind.Option), (color.Name, color.Value, color.Kind));
        var screen = phone.Specs.Single(s => s.Code == "screen_size");
        Assert.Equal((6.7m, "Zoll", "Display"), (screen.Number, screen.Unit, screen.Group));
        Assert.True(phone.Specs.Single(s => s.Code == "network_5g").Boolean);

        Assert.Equal(["ar", "de", "en", "fa", "hy"], phone.Alternates.Select(a => a.Locale).Order());
        Assert.StartsWith("گوشی-هوشمند", phone.Alternates.Single(a => a.Locale == "fa").Slug);
    }

    [Fact]
    public async Task Untranslated_products_fall_back_to_the_default_language()
    {
        await using (var db = _database.CreateContext())
        {
            var product = await TestData.SaveProductAsync(db);
            product.Slug = "phone-x";
            product.IsPublished = true;
            await db.SaveChangesAsync();
        }
        var queries = Queries(out var context);
        await using var _ = context;
        var number = (await queries.ListAsync(ProductList.Newest, "en", 1)).Single().Number;

        var detail = (await queries.GetAsync(number, "hy", "en")).Value;

        Assert.Equal("Phone X", detail.Name);
        Assert.Equal([new ProductAlternate("en", "phone-x")], detail.Alternates);
        Assert.Equal("phone-x", (await queries.GetSlugAsync(number, "hy")).Value);
    }

    [Fact]
    public async Task Unpublished_deleted_and_unknown_products_are_not_found()
    {
        long hidden, deleted;
        await using (var db = _database.CreateContext())
        {
            var draft = await TestData.SaveProductAsync(db, sku: "DRAFT");
            var gone = await TestData.SaveProductAsync(db, sku: "GONE");
            gone.IsPublished = true;
            db.Products.Remove(gone);
            await db.SaveChangesAsync();
            (hidden, deleted) = (draft.PublicNumber, gone.PublicNumber);
        }
        var queries = Queries(out var context);
        await using var _ = context;

        foreach (var number in new[] { hidden, deleted, 999_999L })
        {
            var result = await queries.GetAsync(number, "fa", "fa");
            Assert.Equal(ErrorKind.NotFound, result.Error!.Kind);
            Assert.Equal(ProductQueries.NotFoundCode, result.Error.Code);
            Assert.Equal(ErrorKind.NotFound, (await queries.GetSlugAsync(number, "fa")).Error!.Kind);
        }
        Assert.Empty(await queries.ListAsync(ProductList.Newest, "fa", 10));
    }
}
