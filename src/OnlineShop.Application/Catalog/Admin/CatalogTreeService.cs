using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnlineShop.Application.Common;
using OnlineShop.Domain.Catalog;

namespace OnlineShop.Application.Catalog.Admin;

/// <summary>
/// The admin's category tree: categories → sub-categories → product groups, each with a name per language (categories
/// and sub-categories also a slug). Deleting is a soft delete and only allowed for empty items, so no product is
/// left without a group.
/// </summary>
public sealed class CatalogTreeService(IApplicationDbContext db, IOptions<StoreOptions> store)
{
    private StoreOptions Store => store.Value;

    public async Task<CatalogTree> GetTreeAsync(CancellationToken cancellationToken = default)
    {
        var categories = await db.Categories.AsNoTracking()
            .Include(c => c.SubCategories).ThenInclude(s => s.Groups)
            .ToListAsync(cancellationToken);
        var counts = await db.Products.AsNoTracking()
            .GroupBy(p => p.ProductGroupId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        var specs = await db.ProductSpecs.AsNoTracking().OrderBy(s => s.SortOrder).Select(s => new { s.Id, s.Code }).ToListAsync(cancellationToken);
        // Template codes in the specs' order; entries of deleted specs drop out.
        List<string> Template(IEnumerable<TemplateSpec> entries)
        {
            var ids = entries.Select(e => e.SpecId).ToHashSet();
            return specs.Where(s => ids.Contains(s.Id)).Select(s => s.Code).ToList();
        }

        var nodes = categories
            .OrderBy(c => c.Name, StringComparer.CurrentCulture)
            .Select(c => new CategoryNode(
                c.Id,
                c.ImageUrl,
                LocalizedTexts.Read(Store, c.Name, c.Slug, null, c.Translations),
                c.SubCategories
                    .OrderBy(s => s.Name, StringComparer.CurrentCulture)
                    .Select(s => new SubCategoryNode(
                        s.Id,
                        LocalizedTexts.Read(Store, s.Name, s.Slug, null, s.Translations),
                        s.Groups
                            .OrderBy(g => g.Name, StringComparer.CurrentCulture)
                            .Select(g => new GroupNode(g.Id, LocalizedTexts.Read(Store, g.Name, null, null, g.Translations), counts.GetValueOrDefault(g.Id)))
                            .ToList(),
                        Template(s.TemplateSpecs)))
                    .ToList(),
                Template(c.TemplateSpecs)))
            .ToList();

        return new CatalogTree(Store.DefaultLocale, Store.LocaleList, nodes);
    }

    public async Task<Result<CatalogNodeView>> CreateCategoryAsync(SaveCatalogNodeRequest request, CancellationToken cancellationToken = default)
    {
        if (LocalizedTexts.Validate(request.Texts, Store) is { } error)
            return error;
        var main = LocalizedTexts.Default(request.Texts, Store.DefaultLocale);
        var slug = LocalizedTexts.SlugFor(main);
        if (await db.Categories.AnyAsync(c => c.Slug == slug, cancellationToken))
            return AppError.Conflict(CatalogErrors.SlugTaken, $"A category with slug {slug} exists.");

        var category = new Category { Name = main.Name, Slug = slug };
        LocalizedTexts.ApplyTranslations(request.Texts, Store.DefaultLocale, category.Translations, withSlug: true, withDescription: false);
        db.Categories.Add(category);
        await db.SaveChangesAsync(cancellationToken);
        return View(category.Id, category.Name, category.Slug, category.Translations);
    }

    public async Task<Result<CatalogNodeView>> UpdateCategoryAsync(Guid id, SaveCatalogNodeRequest request, CancellationToken cancellationToken = default)
    {
        if (LocalizedTexts.Validate(request.Texts, Store) is { } error)
            return error;
        var category = await db.Categories.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (category is null)
            return NotFound();
        var main = LocalizedTexts.Default(request.Texts, Store.DefaultLocale);
        var slug = LocalizedTexts.SlugFor(main);
        if (await db.Categories.AnyAsync(c => c.Slug == slug && c.Id != id, cancellationToken))
            return AppError.Conflict(CatalogErrors.SlugTaken, $"A category with slug {slug} exists.");

        category.Name = main.Name;
        category.Slug = slug;
        LocalizedTexts.ApplyTranslations(request.Texts, Store.DefaultLocale, category.Translations, withSlug: true, withDescription: false);
        await db.SaveChangesAsync(cancellationToken);
        return View(category.Id, category.Name, category.Slug, category.Translations);
    }

    public async Task<Result<bool>> DeleteCategoryAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var category = await db.Categories.Include(c => c.SubCategories).SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (category is null)
            return NotFound();
        if (category.SubCategories.Count > 0)
            return AppError.Conflict(CatalogErrors.HasChildren, "Delete or move its sub-categories first.");
        db.Categories.Remove(category);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<Result<CatalogNodeView>> CreateSubCategoryAsync(Guid categoryId, SaveCatalogNodeRequest request, CancellationToken cancellationToken = default)
    {
        if (LocalizedTexts.Validate(request.Texts, Store) is { } error)
            return error;
        if (!await db.Categories.AnyAsync(c => c.Id == categoryId, cancellationToken))
            return NotFound();
        var main = LocalizedTexts.Default(request.Texts, Store.DefaultLocale);
        var slug = LocalizedTexts.SlugFor(main);
        if (await db.SubCategories.AnyAsync(s => s.CategoryId == categoryId && s.Slug == slug, cancellationToken))
            return AppError.Conflict(CatalogErrors.SlugTaken, $"A sub-category with slug {slug} exists here.");

        var sub = new SubCategory { Name = main.Name, Slug = slug, CategoryId = categoryId };
        LocalizedTexts.ApplyTranslations(request.Texts, Store.DefaultLocale, sub.Translations, withSlug: true, withDescription: false);
        db.SubCategories.Add(sub);
        await db.SaveChangesAsync(cancellationToken);
        return View(sub.Id, sub.Name, sub.Slug, sub.Translations);
    }

    public async Task<Result<CatalogNodeView>> UpdateSubCategoryAsync(Guid id, SaveCatalogNodeRequest request, CancellationToken cancellationToken = default)
    {
        if (LocalizedTexts.Validate(request.Texts, Store) is { } error)
            return error;
        var sub = await db.SubCategories.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (sub is null)
            return NotFound();
        var main = LocalizedTexts.Default(request.Texts, Store.DefaultLocale);
        var slug = LocalizedTexts.SlugFor(main);
        if (await db.SubCategories.AnyAsync(s => s.CategoryId == sub.CategoryId && s.Slug == slug && s.Id != id, cancellationToken))
            return AppError.Conflict(CatalogErrors.SlugTaken, $"A sub-category with slug {slug} exists here.");

        sub.Name = main.Name;
        sub.Slug = slug;
        LocalizedTexts.ApplyTranslations(request.Texts, Store.DefaultLocale, sub.Translations, withSlug: true, withDescription: false);
        await db.SaveChangesAsync(cancellationToken);
        return View(sub.Id, sub.Name, sub.Slug, sub.Translations);
    }

    public async Task<Result<bool>> DeleteSubCategoryAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var sub = await db.SubCategories.Include(s => s.Groups).SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (sub is null)
            return NotFound();
        if (sub.Groups.Count > 0)
            return AppError.Conflict(CatalogErrors.HasChildren, "Delete or move its product groups first.");
        db.SubCategories.Remove(sub);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<Result<CatalogNodeView>> CreateGroupAsync(Guid subCategoryId, SaveCatalogNodeRequest request, CancellationToken cancellationToken = default)
    {
        if (LocalizedTexts.Validate(request.Texts, Store) is { } error)
            return error;
        if (!await db.SubCategories.AnyAsync(s => s.Id == subCategoryId, cancellationToken))
            return NotFound();
        var main = LocalizedTexts.Default(request.Texts, Store.DefaultLocale);

        var group = new ProductGroup { Name = main.Name, SubCategoryId = subCategoryId };
        LocalizedTexts.ApplyTranslations(request.Texts, Store.DefaultLocale, group.Translations, withSlug: false, withDescription: false);
        db.ProductGroups.Add(group);
        await db.SaveChangesAsync(cancellationToken);
        return View(group.Id, group.Name, null, group.Translations);
    }

    public async Task<Result<CatalogNodeView>> UpdateGroupAsync(Guid id, SaveCatalogNodeRequest request, CancellationToken cancellationToken = default)
    {
        if (LocalizedTexts.Validate(request.Texts, Store) is { } error)
            return error;
        var group = await db.ProductGroups.SingleOrDefaultAsync(g => g.Id == id, cancellationToken);
        if (group is null)
            return NotFound();

        group.Name = LocalizedTexts.Default(request.Texts, Store.DefaultLocale).Name;
        LocalizedTexts.ApplyTranslations(request.Texts, Store.DefaultLocale, group.Translations, withSlug: false, withDescription: false);
        await db.SaveChangesAsync(cancellationToken);
        return View(group.Id, group.Name, null, group.Translations);
    }

    public async Task<Result<bool>> DeleteGroupAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var group = await db.ProductGroups.SingleOrDefaultAsync(g => g.Id == id, cancellationToken);
        if (group is null)
            return NotFound();
        if (await db.Products.AnyAsync(p => p.ProductGroupId == id, cancellationToken))
            return AppError.Conflict(CatalogErrors.HasProducts, "Move or delete its products first.");
        db.ProductGroups.Remove(group);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Replaces a category's spec template.</summary>
    public async Task<Result<IReadOnlyList<string>>> SetCategoryTemplateAsync(Guid id, SaveTemplateRequest request, CancellationToken cancellationToken = default)
    {
        var category = await db.Categories.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        return category is null ? NotFound() : await SetTemplateAsync(category.TemplateSpecs, request, cancellationToken);
    }

    /// <summary>Replaces a sub-category's spec template (added to its category's for its products).</summary>
    public async Task<Result<IReadOnlyList<string>>> SetSubCategoryTemplateAsync(Guid id, SaveTemplateRequest request, CancellationToken cancellationToken = default)
    {
        var sub = await db.SubCategories.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        return sub is null ? NotFound() : await SetTemplateAsync(sub.TemplateSpecs, request, cancellationToken);
    }

    private async Task<Result<IReadOnlyList<string>>> SetTemplateAsync(ICollection<TemplateSpec> template, SaveTemplateRequest request, CancellationToken cancellationToken)
    {
        var codes = request.SpecCodes.Distinct(StringComparer.Ordinal).ToList();
        var specs = await db.ProductSpecs.Where(s => codes.Contains(s.Code)).OrderBy(s => s.SortOrder).Select(s => new { s.Id, s.Code }).ToListAsync(cancellationToken);
        if (specs.Count != codes.Count)
            return AppError.Validation(CatalogErrors.SpecNotFound, "Unknown spec in the template.");

        foreach (var entry in template.Where(t => specs.All(s => s.Id != t.SpecId)).ToList())
            template.Remove(entry);
        foreach (var spec in specs.Where(s => template.All(t => t.SpecId != s.Id)))
            template.Add(new TemplateSpec { SpecId = spec.Id });
        await db.SaveChangesAsync(cancellationToken);
        return specs.Select(s => s.Code).ToList();
    }

    private CatalogNodeView View(Guid id, string name, string? slug, IEnumerable<CatalogTranslation> translations) =>
        new(id, LocalizedTexts.Read(Store, name, slug, null, translations));

    private static AppError NotFound() => AppError.NotFound(CatalogErrors.NotFound, "Not found.");
}
