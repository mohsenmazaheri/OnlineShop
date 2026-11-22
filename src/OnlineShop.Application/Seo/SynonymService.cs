using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnlineShop.Application.Common;
using OnlineShop.Domain.Seo;

namespace OnlineShop.Application.Seo;

/// <summary>
/// Search synonyms per language ("گوشی = موبایل = تلفن همراه"). Words are stored normalized (<see cref="SearchText"/>);
/// a word belongs to at most one group per language, so a search never fans out unexpectedly.
/// </summary>
public sealed class SynonymService(IApplicationDbContext db, IOptions<StoreOptions> store)
{
    public const int MaxTerms = 20;
    public const int MaxTermLength = 50;

    public async Task<SynonymList> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await db.SearchSynonyms.AsNoTracking().OrderBy(s => s.Locale).ThenBy(s => s.Terms).ToListAsync(cancellationToken);
        return new SynonymList(store.Value.LocaleList, rows.Select(View).ToList());
    }

    public async Task<Result<SynonymView>> CreateAsync(SaveSynonymRequest request, CancellationToken cancellationToken = default)
    {
        var terms = await ValidateAsync(request, null, cancellationToken);
        if (!terms.IsSuccess)
            return terms.Error!;
        var row = new SearchSynonym { Locale = request.Locale, Terms = string.Join(", ", terms.Value) };
        db.SearchSynonyms.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return View(row);
    }

    public async Task<Result<SynonymView>> UpdateAsync(Guid id, SaveSynonymRequest request, CancellationToken cancellationToken = default)
    {
        var row = await db.SearchSynonyms.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (row is null)
            return NotFound();
        var terms = await ValidateAsync(request, id, cancellationToken);
        if (!terms.IsSuccess)
            return terms.Error!;
        row.Locale = request.Locale;
        row.Terms = string.Join(", ", terms.Value);
        await db.SaveChangesAsync(cancellationToken);
        return View(row);
    }

    public async Task<Result<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await db.SearchSynonyms.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (row is null)
            return NotFound();
        db.SearchSynonyms.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<Result<List<string>>> ValidateAsync(SaveSynonymRequest request, Guid? id, CancellationToken cancellationToken)
    {
        if (!store.Value.LocaleList.Contains(request.Locale, StringComparer.Ordinal))
            return AppError.Validation(SeoErrors.LocaleInvalid, "The shop does not offer this language.");
        var terms = request.Terms.Select(SearchText.Normalize).Where(t => t.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        if (terms.Count < 2)
            return AppError.Validation(SeoErrors.TooFewTerms, "A synonym group needs at least two different words.");
        if (terms.Count > MaxTerms)
            return AppError.Validation(SeoErrors.TooManyTerms, $"At most {MaxTerms} words.");
        if (terms.Any(t => t.Length > MaxTermLength || t.Contains(',', StringComparison.Ordinal)))
            return AppError.Validation(SeoErrors.TermTooLong, $"A word has at most {MaxTermLength} characters and no comma.");

        var others = await db.SearchSynonyms.AsNoTracking()
            .Where(s => s.Locale == request.Locale && s.Id != id)
            .Select(s => s.Terms)
            .ToListAsync(cancellationToken);
        var taken = others.SelectMany(Split).ToHashSet(StringComparer.Ordinal);
        if (terms.FirstOrDefault(taken.Contains) is { } clash)
            return AppError.Conflict(SeoErrors.TermInOtherGroup, $"\"{clash}\" is already in another group.");
        return terms;
    }

    internal static IEnumerable<string> Split(string terms) => terms.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static SynonymView View(SearchSynonym s) => new(s.Id, s.Locale, Split(s.Terms).ToList());

    private static AppError NotFound() => AppError.NotFound(SeoErrors.NotFound, "No such synonym group.");
}
