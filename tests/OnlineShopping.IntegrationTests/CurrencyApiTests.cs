using System.Net;
using System.Text.Json;
using OnlineShopping.IntegrationTests.TestSupport;

namespace OnlineShopping.IntegrationTests;

/// <summary>Display currencies on real PostgreSQL: seeded at start-up, public list, admin protected.</summary>
public sealed class CurrencyApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Startup_seeds_the_currencies_and_the_public_list_offers_the_base_with_language_defaults()
    {
        using var client = factory.CreateClient();

        using var body = JsonDocument.Parse(await client.GetStringAsync("/api/v1/currencies"));

        var root = body.RootElement;
        Assert.Equal("IRR", root.GetProperty("base").GetString());
        // Automatic rates are off in tests, so only the base currency has a rate and is offered.
        var codes = root.GetProperty("currencies").EnumerateArray().Select(c => c.GetProperty("code").GetString()).ToList();
        Assert.Equal(["IRR"], codes);
        Assert.Equal("IRR", root.GetProperty("localeDefaults").GetProperty("en").GetString());
    }

    [Fact]
    public async Task The_admin_endpoints_need_a_signed_in_user_with_settings_manage()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/admin/currencies");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
