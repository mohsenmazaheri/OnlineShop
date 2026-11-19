using System.ComponentModel.DataAnnotations;

namespace OnlineShopping.Infrastructure.Pricing;

public enum ExchangeRateProvider
{
    /// <summary>No automatic rates; the admin enters every rate.</summary>
    None,

    /// <summary>fawazahmed0/exchange-api: free, no key, no rate limit, CC0 data, updated daily.</summary>
    CurrencyApi,
}

/// <summary>Section <c>ExchangeRates</c>: where automatic display rates come from and how often they are refreshed.</summary>
public sealed class ExchangeRateOptions
{
    public const string SectionName = "ExchangeRates";

    public ExchangeRateProvider Provider { get; set; } = ExchangeRateProvider.None;

    /// <summary>Hours between automatic updates (the free sources publish once a day).</summary>
    [Range(1, 168)]
    public int RefreshHours { get; set; } = 12;

    /// <summary>Primary and fallback addresses of the CurrencyApi data ({base} = lower-case base currency).</summary>
    public string CurrencyApiUrl { get; set; } = "https://cdn.jsdelivr.net/npm/@fawazahmed0/currency-api@latest/v1/currencies/{base}.min.json";

    public string CurrencyApiFallbackUrl { get; set; } = "https://latest.currency-api.pages.dev/v1/currencies/{base}.min.json";
}
