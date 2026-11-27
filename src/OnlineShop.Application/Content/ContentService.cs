using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnlineShop.Application.Catalog;
using OnlineShop.Application.Common;
using OnlineShop.Application.Media;
using OnlineShop.Domain.Content;

namespace OnlineShop.Application.Content;

/// <summary>
/// The blog and the content pages (content.manage). Texts per language (translated by hand; a language without a title
/// falls back to the default one and is not indexed there), slugs unique per kind in every language, and page slugs
/// that never shadow the shop's own addresses. Only published content whose publication time has come is shown.
/// </summary>
public sealed class ContentService(IApplicationDbContext db, IProductQueries products, MediaService media, TimeProvider clock, IOptions<StoreOptions> store)
{
    public const int PageSize = 12;

    /// <summary>First path segments of the shop's own pages: a content page can never use them.</summary>
    public static readonly IReadOnlySet<string> ReservedSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "account", "admin", "api", "b", "blog", "c", "cart", "checkout", "compare", "currency", "l", "offline", "order", "p", "search",
        "sitemap", "sitemaps", "manifest", "robots", "icon", "apple-icon", "sw",
    };

    private StoreOptions Store => store.Value;

    public async Task<ContentList> ListAsync(CancellationToken cancellationToken = default)
    {
        var items = await db.ContentPages.AsNoTracking().OrderBy(p => p.Kind).ThenByDescending(p => p.PublishedUtc ?? p.CreatedUtc).ToListAsync(cancellationToken);
        return new ContentList(Store.DefaultLocale, Store.LocaleList, items.Select(View).ToList());
    }

    public async Task<Result<ContentView>> CreateAsync(SaveContentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var page = new ContentPage { Kind = request.Kind, Slug = "", Title = "", Body = "" };
        var applied = await ApplyAsync(page, request, cancellationToken);
        if (!applied.IsSuccess)
            return applied.Error!;
        db.ContentPages.Add(page);
        await db.SaveChangesAsync(cancellationToken);
        return View(page);
    }

    public async Task<Result<ContentView>> UpdateAsync(Guid id, SaveContentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var page = await db.ContentPages.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (page is null)
            return NotFound();
        var applied = await ApplyAsync(page, request, cancellationToken);
        if (!applied.IsSuccess)
            return applied.Error!;
        await db.SaveChangesAsync(cancellationToken);
        return View(page);
    }

    public async Task<Result<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var page = await db.ContentPages.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (page is null)
            return NotFound();
        var cover = page.CoverImageUrl;
        db.ContentPages.Remove(page);
        await db.SaveChangesAsync(cancellationToken);
        await media.DeleteAsync(cover, cancellationToken);
        return true;
    }

    /// <summary>Replaces the cover image (re-encoded like every upload; the old files are deleted).</summary>
    public async Task<Result<string>> SetCoverAsync(Guid id, Stream upload, long length, CancellationToken cancellationToken = default)
    {
        var page = await db.ContentPages.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (page is null)
            return NotFound();
        var stored = await media.SaveImageAsync(upload, length, cancellationToken);
        if (!stored.IsSuccess)
            return stored.Error!;
        var old = page.CoverImageUrl;
        page.CoverImageUrl = stored.Value.Url;
        await db.SaveChangesAsync(cancellationToken);
        await media.DeleteAsync(old, cancellationToken);
        return page.CoverImageUrl;
    }

    public async Task<Result<bool>> RemoveCoverAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var page = await db.ContentPages.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (page is null)
            return NotFound();
        var old = page.CoverImageUrl;
        page.CoverImageUrl = null;
        await db.SaveChangesAsync(cancellationToken);
        await media.DeleteAsync(old, cancellationToken);
        return true;
    }

    /// <summary>A published post or page by its slug in any language, shown in <paramref name="locale"/>.</summary>
    public async Task<Result<ContentArticle>> GetAsync(ContentKind kind, string slug, string locale, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var page = await Published(now).Where(p => p.Kind == kind && (p.Slug == slug || p.Translations.Any(t => t.Slug == slug)))
            .FirstOrDefaultAsync(cancellationToken);
        if (page is null)
            return NotFound();
        var own = Own(page, locale);
        var numbers = Numbers(page.RelatedProducts);
        var ids = await db.Products.Where(p => numbers.Contains(p.PublicNumber)).Select(p => new { p.Id, p.PublicNumber }).ToListAsync(cancellationToken);
        var related = await products.CardsAsync(ids.OrderBy(p => numbers.IndexOf(p.PublicNumber)).Select(p => p.Id).ToList(), locale, cancellationToken);
        return new ContentArticle(
            page.Kind,
            own?.Title ?? page.Title,
            own?.Slug ?? page.Slug,
            own is null ? page.Summary : own.Summary,
            own?.Body ?? page.Body,
            own is null ? page.MetaTitle : own.MetaTitle,
            own is null ? page.MetaDescription : own.MetaDescription,
            page.CoverImageUrl,
            page.AuthorName,
            SplitTags(own is null ? page.Tags : own.Tags),
            page.PublishedUtc ?? page.CreatedUtc,
            page.ModifiedUtc ?? page.CreatedUtc,
            Alternates(page),
            related);
    }

    /// <summary>A page of published posts in <paramref name="locale"/>, newest first; <paramref name="tag"/> narrows them.</summary>
    public async Task<PostList> ListPostsAsync(string locale, int page, string? tag, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var posts = (await Published(now).Where(p => p.Kind == ContentKind.Post).ToListAsync(cancellationToken))
            .Select(p => (Post: p, Own: Own(p, locale)))
            .Select(x => new PostSummary(
                x.Own?.Title ?? x.Post.Title,
                x.Own?.Slug ?? x.Post.Slug,
                x.Own is null ? x.Post.Summary : x.Own.Summary,
                x.Post.CoverImageUrl,
                x.Post.PublishedUtc ?? x.Post.CreatedUtc,
                SplitTags(x.Own is null ? x.Post.Tags : x.Own.Tags)))
            .OrderByDescending(p => p.PublishedUtc)
            .ToList();
        var tags = posts.SelectMany(p => p.Tags).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var chosen = string.IsNullOrWhiteSpace(tag) ? posts : posts.Where(p => p.Tags.Contains(tag.Trim(), StringComparer.Ordinal)).ToList();
        page = Math.Max(1, page);
        return new PostList(chosen.Skip((page - 1) * PageSize).Take(PageSize).ToList(), page, PageSize, chosen.Count, tags);
    }

    /// <summary>Published pages marked for the footer, in <paramref name="locale"/>.</summary>
    public async Task<IReadOnlyList<FooterPage>> FooterAsync(string locale, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var pages = await Published(now).Where(p => p.Kind == ContentKind.Page && p.ShowInFooter).OrderBy(p => p.Title).ToListAsync(cancellationToken);
        return pages.Select(p => Own(p, locale) is { } own ? new FooterPage(own.Title, own.Slug) : new FooterPage(p.Title, p.Slug)).ToList();
    }

    private IQueryable<ContentPage> Published(DateTime now) =>
        db.ContentPages.AsNoTracking().Where(p => p.IsPublished && (p.PublishedUtc == null || p.PublishedUtc <= now));

    private ContentTranslation? Own(ContentPage page, string locale) =>
        locale == Store.DefaultLocale ? null : page.Translations.FirstOrDefault(t => t.Locale == locale);

    private List<ContentAlternate> Alternates(ContentPage page) => page.Translations
        .Where(t => t.Locale != Store.DefaultLocale && Store.LocaleList.Contains(t.Locale, StringComparer.Ordinal))
        .OrderBy(t => Store.LocaleList.Select((l, i) => (l, i)).First(x => x.l == t.Locale).i)
        .Select(t => new ContentAlternate(t.Locale, t.Slug))
        .Prepend(new ContentAlternate(Store.DefaultLocale, page.Slug))
        .ToList();

    private async Task<Result<bool>> ApplyAsync(ContentPage page, SaveContentRequest request, CancellationToken cancellationToken)
    {
        if (request.Texts.Any(t => !Store.LocaleList.Contains(t.Locale, StringComparer.Ordinal))
            || request.Texts.Select(t => t.Locale).Distinct(StringComparer.Ordinal).Count() != request.Texts.Count)
            return AppError.Validation(ContentErrors.LocaleInvalid, "A text is in a language the shop does not offer, or twice.");
        var main = request.Texts.FirstOrDefault(t => t.Locale == Store.DefaultLocale);
        if (string.IsNullOrWhiteSpace(main?.Title))
            return AppError.Validation(ContentErrors.TitleRequired, $"A title in {Store.DefaultLocale} is required.");
        if (string.IsNullOrWhiteSpace(main.Body))
            return AppError.Validation(ContentErrors.BodyRequired, "The text is required.");

        var texts = request.Texts.Where(t => !string.IsNullOrWhiteSpace(t.Title))
            .Select(t => (Text: t, Slug: Slugs.From(string.IsNullOrWhiteSpace(t.Slug) ? t.Title : t.Slug) is { Length: > 0 } s ? s : Slugs.From(t.Title)))
            .ToList();
        if (request.Kind == ContentKind.Page && texts.Any(t => ReservedSlugs.Contains(t.Slug)))
            return AppError.Validation(ContentErrors.SlugReserved, "This address belongs to the shop itself.");
        var slugs = texts.Select(t => t.Slug).ToList();
        var others = await db.ContentPages.AsNoTracking().Where(p => p.Id != page.Id && p.Kind == request.Kind)
            .Select(p => new { p.Slug, Translated = p.Translations.Select(t => t.Slug).ToList() }).ToListAsync(cancellationToken);
        if (others.Any(o => slugs.Contains(o.Slug) || o.Translated.Any(slugs.Contains)))
            return AppError.Conflict(ContentErrors.SlugTaken, "Another post or page uses this address.");

        page.Kind = request.Kind;
        foreach (var (text, slug) in texts)
        {
            if (text.Locale == Store.DefaultLocale)
            {
                page.Title = text.Title.Trim();
                page.Slug = slug;
                page.Summary = Clean(text.Summary);
                page.Body = text.Body.Trim();
                page.MetaTitle = Clean(text.MetaTitle);
                page.MetaDescription = Clean(text.MetaDescription);
                page.Tags = CleanTags(text.Tags);
                continue;
            }
            var target = page.Translations.FirstOrDefault(t => t.Locale == text.Locale);
            if (target is null)
            {
                target = new ContentTranslation { Locale = text.Locale, Title = "", Slug = "", Body = "" };
                page.Translations.Add(target);
            }
            target.Title = text.Title.Trim();
            target.Slug = slug;
            target.Summary = Clean(text.Summary);
            // An untranslated text keeps the default language's body.
            target.Body = string.IsNullOrWhiteSpace(text.Body) ? page.Body : text.Body.Trim();
            target.MetaTitle = Clean(text.MetaTitle);
            target.MetaDescription = Clean(text.MetaDescription);
            target.Tags = CleanTags(text.Tags);
        }
        foreach (var gone in page.Translations.Where(t => !texts.Any(x => x.Text.Locale == t.Locale)).ToList())
            page.Translations.Remove(gone);

        page.AuthorName = Clean(request.AuthorName);
        page.RelatedProducts = request.RelatedProducts.Count == 0 ? null : string.Join(',', request.RelatedProducts.Where(n => n > 0).Distinct().Take(12));
        page.ShowInFooter = request.Kind == ContentKind.Page && request.ShowInFooter;
        page.IsPublished = request.IsPublished;
        page.PublishedUtc = request.PublishedUtc?.ToUniversalTime() ?? page.PublishedUtc ?? (request.IsPublished ? clock.GetUtcNow().UtcDateTime : null);
        return true;
    }

    private ContentView View(ContentPage p) => new(
        p.Id,
        p.Kind,
        Store.LocaleList
            .OrderBy(l => l == Store.DefaultLocale ? 0 : 1)
            .Select(l => l == Store.DefaultLocale
                ? new ContentText(l, p.Title, p.Slug, p.Summary, p.Body, p.MetaTitle, p.MetaDescription, p.Tags)
                : p.Translations.FirstOrDefault(t => t.Locale == l) is { } t
                    ? new ContentText(l, t.Title, t.Slug, t.Summary, t.Body, t.MetaTitle, t.MetaDescription, t.Tags)
                    : new ContentText(l, "", null, null, "", null, null, null))
            .ToList(),
        p.AuthorName,
        Numbers(p.RelatedProducts),
        p.IsPublished,
        p.PublishedUtc,
        p.ShowInFooter,
        p.CoverImageUrl,
        p.ModifiedUtc ?? p.CreatedUtc);

    private static List<long> Numbers(string? text) =>
        (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(n => long.TryParse(n, out var v) ? v : 0).Where(n => n > 0).ToList();

    private static List<string> SplitTags(string? tags) =>
        // Persian and Arabic commas separate tags too.
        (tags ?? "").Split([',', '،'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal).ToList();

    private static string? CleanTags(string? tags) => SplitTags(tags) is { Count: > 0 } list ? string.Join(", ", list) : null;

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static AppError NotFound() => AppError.NotFound(ContentErrors.NotFound, "No such post or page.");
}
