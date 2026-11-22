using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OnlineShop.Application.Common;
using OnlineShop.Application.Pricing;
using OnlineShop.Domain.Pricing;
using OnlineShop.Infrastructure.Persistence;
using OnlineShop.Infrastructure.Persistence.Seeding;
using OnlineShop.Infrastructure.Pricing;
using OnlineShop.UnitTests.TestSupport;

namespace OnlineShop.UnitTests.Infrastructure;

public sealed class CurrencyApiRateSourceTests
{
    private const string Json = """{"date":"2026-11-19","usd":{"eur":0.9108,"irr":1929404.79765718,"amd":359.15,"btc":0.00001,"shib":12345.6,"bad":"x"}}""";

    private static CurrencyApiRateSource Source(Func<HttpRequestMessage, HttpResponseMessage> respond, List<string>? calls = null) =>
        new(new HttpClient(new StubHandler(respond, calls)),
            Options.Create(new ExchangeRateOptions { CurrencyApiUrl = "https://primary/{base}.json", CurrencyApiFallbackUrl = "https://fallback/{base}.json" }),
            NullLogger<CurrencyApiRateSource>.Instance);

    [Fact]
    public async Task Reads_three_letter_currencies_upper_cased()
    {
        var rates = await Source(_ => Ok(Json)).GetRatesAsync("USD");

        Assert.NotNull(rates);
        Assert.Equal(1929404.79765718m, rates["IRR"]);
        Assert.Equal(0.9108m, rates["EUR"]);
        Assert.Equal(1e-5m, rates["BTC"]);
        Assert.False(rates.ContainsKey("SHIB"));
        Assert.False(rates.ContainsKey("BAD"));
    }

    [Fact]
    public async Task Uses_the_fallback_address_when_the_primary_fails()
    {
        var calls = new List<string>();
        var rates = await Source(r => r.RequestUri!.Host == "primary" ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Ok(Json), calls).GetRatesAsync("USD");

        Assert.Equal(["https://primary/usd.json", "https://fallback/usd.json"], calls);
        Assert.NotNull(rates);
    }

    [Fact]
    public async Task Returns_null_when_both_fail_or_the_answer_is_unusable()
    {
        Assert.Null(await Source(_ => throw new HttpRequestException("offline")).GetRatesAsync("USD"));
        Assert.Null(await Source(_ => Ok("""{"date":"x","eur":{}}""")).GetRatesAsync("USD"));
        Assert.Null(await Source(_ => Ok("not json")).GetRatesAsync("USD"));
    }

    private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond, List<string>? calls) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            calls?.Add(request.RequestUri!.ToString());
            return Task.FromResult(respond(request));
        }
    }
}

public sealed class CurrencySeederTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private async Task SeedAsync(string baseCode = "IRR")
    {
        await using var db = _database.CreateContext();
        await new CurrencySeeder(db, Options.Create(new StoreOptions { Locales = "fa,en,hy,de,ar", Currency = baseCode })).SeedAsync();
    }

    [Fact]
    public async Task First_start_adds_the_usual_currencies_and_language_defaults()
    {
        await SeedAsync();

        await using var db = _database.CreateContext();
        var currencies = await db.Currencies.OrderBy(c => c.SortOrder).ToListAsync();
        Assert.Equal(["IRR", "USD", "EUR", "AMD", "AED"], currencies.Select(c => c.Code));
        Assert.Equal(1m, currencies[0].Rate);
        Assert.False(currencies[0].AutoUpdate);
        Assert.All(currencies.Skip(1), c => Assert.True(c.AutoUpdate && c.Rate == 0));
        Assert.Equal(
            new Dictionary<string, string> { ["fa"] = "IRR", ["en"] = "USD", ["hy"] = "AMD", ["de"] = "EUR", ["ar"] = "AED" },
            await db.LocaleCurrencies.ToDictionaryAsync(l => l.Locale, l => l.CurrencyCode));
    }

    [Fact]
    public async Task Later_starts_keep_the_admins_choices_but_add_a_new_base_currency()
    {
        await SeedAsync();
        await using (var db = _database.CreateContext())
        {
            db.Currencies.Remove(await db.Currencies.SingleAsync(c => c.Code == "AED"));
            await db.SaveChangesAsync();
        }

        await SeedAsync("GBP");

        await using var check = _database.CreateContext();
        var codes = await check.Currencies.Select(c => c.Code).ToListAsync();
        Assert.DoesNotContain("AED", codes);
        Assert.Contains("GBP", codes);
        Assert.Equal(1m, (await check.Currencies.SingleAsync(c => c.Code == "GBP")).Rate);
    }
}

public sealed class ExchangeRateSyncWorkerTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task One_run_updates_rates_and_never_throws()
    {
        await using (var db = _database.CreateContext())
        {
            db.Currencies.Add(new Currency { Code = "USD", Decimals = 2, RoundingStep = 0.01m, AutoUpdate = true, IsEnabled = true });
            await db.SaveChangesAsync();
        }
        var services = new ServiceCollection();
        services.AddScoped(_ => _database.CreateContext());
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddSingleton(Options.Create(new StoreOptions { Locales = "fa", Currency = "IRR" }));
        services.AddSingleton<TimeProvider>(_database.Clock);
        services.AddSingleton<IExchangeRateSource>(new FixedRates());
        services.AddScoped<CurrencyService>();
        await using var provider = services.BuildServiceProvider();
        var worker = new ExchangeRateSyncWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ExchangeRateOptions { Provider = ExchangeRateProvider.CurrencyApi }),
            _database.Clock,
            NullLogger<ExchangeRateSyncWorker>.Instance);

        await worker.RunOnceAsync(CancellationToken.None);

        await using var check = _database.CreateContext();
        Assert.Equal(0.0000005m, (await check.Currencies.SingleAsync()).Rate);

        // A broken setup is logged, not thrown.
        var broken = new ExchangeRateSyncWorker(
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ExchangeRateOptions()), _database.Clock, NullLogger<ExchangeRateSyncWorker>.Instance);
        await broken.RunOnceAsync(CancellationToken.None);
    }

    private sealed class FixedRates : IExchangeRateSource
    {
        public string Name => "Fixed";
        public bool IsEnabled => true;

        public Task<IReadOnlyDictionary<string, decimal>?> GetRatesAsync(string baseCode, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, decimal>?>(new Dictionary<string, decimal> { ["USD"] = 0.0000005m });
    }
}
