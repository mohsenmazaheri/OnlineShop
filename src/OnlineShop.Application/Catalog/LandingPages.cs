using FluentValidation;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnlineShop.Application.Catalog.Admin;
using OnlineShop.Application.Catalog.Browse;
using OnlineShop.Application.Common;
using OnlineShop.Domain.Catalog;

namespace OnlineShop.Application.Catalog;

/// <summary>A landing page for the admin: texts per language (name = title, description = intro), search texts, query.</summary>
public sealed record LandingPageView(Guid Id, IReadOnlyList<LocalizedText> Texts, IReadOnlyList<SeoText> Seo, string Query, bool IsPublished);

public sealed record LandingPageList(string DefaultLocale, IReadOnlyList<string> Locales, IReadOnlyList<LandingPageView> Pages);

/// <summary>
/// Create or change a landing page. <paramref name="Query"/> may be the address of a filtered listing (copied from the
/// address bar), its path, or just its query string; prices (min, max) are in the shop's base currency.
/// </summary>
public sealed record SaveLandingPageRequest(IReadOnlyList<LocalizedText> Texts, IReadOnlyList<SeoText>? Seo, string Query, bool IsPublished);

public sealed class SaveLandingPageRequestValidator : AbstractValidator<SaveLandingPageRequest>
{
    public SaveLandingPageRequestValidator()
    {
        RuleFor(r => r.Texts).NotEmpty().WithErrorCode(CatalogErrors.DefaultTextRequired);
        RuleForEach(r => r.Texts).ChildRules(text =>
        {
            text.RuleFor(t => t.Name).MaximumLength(300).WithErrorCode(CatalogErrors.NameTooLong);
            text.RuleFor(t => t.Slug).MaximumLength(200).WithErrorCode(CatalogErrors.NameTooLong);
            text.RuleFor(t => t.Description).MaximumLength(10_000).WithErrorCode(CatalogErrors.NameTooLong);
        });
        RuleFor(r => r.Query).NotEmpty().WithErrorCode(CatalogErrors.LandingQueryInvalid)
            .MaximumLength(2000).WithErrorCode(CatalogErrors.LandingQueryInvalid);
    }
}

/// <summary>The page's slug in one language (only languages it is translated into, plus the default).</summary>
public sealed record LandingAlternate(string Locale, string Slug);

/// <summary>A landing page as the storefront shows it, in the requested language (the default language as fallback).</summary>
public sealed record LandingPageContent(
    string Title,
    string Slug,
    string? Intro,
    string? MetaTitle,
    string? MetaDescription,
    string Query,
    IReadOnlyList<LandingAlternate> Alternates,
    DateTime ModifiedUtc);

/// <summary>Turns what the admin pasted into the browse query a landing page stores.</summary>
public static class LandingQuery
{
    private static readonly string[] Keys = ["c", "s", "b", "q", "brand", "group", "min", "max", "stock", "sale", "sort"];
    private static readonly string[] Prefixes = ["o.", "n.", "y."];

    /// <summary>
    /// "https://shop/fa/c/phones/mobile?brand=orvia&amp;page=2" → "c=phones&amp;s=mobile&amp;brand=orvia": the scope from
    /// the path (/c/{category}[/{sub}], /b/{brand}), the listing's own keys from the query (no page), in a stable order.
    /// Null when nothing usable is left.
    /// </summary>
    public static string? Normalize(string? input)
    {
        var text = (input ?? "").Trim();
        if (text.Length == 0)
            return null;
        string path, query;
        if (Uri.TryCreate(text, UriKind.Absolute, out var url) && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps))
        {
            (path, query) = (url.AbsolutePath, url.Query);
        }
        else
        {
            var at = text.IndexOf('?', StringComparison.Ordinal);
            (path, query) = at >= 0 ? (text[..at], text[at..]) : text.Contains('=', StringComparison.Ordinal) ? ("", text) : (text, "");
        }

        var pairs = new List<KeyValuePair<string, string>>();
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToList();
        var scope = segments.FindIndex(s => s is "c" or "b");
        if (scope >= 0 && scope + 1 < segments.Count)
        {
            pairs.Add(new(segments[scope], segments[scope + 1]));
            if (segments[scope] == "c" && scope + 2 < segments.Count)
                pairs.Add(new("s", segments[scope + 2]));
        }
        foreach (var (key, values) in QueryHelpers.ParseQuery(query))
        {
            var keep = Keys.Contains(key, StringComparer.Ordinal) || Prefixes.Any(p => key.StartsWith(p, StringComparison.Ordinal) && key.Length > p.Length);
            var value = string.Join(',', values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim()));
            if (keep && value.Length > 0 && !pairs.Any(p => p.Key == key))
                pairs.Add(new(key, value));
        }
        if (pairs.Count == 0)
            return null;
        var scopeKeys = new[] { "c", "s", "b", "q" };
        return string.Join('&', pairs
            .OrderBy(p => Array.IndexOf(scopeKeys, p.Key) is var i and >= 0 ? i : 10)
            .ThenBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value).Replace("%2C", ",", StringComparison.Ordinal)}"));
    }

    /// <summary>The stored query as browse parameters.</summary>
    public static IEnumerable<KeyValuePair<string, string?>> Pairs(string query) =>
        QueryHelpers.ParseQuery(query).Select(p => new KeyValuePair<string, string?>(p.Key, p.Value.ToString()));
}

/// <summary>
/// Landing pages (content.manage): clean, indexed addresses for much-searched combinations, each listing the products
/// of a fixed browse query with its own title and text per language. The query is checked by running it (an unknown
/// category or brand is refused); slugs are unique per language.
/// </summary>
public sealed class LandingPageService(IApplicationDbContext db, IProductBrowse browse, IOptions<StoreOptions> store)
{
    private StoreOptions Store => store.Value;

    public async Task<LandingPageList> ListAsync(CancellationToken cancellationToken = default)
    {
        var pages = await db.LandingPages.AsNoTracking().OrderBy(p => p.Title).ToListAsync(cancellationToken);
        return new LandingPageList(Store.DefaultLocale, Store.LocaleList, pages.Select(View).ToList());
    }

    public async Task<Result<LandingPageView>> CreateAsync(SaveLandingPageRequest request, CancellationToken cancellationToken = default)
    {
        var page = new LandingPage { Title = "", Slug = "", Query = "" };
        var applied = await ApplyAsync(page, request, cancellationToken);
        if (!applied.IsSuccess)
            return applied.Error!;
        db.LandingPages.Add(page);
        await db.SaveChangesAsync(cancellationToken);
        return View(page);
    }

    public async Task<Result<LandingPageView>> UpdateAsync(Guid id, SaveLandingPageRequest request, CancellationToken cancellationToken = default)
    {
        var page = await db.LandingPages.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
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
        var page = await db.LandingPages.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (page is null)
            return NotFound();
        db.LandingPages.Remove(page);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>A published page by its slug in any language, shown in <paramref name="locale"/>.</summary>
    public async Task<Result<LandingPageContent>> GetAsync(string slug, string locale, CancellationToken cancellationToken = default)
    {
        var page = await db.LandingPages.AsNoTracking()
            .Where(p => p.IsPublished && (p.Slug == slug || p.Translations.Any(t => t.Slug == slug)))
            .FirstOrDefaultAsync(cancellationToken);
        if (page is null)
            return NotFound();
        var own = locale == Store.DefaultLocale ? null : page.Translations.FirstOrDefault(t => t.Locale == locale);
        var alternates = page.Translations
            .Where(t => t.Locale != Store.DefaultLocale && Store.LocaleList.Contains(t.Locale, StringComparer.Ordinal))
            .Select(t => new LandingAlternate(t.Locale, t.Slug ?? page.Slug))
            .Prepend(new LandingAlternate(Store.DefaultLocale, page.Slug))
            .ToList();
        return new LandingPageContent(
            own?.Name ?? page.Title,
            own?.Slug ?? page.Slug,
            own is null ? page.Intro : own.Description,
            own is null ? page.MetaTitle : own.MetaTitle,
            own is null ? page.MetaDescription : own.MetaDescription,
            page.Query,
            alternates,
            page.ModifiedUtc ?? page.CreatedUtc);
    }

    private async Task<Result<bool>> ApplyAsync(LandingPage page, SaveLandingPageRequest request, CancellationToken cancellationToken)
    {
        if (LocalizedTexts.Validate(request.Texts, Store) is { } invalid)
            return invalid;
        var query = LandingQuery.Normalize(request.Query);
        if (query is null || !(await browse.BrowseAsync(BrowseQueryParser.Parse(LandingQuery.Pairs(query)), Store.DefaultLocale, cancellationToken)).IsSuccess)
            return AppError.Validation(CatalogErrors.LandingQueryInvalid, "The query does not describe a listing of this shop.");

        var slugs = request.Texts.Where(t => !string.IsNullOrWhiteSpace(t.Name)).Select(LocalizedTexts.SlugFor).ToList();
        var others = await db.LandingPages.AsNoTracking().Where(p => p.Id != page.Id)
            .Select(p => new { p.Slug, Translated = p.Translations.Select(t => t.Slug).ToList() })
            .ToListAsync(cancellationToken);
        if (others.Any(o => slugs.Contains(o.Slug) || o.Translated.Any(s => s != null && slugs.Contains(s))))
            return AppError.Conflict(CatalogErrors.LandingSlugTaken, "Another landing page uses this address.");

        var main = LocalizedTexts.Default(request.Texts, Store.DefaultLocale);
        page.Title = main.Name;
        page.Slug = LocalizedTexts.SlugFor(main);
        page.Intro = main.Description;
        page.Query = query;
        page.IsPublished = request.IsPublished;
        LocalizedTexts.ApplyTranslations(request.Texts, Store.DefaultLocale, page.Translations, withSlug: true, withDescription: true);
        foreach (var seo in request.Seo ?? [])
        {
            var title = Clean(seo.MetaTitle, 200);
            var description = Clean(seo.MetaDescription, 400);
            if (seo.Locale == Store.DefaultLocale)
                (page.MetaTitle, page.MetaDescription) = (title, description);
            else if (page.Translations.FirstOrDefault(t => t.Locale == seo.Locale) is { } translation)
                (translation.MetaTitle, translation.MetaDescription) = (title, description);
        }
        return true;
    }

    private LandingPageView View(LandingPage p) => new(
        p.Id,
        LocalizedTexts.Read(Store, p.Title, p.Slug, p.Intro, p.Translations),
        Store.LocaleList
            .OrderBy(l => l == Store.DefaultLocale ? 0 : 1)
            .Select(l => l == Store.DefaultLocale
                ? new SeoText(l, p.MetaTitle, p.MetaDescription, null)
                : p.Translations.FirstOrDefault(t => t.Locale == l) is { } t
                    ? new SeoText(l, t.MetaTitle, t.MetaDescription, null)
                    : new SeoText(l, null, null, null))
            .ToList(),
        p.Query,
        p.IsPublished);

    private static string? Clean(string? text, int max)
    {
        var trimmed = text?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.Length > max ? trimmed[..max] : trimmed;
    }

    private static AppError NotFound() => AppError.NotFound(CatalogErrors.LandingNotFound, "No such landing page.");
}
