using OnlineShopping.Application.Search;
using OnlineShopping.Domain.Search;

namespace OnlineShopping.UnitTests.TestSupport;

/// <summary>An external search engine in memory: indexes, aliases, canned answers, and a switch that makes it fail.</summary>
public sealed class FakeSearchEngine : ISearchEngine
{
    private int _created;

    public Dictionary<string, Dictionary<Guid, SearchDocument>> Indexes { get; } = [];

    public Dictionary<string, string> Aliases { get; } = [];

    /// <summary>What <see cref="MatchAsync"/> answers.</summary>
    public Dictionary<Guid, double> Matches { get; } = [];

    public string? Similar { get; set; }

    /// <summary>Every call fails while set.</summary>
    public bool Down { get; set; }

    public int Calls { get; private set; }

    public string AliasFor(string locale) => $"test-{locale}";

    /// <summary>The documents a language's alias points at.</summary>
    public Dictionary<Guid, SearchDocument> Live(string locale) => Indexes[Aliases[AliasFor(locale)]];

    public Task<IReadOnlyDictionary<Guid, double>> MatchAsync(SearchTerms terms, string locale, int limit, CancellationToken cancellationToken = default)
    {
        Touch();
        return Task.FromResult<IReadOnlyDictionary<Guid, double>>(new Dictionary<Guid, double>(Matches));
    }

    public Task<string?> SimilarWordAsync(string word, string locale, CancellationToken cancellationToken = default)
    {
        Touch();
        return Task.FromResult(Similar);
    }

    public Task<bool> IndexExistsAsync(string locale, CancellationToken cancellationToken = default)
    {
        Touch();
        return Task.FromResult(Aliases.ContainsKey(AliasFor(locale)));
    }

    public Task<string> CreateIndexAsync(string locale, CancellationToken cancellationToken = default)
    {
        Touch();
        var name = $"{AliasFor(locale)}-{++_created}";
        Indexes[name] = [];
        return Task.FromResult(name);
    }

    public Task WriteAsync(string index, IReadOnlyCollection<SearchDocument> upserts, IReadOnlyCollection<Guid> deletes, CancellationToken cancellationToken = default)
    {
        Touch();
        var target = Indexes[Aliases.GetValueOrDefault(index) ?? index];
        foreach (var d in upserts)
            target[d.ProductId] = d;
        foreach (var id in deletes)
            target.Remove(id);
        return Task.CompletedTask;
    }

    public Task PublishAsync(string locale, string index, CancellationToken cancellationToken = default)
    {
        Touch();
        var alias = AliasFor(locale);
        foreach (var old in Indexes.Keys.Where(k => k.StartsWith(alias + "-", StringComparison.Ordinal) && k != index).ToList())
            Indexes.Remove(old);
        Aliases[alias] = index;
        return Task.CompletedTask;
    }

    private void Touch()
    {
        Calls++;
        if (Down)
            throw new SearchEngineException("The fake engine is down.");
    }
}
