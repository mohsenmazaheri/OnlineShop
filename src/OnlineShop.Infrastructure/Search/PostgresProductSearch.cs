using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OnlineShop.Application.Search;
using OnlineShop.Infrastructure.Persistence;

namespace OnlineShop.Infrastructure.Search;

/// <summary>
/// PostgreSQL full-text search over the search documents. Each word becomes a prefix term of the 'simple' configuration
/// (no stemming: PostgreSQL has none for Persian, Arabic or Armenian; the texts are already in one spelling), its
/// synonyms OR-ed in, phrases kept in order; the weighted vector ranks title over keywords over details over
/// description. pg_trgm's word similarity of the title adds typo tolerance ("samsnug" still finds "samsung") and
/// powers "did you mean". The vector column and both GIN indexes are created by the PostgreSQL migration.
/// </summary>
public sealed class PostgresProductSearch(AppDbContext db) : IDatabaseProductSearch
{
    /// <summary>A word similarity this high counts as a match on its own (pg_trgm's word_similarity).</summary>
    public const double TypoThreshold = 0.45;

    public async Task<IReadOnlyDictionary<Guid, double>> MatchAsync(SearchTerms terms, string locale, int limit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(terms);
        var query = TsQuery(terms);
        var hits = new Dictionary<Guid, double>();
        if (query.Length == 0)
            return hits;
        // ts_rank weights are {D, C, B, A}: description, details/codes, keywords, title.
        const string sql = """
            SELECT d.product_id,
                   (ts_rank('{0.05, 0.2, 0.4, 1.0}', d.search_vector, q.query) * 2 + word_similarity(@text, d.title))::float8 AS score
            FROM search_documents d
            CROSS JOIN (SELECT to_tsquery('simple', @query) AS query) q
            WHERE d.locale = @locale
              AND (d.search_vector @@ q.query OR word_similarity(@text, d.title) > @threshold)
            ORDER BY score DESC
            LIMIT @limit
            """;
        await using var command = await CommandAsync(sql, cancellationToken,
            ("text", terms.Text), ("query", query), ("locale", locale), ("threshold", TypoThreshold), ("limit", limit));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            hits[reader.GetGuid(0)] = reader.GetDouble(1);
        return hits;
    }

    public async Task<string?> SimilarWordAsync(string word, string locale, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(word);
        if (word.Length < 3)
            return null;
        const string sql = """
            SELECT w
            FROM (SELECT DISTINCT unnest(string_to_array(d.title || ' ' || coalesce(d.keywords, ''), ' ')) AS w
                  FROM search_documents d WHERE d.locale = @locale) words
            WHERE length(w) > 2 AND w <> @word AND similarity(w, @word) > 0.3
            ORDER BY similarity(w, @word) DESC, w
            LIMIT 1
            """;
        await using var command = await CommandAsync(sql, cancellationToken, ("word", word), ("locale", locale));
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    /// <summary>A command on the context's connection (opened here when closed), with named parameters.</summary>
    private async Task<System.Data.Common.DbCommand> CommandAsync(string sql, CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await db.Database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
#pragma warning disable CA2100 // The SQL is a constant; every value is a parameter.
        command.CommandText = sql;
#pragma warning restore CA2100
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }
        return command;
    }

    /// <summary>
    /// The tsquery text: "(word:* | synonym:*) &amp; (next:*)", a phrase as "a &lt;-&gt; b:*". Only letters and digits reach
    /// it, so a typed operator can never break the query.
    /// </summary>
    public static string TsQuery(SearchTerms terms)
    {
        ArgumentNullException.ThrowIfNull(terms);
        var slots = new List<string>();
        foreach (var alternatives in terms.Words)
        {
            var options = alternatives
                .Select(phrase => Tokens(phrase))
                .Where(tokens => tokens.Count > 0)
                .Select(tokens => string.Join(" <-> ", tokens.Select((t, i) => i == tokens.Count - 1 ? $"{t}:*" : t)))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (options.Count > 0)
                slots.Add(options.Count == 1 ? options[0] : $"({string.Join(" | ", options)})");
        }
        return string.Join(" & ", slots);
    }

    private static List<string> Tokens(string phrase)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        foreach (var c in phrase)
        {
            if (char.IsLetterOrDigit(c))
            {
                current.Append(c);
            }
            else if (current.Length > 0)
            {
                tokens.Add(current.ToString());
                current.Clear();
            }
        }
        if (current.Length > 0)
            tokens.Add(current.ToString());
        return tokens;
    }
}
