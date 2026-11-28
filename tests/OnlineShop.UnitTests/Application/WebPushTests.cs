using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using OnlineShop.Application.Catalog.Admin;
using OnlineShop.Application.Common;
using OnlineShop.Application.Customers;
using OnlineShop.Application.Notifications;
using OnlineShop.Application.Sales;
using OnlineShop.Domain.Customers;
using OnlineShop.Infrastructure.Identity;
using OnlineShop.Infrastructure.Notifications;
using OnlineShop.Infrastructure.Persistence;
using OnlineShop.UnitTests.TestSupport;

namespace OnlineShop.UnitTests.Application;

public sealed class WebPushCryptoTests
{
    [Fact]
    public void Encryption_matches_the_worked_example_of_RFC_8291()
    {
        // RFC 8291, Appendix A.
        var plaintext = Encoding.UTF8.GetBytes("When I grow up, I want to be a watermelon");
        var userAgentPublic = Base64Url.Decode("BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4");
        var authSecret = Base64Url.Decode("BTBZMqHH6r4Tts7J_aSIgg");
        var salt = Base64Url.Decode("DGv6ra1nlYgDCS1FRnbzlw");
        var serverPublic = Base64Url.Decode("BP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A8");
        var serverParameters = new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = Base64Url.Decode("yfWPiYE-n46HLnH0KqZOF1fJJU3MYrct3AELtAQ-oRw"),
            Q = new ECPoint { X = serverPublic[1..33], Y = serverPublic[33..65] },
        };
        using var serverKey = ECDiffieHellman.Create(serverParameters);

        var message = WebPushCrypto.Encrypt(plaintext, userAgentPublic, authSecret, serverKey, salt);

        Assert.Equal(
            "DGv6ra1nlYgDCS1FRnbzlwAAEABBBP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A_yl95bQpu6cVPTpK4Mqgkf1CXztLVBSt2Ks3oZwbuwXPXLWyouBWLVWGNWQexSgSxsj_Qulcy4a-fN",
            Base64Url.Encode(message));
    }

    [Fact]
    public void Each_message_gets_its_own_key_and_salt_and_bad_keys_are_refused()
    {
        var (publicKey, _) = Vapid.GenerateKeys();
        var browser = Base64Url.Decode(publicKey);
        var auth = RandomNumberGenerator.GetBytes(16);
        var first = WebPushCrypto.Encrypt("hi"u8.ToArray(), browser, auth);
        var second = WebPushCrypto.Encrypt("hi"u8.ToArray(), browser, auth);
        Assert.NotEqual(first[..16], second[..16]);
        Assert.Equal(16 + 4 + 1 + 65 + 3 + 16, first.Length);
        Assert.Throws<ArgumentException>(() => WebPushCrypto.Encrypt("hi"u8.ToArray(), new byte[33], auth));
        Assert.Throws<ArgumentException>(() => WebPushCrypto.Encrypt(new byte[4000], browser, auth));
    }

    [Fact]
    public void Vapid_tokens_are_ES256_signed_for_the_push_service_and_12_hours()
    {
        var (publicKey, privateKey) = Vapid.GenerateKeys();
        Assert.Equal((65, 32), (Base64Url.Decode(publicKey).Length, Base64Url.Decode(privateKey).Length));
        var now = new DateTimeOffset(2026, 11, 28, 10, 0, 0, TimeSpan.Zero);

        var token = Vapid.Token("https://fcm.googleapis.com", "mailto:shop@example.com", now, publicKey, privateKey);

        var parts = token.Split('.');
        using var claims = JsonDocument.Parse(Base64Url.Decode(parts[1]));
        Assert.Equal("https://fcm.googleapis.com", claims.RootElement.GetProperty("aud").GetString());
        Assert.Equal(now.AddHours(12).ToUnixTimeSeconds(), claims.RootElement.GetProperty("exp").GetInt64());
        Assert.Equal("mailto:shop@example.com", claims.RootElement.GetProperty("sub").GetString());
        var publicPoint = Base64Url.Decode(publicKey);
        using var verifier = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = publicPoint[1..33], Y = publicPoint[33..65] } });
        Assert.True(verifier.VerifyData(Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"), Base64Url.Decode(parts[2]), HashAlgorithmName.SHA256));
    }

    private sealed class RecordingHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public HttpRequestMessage? Last { get; private set; }

        public byte[] Body { get; private set; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Last = request;
            Body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            return new HttpResponseMessage(status);
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.Created, PushResult.Sent)]
    [InlineData(HttpStatusCode.Gone, PushResult.Gone)]
    [InlineData(HttpStatusCode.NotFound, PushResult.Gone)]
    [InlineData(HttpStatusCode.TooManyRequests, PushResult.Failed)]
    public async Task The_sender_posts_an_encrypted_signed_message(HttpStatusCode status, PushResult expected)
    {
        var (publicKey, privateKey) = Vapid.GenerateKeys();
        var handler = new RecordingHandler(status);
        var options = Options.Create(new WebPushOptions { PublicKey = publicKey, PrivateKey = privateKey });
        var sender = new WebPushSender(new HttpClient(handler), options, Options.Create(new StoreOptions { PublicUrl = "https://shop.test" }),
            new FakeTimeProvider(new DateTimeOffset(2026, 11, 28, 10, 0, 0, TimeSpan.Zero)));
        var browser = Vapid.GenerateKeys().PublicKey;

        var result = await sender.SendAsync(new PushTarget("https://push.example.com/send/abc", browser, Base64Url.Encode(RandomNumberGenerator.GetBytes(16))),
            new PushMessage("Order 1001", "Shipped", "/en/account/orders/1001"));

        Assert.Equal(expected, result);
        Assert.Equal((true, publicKey), (sender.IsEnabled, sender.PublicKey));
        var request = handler.Last!;
        Assert.Equal(new Uri("https://push.example.com/send/abc"), request.RequestUri);
        Assert.Equal("aes128gcm", Assert.Single(request.Content!.Headers.ContentEncoding));
        Assert.Equal("86400", Assert.Single(request.Headers.GetValues("TTL")));
        var authorization = Assert.Single(request.Headers.GetValues("Authorization"));
        Assert.StartsWith("vapid t=", authorization, StringComparison.Ordinal);
        Assert.EndsWith($", k={publicKey}", authorization, StringComparison.Ordinal);
        // The body is encrypted: the text never travels in the clear.
        Assert.DoesNotContain("Shipped", Encoding.UTF8.GetString(handler.Body), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_keys_the_sender_is_off()
    {
        var sender = new WebPushSender(new HttpClient(new RecordingHandler(HttpStatusCode.Created)), Options.Create(new WebPushOptions()),
            Options.Create(new StoreOptions()), TimeProvider.System);
        Assert.Equal((false, (string?)null), (sender.IsEnabled, sender.PublicKey));
        Assert.Equal(PushResult.Failed, await sender.SendAsync(new PushTarget("https://x.test", "k", "a"), new PushMessage("t", "b", "/")));
        Assert.Equal(PushResult.Failed, await new NoWebPushSender().SendAsync(new PushTarget("https://x.test", "k", "a"), new PushMessage("t", "b", "/")));
    }
}

public sealed class PushSubscriptionServiceTests : DemoCatalogTestBase
{
    private readonly FakeWebPushSender _sender = new();

    private PushSubscriptionService Service(AppDbContext db) => new(db, _sender, Options.Create(Store), NullLogger<PushSubscriptionService>.Instance);

    private static PushSubscriptionRequest Request(string endpoint, string? locale = "en") =>
        new(endpoint, Vapid.GenerateKeys().PublicKey, Base64Url.Encode(RandomNumberGenerator.GetBytes(16)), locale);

    private async Task AddUsersAsync(params string[] ids)
    {
        await using var db = Database.CreateContext();
        foreach (var id in ids)
            db.Users.Add(new AppUser { Id = id, UserName = id, NormalizedUserName = id.ToUpperInvariant() });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Browsers_subscribe_move_between_accounts_and_are_capped()
    {
        await AddUsersAsync("u-1", "u-2");
        await using var db = Database.CreateContext();
        var push = Service(db);

        Assert.True((await push.SubscribeAsync("u-1", Request("https://push.test/a"))).Value);
        Assert.True((await push.SubscribeAsync("u-1", Request("https://push.test/a", "de"))).Value);
        Assert.Equal(("u-1", "de"), await db.PushSubscriptions.Select(s => new ValueTuple<string, string>(s.CustomerId, s.Locale)).SingleAsync());
        // The same browser signed in with another account now belongs to it.
        await push.SubscribeAsync("u-2", Request("https://push.test/a"));
        Assert.Equal("u-2", await db.PushSubscriptions.Select(s => s.CustomerId).SingleAsync());
        Assert.False(await push.UnsubscribeAsync("u-1", "https://push.test/a"));
        Assert.True(await push.UnsubscribeAsync("u-2", "https://push.test/a"));

        for (var i = 0; i < PushSubscription.MaxPerCustomer + 2; i++)
        {
            await push.SubscribeAsync("u-1", Request($"https://push.test/{i}"));
            Database.Clock.Advance(TimeSpan.FromSeconds(1));
        }
        var kept = await db.PushSubscriptions.Where(s => s.CustomerId == "u-1").Select(s => s.Endpoint).ToListAsync();
        Assert.Equal(PushSubscription.MaxPerCustomer, kept.Count);
        Assert.DoesNotContain("https://push.test/0", kept);

        _sender.IsEnabled = false;
        Assert.Equal(PushErrors.Disabled, (await push.SubscribeAsync("u-1", Request("https://push.test/x"))).Error!.Code);
        Assert.Equal(new PushSettingsView(false, null), push.Settings());
    }

    [Fact]
    public async Task Notifications_reach_every_browser_in_its_language_and_forget_gone_ones()
    {
        await AddUsersAsync("u-1");
        await using var db = Database.CreateContext();
        var push = Service(db);
        await push.SubscribeAsync("u-1", Request("https://push.test/phone", "fa"));
        await push.SubscribeAsync("u-1", Request("https://push.test/laptop", "en"));
        await push.SubscribeAsync("u-1", Request("https://push.test/old", "en"));
        await push.SubscribeAsync("u-1", Request("https://push.test/down", "en"));
        _sender.Answers["https://push.test/old"] = PushResult.Gone;
        _sender.Throwing.Add("https://push.test/down");

        var sent = await push.NotifyAsync("u-1", locale => new PushMessage($"title-{locale}", "body", $"/{locale}/account/orders/1"));

        Assert.Equal(2, sent);
        Assert.Contains(_sender.Sent, s => s.Target.Endpoint == "https://push.test/phone" && s.Message.Url == "/fa/account/orders/1");
        Assert.Equal(["https://push.test/down", "https://push.test/laptop", "https://push.test/phone"], (await db.PushSubscriptions.Select(s => s.Endpoint).ToListAsync()).Order());
        Assert.Equal(0, await push.NotifyAsync("nobody", _ => new PushMessage("t", "b", "/")));
    }

    [Fact]
    public void Subscriptions_are_validated()
    {
        var validator = new PushSubscriptionRequestValidator();
        Assert.True(validator.Validate(Request("https://push.test/a")).IsValid);
        Assert.False(validator.Validate(Request("http://push.test/a")).IsValid);
        Assert.False(validator.Validate(Request("https://push.test/a") with { P256dh = "short" }).IsValid);
        Assert.False(validator.Validate(Request("https://push.test/a") with { Auth = Base64Url.Encode(new byte[8]) }).IsValid);
        Assert.False(new PushUnsubscribeRequestValidator().Validate(new PushUnsubscribeRequest("")).IsValid);
        Assert.False(Base64Url.TryDecode("!!!", out _));
        Assert.Equal([1, 2, 3], Base64Url.Decode(Base64Url.Encode([1, 2, 3])));
    }

    [Fact]
    public async Task A_customer_order_update_also_goes_to_their_browsers()
    {
        await SeedDemoAsync();
        await AddUsersAsync("u-1");
        var kit = new SalesKit(Database, Store);
        kit.Identity.Users.Add(new FakeAccountIdentity.User("u-1", "sara@example.com", null, "x", "en"));
        await using var db = Database.CreateContext();
        await new PushSubscriptionService(db, kit.Push, Options.Create(Store), NullLogger<PushSubscriptionService>.Instance)
            .SubscribeAsync("u-1", Request("https://push.test/phone", "en"));
        var number = await NumberOfAsync("DEMO-PHN-001");
        var owner = new CartOwner("u-1", null);
        await new CartService(db, Options.Create(Store)).AddAsync(owner, new AddToCartRequest(number, null, 1), "en");
        var method = (await new ShippingMethodService(db, Options.Create(Store)).CreateAsync(
            new SaveShippingMethodRequest([new LocalizedText("fa", "پست", null, null)], false, 0, 0, null, [], null, null, true, 0))).Value.Id;
        var checkout = kit.Checkout(db);
        var total = (await checkout.QuoteAsync(owner, new CheckoutQuoteRequest("IR", method, null), "en")).Totals.Total;
        var address = new AddressInput(null, "Sara", "09121234567", "IR", null, "Tehran", "Valiasr 10", null, null, false);
        var placed = (await checkout.PlaceAsync(owner, new PlaceOrderRequest(method, null, address, false, null, null, null, null, null, total), "en")).Value;

        var (_, message) = Assert.Single(kit.Push.Sent);
        Assert.Equal(($"Your order {placed.Number} at {Store.Name}", $"/en/account/orders/{placed.Number}"), (message.Title, message.Url));
        Assert.StartsWith("Thank you", message.Body, StringComparison.Ordinal);
    }
}
