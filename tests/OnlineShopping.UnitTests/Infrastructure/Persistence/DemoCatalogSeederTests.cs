using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OnlineShopping.Domain.Catalog;
using OnlineShopping.Infrastructure.Persistence.Seeding.Demo;
using OnlineShopping.UnitTests.TestSupport;
using StoreOptions = OnlineShopping.Application.Common.StoreOptions;

namespace OnlineShopping.UnitTests.Infrastructure.Persistence;

public sealed class DemoCatalogSeederTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private async Task SeedAsync(bool enabled = true, string defaultLocale = "fa", string currency = "IRR")
    {
        await using var db = _database.CreateContext();
        var seeder = new DemoCatalogSeeder(
            db,
            Options.Create(new StoreOptions { Locales = "fa,en,hy,de,ar", DefaultLocale = defaultLocale, Currency = currency }),
            Options.Create(new DemoCatalogOptions { Enabled = enabled }),
            NullLogger<DemoCatalogSeeder>.Instance);
        await seeder.SeedAsync();
    }

    [Fact]
    public async Task Does_nothing_unless_enabled()
    {
        await SeedAsync(enabled: false);

        await using var db = _database.CreateContext();
        Assert.Empty(db.Products);
        Assert.Empty(db.Categories);
    }

    [Fact]
    public async Task Adds_every_demo_product_with_translations_images_and_specs()
    {
        await SeedAsync();

        await using var db = _database.CreateContext();
        var products = await db.Products.Include(p => p.Images).Include(p => p.SpecValues).ToListAsync();
        Assert.Equal(DemoCatalogData.AllProducts.Count(), products.Count);
        Assert.All(products, p =>
        {
            Assert.True(p.IsPublished);
            Assert.Single(p.Images);
            Assert.NotEmpty(p.SpecValues);
            Assert.Equal(["ar", "de", "en", "hy"], p.Translations.Select(t => t.Locale).Order());
            Assert.All(p.Translations, t => Assert.False(string.IsNullOrWhiteSpace(t.Slug)));
        });
        Assert.Equal(Enumerable.Range(1001, products.Count).Select(n => (long)n), products.Select(p => p.PublicNumber).Order());
        Assert.Equal(4, await db.Categories.CountAsync());
        Assert.All(await db.Categories.ToListAsync(), c => Assert.StartsWith("/demo/categories/", c.ImageUrl));
    }

    [Fact]
    public async Task Writes_the_default_language_into_the_items_and_converts_prices_to_the_shop_currency()
    {
        await SeedAsync(defaultLocale: "en", currency: "IRR");

        await using var db = _database.CreateContext();
        var phone = await db.Products.SingleAsync(p => p.SKU == "DEMO-PHN-001");
        Assert.Equal("Orvia X1 Smartphone, 128 GB", phone.Name);
        Assert.Equal("orvia-x1-smartphone-128-gb", phone.Slug);
        Assert.Equal("گوشی هوشمند اورویا X1 ظرفیت ۱۲۸ گیگابایت", phone.Translations.Single(t => t.Locale == "fa").Name);
        Assert.Equal(349_000_000m, phone.Price);
        Assert.Equal(399_000_000m, phone.CompareAtPrice);

        var color = await db.ProductSpecs.Include(s => s.Options).SingleAsync(s => s.Code == "color");
        Assert.Equal("Colour", color.Name);
        Assert.Equal("رنگ", color.Translations.Single(t => t.Locale == "fa").Name);
        Assert.Contains(color.Options, o => o.Code == "blue" && o.Translations.Any(t => t.Locale == "de" && t.Value == "Blau"));
    }

    [Fact]
    public async Task Stores_each_value_in_the_column_of_its_kind()
    {
        await SeedAsync();

        await using var db = _database.CreateContext();
        var values = await db.ProductSpecValues.Include(v => v.Spec).Include(v => v.Option).ToListAsync();
        Assert.All(values, v =>
        {
            switch (v.Spec.Kind)
            {
                case SpecKind.Number: Assert.NotNull(v.NumberValue); break;
                case SpecKind.Boolean: Assert.NotNull(v.BooleanValue); break;
                case SpecKind.Text: Assert.False(string.IsNullOrWhiteSpace(v.TextValue)); break;
                case SpecKind.Option: Assert.Equal(v.SpecId, v.Option!.SpecId); break;
            }
        });
    }

    [Fact]
    public async Task Adds_brands_variants_and_a_spec_template_for_every_sub_category()
    {
        await SeedAsync();

        await using var db = _database.CreateContext();
        Assert.Equal(DemoCatalogData.Brands.Order(), (await db.Brands.Select(b => b.Name).ToListAsync()).Order());
        Assert.Equal(DemoCatalogData.AllProducts.Count(p => p.Brand is not null), await db.Products.CountAsync(p => p.BrandId != null));

        var hoodie = await db.Products.Include(p => p.Variants).SingleAsync(p => p.SKU == "DEMO-FSH-003");
        Assert.Equal(8, hoodie.Variants.Count);
        Assert.Equal(hoodie.Variants.Sum(v => v.Stock), hoodie.Stock);
        Assert.All(hoodie.Variants, v => Assert.Equal(2, v.Options.Count));

        var subs = await db.SubCategories.ToListAsync();
        Assert.All(subs, s => Assert.NotEmpty(s.TemplateSpecs));
    }

    [Fact]
    public async Task Runs_only_once()
    {
        await SeedAsync();
        await SeedAsync();

        await using var db = _database.CreateContext();
        Assert.Equal(DemoCatalogData.AllProducts.Count(), await db.Products.CountAsync());
        Assert.Equal(DemoCatalogData.Specs.Count, await db.ProductSpecs.CountAsync());
    }

    [Fact]
    public async Task Reuses_an_existing_category_and_continues_after_existing_product_numbers()
    {
        await using (var db = _database.CreateContext())
        {
            var category = new Category { Name = "کالای دیجیتال", Slug = "digital" };
            var sub = new SubCategory { Name = "متفرقه", Slug = "misc" };
            var group = new ProductGroup { Name = "متفرقه" };
            group.Products.Add(new Product { Name = "Own product", SKU = "OWN-1", PublicNumber = 1500 });
            sub.Groups.Add(group);
            category.SubCategories.Add(sub);
            db.Categories.Add(category);
            await db.SaveChangesAsync();
        }

        await SeedAsync();

        await using var check = _database.CreateContext();
        var electronics = await check.Categories.Include(c => c.SubCategories).SingleAsync(c => c.Name == "کالای دیجیتال");
        Assert.Equal("digital", electronics.Slug);
        Assert.Equal("/demo/categories/electronics.svg", electronics.ImageUrl);
        Assert.Contains(electronics.SubCategories, s => s.Slug == "mobile-phones");
        Assert.Equal(4, await check.Categories.CountAsync());
        Assert.Equal(1501, await check.Products.Where(p => p.SKU.StartsWith("DEMO-")).MinAsync(p => p.PublicNumber));
    }
}
