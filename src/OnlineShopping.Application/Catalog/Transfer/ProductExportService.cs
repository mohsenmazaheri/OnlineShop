using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnlineShopping.Application.Common;
using OnlineShopping.Domain.Catalog;

namespace OnlineShopping.Application.Catalog.Transfer;

/// <summary>
/// The catalog as a spreadsheet (CSV or Excel) in the layout <see cref="ProductImportService"/> reads back: one row per
/// product with its texts in every shop language and a column per spec in use; and a stock sheet (SKU, stock) with one
/// row per product without variants and per variant.
/// </summary>
public sealed class ProductExportService(IApplicationDbContext db, TableFiles files, IOptions<StoreOptions> store)
{
    private StoreOptions Store => store.Value;

    public async Task<Result<ExportFile>> ProductsAsync(string? format, CancellationToken cancellationToken = default)
    {
        if (files.ByFormat(format) is not { } file)
            return AppError.Validation(TransferErrors.FormatUnknown, "Choose csv or xlsx.");

        // Flat queries: one join of everything (translations of specs, options, groups…) multiplies into a huge result.
        var products = await db.Products.AsNoTracking().OrderBy(p => p.PublicNumber).ToListAsync(cancellationToken);
        var groups = await db.ProductGroups.AsNoTracking()
            .Select(g => new { g.Id, Path = g.SubCategory.Category.Name + ProductColumns.GroupSeparator + g.SubCategory.Name + ProductColumns.GroupSeparator + g.Name })
            .ToDictionaryAsync(g => g.Id, g => g.Path, cancellationToken);
        var brandNames = await db.Brands.AsNoTracking().Select(b => new { b.Id, b.Name }).ToDictionaryAsync(b => b.Id, b => b.Name, cancellationToken);
        var values = (await db.ProductSpecValues.AsNoTracking()
                .Select(v => new SpecCellValue(v.ProductId, v.SpecId, v.Spec.Kind, v.NumberValue, v.BooleanValue, v.TextValue, v.Option != null ? v.Option.Value : null))
                .ToListAsync(cancellationToken))
            .ToLookup(v => v.ProductId);
        var usedSpecIds = values.SelectMany(g => g).Select(v => v.SpecId).ToHashSet();
        var specs = (await db.ProductSpecs.AsNoTracking().OrderBy(s => s.SortOrder).Select(s => new { s.Id, s.Code }).ToListAsync(cancellationToken))
            .Where(s => usedSpecIds.Contains(s.Id))
            .ToList();
        var locales = Store.LocaleList.OrderBy(l => l == Store.DefaultLocale ? 0 : 1).ToList();

        var headers = ProductColumns.Fixed
            .Concat(locales.SelectMany(l => ProductColumns.LocalizedNames.Select(n => $"{n}:{l}")))
            .Concat(specs.Select(s => ProductColumns.SpecPrefix + s.Code))
            .ToList();
        var rows = products.Select(p =>
            {
                var cells = new List<string>
                {
                    p.PublicNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    p.SKU,
                    p.IsPublished ? "true" : "false",
                    groups.GetValueOrDefault(p.ProductGroupId, ""),
                    p.BrandId is { } brandId ? brandNames.GetValueOrDefault(brandId, "") : "",
                    Cells.Format(p.Price),
                    Cells.Format(p.CompareAtPrice),
                    p.Stock.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    p.Gtin ?? "",
                    p.Mpn ?? "",
                    p.WeightGrams?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "",
                };
                foreach (var locale in locales)
                {
                    var t = locale == Store.DefaultLocale ? null : p.Translations.FirstOrDefault(x => x.Locale == locale);
                    var own = locale == Store.DefaultLocale;
                    cells.AddRange(
                    [
                        own ? p.Name : t?.Name ?? "",
                        own ? p.Slug : t?.Slug ?? "",
                        own ? p.Description ?? "" : t?.Description ?? "",
                        own ? p.MetaTitle ?? "" : t?.MetaTitle ?? "",
                        own ? p.MetaDescription ?? "" : t?.MetaDescription ?? "",
                        own ? p.Tags ?? "" : t?.Tags ?? "",
                    ]);
                }
                var specValues = values[p.Id].ToDictionary(v => v.SpecId);
                foreach (var spec in specs)
                    cells.Add(specValues.TryGetValue(spec.Id, out var value) ? SpecCell(value) : "");
                return (IReadOnlyList<string>)cells;
            })
            .ToList();

        return new ExportFile(file.Write(new TableData(headers, rows)), file.ContentType, $"products.{file.Format}");
    }

    public async Task<Result<ExportFile>> StockAsync(string? format, CancellationToken cancellationToken = default)
    {
        if (files.ByFormat(format) is not { } file)
            return AppError.Validation(TransferErrors.FormatUnknown, "Choose csv or xlsx.");

        var products = await db.Products.AsNoTracking()
            .Include(p => p.Variants)
            .OrderBy(p => p.PublicNumber)
            .ToListAsync(cancellationToken);
        var optionValues = await db.SpecOptions.AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Value, cancellationToken);

        var rows = new List<IReadOnlyList<string>>();
        foreach (var p in products)
        {
            var variants = p.Variants.OrderBy(v => v.SortOrder).ToList();
            if (variants.Count == 0)
                rows.Add([p.SKU, p.Name, p.Stock.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
            foreach (var v in variants)
            {
                var label = string.Join(" / ", v.Options.Select(o => optionValues.GetValueOrDefault(o.OptionId, "")));
                rows.Add([v.Sku, $"{p.Name} – {label}", v.Stock.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
            }
        }
        return new ExportFile(file.Write(new TableData([ProductColumns.Sku, "name", ProductColumns.Stock], rows)), file.ContentType, $"stock.{file.Format}");
    }

    private sealed record SpecCellValue(Guid ProductId, Guid SpecId, SpecKind Kind, decimal? Number, bool? Boolean, string? Text, string? Option);

    private static string SpecCell(SpecCellValue value) => value.Kind switch
    {
        SpecKind.Number => Cells.Format(value.Number),
        SpecKind.Boolean => value.Boolean is { } b ? (b ? "true" : "false") : "",
        SpecKind.Option => value.Option ?? "",
        _ => value.Text ?? "",
    };
}
