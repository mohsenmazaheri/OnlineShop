using OnlineShop.Domain.Common;

namespace OnlineShop.Domain.Pricing;

/// <summary>
/// A currency the storefront can show prices in. Prices are stored in the shop's base currency
/// (<c>Store:Currency</c>); <see cref="Rate"/> converts them for display, then the result is rounded to
/// <see cref="RoundingStep"/>. Which currencies a customer can actually pay in is decided by the payment gateways
/// (phase 8).
/// </summary>
public sealed class Currency : BaseEntity
{
    /// <summary>Three upper-case letters: ISO 4217 (USD, IRR) or a shop-defined unit such as IRT for toman.</summary>
    public required string Code { get; set; }

    /// <summary>Optional display symbol or name ("تومان"); empty means the standard symbol of the language.</summary>
    public string? Symbol { get; set; }

    /// <summary>Digits after the decimal point when showing amounts (0 for IRR and AMD, 2 for USD).</summary>
    public int Decimals { get; set; }

    /// <summary>Converted amounts are rounded to a multiple of this (IRR 1000, AMD 10, USD 0.01).</summary>
    public decimal RoundingStep { get; set; }

    /// <summary>Units of this currency per one unit of the base currency (the base currency itself: 1).</summary>
    public decimal Rate { get; set; }

    /// <summary>The background job updates the rate from the configured exchange-rate source.</summary>
    public bool AutoUpdate { get; set; }

    public DateTime? RateUpdatedUtc { get; set; }

    public bool IsEnabled { get; set; }

    public int SortOrder { get; set; }
}

/// <summary>The currency a language starts with (en → USD); a visitor may still choose another one.</summary>
public sealed class LocaleCurrency
{
    public required string Locale { get; set; }
    public required string CurrencyCode { get; set; }
}
