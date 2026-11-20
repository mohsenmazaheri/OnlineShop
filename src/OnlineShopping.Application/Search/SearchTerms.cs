using Microsoft.EntityFrameworkCore;
using OnlineShopping.Application.Common;
using OnlineShopping.Application.Seo;

namespace OnlineShopping.Application.Search;

/// <summary>
/// A search phrase ready for an engine: in one spelling, split into words, each word with its alternatives (itself and
/// the synonyms of its group; an alternative may be a phrase of several words). A product matches when every word
/// matches through one of its alternatives.
/// </summary>
public sealed record SearchTerms(string Text, IReadOnlyList<IReadOnlyList<string>> Words)
{
    public bool IsEmpty => Words.Count == 0;

    /// <summary>
    /// The phrase with the shop's synonyms of that language. Neighbouring words that together are a synonym ("تلفن
    /// همراه") count as one word, so a phrase synonym also finds its single-word partners.
    /// </summary>
    public static async Task<SearchTerms> BuildAsync(IApplicationDbContext db, string text, string locale, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        var normalized = SearchText.Normalize(text);
        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(12).ToList();
        if (words.Count == 0)
            return new SearchTerms("", []);
        var groups = (await db.SearchSynonyms.AsNoTracking().Where(s => s.Locale == locale).Select(s => s.Terms).ToListAsync(cancellationToken))
            .Select(t => t.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToList();
        return From(normalized, words, groups);
    }

    /// <summary>The same without the database (the synonym groups given), for engines and tests.</summary>
    public static SearchTerms From(string normalized, IReadOnlyList<string> words, IReadOnlyList<string[]> groups)
    {
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(groups);
        var slots = new List<IReadOnlyList<string>>();
        for (var i = 0; i < words.Count;)
        {
            // The longest run of up to three words that is a synonym term wins; otherwise the single word.
            var matched = false;
            for (var length = Math.Min(3, words.Count - i); length >= 1 && !matched; length--)
            {
                var phrase = string.Join(' ', words.Skip(i).Take(length));
                var group = groups.FirstOrDefault(g => g.Contains(phrase, StringComparer.Ordinal));
                if (group is null && length > 1)
                    continue;
                slots.Add(group is null ? [phrase] : group.Prepend(phrase).Distinct(StringComparer.Ordinal).ToList());
                i += length;
                matched = true;
            }
        }
        return new SearchTerms(normalized, slots);
    }
}

/// <summary>
/// Text typed with the wrong keyboard layout: "lfhdg" typed on an English layout meant "مبایل" on the Persian one.
/// Maps the standard Persian and the Armenian phonetic layouts to the US layout and back (letter keys only).
/// </summary>
public static class KeyboardLayouts
{
    private const string Latin = "qwertyuiop[]asdfghjkl;'zxcvbnm,\\";
    private const string Persian = "ضصثقفغعهخحجچشسیبلاتنمکگظطزرذدپوپ";
    private const string LatinHy = "qwertyuiop[]asdfghjklzxcvbnm";
    private const string Armenian = "քոերտըւիօպխծասդֆգհյկլզղցվբնմ";

    /// <summary>What the text would read as on the other layouts (only those that change it), most likely first.</summary>
    public static IReadOnlyList<string> Alternatives(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lower = text.ToLowerInvariant();
        var results = new List<string>();
        void Add(string candidate)
        {
            if (candidate != lower && !results.Contains(candidate, StringComparer.Ordinal))
                results.Add(candidate);
        }
        if (lower.Any(c => Latin.Contains(c, StringComparison.Ordinal) && char.IsAsciiLetterLower(c)))
        {
            Add(Map(lower, Latin, Persian));
            Add(Map(lower, LatinHy, Armenian));
        }
        if (lower.Any(c => Persian.Contains(c, StringComparison.Ordinal)))
            Add(Map(lower, Persian, Latin));
        if (lower.Any(c => Armenian.Contains(c, StringComparison.Ordinal)))
            Add(Map(lower, Armenian, LatinHy));
        return results;
    }

    private static string Map(string text, string from, string to) =>
        string.Concat(text.Select(c => from.IndexOf(c, StringComparison.Ordinal) is var i and >= 0 ? to[i] : c));
}

/// <summary>How far apart two words are (edits needed), for "did you mean" without a database extension.</summary>
public static class TextDistance
{
    public static int Levenshtein(string a, string b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        var previous = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var current = new int[b.Length + 1];
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            previous = current;
        }
        return previous[b.Length];
    }
}
