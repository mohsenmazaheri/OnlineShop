namespace OnlineShopping.Application.Pricing;

/// <summary>
/// Where automatic exchange rates come from (chosen by <c>ExchangeRates:Provider</c>). Called only by the background
/// job and the admin's "update now" button, never while rendering a page: the shop keeps its last rates when the
/// source is down or blocked.
/// </summary>
public interface IExchangeRateSource
{
    /// <summary>Shown in the admin ("None" when automatic rates are switched off).</summary>
    string Name { get; }

    bool IsEnabled { get; }

    /// <summary>Units of each currency (upper-case code) per one unit of <paramref name="baseCode"/>; null when unavailable.</summary>
    Task<IReadOnlyDictionary<string, decimal>?> GetRatesAsync(string baseCode, CancellationToken cancellationToken = default);
}

/// <summary>No automatic rates: every rate is entered by hand.</summary>
public sealed class NoExchangeRateSource : IExchangeRateSource
{
    public string Name => "None";
    public bool IsEnabled => false;

    public Task<IReadOnlyDictionary<string, decimal>?> GetRatesAsync(string baseCode, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<string, decimal>?>(null);
}
