using OnlineShopping.Application.Settings;
using OnlineShopping.Domain.Settings;
using OnlineShopping.UnitTests.TestSupport;

namespace OnlineShopping.UnitTests.Application;

public sealed class BrandingServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task Defaults_apply_until_the_admin_saves()
    {
        await using var db = _database.CreateContext();

        Assert.Equal(BrandingService.Defaults, await new BrandingService(db).GetAsync());
    }

    [Fact]
    public async Task Saving_twice_keeps_one_row_and_lower_cases_the_colour()
    {
        await using (var db = _database.CreateContext())
        {
            await new BrandingService(db).SaveAsync(new SaveBrandingRequest("#AA3300", CornerStyle.Round, FontChoice.System, HomeLayout.Catalog));
            await new BrandingService(db).SaveAsync(new SaveBrandingRequest("#1D4ED8", CornerStyle.Sharp, FontChoice.Vazirmatn, HomeLayout.Showcase));
        }

        await using var check = _database.CreateContext();
        Assert.Single(check.StoreBranding);
        Assert.Equal(new BrandingView("#1d4ed8", CornerStyle.Sharp, FontChoice.Vazirmatn, HomeLayout.Showcase), await new BrandingService(check).GetAsync());
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
}
