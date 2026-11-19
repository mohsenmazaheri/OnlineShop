using FluentValidation;

namespace OnlineShopping.Application.Catalog.Admin;

// ── Brands ──────────────────────────────────────────────────────────────────────────────────────────────────────────

/// <summary>A brand for the admin: texts in every shop language (name, slug, description), logo and how many products use it.</summary>
public sealed record BrandView(Guid Id, string? LogoUrl, IReadOnlyList<LocalizedText> Texts, int ProductCount);

public sealed record BrandList(string DefaultLocale, IReadOnlyList<string> Locales, IReadOnlyList<BrandView> Brands);

public sealed record SaveBrandRequest(IReadOnlyList<LocalizedText> Texts);

public sealed class SaveBrandRequestValidator : AbstractValidator<SaveBrandRequest>
{
    public SaveBrandRequestValidator()
    {
        RuleFor(r => r.Texts).NotEmpty().WithErrorCode(CatalogErrors.DefaultTextRequired);
        RuleForEach(r => r.Texts).ChildRules(text =>
        {
            text.RuleFor(t => t.Name).MaximumLength(200).WithErrorCode(CatalogErrors.NameTooLong);
            text.RuleFor(t => t.Slug).MaximumLength(200).WithErrorCode(CatalogErrors.NameTooLong);
            text.RuleFor(t => t.Description).MaximumLength(2000).WithErrorCode(CatalogErrors.NameTooLong);
        });
    }
}

// ── Specs ───────────────────────────────────────────────────────────────────────────────────────────────────────────

/// <summary>A spec's name, group heading and unit in one language (an empty name in another language: not translated).</summary>
public sealed record SpecText(string Locale, string Name, string? GroupName, string? Unit);

/// <summary>An option's display value in one language (empty in another language: not translated).</summary>
public sealed record OptionText(string Locale, string Value);

/// <summary>An option as the admin edits it. A new option may leave <see cref="Code"/> empty (it is made from the value).</summary>
public sealed record SpecOptionInput(string? Code, IReadOnlyList<OptionText> Texts);

/// <summary>
/// Create or change a spec. <see cref="Code"/> may be left empty for a new spec (made from the name); options are
/// replaced by the submitted list, matched by code, and only an unused option can be dropped.
/// </summary>
public sealed record SaveSpecRequest(
    string? Code,
    string Kind,
    bool IsFilterable,
    bool IsComparable,
    bool ShowInSummary,
    IReadOnlyList<SpecText> Texts,
    IReadOnlyList<SpecOptionInput> Options);

public sealed record SpecOptionView(string Code, IReadOnlyList<OptionText> Texts, int UsageCount);

/// <summary>A spec for the admin. <see cref="UsageCount"/>: products and variants that use it (then its kind is fixed).</summary>
public sealed record SpecView(
    Guid Id,
    string Code,
    string Kind,
    bool IsFilterable,
    bool IsComparable,
    bool ShowInSummary,
    IReadOnlyList<SpecText> Texts,
    IReadOnlyList<SpecOptionView> Options,
    int UsageCount);

public sealed record SpecList(string DefaultLocale, IReadOnlyList<string> Locales, IReadOnlyList<SpecView> Specs);

/// <summary>Every spec's id in the new display order.</summary>
public sealed record ReorderSpecsRequest(IReadOnlyList<Guid> Ids);

public sealed class SaveSpecRequestValidator : AbstractValidator<SaveSpecRequest>
{
    public SaveSpecRequestValidator()
    {
        RuleFor(r => r.Code).MaximumLength(64).WithErrorCode(CatalogErrors.CodeInvalid);
        RuleFor(r => r.Texts).NotEmpty().WithErrorCode(CatalogErrors.DefaultTextRequired);
        RuleFor(r => r.Options).Must(o => o.Count <= 200).WithErrorCode(CatalogErrors.NameTooLong);
        RuleForEach(r => r.Texts).ChildRules(text =>
        {
            text.RuleFor(t => t.Name).MaximumLength(200).WithErrorCode(CatalogErrors.NameTooLong);
            text.RuleFor(t => t.GroupName).MaximumLength(200).WithErrorCode(CatalogErrors.NameTooLong);
            text.RuleFor(t => t.Unit).MaximumLength(32).WithErrorCode(CatalogErrors.NameTooLong);
        });
        RuleForEach(r => r.Options).ChildRules(option =>
        {
            option.RuleFor(o => o.Code).MaximumLength(64).WithErrorCode(CatalogErrors.CodeInvalid);
            option.RuleForEach(o => o.Texts).ChildRules(text =>
                text.RuleFor(t => t.Value).MaximumLength(200).WithErrorCode(CatalogErrors.NameTooLong));
        });
    }
}

// ── Variants ────────────────────────────────────────────────────────────────────────────────────────────────────────

/// <summary>A variant's option for one variant spec, by codes ("color" → "beige").</summary>
public sealed record VariantOptionInput(string Spec, string Option);

/// <summary>One variant as the admin edits it; <see cref="Id"/> is empty for a new one.</summary>
public sealed record VariantInput(Guid? Id, string Sku, decimal? Price, decimal? CompareAtPrice, int Stock, IReadOnlyList<VariantOptionInput> Options);

/// <summary>
/// A product's whole variant set: the option specs it varies by (at most three, for example colour and size) and every
/// variant. Variants left out are removed; an empty list removes them all.
/// </summary>
public sealed record SaveVariantsRequest(IReadOnlyList<string> SpecCodes, IReadOnlyList<VariantInput> Variants);

public sealed record VariantView(Guid Id, string Sku, decimal? Price, decimal? CompareAtPrice, int Stock, IReadOnlyList<VariantOptionInput> Options);

public sealed record ProductVariantsView(IReadOnlyList<string> SpecCodes, IReadOnlyList<VariantView> Variants);

public sealed class SaveVariantsRequestValidator : AbstractValidator<SaveVariantsRequest>
{
    public const int MaxSpecs = 3;
    public const int MaxVariants = 100;

    public SaveVariantsRequestValidator()
    {
        RuleFor(r => r.SpecCodes).Must(c => c.Count <= MaxSpecs).WithErrorCode(CatalogErrors.VariantSpecInvalid);
        RuleFor(r => r.Variants).Must(v => v.Count <= MaxVariants).WithErrorCode(CatalogErrors.TooManyVariants);
        RuleForEach(r => r.Variants).ChildRules(variant =>
        {
            variant.RuleFor(v => v.Sku).NotEmpty().WithErrorCode(CatalogErrors.SkuRequired)
                .MaximumLength(64).WithErrorCode(CatalogErrors.SkuRequired);
            variant.RuleFor(v => v.Price).GreaterThanOrEqualTo(0).WithErrorCode(CatalogErrors.PriceInvalid)
                .LessThan(10_000_000_000_000_000m).WithErrorCode(CatalogErrors.PriceInvalid);
            variant.RuleFor(v => v.CompareAtPrice).LessThan(10_000_000_000_000_000m).WithErrorCode(CatalogErrors.CompareAtInvalid);
            variant.RuleFor(v => v.Stock).InclusiveBetween(0, 1_000_000_000).WithErrorCode(CatalogErrors.StockInvalid);
        });
    }
}
