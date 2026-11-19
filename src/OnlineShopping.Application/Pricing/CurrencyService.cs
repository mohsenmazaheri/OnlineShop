using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnlineShopping.Application.Common;
using OnlineShopping.Domain.Pricing;

namespace OnlineShopping.Application.Pricing;

/// <summary>
/// Display currencies: the list the storefront offers, the admin's changes and the automatic rate updates. The base
/// currency (<c>Store:Currency</c>) is fixed at rate 1, always enabled and never updated automatically.
/// </summary>
public sealed class CurrencyService(
    IApplicationDbContext db,
    IOptions<StoreOptions> store,
    IExchangeRateSource rates,
    TimeProvider clock)
{
    private string BaseCode => store.Value.Currency;

    public async Task<CurrencySettings> GetPublicAsync(CancellationToken cancellationToken = default)
    {
        var currencies = await db.Currencies.AsNoTracking()
            .Where(c => c.IsEnabled && c.Rate > 0)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Code)
            .Select(c => new PublicCurrency(c.Code, c.Symbol, c.Decimals, c.RoundingStep, c.Rate))
            .ToListAsync(cancellationToken);

        // The base currency is always offered, even before the admin has saved it.
        if (currencies.All(c => c.Code != BaseCode))
            currencies.Insert(0, new PublicCurrency(BaseCode, null, 0, 1, 1));
        else
            currencies = currencies.Select(c => c.Code == BaseCode ? c with { Rate = 1 } : c).ToList();

        var offered = currencies.Select(c => c.Code).ToHashSet(StringComparer.Ordinal);
        var defaults = await db.LocaleCurrencies.AsNoTracking().ToListAsync(cancellationToken);
        // A language whose currency is disabled (or has no rate yet) starts with the base currency.
        var localeDefaults = store.Value.LocaleList.ToDictionary(
            l => l,
            l => defaults.FirstOrDefault(d => d.Locale == l) is { } d && offered.Contains(d.CurrencyCode) ? d.CurrencyCode : BaseCode,
            StringComparer.Ordinal);

        return new CurrencySettings(BaseCode, currencies, localeDefaults);
    }

    public async Task<CurrencyAdminOverview> GetAdminAsync(CancellationToken cancellationToken = default)
    {
        var currencies = await db.Currencies.AsNoTracking()
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Code)
            .ToListAsync(cancellationToken);
        var defaults = await db.LocaleCurrencies.AsNoTracking().ToListAsync(cancellationToken);

        return new CurrencyAdminOverview(
            BaseCode,
            currencies.Select(View).ToList(),
            store.Value.LocaleList
                .Select(l => new LocaleCurrencyView(l, defaults.FirstOrDefault(d => d.Locale == l)?.CurrencyCode ?? BaseCode))
                .ToList(),
            rates.Name);
    }

    public async Task<Result<CurrencyView>> CreateAsync(SaveCurrencyRequest request, CancellationToken cancellationToken = default)
    {
        var code = CurrencyCodes.Normalize(request.Code);
        if (await db.Currencies.IgnoreQueryFilters().AnyAsync(c => c.Code == code && !c.IsDeleted, cancellationToken))
            return AppError.Conflict(CurrencyErrors.Exists, $"Currency {code} exists.");

        var currency = new Currency { Code = code };
        var applied = Apply(currency, request);
        if (!applied.IsSuccess)
            return applied.Error!;

        db.Currencies.Add(currency);
        await db.SaveChangesAsync(cancellationToken);
        return View(currency);
    }

    public async Task<Result<CurrencyView>> UpdateAsync(string code, SaveCurrencyRequest request, CancellationToken cancellationToken = default)
    {
        code = CurrencyCodes.Normalize(code);
        var currency = await db.Currencies.SingleOrDefaultAsync(c => c.Code == code, cancellationToken);
        if (currency is null)
            return AppError.NotFound(CurrencyErrors.NotFound, $"No currency {code}.");

        var applied = Apply(currency, request);
        if (!applied.IsSuccess)
            return applied.Error!;

        if (!currency.IsEnabled && await db.LocaleCurrencies.AnyAsync(d => d.CurrencyCode == code, cancellationToken))
            return AppError.Validation(CurrencyErrors.DefaultDisabled, $"{code} is a language's default currency.");

        await db.SaveChangesAsync(cancellationToken);
        return View(currency);
    }

    public async Task<Result<LocaleCurrencyView>> SetLocaleDefaultAsync(string locale, SetLocaleCurrencyRequest request, CancellationToken cancellationToken = default)
    {
        if (!store.Value.LocaleList.Contains(locale, StringComparer.Ordinal))
            return AppError.NotFound(CurrencyErrors.LocaleInvalid, $"The store does not offer locale {locale}.");

        var code = CurrencyCodes.Normalize(request.CurrencyCode);
        var currency = await db.Currencies.AsNoTracking().SingleOrDefaultAsync(c => c.Code == code, cancellationToken);
        if (currency is null && code != BaseCode)
            return AppError.NotFound(CurrencyErrors.NotFound, $"No currency {code}.");
        if (currency is { IsEnabled: false })
            return AppError.Validation(CurrencyErrors.DefaultDisabled, $"{code} is disabled.");

        var row = await db.LocaleCurrencies.SingleOrDefaultAsync(d => d.Locale == locale, cancellationToken);
        if (row is null)
            db.LocaleCurrencies.Add(new LocaleCurrency { Locale = locale, CurrencyCode = code });
        else
            row.CurrencyCode = code;
        await db.SaveChangesAsync(cancellationToken);
        return new LocaleCurrencyView(locale, code);
    }

    /// <summary>Updates every automatic rate from the configured source; unknown currencies keep their rate.</summary>
    public async Task<Result<RateSyncResult>> SyncRatesAsync(CancellationToken cancellationToken = default)
    {
        if (!rates.IsEnabled)
            return AppError.Validation(CurrencyErrors.SyncUnavailable, "Automatic exchange rates are switched off.");

        var latest = await rates.GetRatesAsync(BaseCode, cancellationToken);
        if (latest is null)
            return AppError.Validation(CurrencyErrors.SyncUnavailable, "The exchange-rate source could not be reached.");

        var now = clock.GetUtcNow().UtcDateTime;
        var updated = 0;
        foreach (var currency in await db.Currencies.Where(c => c.AutoUpdate && c.Code != BaseCode).ToListAsync(cancellationToken))
        {
            if (!latest.TryGetValue(currency.Code, out var rate) || rate <= 0)
                continue;
            currency.Rate = decimal.Round(rate, 12);
            currency.RateUpdatedUtc = now;
            updated++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return new RateSyncResult(updated, now);
    }

    /// <summary>Copies the request onto the currency; the base currency keeps rate 1, stays on and is never automatic.</summary>
    private Result<bool> Apply(Currency currency, SaveCurrencyRequest request)
    {
        var isBase = currency.Code == BaseCode;
        if (isBase && (!request.IsEnabled || request.AutoUpdate))
            return AppError.Validation(CurrencyErrors.BaseLocked, "The base currency is always on, at rate 1.");

        currency.Symbol = string.IsNullOrWhiteSpace(request.Symbol) ? null : request.Symbol.Trim();
        currency.Decimals = request.Decimals;
        currency.RoundingStep = request.RoundingStep;
        currency.AutoUpdate = !isBase && request.AutoUpdate;
        currency.IsEnabled = isBase || request.IsEnabled;
        currency.SortOrder = request.SortOrder;
        var rate = isBase ? 1 : request.Rate;
        if (rate != currency.Rate)
        {
            currency.Rate = rate;
            currency.RateUpdatedUtc = clock.GetUtcNow().UtcDateTime;
        }
        return true;
    }

    private CurrencyView View(Currency c) => new(
        c.Code, c.Symbol, c.Decimals, c.RoundingStep, c.Code == BaseCode ? 1 : c.Rate, c.AutoUpdate, c.RateUpdatedUtc,
        c.IsEnabled || c.Code == BaseCode, c.SortOrder, c.Code == BaseCode);
}
