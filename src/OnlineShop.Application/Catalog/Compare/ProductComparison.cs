using System.Globalization;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Application.Common;
using OnlineShop.Domain.Catalog;

namespace OnlineShop.Application.Catalog.Compare;

/// <summary>A product as a comparison column shows it: current price and stock (never from a browser cache).</summary>
public sealed record CompareProduct(
    long Number,
    string Name,
    string Slug,
    string? ImageUrl,
    decimal Price,
    decimal? CompareAtPrice,
    bool InStock,
    CategoryLink Category,
    string? Brand,
    decimal RatingAverage,
    int RatingCount);

/// <summary>
/// One product's value for a spec. Numbers and yes/no stay typed (the storefront formats them for the reader); options
/// and texts are translated, a variant spec lists the options its variants use.
/// </summary>
public sealed record CompareValue(decimal? Number, bool? Boolean, IReadOnlyList<string> Texts)
{
    /// <summary>The product has no value for the spec (shown as "—").</summary>
    public static readonly CompareValue None = new(null, null, []);

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEmpty => Number is null && Boolean is null && Texts.Count == 0;
}

/// <summary>
/// One spec across the products: a value per product (<see cref="CompareValue.None"/> when it has none), whether the values
/// differ, and which products have the best value (a number spec with a better direction, when the values differ).
/// </summary>
public sealed record CompareRow(
    string Code,
    string Name,
    SpecKind Kind,
    string? Unit,
    SpecBetter Better,
    IReadOnlyList<CompareValue> Values,
    IReadOnlyList<int> Best,
    bool Differs);

/// <summary>Rows under one spec group heading ("Display", "Battery"), in the specs' order.</summary>
public sealed record CompareGroup(string Name, IReadOnlyList<CompareRow> Rows);

/// <summary>The comparison table. <paramref name="MixedCategories"/>: the products are not all from one category.</summary>
public sealed record ProductComparison(IReadOnlyList<CompareProduct> Products, IReadOnlyList<CompareGroup> Groups, bool MixedCategories);

/// <summary>
/// Builds the comparison of up to <see cref="MaxProducts"/> published products by their public numbers: their
/// comparable specs grouped as on the product page, translated into the requested language (the default language as
/// fallback). Unknown, unpublished or repeated numbers are left out.
/// </summary>
public sealed class ProductComparisonService(IApplicationDbContext db)
{
    public const int MaxProducts = 4;

    /// <summary>"12,15 18" → [12, 15, 18]: numbers in order, repeats and anything else dropped, at most four.</summary>
    public static IReadOnlyList<long> ParseNumbers(string? text) =>
        (text ?? "").Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => long.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0)
            .Where(n => n > 0)
            .Distinct()
            .Take(MaxProducts)
            .ToList();

    public async Task<ProductComparison> CompareAsync(IReadOnlyList<long> numbers, string locale, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(numbers);
        var wanted = numbers.Distinct().Take(MaxProducts).ToList();
        if (wanted.Count == 0)
            return new ProductComparison([], [], false);

        var rows = await db.Products.AsNoTracking()
            .Where(p => p.IsPublished && wanted.Contains(p.PublicNumber))
            .Select(p => new
            {
                p.Id,
                Product = new CompareProduct(
                    p.PublicNumber,
                    p.Translations.Where(t => t.Locale == locale).Select(t => t.Name).FirstOrDefault() ?? p.Name,
                    p.Translations.Where(t => t.Locale == locale).Select(t => t.Slug).FirstOrDefault() ?? p.Slug,
                    p.Images.OrderBy(i => i.SortOrder).Select(i => i.Url).FirstOrDefault(),
                    p.Price,
                    p.CompareAtPrice,
                    p.Stock - p.Reserved > 0,
                    new CategoryLink(
                        p.ProductGroup.SubCategory.Category.Translations.Where(t => t.Locale == locale).Select(t => t.Name).FirstOrDefault()
                            ?? p.ProductGroup.SubCategory.Category.Name,
                        p.ProductGroup.SubCategory.Category.Slug),
                    p.Brand == null ? null : p.Brand.Translations.Where(t => t.Locale == locale).Select(t => t.Name).FirstOrDefault() ?? p.Brand.Name,
                    p.RatingAverage,
                    p.RatingCount),
            })
            .ToListAsync(cancellationToken);
        var products = rows.OrderBy(r => wanted.IndexOf(r.Product.Number)).ToList();
        var ids = products.Select(p => p.Id).ToList();

        var values = await db.ProductSpecValues.AsNoTracking()
            .Where(v => ids.Contains(v.ProductId) && v.Spec.IsComparable)
            .Select(v => new
            {
                v.ProductId,
                v.SpecId,
                v.NumberValue,
                v.BooleanValue,
                Text = v.Option != null
                    ? v.Option.Translations.Where(t => t.Locale == locale).Select(t => t.Value).FirstOrDefault() ?? v.Option.Value
                    : v.TextValue,
            })
            .ToListAsync(cancellationToken);
        var variantOptions = await db.ProductVariants.AsNoTracking()
            .Where(v => ids.Contains(v.ProductId))
            .SelectMany(v => v.Options, (v, o) => new { v.ProductId, o.SpecId, o.OptionId })
            .Distinct()
            .ToListAsync(cancellationToken);
        var optionIds = variantOptions.Select(o => o.OptionId).Distinct().ToList();
        var options = await db.SpecOptions.AsNoTracking()
            .Where(o => optionIds.Contains(o.Id))
            .Select(o => new { o.Id, o.SortOrder, Value = o.Translations.Where(t => t.Locale == locale).Select(t => t.Value).FirstOrDefault() ?? o.Value })
            .ToDictionaryAsync(o => o.Id, cancellationToken);

        var specIds = values.Select(v => v.SpecId).Concat(variantOptions.Select(o => o.SpecId)).Distinct().ToList();
        var specs = await db.ProductSpecs.AsNoTracking()
            .Where(s => specIds.Contains(s.Id) && s.IsComparable)
            .OrderBy(s => s.SortOrder)
            .Select(s => new
            {
                s.Id,
                s.Code,
                s.Kind,
                s.Better,
                Name = s.Translations.Where(t => t.Locale == locale).Select(t => t.Name).FirstOrDefault() ?? s.Name,
                Group = s.Translations.Where(t => t.Locale == locale).Select(t => t.GroupName).FirstOrDefault() ?? s.GroupName ?? "",
                Unit = s.Translations.Where(t => t.Locale == locale).Select(t => t.Unit).FirstOrDefault() ?? s.Unit,
            })
            .ToListAsync(cancellationToken);

        var groups = new List<(string Name, List<CompareRow> Rows)>();
        foreach (var spec in specs)
        {
            var cells = products.Select(p =>
            {
                var own = values.FirstOrDefault(v => v.ProductId == p.Id && v.SpecId == spec.Id);
                if (own is not null)
                    return own.Text is null && own.NumberValue is null && own.BooleanValue is null
                        ? CompareValue.None
                        : new CompareValue(own.NumberValue, own.BooleanValue, own.Text is null ? [] : [own.Text]);
                var choices = variantOptions.Where(o => o.ProductId == p.Id && o.SpecId == spec.Id && options.ContainsKey(o.OptionId))
                    .Select(o => options[o.OptionId]).OrderBy(o => o.SortOrder).Select(o => o.Value).Distinct().ToList();
                return choices.Count == 0 ? CompareValue.None : new CompareValue(null, null, choices);
            }).ToList();
            if (cells.All(c => c.IsEmpty))
                continue;
            var row = new CompareRow(spec.Code, spec.Name, spec.Kind, spec.Unit, spec.Better, cells, BestOf(cells, spec.Kind, spec.Better), Differ(cells));
            var group = groups.FirstOrDefault(g => g.Name == spec.Group);
            if (group.Rows is null)
                groups.Add((spec.Group, [row]));
            else
                group.Rows.Add(row);
        }

        return new ProductComparison(
            products.Select(p => p.Product).ToList(),
            groups.Select(g => new CompareGroup(g.Name, g.Rows)).ToList(),
            products.Select(p => p.Product.Category.Slug).Distinct().Count() > 1);
    }

    /// <summary>Whether the products' values are not all the same (a missing value differs from any value).</summary>
    internal static bool Differ(IReadOnlyList<CompareValue> cells) =>
        cells.Select(c => c.IsEmpty ? "\0" : $"{c.Number?.ToString(CultureInfo.InvariantCulture)}|{c.Boolean}|{string.Join('\u001f', c.Texts.Order(StringComparer.Ordinal))}")
            .Distinct(StringComparer.Ordinal).Count() > 1;

    /// <summary>The columns holding the best number, when the spec has a direction and at least two numbers differ.</summary>
    internal static IReadOnlyList<int> BestOf(IReadOnlyList<CompareValue> cells, SpecKind kind, SpecBetter better)
    {
        if (kind != SpecKind.Number || better == SpecBetter.None)
            return [];
        var numbers = cells.Select((c, i) => (Value: c.Number, Index: i)).Where(x => x.Value is not null).ToList();
        if (numbers.Count < 2 || numbers.Select(x => x.Value).Distinct().Count() < 2)
            return [];
        var best = better == SpecBetter.Higher ? numbers.Max(x => x.Value) : numbers.Min(x => x.Value);
        return numbers.Where(x => x.Value == best).Select(x => x.Index).ToList();
    }
}
