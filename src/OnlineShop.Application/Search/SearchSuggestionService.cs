using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnlineShop.Application.Catalog;
using OnlineShop.Application.Common;
using OnlineShop.Application.Seo;

namespace OnlineShop.Application.Search;

/// <summary>A place to go from the suggestions: a category or a brand, with its path after the language ("c/phones").</summary>
public sealed record SuggestedLink(string Name, string Path);

public sealed record SearchSuggestions(
    IReadOnlyList<ProductCard> Products,
    IReadOnlyList<SuggestedLink> Categories,
    IReadOnlyList<SuggestedLink> Brands,
    IReadOnlyList<string> Terms);

/// <summary>
/// Suggestions while typing (from two characters): the best matching products (with image and price), categories and
/// brands whose name matches, and phrases other customers searched that found something. Kept small and fast.
/// </summary>
public sealed class SearchSuggestionService(IApplicationDbContext db, IProductSearch engine, IProductQueries cards, IOptions<StoreOptions> store)
{
    public const int MaxProducts = 6;
    public const int MaxLinks = 4;

    public async Task<SearchSuggestions> SuggestAsync(string text, string? locale, CancellationToken cancellationToken = default)
    {
        var language = store.Value.ContentLocale(locale);
        var normalized = SearchText.Normalize(text ?? "");
        if (normalized.Length < 2 || normalized.Length > 100)
            return new SearchSuggestions([], [], [], []);

        var terms = await SearchTerms.BuildAsync(db, normalized, language, cancellationToken);
        var scores = await engine.MatchAsync(terms, language, MaxProducts, cancellationToken);
        var products = await cards.CardsAsync(scores.OrderByDescending(s => s.Value).Select(s => s.Key).ToList(), language, cancellationToken);

        var categories = (await db.Categories.AsNoTracking()
                .Select(c => new
                {
                    Name = c.Translations.Where(t => t.Locale == language).Select(t => t.Name).FirstOrDefault() ?? c.Name,
                    Slug = c.Translations.Where(t => t.Locale == language).Select(t => t.Slug).FirstOrDefault() ?? c.Slug,
                })
                .ToListAsync(cancellationToken))
            .Select(c => new SuggestedLink(c.Name, $"c/{c.Slug}"))
            .Concat((await db.SubCategories.AsNoTracking()
                    .Select(s => new
                    {
                        Name = s.Translations.Where(t => t.Locale == language).Select(t => t.Name).FirstOrDefault() ?? s.Name,
                        Slug = s.Translations.Where(t => t.Locale == language).Select(t => t.Slug).FirstOrDefault() ?? s.Slug,
                        Category = s.Category.Translations.Where(t => t.Locale == language).Select(t => t.Slug).FirstOrDefault() ?? s.Category.Slug,
                    })
                    .ToListAsync(cancellationToken))
                .Select(s => new SuggestedLink(s.Name, $"c/{s.Category}/{s.Slug}")));
        var brands = (await db.Brands.AsNoTracking()
                .Select(b => new
                {
                    Name = b.Translations.Where(t => t.Locale == language).Select(t => t.Name).FirstOrDefault() ?? b.Name,
                    Slug = b.Translations.Where(t => t.Locale == language).Select(t => t.Slug).FirstOrDefault() ?? b.Slug,
                })
                .ToListAsync(cancellationToken))
            .Select(b => new SuggestedLink(b.Name, $"b/{b.Slug}"));

        var popular = await db.SearchTermStats.AsNoTracking()
            .Where(t => t.Locale == language && t.Term.StartsWith(normalized) && t.ZeroResults < t.Searches)
            .OrderByDescending(t => t.Searches)
            .Select(t => t.Term)
            .Take(5)
            .ToListAsync(cancellationToken);

        bool Matches(SuggestedLink link) => SearchText.Normalize(link.Name).Split(' ').Any(w => w.StartsWith(normalized, StringComparison.Ordinal))
            || SearchText.Normalize(link.Name).StartsWith(normalized, StringComparison.Ordinal);
        return new SearchSuggestions(
            products,
            categories.Where(Matches).Take(MaxLinks).ToList(),
            brands.Where(Matches).Take(MaxLinks).ToList(),
            popular.Where(t => t != normalized).ToList());
    }
}
