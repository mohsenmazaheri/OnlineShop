using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OnlineShop.Application.Accounts;
using OnlineShop.Application.Customers;
using OnlineShop.Infrastructure.Notifications;
using OnlineShop.Infrastructure.Persistence;
using OnlineShop.IntegrationTests.TestSupport;

namespace OnlineShop.IntegrationTests;

/// <summary>Web Push on PostgreSQL: the shop's key, and a signed-in customer's browser switching notifications on and off.</summary>
public sealed class PushApiTests(PushApiTests.PushFactory factory) : IClassFixture<PushApiTests.PushFactory>
{
    public sealed class PushFactory : ApiFactory
    {
        public const string Email = "push-owner@shop.test";
        public const string Password = "push pass 2026"; // test-only value for this throwaway database

        public static readonly (string PublicKey, string PrivateKey) Keys = Vapid.GenerateKeys();

        protected override IReadOnlyDictionary<string, string?> ConfigurationOverrides => new Dictionary<string, string?>
        {
            ["Database:AdminEmail"] = Email,
            ["Database:AdminPassword"] = Password,
            ["WebPush:PublicKey"] = Keys.PublicKey,
            ["WebPush:PrivateKey"] = Keys.PrivateKey,
        };
    }

    private static PushSubscriptionRequest Browser(string endpoint) =>
        new(endpoint, Vapid.GenerateKeys().PublicKey, Base64Url.Encode(RandomNumberGenerator.GetBytes(16)), "en");

    [Fact]
    public async Task A_customer_switches_notifications_on_and_off_for_a_browser()
    {
        using var anyone = factory.CreateClient();
        var settings = await (await anyone.GetAsync("/api/v1/push")).Content.ReadFromJsonAsync<PushSettingsView>();
        Assert.Equal((true, PushFactory.Keys.PublicKey), (settings!.Enabled, settings.PublicKey));
        Assert.Equal(HttpStatusCode.Unauthorized, (await anyone.PutAsJsonAsync("/api/v1/account/push", Browser("https://push.test/a"))).StatusCode);

        using var customer = factory.CreateClient();
        (await customer.PostAsJsonAsync("/api/v1/account/login", new LoginRequest(PushFactory.Email, PushFactory.Password, false))).EnsureSuccessStatusCode();
        var invalid = await customer.PutAsJsonAsync("/api/v1/account/push", Browser("http://push.test/a"));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        (await customer.PutAsJsonAsync("/api/v1/account/push", Browser("https://push.test/a"))).EnsureSuccessStatusCode();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal("https://push.test/a", await db.PushSubscriptions.Select(s => s.Endpoint).SingleAsync());
        }
        var removed = await customer.PostAsJsonAsync("/api/v1/account/push/remove", new PushUnsubscribeRequest("https://push.test/a"));
        Assert.True(await removed.Content.ReadFromJsonAsync<bool>());
        using (var scope = factory.Services.CreateScope())
            Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().PushSubscriptions.AnyAsync());
    }
}

/// <summary>A shop without VAPID keys offers no notifications.</summary>
public sealed class PushDisabledApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Without_keys_the_shop_sends_no_notifications()
    {
        using var client = factory.CreateClient();
        var settings = await (await client.GetAsync("/api/v1/push")).Content.ReadFromJsonAsync<PushSettingsView>();
        Assert.Equal((false, (string?)null), (settings!.Enabled, settings.PublicKey));
    }
}
