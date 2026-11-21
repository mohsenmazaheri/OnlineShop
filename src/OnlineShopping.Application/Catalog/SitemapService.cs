using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnlineShopping.Application.Common;

namespace OnlineShopping.Application.Catalog;

/// <summary>Which pages a sitemap lists.</summary>
public enum SitemapKind
{
    Products,
    Categories,
    Brands,
    Landing,
    Posts,
    Pages,
}

/// <summary>A page's address in one language: the path after the locale ("p/1001/orvia-x1").</summary>
public sealed record SitemapLink(string Locale, string Path);

public sealed record SitemapVideo(string Url, string? PosterUrl, string Title, string? Description, double? DurationSeconds);

/// <summary>
/// One page in every language it is really translated into (the default language always), when it last changed, and
/// for products its images and videos (the image and video sitemap extensions).
/// </summary>
public sealed record SitemapEntry(IReadOnlyList<SitemapLink> Links, DateTime ModifiedUtc, IReadOnlyList<string> Images, IReadOnlyList<SitemapVideo> Videos);

/// <summary>
/// What the sitemaps list (docs/SEO-MARKETING.md §6): published products, categories and sub-categories with published
/// products, brands with published products, published landing pages, blog posts and content pages. A language appears
/// only where the page is translated (an untranslated language is noindex and canonical to the default one).
/// </summary>
public sealed class SitemapService(IApplicationDbContext db, IOptions<StoreOptions> store, TimeProvider clock)
{
    /// <summary>Search engines read at most 50,000 addresses per sitemap; one per kind is plenty for a small shop.</summary>
    public const int MaxEntries = 10_000;

    private StoreOptions Store => store.Value;

    public Task<IReadOnlyList<SitemapEntry>> ListAsync(SitemapKind kind, CancellationToken cancellationToken = default) => kind switch
    {
        SitemapKind.Products => ProductsAsync(cancellationToken),
        SitemapKind.Categories => CategoriesAsync(cancellationToken),
        SitemapKind.Brands => BrandsAsync(cancellationToken),
        SitemapKind.Posts => ContentAsync(Domain.Content.ContentKind.Post, "blog/", cancellationToken),
        SitemapKind.Pages => ContentAsync(Domain.Content.ContentKind.Page, "", cancellationToken),
        _ => LandingAsync(cancellationToken),
    };

    private async Task<IReadOnlyList<SitemapEntry>> ProductsAsync(CancellationToken cancellationToken)
    {
        var products = await db.Products.AsNoTracking().Where(p => p.IsPublished)
            .OrderBy(p => p.PublicNumber).Take(MaxEntries)
            .Select(p => new { p.Id, p.PublicNumber, p.Slug, p.Name, p.Description, Modified = p.ModifiedUtc ?? p.CreatedUtc })
            .ToListAsync(cancellationToken);
        var ids = products.Select(p => p.Id).ToList();
        var translations = (await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id))
            .SelectMany(p => p.Translations, (p, t) => new { p.Id, t.Locale, t.Slug })
            .ToListAsync(cancellationToken)).ToLookup(t => t.Id);
        var images = (await db.ProductImages.AsNoTracking().Where(i => ids.Contains(i.ProductId))
            .OrderBy(i => i.SortOrder).Select(i => new { i.ProductId, i.Url }).ToListAsync(cancellationToken)).ToLookup(i => i.ProductId, i => i.Url);
        var videos = (await db.ProductVideos.AsNoTracking().Where(v => ids.Contains(v.ProductId))
            .OrderBy(v => v.SortOrder).Select(v => new { v.ProductId, v.Url, v.PosterUrl, v.Title, v.DurationSeconds }).ToListAsync(cancellationToken))
            .ToLookup(v => v.ProductId);

        return products.Select(p => new SitemapEntry(
            Links(p.Slug, translations[p.Id].Select(t => (t.Locale, t.Slug)), slug => $"p/{p.PublicNumber}/{slug}"),
            p.Modified,
            images[p.Id].ToList(),
            videos[p.Id].Select(v => new SitemapVideo(v.Url, v.PosterUrl ?? images[p.Id].FirstOrDefault(), v.Title ?? p.Name, p.Description, v.DurationSeconds)).ToList()))
            .ToList();
    }

    private async Task<IReadOnlyList<SitemapEntry>> CategoriesAsync(CancellationToken cancellationToken)
    {
        var published = db.Products.Where(p => p.IsPublished);
        var categories = await db.Categories.AsNoTracking()
            .Where(c => published.Any(p => p.ProductGroup.SubCategory.CategoryId == c.Id))
            .Select(c => new { c.Id, c.Slug, Modified = c.ModifiedUtc ?? c.CreatedUtc })
            .ToListAsync(cancellationToken);
        var subs = await db.SubCategories.AsNoTracking()
            .Where(s => published.Any(p => p.ProductGroup.SubCategoryId == s.Id))
            .Select(s => new { s.Id, s.CategoryId, s.Slug, Modified = s.ModifiedUtc ?? s.CreatedUtc })
            .ToListAsync(cancellationToken);
        var categoryIds = categories.Select(c => c.Id).ToList();
        var subIds = subs.Select(s => s.Id).ToList();
        var categoryTexts = (await db.Categories.AsNoTracking().Where(c => categoryIds.Contains(c.Id))
            .SelectMany(c => c.Translations, (c, t) => new { c.Id, t.Locale, t.Slug }).ToListAsync(cancellationToken)).ToLookup(t => t.Id);
        var subTexts = (await db.SubCategories.AsNoTracking().Where(s => subIds.Contains(s.Id))
            .SelectMany(s => s.Translations, (s, t) => new { s.Id, t.Locale, t.Slug }).ToListAsync(cancellationToken)).ToLookup(t => t.Id);

        var entries = categories.Select(c => new SitemapEntry(
            Links(c.Slug, categoryTexts[c.Id].Select(t => (t.Locale, t.Slug)), slug => $"c/{slug}"), c.Modified, [], [])).ToList();
        foreach (var s in subs)
        {
            var category = categories.First(c => c.Id == s.CategoryId);
            string CategorySlug(string locale) => categoryTexts[category.Id].FirstOrDefault(t => t.Locale == locale)?.Slug ?? category.Slug;
            var links = new List<SitemapLink> { new(Store.DefaultLocale, $"c/{category.Slug}/{s.Slug}") };
            links.AddRange(subTexts[s.Id]
                .Where(t => t.Locale != Store.DefaultLocale && Store.LocaleList.Contains(t.Locale, StringComparer.Ordinal))
                .OrderBy(t => Position(t.Locale))
                .Select(t => new SitemapLink(t.Locale, $"c/{CategorySlug(t.Locale)}/{t.Slug ?? s.Slug}")));
            entries.Add(new SitemapEntry(links, s.Modified, [], []));
        }
        return entries;
    }

    private async Task<IReadOnlyList<SitemapEntry>> BrandsAsync(CancellationToken cancellationToken)
    {
        var brands = await db.Brands.AsNoTracking()
            .Where(b => db.Products.Any(p => p.IsPublished && p.BrandId == b.Id))
            .Select(b => new { b.Id, b.Slug, b.LogoUrl, Modified = b.ModifiedUtc ?? b.CreatedUtc })
            .ToListAsync(cancellationToken);
        var ids = brands.Select(b => b.Id).ToList();
        var texts = (await db.Brands.AsNoTracking().Where(b => ids.Contains(b.Id))
            .SelectMany(b => b.Translations, (b, t) => new { b.Id, t.Locale, t.Slug }).ToListAsync(cancellationToken)).ToLookup(t => t.Id);
        return brands.Select(b => new SitemapEntry(
            Links(b.Slug, texts[b.Id].Select(t => (t.Locale, t.Slug)), slug => $"b/{slug}"),
            b.Modified,
            b.LogoUrl is null ? [] : [b.LogoUrl],
            [])).ToList();
    }

    private async Task<IReadOnlyList<SitemapEntry>> LandingAsync(CancellationToken cancellationToken)
    {
        var pages = await db.LandingPages.AsNoTracking().Where(p => p.IsPublished)
            .Select(p => new { p.Id, p.Slug, Modified = p.ModifiedUtc ?? p.CreatedUtc })
            .ToListAsync(cancellationToken);
        var ids = pages.Select(p => p.Id).ToList();
        var texts = (await db.LandingPages.AsNoTracking().Where(p => ids.Contains(p.Id))
            .SelectMany(p => p.Translations, (p, t) => new { p.Id, t.Locale, t.Slug }).ToListAsync(cancellationToken)).ToLookup(t => t.Id);
        return pages.Select(p => new SitemapEntry(Links(p.Slug, texts[p.Id].Select(t => (t.Locale, t.Slug)), slug => $"l/{slug}"), p.Modified, [], [])).ToList();
    }

    /// <summary>Published posts or pages whose time has come (blog/{slug} or {slug}).</summary>
    private async Task<IReadOnlyList<SitemapEntry>> ContentAsync(Domain.Content.ContentKind kind, string prefix, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var pages = await db.ContentPages.AsNoTracking()
            .Where(p => p.Kind == kind && p.IsPublished && (p.PublishedUtc == null || p.PublishedUtc <= now))
            .Select(p => new { p.Slug, p.CoverImageUrl, Modified = p.ModifiedUtc ?? p.CreatedUtc, Translated = p.Translations.Select(t => new { t.Locale, t.Slug }).ToList() })
            .ToListAsync(cancellationToken);
        return pages.Select(p => new SitemapEntry(
            Links(p.Slug, p.Translated.Select(t => (t.Locale, (string?)t.Slug)), slug => $"{prefix}{slug}"),
            p.Modified,
            p.CoverImageUrl is null ? [] : [p.CoverImageUrl],
            [])).ToList();
    }

    /// <summary>Where a language comes in the shop's list (links follow it).</summary>
    private int Position(string locale) => Store.LocaleList.Select((l, i) => (l, i)).FirstOrDefault(x => x.l == locale).i;

    /// <summary>The default language's address and one per translated shop language.</summary>
    private List<SitemapLink> Links(string ownSlug, IEnumerable<(string Locale, string? Slug)> translations, Func<string, string> path)
    {
        var links = new List<SitemapLink> { new(Store.DefaultLocale, path(ownSlug)) };
        links.AddRange(translations
            .Where(t => t.Locale != Store.DefaultLocale && Store.LocaleList.Contains(t.Locale, StringComparer.Ordinal))
            .OrderBy(t => Position(t.Locale))
            .Select(t => new SitemapLink(t.Locale, path(string.IsNullOrEmpty(t.Slug) ? ownSlug : t.Slug))));
        return links;
    }
}
