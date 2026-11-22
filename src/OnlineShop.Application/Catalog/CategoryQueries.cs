using System.Globalization;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Application.Common;
using OnlineShop.Domain.Catalog;

namespace OnlineShop.Application.Catalog;

public sealed record SubCategorySummary(Guid Id, string Name, string Slug);

/// <param name="ImageUrl">Tile image (site-relative path or absolute URL); null when the category has none.</param>
public sealed record CategorySummary(Guid Id, string Name, string Slug, string? ImageUrl, IReadOnlyList<SubCategorySummary> SubCategories);

public interface ICategoryQueries
{
    /// <summary>
    /// All live categories with their live sub-categories, ordered by name, for menus and the home page. Names are in
    /// <paramref name="locale"/> where translated, otherwise in the default language (null: default language).
    /// </summary>
    Task<IReadOnlyList<CategorySummary>> GetMenuAsync(string? locale = null, CancellationToken cancellationToken = default);

    Task<Result<CategorySummary>> GetBySlugAsync(string slug, string? locale = null, CancellationToken cancellationToken = default);
}

public sealed class CategoryQueries(IApplicationDbContext db) : ICategoryQueries
{
    public const string NotFoundCode = "category.not_found";
    public const string SlugRequiredCode = "category.slug_required";

    // Translated to SQL: selects only the needed columns, and the name in the requested language with a fallback to
    // the item's own (default-language) name. Soft-deleted rows (also in the SubCategories navigation) are removed by
    // the global query filter.
    private static Expression<Func<Category, CategorySummary>> ToSummary(string? locale) => c => new CategorySummary(
        c.Id,
        c.Translations.Where(t => t.Locale == locale).Select(t => t.Name).FirstOrDefault() ?? c.Name,
        // The slug of the reader's language, as the category page address uses (/{locale}/c/{slug}).
        c.Translations.Where(t => t.Locale == locale).Select(t => t.Slug).FirstOrDefault() ?? c.Slug,
        c.ImageUrl,
        c.SubCategories
            .Select(s => new SubCategorySummary(
                s.Id,
                s.Translations.Where(t => t.Locale == locale).Select(t => t.Name).FirstOrDefault() ?? s.Name,
                s.Translations.Where(t => t.Locale == locale).Select(t => t.Slug).FirstOrDefault() ?? s.Slug))
            .ToList());

    public async Task<IReadOnlyList<CategorySummary>> GetMenuAsync(string? locale = null, CancellationToken cancellationToken = default)
    {
        var categories = await db.Categories
            .AsNoTracking()
            .Select(ToSummary(locale))
            .ToListAsync(cancellationToken);

        // Sorted here (a short list): by the translated names, in the reader's alphabet.
        var byName = Comparer(locale);
        return categories
            .Select(c => c with { SubCategories = c.SubCategories.OrderBy(s => s.Name, byName).ToList() })
            .OrderBy(c => c.Name, byName)
            .ToList();
    }

    public async Task<Result<CategorySummary>> GetBySlugAsync(string slug, string? locale = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return AppError.Validation(SlugRequiredCode, "A category slug is required.");

        var category = await db.Categories
            .AsNoTracking()
            .Where(c => c.Slug == slug)
            .Select(ToSummary(locale))
            .SingleOrDefaultAsync(cancellationToken);

        return category is null
            ? AppError.NotFound(NotFoundCode, $"No category with slug '{slug}'.")
            : category with { SubCategories = category.SubCategories.OrderBy(s => s.Name, Comparer(locale)).ToList() };
    }

    private static StringComparer Comparer(string? locale)
    {
        try
        {
            return StringComparer.Create(CultureInfo.GetCultureInfo(locale ?? ""), ignoreCase: true);
        }
        catch (CultureNotFoundException)
        {
            return StringComparer.OrdinalIgnoreCase;
        }
    }
}
