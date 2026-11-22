using OnlineShop.Application.Settings;
using OnlineShop.Domain.Settings;
using OnlineShop.UnitTests.TestSupport;

namespace OnlineShop.UnitTests.Application;

public sealed class BrandingServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();
    private readonly InMemoryFileStorage _storage = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task Defaults_apply_until_the_admin_saves()
    {
        await using var db = _database.CreateContext();

        Assert.Equal(BrandingService.Defaults, await new BrandingService(db, _storage.Media()).GetAsync());
    }

    [Fact]
    public async Task Saving_twice_keeps_one_row_and_lower_cases_the_colour()
    {
        await using (var db = _database.CreateContext())
        {
            await new BrandingService(db, _storage.Media()).SaveAsync(new SaveBrandingRequest("#AA3300", CornerStyle.Round, FontChoice.System, HomeLayout.Catalog));
            await new BrandingService(db, _storage.Media()).SaveAsync(new SaveBrandingRequest("#1D4ED8", CornerStyle.Sharp, FontChoice.Vazirmatn, HomeLayout.Showcase));
        }

        await using var check = _database.CreateContext();
        Assert.Single(check.StoreBranding);
        Assert.Equal(new BrandingView("#1d4ed8", CornerStyle.Sharp, FontChoice.Vazirmatn, HomeLayout.Showcase, null, null), await new BrandingService(check, _storage.Media()).GetAsync());
    }

    [Theory]
    [InlineData("#0f766e", true)]
    [InlineData("#0F766E", true)]
    [InlineData("0f766e", false)]
    [InlineData("#0f766", false)]
    [InlineData("red", false)]
    [InlineData("#0f766e;}body{display:none", false)]
    public void Only_hex_colours_pass_because_they_go_into_css(string color, bool valid)
    {
        var result = new SaveBrandingRequestValidator().Validate(new SaveBrandingRequest(color, CornerStyle.Soft, FontChoice.Vazirmatn, HomeLayout.Classic));

        Assert.Equal(valid, result.IsValid);
        if (!valid) Assert.Equal(BrandingErrors.ColorInvalid, result.Errors.Single().ErrorCode);
    }

    [Fact]
    public void Unknown_enum_values_are_rejected()
    {
        var result = new SaveBrandingRequestValidator().Validate(new SaveBrandingRequest("#000000", (CornerStyle)9, FontChoice.System, (HomeLayout)9));

        Assert.Equal(2, result.Errors.Count);
    }

    [Fact]
    public async Task The_logo_and_banner_are_uploaded_replaced_and_removed()
    {
        await using var db = _database.CreateContext();
        var service = new BrandingService(db, _storage.Media());
        var png = TestImages.Png(400, 200);

        var first = (await service.SetImageAsync(BrandingImage.Logo, new MemoryStream(png), png.Length)).Value;
        var second = (await service.SetImageAsync(BrandingImage.Logo, new MemoryStream(png), png.Length)).Value;
        var hero = (await service.SetImageAsync(BrandingImage.Hero, new MemoryStream(png), png.Length)).Value;

        Assert.EndsWith("/300.webp", second.LogoUrl);
        Assert.EndsWith("/1200.webp", hero.HeroImageUrl);
        Assert.NotEqual(first.LogoUrl, second.LogoUrl);
        Assert.Equal(6, _storage.Files.Count);

        var removed = await service.RemoveImageAsync(BrandingImage.Logo);
        Assert.Null(removed.LogoUrl);
        Assert.Equal(3, _storage.Files.Count);
        Assert.Equal("media.type_not_allowed", (await service.SetImageAsync(BrandingImage.Hero, new MemoryStream("x"u8.ToArray()), 1)).Error!.Code);
    }
}
