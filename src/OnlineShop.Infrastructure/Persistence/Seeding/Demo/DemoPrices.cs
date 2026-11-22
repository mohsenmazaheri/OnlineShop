namespace OnlineShop.Infrastructure.Persistence.Seeding.Demo;

/// <summary>
/// Demo prices are written in euros and stored in the shop's base currency (<c>Store:Currency</c>), so a demo looks
/// plausible in every market. The rates are rough, fixed demo values — never used for real prices.
/// </summary>
public static class DemoPrices
{
    private static readonly Dictionary<string, (decimal PerEuro, decimal Step)> Rates = new(StringComparer.Ordinal)
    {
        ["EUR"] = (1m, 0.01m),
        ["USD"] = (1.1m, 0.01m),
        ["GBP"] = (0.86m, 0.01m),
        ["AMD"] = (430m, 100m),
        ["IRR"] = (1_000_000m, 100_000m),
    };

    /// <summary>The euro amount in <paramref name="currency"/>, rounded to that currency's usual step.</summary>
    public static decimal Convert(decimal euros, string currency)
    {
        var (perEuro, step) = Rates.TryGetValue(currency, out var rate) ? rate : (1m, 0.01m);
        return Math.Round(euros * perEuro / step, MidpointRounding.AwayFromZero) * step;
    }
}
