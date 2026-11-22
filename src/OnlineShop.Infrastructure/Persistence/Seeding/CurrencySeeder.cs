using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnlineShop.Domain.Pricing;
using StoreOptions = OnlineShop.Application.Common.StoreOptions;

namespace OnlineShop.Infrastructure.Persistence.Seeding;

/// <summary>
/// First start of a shop: the base currency plus the usual display currencies and each language's starting currency
/// (fa → IRR, en → USD, de → EUR, hy → AMD, ar → AED). Their rates start empty (not offered) until the background job
/// or the admin sets them. Later starts only make sure the base currency exists; the admin owns everything else.
/// </summary>
public sealed class CurrencySeeder(AppDbContext db, IOptions<StoreOptions> store)
{
    /// <summary>Code, decimals shown, rounding step of converted prices.</summary>
    public static IReadOnlyList<(string Code, int Decimals, decimal Step)> Defaults { get; } =
    [
        ("IRR", 0, 1000m),
        ("USD", 2, 0.01m),
        ("EUR", 2, 0.01m),
        ("AMD", 0, 10m),
        ("AED", 2, 0.01m),
    ];

    public static IReadOnlyDictionary<string, string> LocaleDefaults { get; } = new Dictionary<string, string>
    {
        ["fa"] = "IRR",
        ["en"] = "USD",
        ["de"] = "EUR",
        ["hy"] = "AMD",
        ["ar"] = "AED",
    };

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var baseCode = store.Value.Currency;
        var firstStart = !await db.Currencies.IgnoreQueryFilters().AnyAsync(cancellationToken);

        if (firstStart)
        {
            var order = 0;
            foreach (var (code, decimals, step) in Defaults.Where(d => d.Code != baseCode).Prepend(Default(baseCode)))
            {
                db.Currencies.Add(new Currency
                {
                    Code = code,
                    Decimals = decimals,
                    RoundingStep = step,
                    Rate = code == baseCode ? 1 : 0,
                    AutoUpdate = code != baseCode,
                    IsEnabled = true,
                    SortOrder = order++,
                });
            }
            foreach (var locale in store.Value.LocaleList)
            {
                if (LocaleDefaults.TryGetValue(locale, out var code) && !await db.LocaleCurrencies.AnyAsync(l => l.Locale == locale, cancellationToken))
                    db.LocaleCurrencies.Add(new LocaleCurrency { Locale = locale, CurrencyCode = code });
            }
        }
        else if (!await db.Currencies.AnyAsync(c => c.Code == baseCode, cancellationToken))
        {
            var (code, decimals, step) = Default(baseCode);
            db.Currencies.Add(new Currency { Code = code, Decimals = decimals, RoundingStep = step, Rate = 1, IsEnabled = true });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static (string Code, int Decimals, decimal Step) Default(string code) =>
        Defaults.FirstOrDefault(d => d.Code == code) is { Code: not null } known ? known : (code, 2, 0.01m);
}
