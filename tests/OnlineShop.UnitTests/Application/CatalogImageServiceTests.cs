using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnlineShop.Application.Catalog.Admin;
using OnlineShop.Application.Common;
using OnlineShop.Application.Media;
using OnlineShop.Domain.Catalog;
using OnlineShop.Infrastructure.Persistence;
using OnlineShop.UnitTests.TestSupport;

namespace OnlineShop.UnitTests.Application;

public sealed class CatalogImageServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();
    private readonly InMemoryFileStorage _storage = new();
    private readonly StoreOptions _store = new() { Locales = "fa,en", DefaultLocale = "fa" };

    public void Dispose() => _database.Dispose();

    private CatalogImageService Service(AppDbContext db) => new(db, _storage.Media(), Options.Create(_store));

    private async Task<(long Number, Guid CategoryId)> SeedAsync()
    {
        await using var db = _database.CreateContext();
        var product = await TestData.SaveProductAsync(db);
        var categoryId = await db.Categories.Select(c => c.Id).SingleAsync();
        return (product.PublicNumber, categoryId);
    }

    private static MemoryStream Png() => new(TestImages.Png(640, 480));

    [Fact]
    public async Task Uploads_reorders_and_translates_product_images()
    {
        var (number, _) = await SeedAsync();
        await using var db = _database.CreateContext();
        var first = (await Service(db).AddProductImageAsync(number, Png(), Png().Length)).Value;
        var second = (await Service(db).AddProductImageAsync(number, Png(), Png().Length)).Value;

        var saved = await Service(db).SaveProductImagesAsync(number, new SaveProductImagesRequest(
        [
            new ProductImageInput(second.Id, [new ImageAltText("fa", "نمای جلو"), new ImageAltText("en", "Front view")]),
            new ProductImageInput(first.Id, [new ImageAltText("fa", ""), new ImageAltText("en", "")]),
        ]));

        Assert.Equal([second.Id, first.Id], saved.Value.Select(i => i.Id));
        Assert.Equal([new ImageAltText("fa", "نمای جلو"), new ImageAltText("en", "Front view")], saved.Value[0].Alts);
        Assert.Equal((640, 480), (second.Width, second.Height));
        Assert.StartsWith(MediaUrls.Prefix, second.Url);
        Assert.Equal(6, _storage.Files.Count);
    }

    [Fact]
    public async Task Reordering_needs_every_image_exactly_once()
    {
        var (number, _) = await SeedAsync();
        await using var db = _database.CreateContext();
        var image = (await Service(db).AddProductImageAsync(number, Png(), Png().Length)).Value;

        var missing = await Service(db).SaveProductImagesAsync(number, new SaveProductImagesRequest([]));
        var twice = await Service(db).SaveProductImagesAsync(number, new SaveProductImagesRequest([new(image.Id, []), new(image.Id, [])]));

        Assert.Equal(CatalogErrors.ImagesMismatch, missing.Error!.Code);
        Assert.Equal(CatalogErrors.ImagesMismatch, twice.Error!.Code);
    }

    [Fact]
    public async Task Deleting_an_image_removes_its_files_and_limits_apply()
    {
        var (number, _) = await SeedAsync();
        await using var db = _database.CreateContext();
        var image = (await Service(db).AddProductImageAsync(number, Png(), Png().Length)).Value;

        Assert.True((await Service(db).DeleteProductImageAsync(number, image.Id)).IsSuccess);
        Assert.Empty(_storage.Files);
        Assert.Equal(CatalogErrors.NotFound, (await Service(db).DeleteProductImageAsync(number, image.Id)).Error!.Code);
        Assert.Equal(CatalogErrors.NotFound, (await Service(db).AddProductImageAsync(999_999, Png(), Png().Length)).Error!.Code);

        for (var i = 0; i < CatalogImageService.MaxImagesPerProduct; i++)
            await Service(db).AddProductImageAsync(number, Png(), Png().Length);
        Assert.Equal(CatalogErrors.TooManyImages, (await Service(db).AddProductImageAsync(number, Png(), Png().Length)).Error!.Code);
    }

    [Fact]
    public async Task A_category_image_replaces_the_previous_one()
    {
        var (_, categoryId) = await SeedAsync();
        await using var db = _database.CreateContext();

        var first = (await Service(db).SetCategoryImageAsync(categoryId, Png(), Png().Length)).Value;
        var second = (await Service(db).SetCategoryImageAsync(categoryId, Png(), Png().Length)).Value;

        Assert.EndsWith("/600.webp", second);
        var oldKey = MediaUrls.KeyOf(first)!;
        Assert.DoesNotContain(_storage.Files.Keys, k => k.StartsWith(oldKey, StringComparison.Ordinal));
        Assert.True((await Service(db).RemoveCategoryImageAsync(categoryId)).IsSuccess);
        Assert.Empty(_storage.Files);
        Assert.Null((await db.Set<Category>().AsNoTracking().SingleAsync()).ImageUrl);
    }
}
