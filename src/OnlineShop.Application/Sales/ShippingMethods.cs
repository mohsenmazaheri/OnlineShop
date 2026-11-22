using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnlineShop.Application.Catalog.Admin;
using OnlineShop.Application.Common;
using OnlineShop.Domain.Sales;
using OnlineShop.Domain.Settings;

namespace OnlineShop.Application.Sales;

/// <summary>Stable error codes of shipping methods and tax (translated under admin.shippingErrors).</summary>
public static class ShippingErrors
{
    public const string NotFound = "shipping.not_found";
    public const string CostInvalid = "shipping.cost_invalid";
    public const string CountryInvalid = "shipping.country_invalid";
    public const string DaysInvalid = "shipping.days_invalid";
    public const string TaxRateInvalid = "shipping.tax_rate_invalid";
}

/// <summary>A shipping method as the admin edits it (texts: name and description per language).</summary>
public sealed record ShippingMethodAdminView(
    Guid Id,
    IReadOnlyList<LocalizedText> Texts,
    bool IsPickup,
    decimal Cost,
    decimal CostPerKg,
    decimal? FreeOver,
    IReadOnlyList<string> Countries,
    int? MinDays,
    int? MaxDays,
    bool IsActive,
    int SortOrder);

public sealed record SaveShippingMethodRequest(
    IReadOnlyList<LocalizedText> Texts,
    bool IsPickup,
    decimal Cost,
    decimal CostPerKg,
    decimal? FreeOver,
    IReadOnlyList<string> Countries,
    int? MinDays,
    int? MaxDays,
    bool IsActive,
    int SortOrder);

public sealed class SaveShippingMethodRequestValidator : AbstractValidator<SaveShippingMethodRequest>
{
    public SaveShippingMethodRequestValidator()
    {
        RuleFor(r => r.Texts).NotNull();
        RuleFor(r => r.Cost).InclusiveBetween(0, 1_000_000_000_000m).WithErrorCode(ShippingErrors.CostInvalid);
        RuleFor(r => r.CostPerKg).InclusiveBetween(0, 1_000_000_000_000m).WithErrorCode(ShippingErrors.CostInvalid);
        RuleFor(r => r.FreeOver!.Value).InclusiveBetween(0, 1_000_000_000_000m).WithErrorCode(ShippingErrors.CostInvalid).When(r => r.FreeOver is not null);
        RuleFor(r => r.Countries).NotNull().Must(c => c.Count <= 100).WithErrorCode(ShippingErrors.CountryInvalid);
        RuleFor(r => r.MinDays!.Value).InclusiveBetween(0, 365).WithErrorCode(ShippingErrors.DaysInvalid).When(r => r.MinDays is not null);
        RuleFor(r => r.MaxDays!.Value).InclusiveBetween(0, 365).WithErrorCode(ShippingErrors.DaysInvalid).When(r => r.MaxDays is not null);
        RuleFor(r => r).Must(r => r.MinDays is null || r.MaxDays is null || r.MinDays <= r.MaxDays).WithErrorCode(ShippingErrors.DaysInvalid)
            .OverridePropertyName(nameof(SaveShippingMethodRequest.MaxDays));
        RuleFor(r => r.SortOrder).InclusiveBetween(0, 10_000);
    }
}

/// <summary>A method offered at checkout, in the customer's language, with its cost for the current cart.</summary>
public sealed record ShippingOption(
    Guid Id,
    string Name,
    string? Description,
    bool IsPickup,
    IReadOnlyList<string> Countries,
    int? MinDays,
    int? MaxDays,
    decimal Cost);

public sealed record SaveTaxRequest(decimal Rate, bool PricesIncludeTax);

public sealed class SaveTaxRequestValidator : AbstractValidator<SaveTaxRequest>
{
    public SaveTaxRequestValidator()
    {
        RuleFor(r => r.Rate).InclusiveBetween(0, 100).WithErrorCode(ShippingErrors.TaxRateInvalid)
            .Must(r => decimal.Round(r, 2) == r).WithErrorCode(ShippingErrors.TaxRateInvalid);
    }
}

/// <summary>Shipping methods: the admin defines them; checkout offers the active ones that serve the address's country.</summary>
public sealed class ShippingMethodService(IApplicationDbContext db, IOptions<StoreOptions> store)
{
    public async Task<IReadOnlyList<ShippingMethodAdminView>> ListAsync(CancellationToken cancellationToken = default)
    {
        var methods = await db.ShippingMethods.AsNoTracking().Include(m => m.Translations)
            .OrderBy(m => m.SortOrder).ThenBy(m => m.CreatedUtc).ToListAsync(cancellationToken);
        return methods.Select(View).ToList();
    }

    public async Task<Result<ShippingMethodAdminView>> CreateAsync(SaveShippingMethodRequest request, CancellationToken cancellationToken = default)
    {
        var method = new ShippingMethod { Name = "" };
        var error = Apply(method, request);
        if (error is not null)
            return error;
        db.ShippingMethods.Add(method);
        await db.SaveChangesAsync(cancellationToken);
        return View(method);
    }

    public async Task<Result<ShippingMethodAdminView>> UpdateAsync(Guid id, SaveShippingMethodRequest request, CancellationToken cancellationToken = default)
    {
        var method = await db.ShippingMethods.Include(m => m.Translations).FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (method is null)
            return AppError.NotFound(ShippingErrors.NotFound, "Shipping method not found.");
        var error = Apply(method, request);
        if (error is not null)
            return error;
        await db.SaveChangesAsync(cancellationToken);
        return View(method);
    }

    /// <summary>Soft delete: orders keep their copy of the name and cost.</summary>
    public async Task<Result<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var method = await db.ShippingMethods.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (method is null)
            return AppError.NotFound(ShippingErrors.NotFound, "Shipping method not found.");
        db.ShippingMethods.Remove(method);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>The active methods (tracked copies are not needed: read-only), in the admin's order.</summary>
    public async Task<IReadOnlyList<ShippingMethod>> ActiveAsync(CancellationToken cancellationToken = default) =>
        await db.ShippingMethods.AsNoTracking().Include(m => m.Translations)
            .Where(m => m.IsActive)
            .OrderBy(m => m.SortOrder).ThenBy(m => m.CreatedUtc)
            .ToListAsync(cancellationToken);

    /// <summary>A method as the customer sees it, with its cost for goods worth <paramref name="goods"/> and weight.</summary>
    public static ShippingOption Option(ShippingMethod method, string locale, decimal goods, int weightGrams, bool freeShippingCode = false)
    {
        ArgumentNullException.ThrowIfNull(method);
        var text = method.Translations.FirstOrDefault(t => t.Locale == locale);
        return new ShippingOption(
            method.Id, text?.Name ?? method.Name, text is null ? method.Description : text.Description, method.IsPickup, method.CountryList,
            method.MinDays, method.MaxDays, CheckoutPricing.ShippingCost(method, goods, weightGrams, freeShippingCode));
    }

    private AppError? Apply(ShippingMethod method, SaveShippingMethodRequest request)
    {
        var options = store.Value;
        var invalid = LocalizedTexts.Validate(request.Texts, options);
        if (invalid is not null)
            return invalid;
        var countries = request.Countries.Select(c => c.Trim().ToUpperInvariant()).Distinct(StringComparer.Ordinal).ToList();
        if (countries.Any(c => !options.ShippingCountryList.Contains(c, StringComparer.Ordinal)))
            return AppError.Validation(ShippingErrors.CountryInvalid, "The shop does not ship to one of the countries.");

        var text = LocalizedTexts.Default(request.Texts, options.DefaultLocale);
        method.Name = text.Name;
        method.Description = string.IsNullOrWhiteSpace(text.Description) ? null : text.Description.Trim();
        LocalizedTexts.ApplyTranslations(request.Texts, options.DefaultLocale, method.Translations, withSlug: false, withDescription: true);
        method.IsPickup = request.IsPickup;
        method.Cost = request.Cost;
        method.CostPerKg = request.IsPickup ? 0 : request.CostPerKg;
        method.FreeOver = request.FreeOver;
        method.Countries = request.IsPickup ? "" : string.Join(',', countries);
        method.MinDays = request.MinDays;
        method.MaxDays = request.MaxDays;
        method.IsActive = request.IsActive;
        method.SortOrder = request.SortOrder;
        return null;
    }

    private ShippingMethodAdminView View(ShippingMethod m) => new(
        m.Id, LocalizedTexts.Read(store.Value, m.Name, null, m.Description, m.Translations), m.IsPickup, m.Cost, m.CostPerKg, m.FreeOver,
        m.CountryList, m.MinDays, m.MaxDays, m.IsActive, m.SortOrder);
}

/// <summary>The shop's tax rate (one row; no tax until the admin saves one).</summary>
public sealed class TaxSettingsService(IApplicationDbContext db)
{
    public async Task<TaxView> GetAsync(CancellationToken cancellationToken = default) =>
        await db.TaxSettings.AsNoTracking().OrderBy(t => t.CreatedUtc)
            .Select(t => new TaxView(t.Rate, t.PricesIncludeTax))
            .FirstOrDefaultAsync(cancellationToken)
        ?? new TaxView(0, true);

    public async Task<TaxView> SaveAsync(SaveTaxRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var row = await db.TaxSettings.OrderBy(t => t.CreatedUtc).FirstOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            row = new TaxSettings();
            db.TaxSettings.Add(row);
        }
        row.Rate = request.Rate;
        row.PricesIncludeTax = request.PricesIncludeTax;
        await db.SaveChangesAsync(cancellationToken);
        return new TaxView(row.Rate, row.PricesIncludeTax);
    }
}
