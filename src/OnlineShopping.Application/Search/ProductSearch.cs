using Microsoft.EntityFrameworkCore;
using OnlineShopping.Application.Catalog;
using OnlineShopping.Application.Common;
using OnlineShopping.Application.Seo;

namespace OnlineShopping.Application.Search;

/// <summary>
/// A search engine over the search documents. The default is PostgreSQL full-text search with trigram typo tolerance
/// (Infrastructure); <see cref="SimpleProductSearch"/> works on any database. An external engine is another
/// implementation (<see cref="ISearchEngine"/>, with the database as fallback). Only the matching and ranking live
/// here; prices and stock always come from the database.
/// </summary>
public interface IProductSearch
{
    /// <summary>Matching products of one language with a relevance score (higher is better), the best first.</summary>
    Task<IReadOnlyDictionary<Guid, double>> MatchAsync(SearchTerms terms, string locale, int limit, CancellationToken cancellationToken = default);

    /// <summary>The indexed word closest to a misspelt one, or null when nothing is close.</summary>
    Task<string?> SimilarWordAsync(string word, string locale, CancellationToken cancellationToken = default);
}

/// <summary>
/// Matching in memory over the language's documents: every word must be found (through one of its alternatives) in the
/// title (weight 4), keywords (2), details or codes (1) or description (0.5); a word matches the start of an indexed
/// word, so typing "sams" finds "samsung". No typo tolerance: "did you mean" covers misspellings. Fine for small
/// catalogues and the engine of the unit tests (SQLite).
/// </summary>
public sealed class SimpleProductSearch(IApplicationDbContext db) : IDatabaseProductSearch
{
    public async Task<IReadOnlyDictionary<Guid, double>> MatchAsync(SearchTerms terms, string locale, int limit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(terms);
        if (terms.IsEmpty)
            return new Dictionary<Guid, double>();
        var documents = await db.SearchDocuments.AsNoTracking().Where(d => d.Locale == locale)
            .Select(d => new { d.ProductId, d.Title, d.Keywords, d.Details, d.Codes, d.Description })
            .ToListAsync(cancellationToken);

        var scores = new Dictionary<Guid, double>();
        foreach (var d in documents)
        {
            var parts = new (string Text, double Weight)[] { (d.Title, 4), (d.Keywords ?? "", 2), ($"{d.Details} {d.Codes}", 1), (d.Description ?? "", 0.5) };
            double score = 0;
            foreach (var alternatives in terms.Words)
            {
                var best = alternatives.Max(a => parts.Where(p => StartsAWord(p.Text, a)).Select(p => p.Weight).DefaultIfEmpty(0).Max());
                if (best == 0)
                {
                    score = 0;
                    break;
                }
                score += best;
            }
            if (score > 0)
                scores[d.ProductId] = score;
        }
        return scores.OrderByDescending(s => s.Value).Take(limit).ToDictionary(s => s.Key, s => s.Value);
    }

    public async Task<string?> SimilarWordAsync(string word, string locale, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(word);
        if (word.Length < 3)
            return null;
        var texts = await db.SearchDocuments.AsNoTracking().Where(d => d.Locale == locale).Select(d => d.Title + " " + d.Keywords).ToListAsync(cancellationToken);
        var allowed = word.Length >= 7 ? 2 : 1;
        return texts.SelectMany(t => t.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(w => w.Length > 2 && w != word)
            .Distinct(StringComparer.Ordinal)
            .Select(w => (Word: w, Distance: TextDistance.Levenshtein(w, word)))
            .Where(x => x.Distance <= allowed)
            .OrderBy(x => x.Distance).ThenBy(x => x.Word, StringComparer.Ordinal)
            .Select(x => x.Word)
            .FirstOrDefault();
    }

    /// <summary>Whether the phrase starts a word of the text (both in one spelling).</summary>
    internal static bool StartsAWord(string text, string phrase)
    {
        for (var at = text.IndexOf(phrase, StringComparison.Ordinal); at >= 0; at = text.IndexOf(phrase, at + 1, StringComparison.Ordinal))
        {
            if (at == 0 || text[at - 1] == ' ')
                return true;
        }
        return false;
    }
}

/// <summary>
/// A search that is a product's code: its public number, SKU (a variant's too) or barcode. The storefront then opens
/// that product directly.
/// </summary>
public static class ProductCodeLookup
{
    public static async Task<(Guid Id, long Number)?> FindAsync(IApplicationDbContext db, string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        var code = (text ?? "").Trim();
        if (code.Length is < 3 or > 64 || code.Contains(' ', StringComparison.Ordinal))
            return null;
        var published = db.Products.AsNoTracking().Where(p => p.IsPublished);
        // ToLower() runs in SQL (lower()); the culture rules of the analyzers do not apply.
#pragma warning disable CA1304, CA1311, CA1862
        var lower = code.ToLower();
        var digits = SearchText.Normalize(code);
        var gtin = ProductCodes.NormalizeGtin(code);
        var number = long.TryParse(digits, out var n) && digits.Length <= 15 ? n : -1;
        var hit = await published
            .Where(p => p.PublicNumber == number || p.SKU.ToLower() == lower || (gtin != null && p.Gtin == gtin)
                || p.Variants.Any(v => v.Sku.ToLower() == lower || (gtin != null && v.Gtin == gtin)))
#pragma warning restore CA1304, CA1311, CA1862
            .Select(p => new { p.Id, p.PublicNumber })
            .FirstOrDefaultAsync(cancellationToken);
        return hit is null ? null : (hit.Id, hit.PublicNumber);
    }
}
