using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using OnlineShopping.Application.Catalog.Admin;
using OnlineShopping.Application.Common;
using OnlineShopping.Application.Inventory;
using OnlineShopping.Domain.Catalog;

namespace OnlineShopping.Application.Catalog.Transfer;

/// <summary>
/// Bulk import of the product sheet that <see cref="ProductExportService"/> writes (CSV or Excel). Rows are matched by
/// "number", else by SKU; a row without a match creates a product. Only the columns in the file change: a sheet with
/// just "sku" and "price" updates prices. Missing categories (by name, "Category > Sub-category > Group") and brands
/// are created. Every row goes through the same rules as the product editor. The file is applied as a whole inside
/// one transaction: with any problem — or as a check — nothing is saved.
/// </summary>
public sealed class ProductImportService(
    IApplicationDbContext db,
    ProductAdminService products,
    CatalogTreeService tree,
    BrandAdminService brands,
    TableFiles files,
    IValidator<SaveProductRequest> validator,
    IOptions<StoreOptions> store,
    StockLedger ledger)
{
    public const int MaxRows = 5000;
    public const long MaxBytes = 10 * 1024 * 1024;
    private const int MaxProblems = 200;

    private StoreOptions Store => store.Value;

    public async Task<Result<ImportReport>> ProductsAsync(Stream upload, long length, bool dryRun, CancellationToken cancellationToken = default)
    {
        var read = await ReadTableAsync(files, upload, length, cancellationToken);
        if (!read.IsSuccess)
            return read.Error!;
        var table = read.Value;
        var columns = Columns(table.Headers, out var problems);
        if (!columns.ContainsKey(ProductColumns.Sku))
            problems.Add(new(1, ProductColumns.Sku, TransferErrors.ColumnMissing));
        var specs = await db.ProductSpecs.AsNoTracking().Include(s => s.Options).ToDictionaryAsync(s => s.Code, StringComparer.Ordinal, cancellationToken);
        foreach (var code in columns.Keys.Where(k => k.StartsWith(ProductColumns.SpecPrefix, StringComparison.Ordinal)).Select(k => k[ProductColumns.SpecPrefix.Length..]))
        {
            if (!specs.ContainsKey(code))
                problems.Add(new(1, ProductColumns.SpecPrefix + code, TransferErrors.ColumnUnknown));
        }
        if (problems.Count > 0)
            return new ImportReport(false, 0, 0, table.Rows.Count, problems);

        var context = new ImportContext(await LoadTreeAsync(cancellationToken), await LoadBrandsAsync(cancellationToken), specs);
        var existing = await db.Products.AsNoTracking().Select(p => new { p.SKU, p.PublicNumber }).ToListAsync(cancellationToken);
        var bySku = existing.ToDictionary(p => p.SKU, p => p.PublicNumber, StringComparer.Ordinal);
        var numbers = existing.Select(p => p.PublicNumber).ToHashSet();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var (created, updated) = (0, 0);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        for (var i = 0; i < table.Rows.Count && problems.Count < MaxProblems; i++)
        {
            var row = new Row(i + 2, table.Rows[i], columns);
            await transaction.CreateSavepointAsync("row", cancellationToken);
            try
            {
                var outcome = await ImportRowAsync(row, context, bySku, numbers, seen, cancellationToken);
                if (outcome.Problem is { } problem)
                {
                    problems.Add(problem);
                    await transaction.RollbackToSavepointAsync("row", cancellationToken);
                    db.ChangeTracker.Clear();
                }
                else if (outcome.Created)
                {
                    created++;
                }
                else
                {
                    updated++;
                }
            }
            catch (DbUpdateException)
            {
                problems.Add(new(row.Number, null, TransferErrors.FileUnreadable));
                await transaction.RollbackToSavepointAsync("row", cancellationToken);
                db.ChangeTracker.Clear();
            }
        }

        var applied = !dryRun && problems.Count == 0;
        if (applied)
            await transaction.CommitAsync(cancellationToken);
        else
            await transaction.RollbackAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return applied
            ? new ImportReport(true, created, updated, table.Rows.Count, [])
            : new ImportReport(false, created, updated, table.Rows.Count, problems);
    }

    /// <summary>Stock by SKU (a product without variants, or a variant); a product with variants gets the variants' sum.</summary>
    public async Task<Result<ImportReport>> StockAsync(Stream upload, long length, bool dryRun, CancellationToken cancellationToken = default)
    {
        var read = await ReadTableAsync(files, upload, length, cancellationToken);
        if (!read.IsSuccess)
            return read.Error!;
        var table = read.Value;
        var columns = Columns(table.Headers, out var problems, allowAnything: true);
        foreach (var required in new[] { ProductColumns.Sku, ProductColumns.Stock }.Where(c => !columns.ContainsKey(c)))
            problems.Add(new(1, required, TransferErrors.ColumnMissing));
        if (problems.Count > 0)
            return new ImportReport(false, 0, 0, table.Rows.Count, problems);

        var productsBySku = await db.Products.AsNoTracking()
            .Select(p => new { p.Id, p.SKU, HasVariants = p.Variants.Any() })
            .ToDictionaryAsync(p => p.SKU, StringComparer.Ordinal, cancellationToken);
        var variantsBySku = await db.ProductVariants.AsNoTracking()
            .Select(v => new { v.Id, v.ProductId, v.Sku })
            .ToDictionaryAsync(v => v.Sku, StringComparer.Ordinal, cancellationToken);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var changes = new List<(StockItem Item, int Stock)>();

        for (var i = 0; i < table.Rows.Count && problems.Count < MaxProblems; i++)
        {
            var row = new Row(i + 2, table.Rows[i], columns);
            var sku = row[ProductColumns.Sku]?.Trim() ?? "";
            var (ok, value) = Cells.Number(row[ProductColumns.Stock] ?? "");
            if (sku.Length == 0)
                problems.Add(new(row.Number, ProductColumns.Sku, TransferErrors.SkuRequired));
            else if (!seen.Add(sku))
                problems.Add(new(row.Number, ProductColumns.Sku, TransferErrors.SkuRepeated));
            else if (!ok || value is not { } stock || stock < 0 || stock > 1_000_000_000 || stock != decimal.Truncate(stock))
                problems.Add(new(row.Number, ProductColumns.Stock, TransferErrors.NumberInvalid));
            else if (variantsBySku.TryGetValue(sku, out var variant))
                changes.Add((new StockItem(variant.ProductId, variant.Id), (int)stock));
            else if (productsBySku.TryGetValue(sku, out var product))
            {
                if (product.HasVariants)
                    problems.Add(new(row.Number, ProductColumns.Sku, TransferErrors.StockOnVariants));
                else
                    changes.Add((new StockItem(product.Id, null), (int)stock));
            }
            else
            {
                problems.Add(new(row.Number, ProductColumns.Sku, TransferErrors.SkuNotFound));
            }
        }

        // Each changed number becomes an adjustment line in the ledger, all of them together.
        var applied = !dryRun && problems.Count == 0;
        if (applied)
        {
            var saved = await db.InTransactionAsync(async () =>
            {
                foreach (var (item, stock) in changes)
                {
                    var set = await ledger.SetTotalAsync(item, stock, StockReferences.Import, cancellationToken);
                    if (!set.IsSuccess)
                        return set.Error!;
                }
                return Result.Success(true);
            }, cancellationToken);
            if (!saved.IsSuccess)
                return saved.Error!;
        }
        db.ChangeTracker.Clear();
        return new ImportReport(applied, 0, changes.Count, table.Rows.Count, applied ? [] : problems);
    }

    /// <summary>Reads an uploaded CSV or Excel file (at most 10 MB and 5000 rows); also used for stocktake counts.</summary>
    internal static async Task<Result<TableData>> ReadTableAsync(TableFiles files, Stream upload, long length, CancellationToken cancellationToken)
    {
        if (length <= 0)
            return AppError.Validation(TransferErrors.FileEmpty, "The file is empty.");
        if (length > MaxBytes)
            return AppError.Validation(TransferErrors.FileTooLarge, "The file is larger than 10 MB.");
        using var buffer = new MemoryStream();
        await upload.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length > MaxBytes)
            return AppError.Validation(TransferErrors.FileTooLarge, "The file is larger than 10 MB.");
        buffer.Position = 0;

        TableData table;
        try
        {
            table = files.Detect(buffer.GetBuffer().AsSpan(0, (int)Math.Min(4, buffer.Length))).Read(buffer, MaxRows);
        }
#pragma warning disable CA1031 // A spreadsheet library may throw anything on a broken file: it is reported, not rethrown.
        catch (Exception)
#pragma warning restore CA1031
        {
            return AppError.Validation(TransferErrors.FileUnreadable, "The file cannot be read as CSV or Excel.");
        }
        if (table.Headers.Count == 0)
            return AppError.Validation(TransferErrors.FileEmpty, "The file has no rows.");
        if (table.Rows.Count > MaxRows)
            return AppError.Validation(TransferErrors.TooManyRows, $"At most {MaxRows} rows at once.");
        return table;
    }

    /// <summary>Header → column index (lower case); unknown and repeated columns are problems.</summary>
    private Dictionary<string, int> Columns(IReadOnlyList<string> headers, out List<ImportProblem> problems, bool allowAnything = false)
    {
        problems = [];
        var known = ProductColumns.Fixed
            .Concat(Store.LocaleList.SelectMany(l => ProductColumns.LocalizedNames.Select(n => $"{n}:{l}")))
            .ToHashSet(StringComparer.Ordinal);
        var columns = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < headers.Count; i++)
        {
            var header = headers[i].Trim().TrimStart('﻿').ToLowerInvariant();
            if (header.Length == 0)
                continue;
            if (!allowAnything && !known.Contains(header) && !header.StartsWith(ProductColumns.SpecPrefix, StringComparison.Ordinal))
                problems.Add(new(1, header, TransferErrors.ColumnUnknown));
            else if (!columns.TryAdd(header, i))
                problems.Add(new(1, header, TransferErrors.ColumnRepeated));
        }
        return columns;
    }

    private sealed record Outcome(bool Created, ImportProblem? Problem);

    private async Task<Outcome> ImportRowAsync(
        Row row, ImportContext context, Dictionary<string, long> bySku, HashSet<long> numbers, HashSet<string> seen, CancellationToken cancellationToken)
    {
        Outcome Fail(string? column, string code) => new(false, new ImportProblem(row.Number, column, code));

        var sku = row[ProductColumns.Sku]?.Trim() ?? "";
        if (sku.Length == 0)
            return Fail(ProductColumns.Sku, TransferErrors.SkuRequired);
        if (!seen.Add(sku))
            return Fail(ProductColumns.Sku, TransferErrors.SkuRepeated);

        long? number = null;
        if (Cells.Text(row[ProductColumns.Number] ?? "") is { } numberText)
        {
            if (!long.TryParse(numberText, out var n) || !numbers.Contains(n))
                return Fail(ProductColumns.Number, TransferErrors.NumberUnknown);
            number = n;
        }
        else if (bySku.TryGetValue(sku, out var matched))
        {
            number = matched;
        }

        var view = number is { } existingNumber ? (await products.GetAsync(existingNumber, cancellationToken)).Value : null;
        var draft = Draft.From(view, Store);
        draft.Sku = sku;

        if (row.Has(ProductColumns.Price))
        {
            var (ok, value) = Cells.Number(row[ProductColumns.Price]!);
            if (!ok || value is null)
                return Fail(ProductColumns.Price, TransferErrors.NumberInvalid);
            draft.Price = value.Value;
        }
        else if (view is null)
        {
            return Fail(ProductColumns.Price, TransferErrors.NumberInvalid);
        }
        if (row.Has(ProductColumns.CompareAtPrice))
        {
            var (ok, value) = Cells.Number(row[ProductColumns.CompareAtPrice]!);
            if (!ok)
                return Fail(ProductColumns.CompareAtPrice, TransferErrors.NumberInvalid);
            draft.CompareAtPrice = value;
        }
        if (row.Has(ProductColumns.Stock))
        {
            var (ok, value) = Cells.Number(row[ProductColumns.Stock]!);
            if (!ok || (value is { } s && s != decimal.Truncate(s)) || value > int.MaxValue || value < int.MinValue)
                return Fail(ProductColumns.Stock, TransferErrors.NumberInvalid);
            draft.Stock = (int)(value ?? 0);
        }
        if (row.Has(ProductColumns.WeightGrams))
        {
            var (ok, value) = Cells.Number(row[ProductColumns.WeightGrams]!);
            if (!ok || (value is { } w && (w != decimal.Truncate(w) || w > int.MaxValue || w < int.MinValue)))
                return Fail(ProductColumns.WeightGrams, TransferErrors.NumberInvalid);
            draft.WeightGrams = (int?)value;
        }
        if (row.Has(ProductColumns.Published))
        {
            var (ok, value) = Cells.Boolean(row[ProductColumns.Published]!);
            if (!ok)
                return Fail(ProductColumns.Published, TransferErrors.BooleanInvalid);
            draft.IsPublished = value ?? draft.IsPublished;
        }
        if (row.Has(ProductColumns.Gtin))
            draft.Gtin = Cells.Text(row[ProductColumns.Gtin]!);
        if (row.Has(ProductColumns.Mpn))
            draft.Mpn = Cells.Text(row[ProductColumns.Mpn]!);

        if (Cells.Text(row[ProductColumns.Group] ?? "") is { } path)
        {
            var group = await ResolveGroupAsync(path, context, cancellationToken);
            if (!group.IsSuccess)
                return Fail(ProductColumns.Group, group.Error!.Code);
            draft.GroupId = group.Value;
        }
        if (draft.GroupId == Guid.Empty)
            return Fail(ProductColumns.Group, TransferErrors.GroupInvalid);

        if (row.Has(ProductColumns.Brand))
        {
            var brand = await ResolveBrandAsync(Cells.Text(row[ProductColumns.Brand]!), context, cancellationToken);
            if (!brand.IsSuccess)
                return Fail(ProductColumns.Brand, brand.Error!.Code);
            draft.BrandId = brand.Value;
        }

        foreach (var locale in Store.LocaleList)
            draft.ApplyLocale(locale, name => row.Has($"{name}:{locale}") ? row[$"{name}:{locale}"]!.Trim() : null);

        foreach (var (code, spec) in context.Specs.Where(s => row.Has(ProductColumns.SpecPrefix + s.Key)))
        {
            var column = ProductColumns.SpecPrefix + code;
            var value = SpecValue(spec, row[column]!);
            if (!value.IsSuccess)
                return Fail(column, value.Error!.Code);
            draft.Specs.RemoveAll(s => s.Code == code);
            if (value.Value is { } input)
                draft.Specs.Add(input);
        }

        var request = draft.ToRequest();
        if (validator.Validate(request) is { IsValid: false } invalid)
            return Fail(null, invalid.Errors[0].ErrorCode);
        var saved = number is { } target ? await products.UpdateAsync(target, request, StockReferences.Import, cancellationToken) : await products.CreateAsync(request, StockReferences.Import, cancellationToken);
        if (!saved.IsSuccess)
            return Fail(null, saved.Error!.Code);

        bySku.Remove(view?.Sku ?? "");
        bySku[saved.Value.Sku] = saved.Value.Number;
        numbers.Add(saved.Value.Number);
        return new Outcome(number is null, null);
    }

    /// <summary>A spec cell as the editor's input; null means "remove the value".</summary>
    private static Result<ProductSpecInput?> SpecValue(ProductSpec spec, string cell)
    {
        var text = cell.Trim();
        if (text.Length == 0)
            return (ProductSpecInput?)null;
        switch (spec.Kind)
        {
            case SpecKind.Number:
                var (numberOk, number) = Cells.Number(text);
                return numberOk ? new ProductSpecInput(spec.Code, number, null, null, null) : AppError.Validation(TransferErrors.NumberInvalid, "Not a number.");
            case SpecKind.Boolean:
                var (boolOk, boolean) = Cells.Boolean(text);
                return boolOk ? new ProductSpecInput(spec.Code, null, boolean, null, null) : AppError.Validation(TransferErrors.BooleanInvalid, "Not yes or no.");
            case SpecKind.Option:
                // By code, or by its value in any language.
                var option = spec.Options.FirstOrDefault(o => string.Equals(o.Code, text, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(o.Value, text, StringComparison.OrdinalIgnoreCase)
                    || o.Translations.Any(t => string.Equals(t.Value, text, StringComparison.OrdinalIgnoreCase)));
                return option is null
                    ? AppError.Validation(TransferErrors.OptionUnknown, $"{text} is not an option of {spec.Code}.")
                    : new ProductSpecInput(spec.Code, null, null, null, option.Code);
            default:
                return new ProductSpecInput(spec.Code, null, null, text, null);
        }
    }

    // ── Categories and brands by name, created when missing ─────────────────────────────────────────────────────────

    private sealed record Node(Guid Id, Guid? ParentId, HashSet<string> Names);

    private sealed record ImportContext(List<Node> Tree, List<Node> Brands, Dictionary<string, ProductSpec> Specs);

    private static HashSet<string> NamesOf(string name, IEnumerable<CatalogTranslation> translations) =>
        translations.Select(t => t.Name).Prepend(name).Select(n => n.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Categories (no parent), sub-categories (parent: category) and groups (parent: sub-category) as one list.</summary>
    private async Task<List<Node>> LoadTreeAsync(CancellationToken cancellationToken)
    {
        var categories = await db.Categories.AsNoTracking().Include(c => c.SubCategories).ThenInclude(s => s.Groups).ToListAsync(cancellationToken);
        return categories.SelectMany(c =>
                c.SubCategories.SelectMany(s => s.Groups.Select(g => new Node(g.Id, s.Id, NamesOf(g.Name, g.Translations))))
                    .Concat(c.SubCategories.Select(s => new Node(s.Id, c.Id, NamesOf(s.Name, s.Translations))))
                    .Append(new Node(c.Id, null, NamesOf(c.Name, c.Translations))))
            .ToList();
    }

    private async Task<List<Node>> LoadBrandsAsync(CancellationToken cancellationToken) =>
        (await db.Brands.AsNoTracking().ToListAsync(cancellationToken)).Select(b => new Node(b.Id, null, NamesOf(b.Name, b.Translations))).ToList();

    private async Task<Result<Guid>> ResolveGroupAsync(string path, ImportContext context, CancellationToken cancellationToken)
    {
        var parts = path.Split(['>', '›', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 3)
            return AppError.Validation(TransferErrors.GroupInvalid, "Write the group as Category > Sub-category > Group.");

        Guid? parent = null;
        for (var level = 0; level < 3; level++)
        {
            var name = parts[level];
            // Level 0 has no parent; a name may repeat at another level, so the parent decides.
            var node = context.Tree.FirstOrDefault(n => n.ParentId == parent && n.Names.Contains(name) && (level > 0 || n.ParentId is null));
            if (node is null)
            {
                var texts = new SaveCatalogNodeRequest([new LocalizedText(Store.DefaultLocale, name, null, null)]);
                var created = level switch
                {
                    0 => await tree.CreateCategoryAsync(texts, cancellationToken),
                    1 => await tree.CreateSubCategoryAsync(parent!.Value, texts, cancellationToken),
                    _ => await tree.CreateGroupAsync(parent!.Value, texts, cancellationToken),
                };
                if (!created.IsSuccess)
                    return created.Error!;
                node = new Node(created.Value.Id, parent, [name]);
                context.Tree.Add(node);
            }
            parent = node.Id;
        }
        return parent!.Value;
    }

    private async Task<Result<Guid?>> ResolveBrandAsync(string? name, ImportContext context, CancellationToken cancellationToken)
    {
        if (name is null)
            return (Guid?)null;
        if (context.Brands.FirstOrDefault(b => b.Names.Contains(name)) is { } brand)
            return brand.Id;
        var created = await brands.CreateAsync(new SaveBrandRequest([new LocalizedText(Store.DefaultLocale, name, null, null)]), cancellationToken);
        if (!created.IsSuccess)
            return created.Error!;
        context.Brands.Add(new Node(created.Value.Id, null, [name]));
        return created.Value.Id;
    }

    /// <summary>A data row with access by column name; <see cref="Has"/> is false for columns not in the file.</summary>
    private sealed class Row(int number, IReadOnlyList<string> cells, Dictionary<string, int> columns)
    {
        public int Number => number;

        public bool Has(string column) => columns.ContainsKey(column);

        public string? this[string column] => columns.TryGetValue(column, out var i) ? (i < cells.Count ? cells[i] : "") : null;
    }

    /// <summary>The product being built from the existing values and the row's cells.</summary>
    private sealed class Draft
    {
        public string Sku { get; set; } = "";
        public decimal Price { get; set; }
        public decimal? CompareAtPrice { get; set; }
        public DateTime? SaleEndsUtc { get; set; }
        public int Stock { get; set; }
        public bool IsPublished { get; set; }
        public Guid GroupId { get; set; }
        public Guid? BrandId { get; set; }
        public string? Gtin { get; set; }
        public string? Mpn { get; set; }
        public int? WeightGrams { get; set; }
        public List<LocalizedText> Texts { get; private init; } = [];
        public List<SeoText> Seo { get; private init; } = [];
        public List<ProductSpecInput> Specs { get; private init; } = [];

        public static Draft From(ProductEditView? view, StoreOptions store) => view is null
            ? new Draft
            {
                Texts = store.LocaleList.Select(l => new LocalizedText(l, "", null, null)).ToList(),
                Seo = store.LocaleList.Select(l => new SeoText(l, null, null, null)).ToList(),
            }
            : new Draft
            {
                Price = view.Price,
                CompareAtPrice = view.CompareAtPrice,
                Stock = view.Stock,
                IsPublished = view.IsPublished,
                GroupId = view.ProductGroupId,
                BrandId = view.BrandId,
                Gtin = view.Gtin,
                Mpn = view.Mpn,
                WeightGrams = view.WeightGrams,
                Texts = [.. view.Texts],
                Seo = [.. view.Seo],
                Specs = [.. view.Specs],
                SaleEndsUtc = view.SaleEndsUtc,
            };

        /// <summary>Changes the texts of one language from the columns the row has (<paramref name="cell"/> is null for the others).</summary>
        public void ApplyLocale(string locale, Func<string, string?> cell)
        {
            var text = Texts.FirstOrDefault(t => t.Locale == locale) ?? new LocalizedText(locale, "", null, null);
            text = text with
            {
                Name = cell("name") ?? text.Name,
                Slug = cell("slug") is { } slug ? (slug.Length == 0 ? null : slug) : text.Slug,
                Description = cell("description") is { } d ? (d.Length == 0 ? null : d) : text.Description,
            };
            Texts.RemoveAll(t => t.Locale == locale);
            Texts.Add(text);

            var seo = Seo.FirstOrDefault(s => s.Locale == locale) ?? new SeoText(locale, null, null, null);
            seo = seo with
            {
                MetaTitle = cell("meta_title") is { } t ? Cells.Text(t) : seo.MetaTitle,
                MetaDescription = cell("meta_description") is { } md ? Cells.Text(md) : seo.MetaDescription,
                Tags = cell("tags") is { } tags ? Cells.Text(tags) : seo.Tags,
            };
            Seo.RemoveAll(s => s.Locale == locale);
            Seo.Add(seo);
        }

        public SaveProductRequest ToRequest() =>
            new(Sku, Price, CompareAtPrice, Stock, IsPublished, GroupId, Texts, Specs, BrandId, Gtin, Mpn, WeightGrams, Seo, SaleEndsUtc);
    }
}
