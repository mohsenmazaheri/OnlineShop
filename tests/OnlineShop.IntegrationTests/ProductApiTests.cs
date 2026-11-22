using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OnlineShop.Application.Catalog;
using OnlineShop.Domain.Catalog;
using OnlineShop.Infrastructure.Persistence;
using OnlineShop.IntegrationTests.TestSupport;

namespace OnlineShop.IntegrationTests;

/// <summary>Public product endpoints on real PostgreSQL (translations, typed specs, decimal ordering).</summary>
public sealed class ProductApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<long> SeedAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var color = new ProductSpec { Code = $"color-{Guid.NewGuid():N}", Name = "رنگ", GroupName = "کلی", Kind = SpecKind.Option, ShowInSummary = true,
            Translations = [new SpecTranslation { Locale = "en", Name = "Colour", GroupName = "General" }] };
        var black = new SpecOption { Code = "black", Value = "مشکی", Translations = [new SpecOptionTranslation { Locale = "en", Value = "Black" }] };
        color.Options.Add(black);

        var number = Random.Shared.NextInt64(100_000, 900_000);
        var group = new ProductGroup { Name = "گوشی", SubCategory = new SubCategory { Name = "موبایل", Slug = "mobile",
            Category = new Category { Name = "دیجیتال", Slug = $"digital-{Guid.NewGuid():N}", Translations = [new CatalogTranslation { Locale = "en", Name = "Electronics" }] } } };
        var phone = new Product
        {
            Name = "گوشی نمونه", Slug = "گوشی-نمونه", SKU = $"P-{number}", PublicNumber = number, Price = 80, CompareAtPrice = 100, Stock = 3,
            IsPublished = true, ProductGroup = group,
            Translations = [new CatalogTranslation { Locale = "en", Name = "Sample phone", Slug = "sample-phone", Description = "A phone." }],
            Images = [new ProductImage { Url = "/demo/products/phone-x1.svg" }],
            SpecValues = [new ProductSpecValue { Spec = color, Option = black }],
        };
        var bigDiscount = new Product { Name = "B", Slug = "b", SKU = $"B-{number}", PublicNumber = number + 1, Price = 10, CompareAtPrice = 100, Stock = 1, IsPublished = true, ProductGroup = group };
        var draft = new Product { Name = "Draft", Slug = "draft", SKU = $"D-{number}", PublicNumber = number + 2, Price = 1, IsPublished = false, ProductGroup = group };
        db.AddRange(phone, bigDiscount, draft);
        await db.SaveChangesAsync();
        return number;
    }

    [Fact]
    public async Task Detail_is_translated_with_specs_and_alternates()
    {
        var number = await SeedAsync();
        using var client = factory.CreateClient();

        using var body = JsonDocument.Parse(await client.GetStringAsync($"/api/v1/catalog/products/{number}?locale=en"));

        var root = body.RootElement;
        Assert.Equal("Sample phone", root.GetProperty("name").GetString());
        Assert.Equal("A phone.", root.GetProperty("description").GetString());
        Assert.Equal("Electronics", root.GetProperty("category").GetProperty("name").GetString());
        var spec = root.GetProperty("specs")[0];
        Assert.Equal(("Colour", "Black", "Option"), (spec.GetProperty("name").GetString(), spec.GetProperty("value").GetString(), spec.GetProperty("kind").GetString()));
        var alternates = root.GetProperty("alternates").EnumerateArray().ToDictionary(a => a.GetProperty("locale").GetString()!, a => a.GetProperty("slug").GetString());
        Assert.Equal("گوشی-نمونه", alternates["fa"]);
        Assert.Equal("sample-phone", alternates["en"]);
    }

    [Fact]
    public async Task Discounted_list_orders_by_discount_share_and_hides_drafts()
    {
        var number = await SeedAsync();
        using var client = factory.CreateClient();

        using var body = JsonDocument.Parse(await client.GetStringAsync("/api/v1/catalog/products?list=Discounted&locale=fa&take=48"));

        var numbers = body.RootElement.EnumerateArray().Select(p => p.GetProperty("number").GetInt64()).ToList();
        Assert.True(numbers.IndexOf(number + 1) < numbers.IndexOf(number), "90% off comes before 20% off");
        Assert.DoesNotContain(number + 2, numbers);
    }

    [Fact]
    public async Task Slug_endpoint_and_unknown_products()
    {
        var number = await SeedAsync();
        using var client = factory.CreateClient();

        using var slug = JsonDocument.Parse(await client.GetStringAsync($"/api/v1/catalog/products/{number}/slug?locale=en"));
        Assert.Equal("sample-phone", slug.RootElement.GetProperty("slug").GetString());

        foreach (var path in new[] { $"/api/v1/catalog/products/{number + 2}", "/api/v1/catalog/products/1/slug" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(ProductQueries.NotFoundCode, problem.RootElement.GetProperty("code").GetString());
        }
    }
}
