using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnlineShopping.Application.Common;
using OnlineShopping.Domain.Catalog;

namespace OnlineShopping.Application.Catalog.Admin;

/// <summary>
/// The shop's spec library: specs ("Screen size", "Colour") with their kind, flags, texts per language and options.
/// A spec's kind can only change while no product or variant uses it; a used spec or option cannot be deleted.
/// </summary>
public sealed partial class SpecAdminService(IApplicationDbContext db, IOptions<StoreOptions> store)
{
    private StoreOptions Store => store.Value;

    public async Task<SpecList> ListAsync(CancellationToken cancellationToken = default)
    {
        var specs = await db.ProductSpecs.AsNoTracking().Include(s => s.Options).OrderBy(s => s.SortOrder).ToListAsync(cancellationToken);
        var usage = await UsageAsync(cancellationToken);
        return new SpecList(Store.DefaultLocale, Store.LocaleList, specs.Select(s => View(s, usage)).ToList());
    }

    public async Task<Result<SpecView>> CreateAsync(SaveSpecRequest request, CancellationToken cancellationToken = default)
    {
        var last = await db.ProductSpecs.MaxAsync(s => (int?)s.SortOrder, cancellationToken) ?? -1;
        var spec = new ProductSpec { Code = "", Name = "", SortOrder = last + 1 };
        var applied = await ApplyAsync(spec, request, new Usage([], []), cancellationToken);
        if (!applied.IsSuccess)
            return applied.Error!;
        db.ProductSpecs.Add(spec);
        await db.SaveChangesAsync(cancellationToken);
        return View(spec, new Usage([], []));
    }

    public async Task<Result<SpecView>> UpdateAsync(Guid id, SaveSpecRequest request, CancellationToken cancellationToken = default)
    {
        var spec = await db.ProductSpecs.Include(s => s.Options).SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (spec is null)
            return NotFound();
        var usage = await UsageAsync(cancellationToken);
        var applied = await ApplyAsync(spec, request, usage, cancellationToken);
        if (!applied.IsSuccess)
            return applied.Error!;
        await db.SaveChangesAsync(cancellationToken);
        return View(spec, usage);
    }

    /// <summary>Soft delete of an unused spec; it also leaves every spec template.</summary>
    public async Task<Result<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var spec = await db.ProductSpecs.Include(s => s.Options).SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (spec is null)
            return NotFound();
        if ((await UsageAsync(cancellationToken)).Of(spec) > 0)
            return AppError.Conflict(CatalogErrors.SpecInUse, "Products or variants use this spec.");

        foreach (var category in await db.Categories.Where(c => c.TemplateSpecs.Any(t => t.SpecId == id)).ToListAsync(cancellationToken))
            category.TemplateSpecs.Remove(category.TemplateSpecs.First(t => t.SpecId == id));
        foreach (var sub in await db.SubCategories.Where(c => c.TemplateSpecs.Any(t => t.SpecId == id)).ToListAsync(cancellationToken))
            sub.TemplateSpecs.Remove(sub.TemplateSpecs.First(t => t.SpecId == id));
        foreach (var option in spec.Options.ToList())
            db.SpecOptions.Remove(option);
        db.ProductSpecs.Remove(spec);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Puts the specs in the given order (the product page and the editor list them so); unknown ids are ignored.</summary>
    public async Task<SpecList> ReorderAsync(ReorderSpecsRequest request, CancellationToken cancellationToken = default)
    {
        var specs = await db.ProductSpecs.OrderBy(s => s.SortOrder).ToListAsync(cancellationToken);
        var ordered = request.Ids.Distinct()
            .Select(id => specs.FirstOrDefault(s => s.Id == id))
            .OfType<ProductSpec>()
            .Concat(specs.Where(s => !request.Ids.Contains(s.Id)))
            .ToList();
        for (var i = 0; i < ordered.Count; i++)
            ordered[i].SortOrder = i;
        await db.SaveChangesAsync(cancellationToken);
        return await ListAsync(cancellationToken);
    }

    private async Task<Result<bool>> ApplyAsync(ProductSpec spec, SaveSpecRequest request, Usage usage, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<SpecKind>(request.Kind, ignoreCase: false, out var kind) || !Enum.IsDefined(kind))
            return AppError.Validation(CatalogErrors.SpecValueInvalid, $"Unknown spec kind {request.Kind}.");
        if (ValidateLocales(request.Texts.Select(t => t.Locale).ToList()) is { } localeError)
            return localeError;
        var main = request.Texts.FirstOrDefault(t => t.Locale == Store.DefaultLocale);
        if (string.IsNullOrWhiteSpace(main?.Name))
            return AppError.Validation(CatalogErrors.DefaultTextRequired, $"A name in {Store.DefaultLocale} is required.");
        if (kind != spec.Kind && usage.Of(spec) > 0)
            return AppError.Conflict(CatalogErrors.SpecInUse, "The kind of a spec in use cannot change.");

        var codes = await db.ProductSpecs.Where(s => s.Id != spec.Id).Select(s => s.Code).ToListAsync(cancellationToken);
        var code = MakeCode(request.Code, main.Name, codes, "spec");
        if (code is null)
            return AppError.Validation(CatalogErrors.CodeInvalid, "Codes use lower-case Latin letters, digits and _.");
        if (codes.Contains(code, StringComparer.Ordinal))
            return AppError.Conflict(CatalogErrors.CodeTaken, $"Another spec has code {code}.");

        var options = ApplyOptions(spec, kind == SpecKind.Option ? request.Options : [], usage);
        if (!options.IsSuccess)
            return options.Error!;

        spec.Code = code;
        spec.Kind = kind;
        spec.Name = main.Name.Trim();
        spec.GroupName = Clean(main.GroupName);
        spec.Unit = Clean(main.Unit);
        spec.IsFilterable = request.IsFilterable;
        spec.IsComparable = request.IsComparable;
        // Only numbers have a better direction.
        spec.Better = kind == SpecKind.Number ? request.Better : SpecBetter.None;
        spec.ShowInSummary = request.ShowInSummary;
        foreach (var text in request.Texts.Where(t => t.Locale != Store.DefaultLocale))
        {
            var existing = spec.Translations.FirstOrDefault(t => t.Locale == text.Locale);
            if (string.IsNullOrWhiteSpace(text.Name))
            {
                if (existing is not null)
                    spec.Translations.Remove(existing);
                continue;
            }
            var target = existing ?? new SpecTranslation { Locale = text.Locale, Name = "" };
            target.Name = text.Name.Trim();
            target.GroupName = Clean(text.GroupName);
            target.Unit = Clean(text.Unit);
            if (existing is null)
                spec.Translations.Add(target);
        }
        return true;
    }

    /// <summary>Replaces the options with the submitted list (matched by code); a used option cannot be dropped.</summary>
    private Result<bool> ApplyOptions(ProductSpec spec, IReadOnlyList<SpecOptionInput> inputs, Usage usage)
    {
        var taken = new List<string>();
        var plan = new List<(SpecOption? Existing, string Code, SpecOptionInput Input)>();
        foreach (var input in inputs)
        {
            if (ValidateLocales(input.Texts.Select(t => t.Locale).ToList()) is { } localeError)
                return localeError;
            var value = input.Texts.FirstOrDefault(t => t.Locale == Store.DefaultLocale)?.Value;
            if (string.IsNullOrWhiteSpace(value))
                return AppError.Validation(CatalogErrors.DefaultTextRequired, $"Every option needs a value in {Store.DefaultLocale}.");
            var existing = string.IsNullOrWhiteSpace(input.Code) ? null : spec.Options.FirstOrDefault(o => o.Code == input.Code.Trim());
            var code = existing?.Code ?? MakeCode(input.Code, value, taken.Concat(spec.Options.Select(o => o.Code)).ToList(), "option");
            if (code is null)
                return AppError.Validation(CatalogErrors.CodeInvalid, "Codes use lower-case Latin letters, digits and _.");
            if (taken.Contains(code, StringComparer.Ordinal))
                return AppError.Validation(CatalogErrors.OptionRepeated, $"Option {code} appears twice.");
            taken.Add(code);
            plan.Add((existing, code, input));
        }

        var removed = spec.Options.Where(o => plan.All(p => p.Existing != o)).ToList();
        if (removed.Any(o => usage.OfOption(o.Id) > 0))
            return AppError.Conflict(CatalogErrors.OptionInUse, "An option in use cannot be removed.");
        foreach (var option in removed)
        {
            spec.Options.Remove(option);
            db.SpecOptions.Remove(option);
        }

        for (var i = 0; i < plan.Count; i++)
        {
            var (existing, code, input) = plan[i];
            var option = existing ?? new SpecOption { Code = code, Value = "", Spec = spec };
            option.Value = input.Texts.First(t => t.Locale == Store.DefaultLocale).Value.Trim();
            option.SortOrder = i;
            foreach (var text in input.Texts.Where(t => t.Locale != Store.DefaultLocale))
            {
                var translation = option.Translations.FirstOrDefault(t => t.Locale == text.Locale);
                if (string.IsNullOrWhiteSpace(text.Value))
                {
                    if (translation is not null)
                        option.Translations.Remove(translation);
                }
                else if (translation is null)
                {
                    option.Translations.Add(new SpecOptionTranslation { Locale = text.Locale, Value = text.Value.Trim() });
                }
                else
                {
                    translation.Value = text.Value.Trim();
                }
            }
            if (existing is null)
            {
                spec.Options.Add(option);
                db.SpecOptions.Add(option);
            }
        }
        return true;
    }

    private AppError? ValidateLocales(List<string> locales)
    {
        if (locales.Any(l => !Store.LocaleList.Contains(l, StringComparer.Ordinal)))
            return AppError.Validation(CatalogErrors.LocaleInvalid, "A text is in a language the shop does not offer.");
        if (locales.Distinct(StringComparer.Ordinal).Count() != locales.Count)
            return AppError.Validation(CatalogErrors.LocaleRepeated, "A language appears twice.");
        return null;
    }

    /// <summary>
    /// The given code when valid; otherwise (left empty) one made from the name, or "{prefix}_{n}" for names in another
    /// script. Null when a given code is invalid.
    /// </summary>
    internal static string? MakeCode(string? given, string name, IReadOnlyCollection<string> taken, string prefix)
    {
        if (!string.IsNullOrWhiteSpace(given))
            return CodePattern().IsMatch(given.Trim()) ? given.Trim() : null;

        var fromName = Slugs.From(name).Replace('-', '_');
        fromName = fromName[..Math.Min(fromName.Length, 56)].TrimEnd('_');
        var stem = CodePattern().IsMatch(fromName) ? fromName : prefix;
        if (stem == fromName && !taken.Contains(stem, StringComparer.Ordinal))
            return stem;
        var n = 1;
        while (taken.Contains($"{stem}_{n}", StringComparer.Ordinal))
            n++;
        return $"{stem}_{n}";
    }

    [GeneratedRegex("^[a-z][a-z0-9_]{0,63}$")]
    private static partial Regex CodePattern();

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>How many product values and variants use each spec and option.</summary>
    private sealed record Usage(Dictionary<Guid, int> Specs, Dictionary<Guid, int> Options)
    {
        public int Of(ProductSpec spec) => Specs.GetValueOrDefault(spec.Id);
        public int OfOption(Guid optionId) => Options.GetValueOrDefault(optionId);
    }

    private async Task<Usage> UsageAsync(CancellationToken cancellationToken)
    {
        var values = await db.ProductSpecValues.AsNoTracking()
            .Select(v => new { v.SpecId, v.OptionId })
            .ToListAsync(cancellationToken);
        var variantOptions = await db.ProductVariants.AsNoTracking()
            .SelectMany(v => v.Options)
            .Select(o => new { o.SpecId, OptionId = (Guid?)o.OptionId })
            .ToListAsync(cancellationToken);
        var all = values.Select(v => (v.SpecId, v.OptionId)).Concat(variantOptions.Select(v => (v.SpecId, v.OptionId))).ToList();

        return new Usage(
            all.GroupBy(v => v.SpecId).ToDictionary(g => g.Key, g => g.Count()),
            all.Where(v => v.OptionId is not null).GroupBy(v => v.OptionId!.Value).ToDictionary(g => g.Key, g => g.Count()));
    }

    private SpecView View(ProductSpec s, Usage usage) => new(
        s.Id,
        s.Code,
        s.Kind.ToString(),
        s.IsFilterable,
        s.IsComparable,
        s.ShowInSummary,
        Store.LocaleList
            .OrderBy(l => l == Store.DefaultLocale ? 0 : 1)
            .Select(l => l == Store.DefaultLocale
                ? new SpecText(l, s.Name, s.GroupName, s.Unit)
                : s.Translations.FirstOrDefault(t => t.Locale == l) is { } t
                    ? new SpecText(l, t.Name, t.GroupName, t.Unit)
                    : new SpecText(l, "", null, null))
            .ToList(),
        s.Options
            .Where(o => !o.IsDeleted)
            .OrderBy(o => o.SortOrder)
            .Select(o => new SpecOptionView(
                o.Code,
                Store.LocaleList
                    .OrderBy(l => l == Store.DefaultLocale ? 0 : 1)
                    .Select(l => new OptionText(l, l == Store.DefaultLocale ? o.Value : o.Translations.FirstOrDefault(t => t.Locale == l)?.Value ?? ""))
                    .ToList(),
                usage.OfOption(o.Id)))
            .ToList(),
        usage.Of(s),
        s.Better);

    private static AppError NotFound() => AppError.NotFound(CatalogErrors.SpecNotFound, "No such spec.");
}
