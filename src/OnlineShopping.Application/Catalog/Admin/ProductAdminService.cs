using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnlineShopping.Application.Common;
using OnlineShopping.Domain.Catalog;

namespace OnlineShopping.Application.Catalog.Admin;

/// <summary>
/// Products for the admin: list with search, the edit form, create/update/delete. Texts follow the default-language
/// rule of <see cref="LocalizedTexts"/>; spec values are replaced by the submitted set (empty value = removed); a new
/// product gets the next public number, which is never reused (a deleted product keeps its number).
/// </summary>
public sealed class ProductAdminService(IApplicationDbContext db, IOptions<StoreOptions> store)
{
    public const int PageSize = 20;
    private const long FirstPublicNumber = 1001;

    private StoreOptions Store => store.Value;

    public async Task<ProductPage> ListAsync(string? search, Guid? groupId, int page, string locale, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        var query = db.Products.AsNoTracking();
        if (groupId is { } group)
            query = query.Where(p => p.ProductGroupId == group);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            var number = long.TryParse(term, out var n) ? n : -1;
            // ToLower() runs in SQL (lower()), not in .NET: the culture rules of the analyzers do not apply here.
#pragma warning disable CA1304, CA1311, CA1862
            var lower = term.ToLower();
            query = query.Where(p =>
                p.PublicNumber == number
                || p.SKU.ToLower().Contains(lower)
                || p.Name.ToLower().Contains(lower)
                || p.Translations.Any(t => t.Name.ToLower().Contains(lower)));
#pragma warning restore CA1304, CA1311, CA1862
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(p => p.PublicNumber)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(p => new
            {
                p.PublicNumber,
                Name = p.Translations.Where(t => t.Locale == locale).Select(t => t.Name).FirstOrDefault() ?? p.Name,
                p.SKU,
                p.Price,
                p.Stock,
                p.IsPublished,
                Category = p.ProductGroup.SubCategory.Category.Name,
                Sub = p.ProductGroup.SubCategory.Name,
                Group = p.ProductGroup.Name,
                Image = p.Images.OrderBy(i => i.SortOrder).Select(i => i.Url).FirstOrDefault(),
                Locales = p.Translations.Select(t => t.Locale).ToList(),
            })
            .ToListAsync(cancellationToken);

        var items = rows.Select(r => new ProductListItem(
                r.PublicNumber, r.Name, r.SKU, r.Price, r.Stock, r.IsPublished, $"{r.Category} › {r.Sub} › {r.Group}", r.Image,
                LocalizedTexts.Missing(Store, r.Locales)))
            .ToList();
        return new ProductPage(items, total, page, PageSize);
    }

    public async Task<Result<ProductEditView>> GetAsync(long number, CancellationToken cancellationToken = default)
    {
        var product = await db.Products.AsNoTracking()
            .Include(p => p.SpecValues).ThenInclude(v => v.Spec)
            .Include(p => p.SpecValues).ThenInclude(v => v.Option)
            .Include(p => p.Images)
            .SingleOrDefaultAsync(p => p.PublicNumber == number, cancellationToken);
        return product is null ? NotFound() : View(product);
    }

    /// <summary>The specs the editor offers, named in <paramref name="locale"/>.</summary>
    public async Task<IReadOnlyList<SpecChoice>> GetSpecsAsync(string locale, CancellationToken cancellationToken = default)
    {
        var specs = await db.ProductSpecs.AsNoTracking()
            .Include(s => s.Options)
            .OrderBy(s => s.SortOrder)
            .ToListAsync(cancellationToken);

        return specs.Select(s =>
            {
                var t = s.Translations.FirstOrDefault(x => x.Locale == locale);
                return new SpecChoice(
                    s.Code,
                    t?.Name ?? s.Name,
                    t?.GroupName ?? s.GroupName,
                    s.Kind.ToString(),
                    t?.Unit ?? s.Unit,
                    s.Options.OrderBy(o => o.SortOrder)
                        .Select(o => new SpecOptionChoice(o.Code, o.Translations.FirstOrDefault(x => x.Locale == locale)?.Value ?? o.Value))
                        .ToList());
            })
            .ToList();
    }

    public async Task<Result<ProductEditView>> CreateAsync(SaveProductRequest request, CancellationToken cancellationToken = default)
    {
        var product = new Product { Name = "" };
        var applied = await ApplyAsync(product, request, cancellationToken);
        if (!applied.IsSuccess)
            return applied.Error!;

        // The next number after every product ever created (deleted ones included): numbers are never reused.
        var last = await db.Products.IgnoreQueryFilters().MaxAsync(p => (long?)p.PublicNumber, cancellationToken) ?? 0;
        product.PublicNumber = Math.Max(FirstPublicNumber, last + 1);
        db.Products.Add(product);
        await db.SaveChangesAsync(cancellationToken);
        return View(product);
    }

    public async Task<Result<ProductEditView>> UpdateAsync(long number, SaveProductRequest request, CancellationToken cancellationToken = default)
    {
        var product = await db.Products
            .Include(p => p.SpecValues).ThenInclude(v => v.Spec)
            .Include(p => p.SpecValues).ThenInclude(v => v.Option)
            .Include(p => p.Images)
            .SingleOrDefaultAsync(p => p.PublicNumber == number, cancellationToken);
        if (product is null)
            return NotFound();

        var applied = await ApplyAsync(product, request, cancellationToken);
        if (!applied.IsSuccess)
            return applied.Error!;
        await db.SaveChangesAsync(cancellationToken);
        return View(product);
    }

    /// <summary>Soft delete: the product leaves the shop, its number stays taken, and orders keep their snapshot.</summary>
    public async Task<Result<bool>> DeleteAsync(long number, CancellationToken cancellationToken = default)
    {
        var product = await db.Products.SingleOrDefaultAsync(p => p.PublicNumber == number, cancellationToken);
        if (product is null)
            return NotFound();
        db.Products.Remove(product);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<Result<bool>> ApplyAsync(Product product, SaveProductRequest request, CancellationToken cancellationToken)
    {
        if (LocalizedTexts.Validate(request.Texts, Store) is { } textError)
            return textError;
        if (!await db.ProductGroups.AnyAsync(g => g.Id == request.ProductGroupId, cancellationToken))
            return AppError.Validation(CatalogErrors.GroupRequired, "Choose an existing product group.");
        var sku = request.Sku.Trim();
        if (await db.Products.AnyAsync(p => p.SKU == sku && p.Id != product.Id, cancellationToken))
            return AppError.Conflict(CatalogErrors.SkuTaken, $"Another product has SKU {sku}.");

        var specs = await ResolveSpecsAsync(request.Specs, cancellationToken);
        if (!specs.IsSuccess)
            return specs.Error!;

        var main = LocalizedTexts.Default(request.Texts, Store.DefaultLocale);
        product.Name = main.Name;
        product.Slug = LocalizedTexts.SlugFor(main);
        product.Description = main.Description;
        product.SKU = sku;
        product.Price = request.Price;
        product.CompareAtPrice = request.CompareAtPrice;
        product.Stock = request.Stock;
        product.IsPublished = request.IsPublished;
        product.ProductGroupId = request.ProductGroupId;
        LocalizedTexts.ApplyTranslations(request.Texts, Store.DefaultLocale, product.Translations, withSlug: true, withDescription: true);
        ApplySpecs(product, specs.Value);
        return true;
    }

    private sealed record ResolvedSpec(ProductSpec Spec, decimal? Number, bool? Boolean, string? Text, SpecOption? Option);

    /// <summary>Checks every submitted value against its spec's kind; an entry without any value means "remove".</summary>
    private async Task<Result<List<ResolvedSpec>>> ResolveSpecsAsync(IReadOnlyList<ProductSpecInput> inputs, CancellationToken cancellationToken)
    {
        var codes = inputs.Select(i => i.Code).Distinct(StringComparer.Ordinal).ToList();
        var specs = await db.ProductSpecs.Include(s => s.Options).Where(s => codes.Contains(s.Code)).ToDictionaryAsync(s => s.Code, cancellationToken);
        var resolved = new List<ResolvedSpec>();

        foreach (var input in inputs)
        {
            if (!specs.TryGetValue(input.Code, out var spec))
                return AppError.Validation(CatalogErrors.SpecNotFound, $"Unknown spec {input.Code}.");
            var text = string.IsNullOrWhiteSpace(input.Text) ? null : input.Text.Trim();
            if (input.Number is null && input.Boolean is null && text is null && string.IsNullOrEmpty(input.OptionCode))
                continue;

            ResolvedSpec? value = spec.Kind switch
            {
                SpecKind.Number when input.Number is { } n => new(spec, n, null, null, null),
                SpecKind.Boolean when input.Boolean is { } b => new(spec, null, b, null, null),
                SpecKind.Text when text is { Length: <= 500 } => new(spec, null, null, text, null),
                SpecKind.Option when spec.Options.FirstOrDefault(o => o.Code == input.OptionCode) is { } option => new(spec, null, null, null, option),
                _ => null,
            };
            if (value is null)
                return AppError.Validation(CatalogErrors.SpecValueInvalid, $"The value of {input.Code} does not fit its kind.");
            resolved.Add(value);
        }
        return resolved;
    }

    private void ApplySpecs(Product product, List<ResolvedSpec> values)
    {
        // Removed values are soft-deleted (the interceptor turns Remove into IsDeleted); the view skips them.
        foreach (var existing in product.SpecValues.Where(v => !v.IsDeleted && values.All(r => r.Spec.Id != v.SpecId)).ToList())
            db.ProductSpecValues.Remove(existing);
        foreach (var value in values)
        {
            var row = product.SpecValues.FirstOrDefault(v => !v.IsDeleted && v.SpecId == value.Spec.Id);
            if (row is null)
            {
                row = new ProductSpecValue { Product = product, Spec = value.Spec };
                product.SpecValues.Add(row);
                db.ProductSpecValues.Add(row);
            }
            row.NumberValue = value.Number;
            row.BooleanValue = value.Boolean;
            row.TextValue = value.Text;
            row.Option = value.Option;
            row.OptionId = value.Option?.Id;
        }
    }

    private ProductEditView View(Product p) => new(
        p.PublicNumber,
        p.SKU,
        p.Price,
        p.CompareAtPrice,
        p.Stock,
        p.IsPublished,
        p.ProductGroupId,
        LocalizedTexts.Read(Store, p.Name, p.Slug, p.Description, p.Translations),
        p.SpecValues
            .Where(v => !v.IsDeleted)
            .OrderBy(v => v.Spec.SortOrder)
            .Select(v => new ProductSpecInput(v.Spec.Code, v.NumberValue, v.BooleanValue, v.TextValue, v.Option?.Code))
            .ToList(),
        p.Images.OrderBy(i => i.SortOrder).Select(i => i.Url).ToList());

    private static AppError NotFound() => AppError.NotFound(CatalogErrors.NotFound, "No such product.");
}
