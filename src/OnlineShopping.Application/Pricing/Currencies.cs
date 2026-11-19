using FluentValidation;

namespace OnlineShopping.Application.Pricing;

/// <summary>Error codes of the currency admin (translated by the web app under <c>admin.currencyErrors</c>).</summary>
public static class CurrencyErrors
{
    public const string NotFound = "currency.not_found";
    public const string CodeInvalid = "currency.code_invalid";
    public const string Exists = "currency.exists";
    public const string BaseLocked = "currency.base_locked";
    public const string RateInvalid = "currency.rate_invalid";
    public const string DecimalsInvalid = "currency.decimals_invalid";
    public const string StepInvalid = "currency.step_invalid";
    public const string SymbolTooLong = "currency.symbol_too_long";
    public const string LocaleInvalid = "currency.locale_invalid";
    public const string DefaultDisabled = "currency.default_disabled";
    public const string SyncUnavailable = "currency.sync_unavailable";
}

/// <summary>Create or change a currency. <see cref="Code"/> is ignored on update (the URL names the currency).</summary>
public sealed record SaveCurrencyRequest(
    string Code,
    string? Symbol,
    int Decimals,
    decimal RoundingStep,
    decimal Rate,
    bool AutoUpdate,
    bool IsEnabled,
    int SortOrder);

public sealed record SetLocaleCurrencyRequest(string CurrencyCode);

/// <summary>A currency as the storefront uses it to convert and show prices.</summary>
public sealed record PublicCurrency(string Code, string? Symbol, int Decimals, decimal RoundingStep, decimal Rate);

/// <summary>
/// Everything the storefront needs for prices: the base currency, the currencies a visitor may choose (enabled, with a
/// rate), and each language's starting currency.
/// </summary>
public sealed record CurrencySettings(string Base, IReadOnlyList<PublicCurrency> Currencies, IReadOnlyDictionary<string, string> LocaleDefaults);

public sealed record CurrencyView(
    string Code,
    string? Symbol,
    int Decimals,
    decimal RoundingStep,
    decimal Rate,
    bool AutoUpdate,
    DateTime? RateUpdatedUtc,
    bool IsEnabled,
    int SortOrder,
    bool IsBase);

public sealed record LocaleCurrencyView(string Locale, string CurrencyCode);

/// <summary>The currency admin page: all currencies, the language defaults and where automatic rates come from.</summary>
public sealed record CurrencyAdminOverview(
    string Base,
    IReadOnlyList<CurrencyView> Currencies,
    IReadOnlyList<LocaleCurrencyView> LocaleDefaults,
    string RateSource);

public sealed record RateSyncResult(int Updated, DateTime? SyncedUtc);

public sealed class SaveCurrencyRequestValidator : AbstractValidator<SaveCurrencyRequest>
{
    public const int MaxSymbolLength = 12;

    public SaveCurrencyRequestValidator()
    {
        RuleFor(r => r.Code).Must(CurrencyCodes.IsValid).WithErrorCode(CurrencyErrors.CodeInvalid);
        RuleFor(r => r.Symbol).MaximumLength(MaxSymbolLength).WithErrorCode(CurrencyErrors.SymbolTooLong);
        RuleFor(r => r.Decimals).InclusiveBetween(0, 4).WithErrorCode(CurrencyErrors.DecimalsInvalid);
        RuleFor(r => r.RoundingStep).GreaterThan(0).WithErrorCode(CurrencyErrors.StepInvalid)
            .LessThanOrEqualTo(1_000_000).WithErrorCode(CurrencyErrors.StepInvalid);
        // A rate of 0 is allowed while the background job has not fetched one yet; such a currency is not offered.
        RuleFor(r => r.Rate).GreaterThanOrEqualTo(0).WithErrorCode(CurrencyErrors.RateInvalid)
            .LessThan(1_000_000_000_000m).WithErrorCode(CurrencyErrors.RateInvalid);
    }
}

public sealed class SetLocaleCurrencyRequestValidator : AbstractValidator<SetLocaleCurrencyRequest>
{
    public SetLocaleCurrencyRequestValidator()
    {
        RuleFor(r => r.CurrencyCode).Must(CurrencyCodes.IsValid).WithErrorCode(CurrencyErrors.CodeInvalid);
    }
}

public static class CurrencyCodes
{
    /// <summary>Three ASCII letters (normalised to upper case by the service).</summary>
    public static bool IsValid(string? code) =>
        code is { Length: 3 } && code.All(char.IsAsciiLetter);

    public static string Normalize(string code) => code.Trim().ToUpperInvariant();
}
