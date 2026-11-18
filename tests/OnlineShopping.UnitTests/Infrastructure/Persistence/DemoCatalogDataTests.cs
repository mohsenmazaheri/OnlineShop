using OnlineShopping.Application.Catalog;
using OnlineShopping.Domain.Catalog;
using OnlineShopping.Infrastructure.Persistence.Seeding.Demo;

namespace OnlineShopping.UnitTests.Infrastructure.Persistence;

/// <summary>Checks the hand-written demo content itself: complete in every language, consistent, images present.</summary>
public sealed class DemoCatalogDataTests
{
    private static readonly List<DemoProduct> Products = DemoCatalogData.AllProducts.ToList();

    private static IEnumerable<DemoText> AllTexts() =>
        DemoCatalogData.Categories.Select(c => c.Name)
            .Concat(DemoCatalogData.Categories.SelectMany(c => c.SubCategories).Select(s => s.Name))
            .Concat(DemoCatalogData.Categories.SelectMany(c => c.SubCategories).SelectMany(s => s.Groups).Select(g => g.Name))
            .Concat(Products.SelectMany(p => new[] { p.Name, p.Description }))
            .Concat(DemoCatalogData.Specs.SelectMany(s => new[] { s.Name, s.Group }.Concat(s.Unit is null ? [] : [s.Unit])))
            .Concat(DemoCatalogData.Specs.SelectMany(s => s.Options ?? []).Select(o => o.Value));

    [Fact]
    public void Every_text_is_written_in_every_language()
    {
        Assert.All(AllTexts(), text => Assert.All(DemoText.Locales, l => Assert.False(string.IsNullOrWhiteSpace(text.For(l)), $"{text.En}: {l} missing")));
        // Every product name gives a usable slug in every language.
        Assert.All(Products, p => Assert.All(DemoText.Locales, l => Assert.NotEmpty(Slugs.From(p.Name.For(l)))));
    }

    [Fact]
    public void Skus_are_unique_demo_skus_and_prices_make_sense()
    {
        Assert.Equal(Products.Count, Products.Select(p => p.Sku).Distinct().Count());
        Assert.All(Products, p =>
        {
            Assert.StartsWith(DemoCatalogSeeder.SkuPrefix, p.Sku);
            Assert.True(p.PriceEur > 0);
            Assert.True(p.CompareAtEur is null || p.CompareAtEur > p.PriceEur, $"{p.Sku}: the old price must be higher");
            Assert.True(p.Stock > 0);
        });
        Assert.True(Products.Count >= 20);
    }

    [Fact]
    public void Every_value_matches_its_spec_kind_and_options()
    {
        var specs = DemoCatalogData.Specs.ToDictionary(s => s.Code);
        Assert.Equal(specs.Count, DemoCatalogData.Specs.Count);

        foreach (var product in Products)
        {
            Assert.Equal(product.Specs.Count, product.Specs.Select(v => v.Spec).Distinct().Count());
            foreach (var value in product.Specs)
            {
                Assert.True(specs.TryGetValue(value.Spec, out var spec), $"{product.Sku}: unknown spec {value.Spec}");
                var set = new object?[] { value.Number, value.Boolean, value.Text, value.Option }.Count(v => v is not null);
                Assert.Equal(1, set);
                switch (spec.Kind)
                {
                    case SpecKind.Number: Assert.NotNull(value.Number); break;
                    case SpecKind.Boolean: Assert.NotNull(value.Boolean); break;
                    case SpecKind.Text: Assert.NotNull(value.Text); break;
                    case SpecKind.Option: Assert.Contains(spec.Options!, o => o.Code == value.Option); break;
                }
            }
        }
    }

    [Fact]
    public void Every_image_exists_in_the_web_app()
    {
        var publicFolder = Path.Combine(RepositoryRoot(), "apps", "web", "public");
        var images = Products.Select(p => p.Image).Concat(DemoCatalogData.Categories.Select(c => c.Image));

        Assert.All(images, url => Assert.True(File.Exists(Path.Combine(publicFolder, url.TrimStart('/'))), $"missing {url}"));
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OnlineShopping.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("OnlineShopping.sln not found above the test folder.");
    }
}
