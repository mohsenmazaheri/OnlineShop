using Microsoft.EntityFrameworkCore;
using OnlineShop.Application.Catalog.Compare;
using OnlineShop.Domain.Catalog;
using OnlineShop.Infrastructure.Identity;

namespace OnlineShop.UnitTests.Application;

public sealed class ProductComparisonTests : DemoCatalogTestBase
{
    [Fact]
    public async Task Comparable_specs_come_by_group_with_the_best_value_and_the_differences_marked()
    {
        await SeedDemoAsync();
        var first = await NumberOfAsync("DEMO-PHN-001");
        var second = await NumberOfAsync("DEMO-PHN-002");
        await using var db = Database.CreateContext();

        // Repeats and unknown numbers are dropped; the order is the one asked for.
        var result = await new ProductComparisonService(db).CompareAsync([second, first, second, 999_999], "en");

        Assert.Equal([second, first], result.Products.Select(p => p.Number));
        Assert.False(result.MixedCategories);
        Assert.All(result.Products, p => Assert.True(p.Price > 0));
        var rows = result.Groups.SelectMany(g => g.Rows).ToDictionary(r => r.Code);
        var storage = rows["storage"];
        Assert.Equal([256m, 128m], storage.Values.Select(v => v.Number!.Value));
        Assert.Equal("GB", storage.Unit);
        Assert.Equal([0], storage.Best);
        Assert.True(storage.Differs);
        // Lighter is better.
        Assert.Equal([1], rows["weight"].Best);
        Assert.False(rows["network_5g"].Differs);
        Assert.Empty(rows["network_5g"].Best);
        Assert.Equal([["Black"], ["Blue"]], rows["color"].Values.Select(v => v.Texts));
        // A group holds its specs together, in the specs' order.
        var memory = result.Groups.Single(g => g.Rows.Any(r => r.Code == "storage"));
        Assert.Contains(memory.Rows, r => r.Code == "ram");
    }

    [Fact]
    public async Task Variant_options_missing_values_translations_and_hidden_specs()
    {
        await SeedDemoAsync();
        var phone = await NumberOfAsync("DEMO-PHN-001");
        var hoodie = await NumberOfAsync("DEMO-FSH-003");
        await using var db = Database.CreateContext();
        var service = new ProductComparisonService(db);

        var result = await service.CompareAsync([phone, hoodie], "en");
        Assert.True(result.MixedCategories);
        var rows = result.Groups.SelectMany(g => g.Rows).ToDictionary(r => r.Code);
        // The hoodie's colours are its variants' options, in the spec's option order.
        Assert.Equal(["Blue"], rows["color"].Values[0].Texts);
        Assert.Equal(["Grey", "Beige"], rows["color"].Values[1].Texts);
        Assert.True(rows["storage"].Values[1].IsEmpty);
        Assert.Empty(rows["storage"].Best);

        var persian = await service.CompareAsync([phone], "fa");
        Assert.Equal(["آبی"], persian.Groups.SelectMany(g => g.Rows).Single(r => r.Code == "color").Values[0].Texts);

        await db.ProductSpecs.Where(s => s.Code == "storage").ExecuteUpdateAsync(s => s.SetProperty(x => x.IsComparable, false));
        await db.Products.Where(p => p.PublicNumber == hoodie).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsPublished, false));
        var after = await service.CompareAsync([phone, hoodie], "en");
        Assert.Equal([phone], after.Products.Select(p => p.Number));
        Assert.DoesNotContain(after.Groups.SelectMany(g => g.Rows), r => r.Code == "storage");
        Assert.Empty((await service.CompareAsync([], "en")).Products);
    }

    [Fact]
    public void Numbers_are_parsed_strictly_and_best_values_need_a_direction_and_a_difference()
    {
        Assert.Equal([12L, 15, 9, 8], ProductComparisonService.ParseNumbers("12,15 12;abc,-3,0,9,8,7"));
        Assert.Empty(ProductComparisonService.ParseNumbers(null));

        CompareValue N(decimal? n) => n is null ? CompareValue.None : new CompareValue(n, null, []);
        Assert.Equal([0, 2], ProductComparisonService.BestOf([N(5), N(3), N(5)], SpecKind.Number, SpecBetter.Higher));
        Assert.Equal([1], ProductComparisonService.BestOf([N(5), N(3), N(null)], SpecKind.Number, SpecBetter.Lower));
        Assert.Empty(ProductComparisonService.BestOf([N(5), N(5)], SpecKind.Number, SpecBetter.Higher));
        Assert.Empty(ProductComparisonService.BestOf([N(5), N(null)], SpecKind.Number, SpecBetter.Higher));
        Assert.Empty(ProductComparisonService.BestOf([N(5), N(3)], SpecKind.Number, SpecBetter.None));

        Assert.False(ProductComparisonService.Differ([N(5), N(5)]));
        Assert.True(ProductComparisonService.Differ([N(5), N(null)]));
        Assert.False(ProductComparisonService.Differ([new CompareValue(null, null, ["a", "b"]), new CompareValue(null, null, ["b", "a"])]));
    }
}

public sealed class CompareListServiceTests : DemoCatalogTestBase
{
    private async Task AddUserAsync(string id)
    {
        await using var db = Database.CreateContext();
        db.Users.Add(new AppUser { Id = id, UserName = id, NormalizedUserName = id.ToUpperInvariant() });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_customer_keeps_up_to_four_published_products_of_their_own()
    {
        await SeedDemoAsync();
        await AddUserAsync("u-1");
        await AddUserAsync("u-2");
        var numbers = new List<long>();
        foreach (var sku in new[] { "DEMO-PHN-001", "DEMO-PHN-002", "DEMO-PHN-003", "DEMO-LAP-001", "DEMO-LAP-002" })
            numbers.Add(await NumberOfAsync(sku));
        await using var db = Database.CreateContext();
        var lists = new CompareListService(db, Database.Clock);

        var saved = await lists.SaveAsync("u-1", [numbers[1], 999_999, numbers[0], numbers[1], .. numbers[2..]]);
        Assert.Equal([numbers[1], numbers[0], numbers[2], numbers[3]], saved.Numbers);
        Assert.Equal(saved.Numbers, (await lists.GetAsync("u-1")).Numbers);
        Assert.Empty((await lists.GetAsync("u-2")).Numbers);

        await lists.SaveAsync("u-2", [numbers[4]]);
        await db.Products.Where(p => p.PublicNumber == numbers[0]).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsPublished, false));
        Assert.Equal([numbers[1], numbers[2], numbers[3]], (await lists.GetAsync("u-1")).Numbers);
        Assert.Equal([numbers[4]], (await lists.GetAsync("u-2")).Numbers);

        Assert.Empty((await lists.SaveAsync("u-1", [])).Numbers);
        Assert.False(await db.CompareItems.AnyAsync(c => c.CustomerId == "u-1"));
        Assert.True(new SaveCompareListRequestValidator().Validate(new SaveCompareListRequest(numbers)).IsValid);
        Assert.False(new SaveCompareListRequestValidator().Validate(new SaveCompareListRequest(Enumerable.Range(1, 21).Select(n => (long)n).ToList())).IsValid);
    }
}
