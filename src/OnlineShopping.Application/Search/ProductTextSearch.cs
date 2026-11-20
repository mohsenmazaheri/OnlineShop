using Microsoft.EntityFrameworkCore;
using OnlineShopping.Application.Common;
using OnlineShopping.Application.Seo;
using OnlineShopping.Domain.Search;

namespace OnlineShopping.Application.Search;

/// <summary>What a text search found: scores, and the help shown with the results.</summary>
/// <param name="Scores">Matching products with their relevance.</param>
/// <param name="ExactNumber">The search was a product's code (number, SKU, barcode): the shop opens it directly.</param>
/// <param name="CorrectedText">Nothing matched as typed, so these words (the other keyboard layout) were searched.</param>
/// <param name="DidYouMean">Nothing matched: the closest words the shop knows, offered as a link.</param>
public sealed record TextSearchOutcome(IReadOnlyDictionary<Guid, double> Scores, long? ExactNumber, string? CorrectedText, string? DidYouMean);

/// <summary>
/// One search as the shop runs it: a product code opens the product; otherwise the words (with the shop's synonyms) go
/// to the engine; with no result, the text is tried as typed on the other keyboard layout, then the closest known
/// words are offered ("did you mean").
/// </summary>
public sealed class ProductTextSearch(IApplicationDbContext db, IProductSearch engine)
{
    public const int MaxResults = 1000;

    public async Task<TextSearchOutcome> SearchAsync(string text, string locale, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        var exact = await ProductCodeLookup.FindAsync(db, text, cancellationToken);
        var terms = await SearchTerms.BuildAsync(db, text, locale, cancellationToken);
        var scores = new Dictionary<Guid, double>(await engine.MatchAsync(terms, locale, MaxResults, cancellationToken));
        if (exact is { } hit)
            scores[hit.Id] = scores.Values.DefaultIfEmpty(0).Max() + 1;
        if (scores.Count > 0)
            return new TextSearchOutcome(scores, exact?.Number, null, null);

        // From the text as typed: normalizing drops punctuation, and "," is the Persian «و» key.
        foreach (var alternative in KeyboardLayouts.Alternatives(text.Trim()).Select(SearchText.Normalize).Where(a => a.Length > 0 && a != terms.Text).Distinct(StringComparer.Ordinal))
        {
            var retried = await engine.MatchAsync(await SearchTerms.BuildAsync(db, alternative, locale, cancellationToken), locale, MaxResults, cancellationToken);
            if (retried.Count > 0)
                return new TextSearchOutcome(retried, null, alternative, null);
        }

        var corrected = new List<string>();
        foreach (var word in terms.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            corrected.Add(await engine.SimilarWordAsync(word, locale, cancellationToken) ?? word);
        var suggestion = string.Join(' ', corrected);
        return new TextSearchOutcome(scores, null, null, suggestion != terms.Text && suggestion.Length > 0 ? suggestion : null);
    }
}

/// <summary>
/// Counts searches per phrase and language, and those that found nothing (suggestions offer popular phrases; the admin
/// sees what customers could not find). A lost count under a race is harmless.
/// </summary>
public sealed class SearchTermLog(IApplicationDbContext db, TimeProvider clock)
{
    public async Task RecordAsync(string locale, string text, bool found, CancellationToken cancellationToken = default)
    {
        var term = SearchText.Normalize(text);
        if (term.Length is 0 or > 100)
            return;
        var now = clock.GetUtcNow().UtcDateTime;
        var zero = found ? 0 : 1;
        var updated = await db.SearchTermStats
            .Where(t => t.Locale == locale && t.Term == term)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Searches, t => t.Searches + 1)
                .SetProperty(t => t.ZeroResults, t => t.ZeroResults + zero)
                .SetProperty(t => t.LastSearchedUtc, now), cancellationToken);
        if (updated > 0)
            return;
        db.SearchTermStats.Add(new SearchTermStat { Locale = locale, Term = term, Searches = 1, ZeroResults = zero, LastSearchedUtc = now });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Someone recorded the same phrase at the same moment: theirs counts.
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }
}
