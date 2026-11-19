using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OnlineShopping.Application.Pricing;

namespace OnlineShopping.Infrastructure.Pricing;

/// <summary>
/// Daily rates from fawazahmed0/exchange-api (CC0 data, no key, no rate limit). The response for base <c>usd</c> is
/// <c>{ "date": "…", "usd": { "eur": 0.91, "irr": 1929404.8, … } }</c>. The fallback address is tried when the
/// primary one fails, as the project asks. Only the base currency code is sent; no shop or customer data.
/// </summary>
public sealed partial class CurrencyApiRateSource(HttpClient http, IOptions<ExchangeRateOptions> options, ILogger<CurrencyApiRateSource> logger)
    : IExchangeRateSource
{
    public string Name => "CurrencyApi (fawazahmed0/exchange-api)";
    public bool IsEnabled => true;

    public async Task<IReadOnlyDictionary<string, decimal>?> GetRatesAsync(string baseCode, CancellationToken cancellationToken = default)
    {
        var key = baseCode.ToLowerInvariant();
        foreach (var template in new[] { options.Value.CurrencyApiUrl, options.Value.CurrencyApiFallbackUrl })
        {
            var url = template.Replace("{base}", key, StringComparison.Ordinal);
            try
            {
                using var response = await http.GetAsync(new Uri(url), cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    LogFailed(logger, url, ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture));
                    continue;
                }
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var rates = await ParseAsync(stream, key, cancellationToken);
                if (rates is not null)
                    return rates;
                LogFailed(logger, url, "unexpected response");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw;
                LogFailed(logger, url, ex.GetType().Name);
            }
        }
        return null;
    }

    /// <summary>Reads the rates object of the base currency; codes come back upper-case.</summary>
    public static async Task<IReadOnlyDictionary<string, decimal>?> ParseAsync(Stream json, string baseKey, CancellationToken cancellationToken = default)
    {
        using var document = await JsonDocument.ParseAsync(json, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty(baseKey, out var table) || table.ValueKind != JsonValueKind.Object)
            return null;

        var rates = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var entry in table.EnumerateObject())
        {
            // Only real three-letter currencies (the data also lists crypto and metals with longer codes).
            if (entry.Name.Length != 3 || entry.Value.ValueKind != JsonValueKind.Number)
                continue;
            if (entry.Value.TryGetDecimal(out var rate) && rate > 0)
                rates[entry.Name.ToUpperInvariant()] = rate;
        }
        return rates.Count > 0 ? rates : null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Exchange rates from {Url} failed: {Reason}.")]
    private static partial void LogFailed(ILogger logger, string url, string reason);
}
