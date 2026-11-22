using Microsoft.Extensions.Options;
using OnlineShop.Application.Common;
using OnlineShop.Application.Pricing;
using OnlineShop.Domain.Pricing;
using OnlineShop.Infrastructure.Persistence;
using OnlineShop.UnitTests.TestSupport;

namespace OnlineShop.UnitTests.Application;

public sealed class CurrencyServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();
    private readonly AppDbContext _db;
    private readonly StoreOptions _store = new() { Locales = "fa,en,de", DefaultLocale = "fa", Currency = "IRR" };

    public CurrencyServiceTests() => _db = _database.CreateContext();

    public void Dispose()
    {
        _db.Dispose();
        _database.Dispose();
    }

    private CurrencyService Service(IExchangeRateSource? rates = null) =>
        new(_db, Options.Create(_store), rates ?? new NoExchangeRateSource(), _database.Clock);

    private static SaveCurrencyRequest Request(string code, decimal rate = 0, bool enabled = true, bool auto = false, string? symbol = null) =>
        new(code, symbol, 2, 0.01m, rate, auto, enabled, 1);

    private async Task SeedAsync()
    {
        _db.Currencies.AddRange(
            new Currency { Code = "IRR", Decimals = 0, RoundingStep = 1000, Rate = 1, IsEnabled = true },
            new Currency { Code = "USD", Decimals = 2, RoundingStep = 0.01m, Rate = 0.0000005m, AutoUpdate = true, IsEnabled = true, SortOrder = 1 },
            new Currency { Code = "EUR", Decimals = 2, RoundingStep = 0.01m, Rate = 0, AutoUpdate = true, IsEnabled = true, SortOrder = 2 },
            new Currency { Code = "AED", Decimals = 2, RoundingStep = 0.01m, Rate = 0.000002m, IsEnabled = false, SortOrder = 3 });
        _db.LocaleCurrencies.AddRange(
            new LocaleCurrency { Locale = "en", CurrencyCode = "USD" },
            new LocaleCurrency { Locale = "de", CurrencyCode = "EUR" });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Public_list_offers_enabled_currencies_with_a_rate_and_falls_back_to_the_base()
    {
        await SeedAsync();

        var settings = await Service().GetPublicAsync();

        Assert.Equal("IRR", settings.Base);
        Assert.Equal(["IRR", "USD"], settings.Currencies.Select(c => c.Code));
        Assert.Equal("USD", settings.LocaleDefaults["en"]);
        // EUR has no rate yet and fa has no row: both start with the base currency.
        Assert.Equal("IRR", settings.LocaleDefaults["de"]);
        Assert.Equal("IRR", settings.LocaleDefaults["fa"]);
    }

    [Fact]
    public async Task Public_list_always_contains_the_base_currency()
    {
        var settings = await Service().GetPublicAsync();

        Assert.Equal([new PublicCurrency("IRR", null, 0, 1, 1)], settings.Currencies);
    }

    [Fact]
    public async Task Create_normalises_the_code_and_rejects_duplicates()
    {
        await SeedAsync();

        var created = await Service().CreateAsync(Request("irt", rate: 0.1m, symbol: " تومان "));
        var again = await Service().CreateAsync(Request("IRT"));

        Assert.Equal(("IRT", "تومان", 0.1m), (created.Value.Code, created.Value.Symbol, created.Value.Rate));
        Assert.Equal(CurrencyErrors.Exists, again.Error!.Code);
    }

    [Fact]
    public async Task The_base_currency_stays_on_at_rate_1()
    {
        await SeedAsync();

        var disable = await Service().UpdateAsync("IRR", Request("IRR", rate: 5, enabled: false));
        var changed = await Service().UpdateAsync("irr", Request("IRR", rate: 5) with { Decimals = 0, RoundingStep = 10_000 });

        Assert.Equal(CurrencyErrors.BaseLocked, disable.Error!.Code);
        Assert.Equal((1m, 10_000m, true), (changed.Value.Rate, changed.Value.RoundingStep, changed.Value.IsBase));
    }

    [Fact]
    public async Task A_language_default_cannot_be_disabled_and_unknown_currencies_are_not_found()
    {
        await SeedAsync();

        var disable = await Service().UpdateAsync("USD", Request("USD", rate: 0.0000005m, enabled: false));
        var missing = await Service().UpdateAsync("GBP", Request("GBP"));

        Assert.Equal(CurrencyErrors.DefaultDisabled, disable.Error!.Code);
        Assert.Equal(ErrorKind.NotFound, missing.Error!.Kind);
    }

    [Fact]
    public async Task Language_defaults_accept_only_offered_locales_and_enabled_currencies()
    {
        await SeedAsync();
        var service = Service();

        var set = await service.SetLocaleDefaultAsync("fa", new SetLocaleCurrencyRequest("usd"));
        var unknownLocale = await service.SetLocaleDefaultAsync("hy", new SetLocaleCurrencyRequest("USD"));
        var disabled = await service.SetLocaleDefaultAsync("fa", new SetLocaleCurrencyRequest("AED"));

        Assert.Equal(new LocaleCurrencyView("fa", "USD"), set.Value);
        Assert.Equal(CurrencyErrors.LocaleInvalid, unknownLocale.Error!.Code);
        Assert.Equal(CurrencyErrors.DefaultDisabled, disabled.Error!.Code);
        Assert.Contains(new LocaleCurrencyView("fa", "USD"), (await service.GetAdminAsync()).LocaleDefaults);
    }

    [Fact]
    public async Task Sync_updates_only_automatic_currencies_it_has_a_rate_for()
    {
        await SeedAsync();
        _db.Currencies.Single(c => c.Code == "AED").AutoUpdate = false;
        await _db.SaveChangesAsync();
        var source = new FakeRates(new Dictionary<string, decimal> { ["USD"] = 0.00000052m, ["AED"] = 9m, ["IRR"] = 7m });

        var result = await Service(source).SyncRatesAsync();

        Assert.Equal(1, result.Value.Updated);
        var rates = _db.Currencies.ToDictionary(c => c.Code, c => c.Rate);
        Assert.Equal(0.00000052m, rates["USD"]);
        Assert.Equal(0.000002m, rates["AED"]);
        Assert.Equal(1m, rates["IRR"]);
        Assert.Equal(0m, rates["EUR"]);
        Assert.Equal("IRR", source.RequestedBase);
    }

    [Fact]
    public async Task Sync_reports_when_rates_are_switched_off_or_unreachable()
    {
        Assert.Equal(CurrencyErrors.SyncUnavailable, (await Service().SyncRatesAsync()).Error!.Code);
        Assert.Equal(CurrencyErrors.SyncUnavailable, (await Service(new FakeRates(null)).SyncRatesAsync()).Error!.Code);
    }

    [Theory]
    [InlineData("USD", true)]
    [InlineData("irt", true)]
    [InlineData("US", false)]
    [InlineData("US1", false)]
    [InlineData("دلا", false)]
    public void Codes_are_three_ascii_letters(string code, bool valid) => Assert.Equal(valid, CurrencyCodes.IsValid(code));

    [Fact]
    public void Validation_rejects_bad_numbers()
    {
        var result = new SaveCurrencyRequestValidator().Validate(new SaveCurrencyRequest("USD", new string('x', 20), 7, 0, -1, false, true, 0));

        Assert.Equal(
            [CurrencyErrors.SymbolTooLong, CurrencyErrors.DecimalsInvalid, CurrencyErrors.StepInvalid, CurrencyErrors.RateInvalid],
            result.Errors.Select(e => e.ErrorCode));
    }

    private sealed class FakeRates(IReadOnlyDictionary<string, decimal>? rates) : IExchangeRateSource
    {
        public string? RequestedBase { get; private set; }
        public string Name => "Fake";
        public bool IsEnabled => true;

        public Task<IReadOnlyDictionary<string, decimal>?> GetRatesAsync(string baseCode, CancellationToken cancellationToken = default)
        {
            RequestedBase = baseCode;
            return Task.FromResult(rates);
        }
    }
}
