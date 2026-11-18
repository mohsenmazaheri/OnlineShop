using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using OnlineShopping.Application.Common;
using OnlineShopping.Domain.Catalog;

namespace OnlineShopping.Application.Catalog;

/// <summary>Which products a list shows.</summary>
public enum ProductList
{
    /// <summary>Products with an old price, the largest discount first.</summary>
    Discounted,

    /// <summary>The most recently added products first.</summary>
    Newest,
}

/// <summary>A product as product cards show it (lists, rails, search results).</summary>
public sealed record ProductCard(
    long Number,
    string Name,
    string Slug,
    string? ImageUrl,
    decimal Price,
    decimal? CompareAtPrice,
    bool InStock);

public sealed record CategoryLink(string Name, string Slug);

/// <summary>
/// One spec line. <paramref name="Value"/> holds text and translated option values; numbers and yes/no stay typed so
/// the storefront formats them for the reader (digits, decimal separator, "Yes"/"No").
/// </summary>
public sealed record ProductSpecLine(
    string Code,
    string Group,
    string Name,
    SpecKind Kind,
    string? Unit,
    decimal? Number,
    bool? Boolean,
    string? Value,
    bool InSummary);

/// <summary>The product's slug in one language (only languages it is really translated into, plus the default).</summary>
public sealed record ProductAlternate(string Locale, string Slug);

public sealed record ProductDetail(
    long Number,
    string Name,
    string Slug,
    string? Description,
    string Sku,
    decimal Price,
    decimal? CompareAtPrice,
    int Stock,
    bool InStock,
    IReadOnlyList<string> Images,
    CategoryLink Category,
    CategoryLink SubCategory,
    IReadOnlyList<ProductSpecLine> Specs,
    IReadOnlyList<ProductAlternate> Alternates);

public interface IProductQueries
{
    /// <summary>Published products for a list, named in <paramref name="locale"/> (default language as fallback).</summary>
    Task<IReadOnlyList<ProductCard>> ListAsync(ProductList list, string locale, int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// One published product by its public number, in <paramref name="locale"/>. <paramref name="defaultLocale"/> is the
    /// language of the product's own columns (listed among the alternates).
    /// </summary>
    Task<Result<ProductDetail>> GetAsync(long number, string locale, string defaultLocale, CancellationToken cancellationToken = default);

    /// <summary>The product's slug in <paramref name="locale"/>: the storefront redirects any other slug to it.</summary>
    Task<Result<string>> GetSlugAsync(long number, string locale, CancellationToken cancellationToken = default);
}

public sealed class ProductQueries(IApplicationDbContext db) : IProductQueries
{
    public const string NotFoundCode = "product.not_found";
    public const int MaxListSize = 48;

    private IQueryable<Product> Published => db.Products.AsNoTracking().Where(p => p.IsPublished);

    private static Expression<Func<Product, ProductCard>> ToCard(string locale) => p => new ProductCard(
        p.PublicNumber,
        p.Translations.Where(t => t.Locale == locale).Select(t => t.Name).FirstOrDefault() ?? p.Name,
        p.Translations.Where(t => t.Locale == locale).Select(t => t.Slug).FirstOrDefault() ?? p.Slug,
        p.Images.OrderBy(i => i.SortOrder).Select(i => i.Url).FirstOrDefault(),
        p.Price,
        p.CompareAtPrice,
        p.Stock > 0);

    public async Task<IReadOnlyList<ProductCard>> ListAsync(ProductList list, string locale, int take, CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, MaxListSize);
        var query = list switch
        {
            // Discount share = (old - new) / old; the largest first, then the newest.
            ProductList.Discounted => Published
                .Where(p => p.CompareAtPrice != null && p.CompareAtPrice > p.Price)
                .OrderByDescending(p => (p.CompareAtPrice!.Value - p.Price) / p.CompareAtPrice.Value)
                .ThenByDescending(p => p.PublicNumber),
            _ => Published.OrderByDescending(p => p.CreatedUtc).ThenByDescending(p => p.PublicNumber),
        };

        return await query.Take(take).Select(ToCard(locale)).ToListAsync(cancellationToken);
    }

    public async Task<Result<ProductDetail>> GetAsync(long number, string locale, string defaultLocale, CancellationToken cancellationToken = default)
    {
        var product = await Published
            .Where(p => p.PublicNumber == number)
            .Select(p => new
            {
                p.Id,
                p.PublicNumber,
                Name = p.Translations.Where(t => t.Locale == locale).Select(t => t.Name).FirstOrDefault() ?? p.Name,
                Slug = p.Translations.Where(t => t.Locale == locale).Select(t => t.Slug).FirstOrDefault() ?? p.Slug,
                Description = p.Translations.Where(t => t.Locale == locale).Select(t => t.Description).FirstOrDefault() ?? p.Description,
                p.SKU,
                p.Price,
                p.CompareAtPrice,
                p.Stock,
                Category = new CategoryLink(
                    p.ProductGroup.SubCategory.Category.Translations.Where(t => t.Locale == locale).Select(t => t.Name).FirstOrDefault()
                        ?? p.ProductGroup.SubCategory.Category.Name,
                    p.ProductGroup.SubCategory.Category.Slug),
                SubCategory = new CategoryLink(
                    p.ProductGroup.SubCategory.Translations.Where(t => t.Locale == locale).Select(t => t.Name).FirstOrDefault()
                        ?? p.ProductGroup.SubCategory.Name,
                    p.ProductGroup.SubCategory.Slug),
                BaseSlug = p.Slug,
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (product is null)
            return AppError.NotFound(NotFoundCode, $"No product with number {number}.");

        // Flat follow-up queries instead of nested collections: no cartesian product, and every provider can run them
        // (nested collections with per-row translation lookups need SQL APPLY/LATERAL, which SQLite lacks).
        var images = await db.ProductImages.AsNoTracking()
            .Where(i => i.ProductId == product.Id)
            .OrderBy(i => i.SortOrder)
            .Select(i => i.Url)
            .ToListAsync(cancellationToken);

        var specs = await db.ProductSpecValues.AsNoTracking()
            .Where(v => v.ProductId == product.Id)
            .OrderBy(v => v.Spec.SortOrder)
            .Select(v => new ProductSpecLine(
                v.Spec.Code,
                v.Spec.Translations.Where(t => t.Locale == locale).Select(t => t.GroupName).FirstOrDefault() ?? v.Spec.GroupName ?? "",
                v.Spec.Translations.Where(t => t.Locale == locale).Select(t => t.Name).FirstOrDefault() ?? v.Spec.Name,
                v.Spec.Kind,
                v.Spec.Translations.Where(t => t.Locale == locale).Select(t => t.Unit).FirstOrDefault() ?? v.Spec.Unit,
                v.NumberValue,
                v.BooleanValue,
                v.Option != null
                    ? v.Option.Translations.Where(t => t.Locale == locale).Select(t => t.Value).FirstOrDefault() ?? v.Option.Value
                    : v.TextValue,
                v.Spec.ShowInSummary))
            .ToListAsync(cancellationToken);

        var translated = await db.Products.AsNoTracking()
            .Where(p => p.Id == product.Id)
            .SelectMany(p => p.Translations)
            .Select(t => new ProductAlternate(t.Locale, t.Slug ?? ""))
            .ToListAsync(cancellationToken);

        // The default language (the product's own columns) is always available.
        var alternates = translated
            .Where(t => t.Locale != defaultLocale)
            .Select(t => t.Slug.Length > 0 ? t : t with { Slug = product.BaseSlug })
            .Prepend(new ProductAlternate(defaultLocale, product.BaseSlug))
            .ToList();
        return new ProductDetail(
            product.PublicNumber, product.Name, product.Slug, product.Description, product.SKU, product.Price,
            product.CompareAtPrice, product.Stock, product.Stock > 0, images, product.Category, product.SubCategory,
            specs, alternates);
    }

    public async Task<Result<string>> GetSlugAsync(long number, string locale, CancellationToken cancellationToken = default)
    {
        var slug = await Published
            .Where(p => p.PublicNumber == number)
            .Select(p => p.Translations.Where(t => t.Locale == locale).Select(t => t.Slug).FirstOrDefault() ?? p.Slug)
            .SingleOrDefaultAsync(cancellationToken);

        return slug is null ? AppError.NotFound(NotFoundCode, $"No product with number {number}.") : slug;
    }
}
