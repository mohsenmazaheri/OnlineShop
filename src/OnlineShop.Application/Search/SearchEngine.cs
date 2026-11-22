using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OnlineShop.Domain.Search;

namespace OnlineShop.Application.Search;

/// <summary>The database's own search (PostgreSQL full-text, or the simple engine): always there, the fallback of an engine.</summary>
public interface IDatabaseProductSearch : IProductSearch;

/// <summary>
/// An external search engine (OpenSearch or Elasticsearch) holding a rebuildable copy of the search documents: one
/// index per shop language, reached through an alias so a full rebuild can switch to a new index without downtime.
/// </summary>
public interface ISearchEngine : IProductSearch
{
    /// <summary>The alias searches and incremental writes of a language go to.</summary>
    string AliasFor(string locale);

    /// <summary>Whether the language's alias exists (the engine has been built).</summary>
    Task<bool> IndexExistsAsync(string locale, CancellationToken cancellationToken = default);

    /// <summary>Creates a new, empty index for the language (with its analyzer) and returns its name.</summary>
    Task<string> CreateIndexAsync(string locale, CancellationToken cancellationToken = default);

    /// <summary>Adds or replaces documents and removes products from an index or alias.</summary>
    Task WriteAsync(string index, IReadOnlyCollection<SearchDocument> upserts, IReadOnlyCollection<Guid> deletes, CancellationToken cancellationToken = default);

    /// <summary>Points the language's alias at the index in one step and removes the language's older indexes.</summary>
    Task PublishAsync(string locale, string index, CancellationToken cancellationToken = default);
}

/// <summary>An answer of the search engine that means it did not do what was asked.</summary>
public sealed class SearchEngineException(string message) : Exception(message);

/// <summary>
/// Whether the external engine may answer searches: it has been built (<see cref="SetReady"/>), it is not too far behind
/// the database (<see cref="SetBehind"/>), and it has not failed in the last few seconds (<see cref="Fail"/>).
/// One per process, shared by the index worker and every request.
/// </summary>
public sealed class SearchEngineHealth
{
    private readonly Lock _gate = new();
    private bool _ready;
    private bool _behind;
    private DateTime? _downUntil;

    public bool Ready
    {
        get
        {
            lock (_gate)
                return _ready;
        }
    }

    /// <summary>Searches may go to the engine now.</summary>
    public bool CanServe(DateTime now)
    {
        lock (_gate)
            return _ready && !_behind && !(_downUntil > now);
    }

    /// <summary>The engine may be tried now (no recent failure).</summary>
    public bool CanTry(DateTime now)
    {
        lock (_gate)
            return !(_downUntil > now);
    }

    /// <returns>Whether this changed the state.</returns>
    public bool SetReady(bool ready)
    {
        lock (_gate)
        {
            var changed = _ready != ready;
            _ready = ready;
            return changed;
        }
    }

    /// <returns>Whether this changed the state.</returns>
    public bool SetBehind(bool behind)
    {
        lock (_gate)
        {
            var changed = _behind != behind;
            _behind = behind;
            return changed;
        }
    }

    /// <summary>The engine failed: leave it alone until <paramref name="now"/> + <paramref name="retry"/>.</summary>
    /// <returns>Whether it was considered up until now (log the outage once).</returns>
    public bool Fail(DateTime now, TimeSpan retry)
    {
        lock (_gate)
        {
            var wasUp = _downUntil is null;
            _downUntil = now + retry;
            return wasUp;
        }
    }

    /// <summary>The engine answered.</summary>
    /// <returns>Whether it had been down (log the recovery once).</returns>
    public bool Recover()
    {
        lock (_gate)
        {
            var wasDown = _downUntil is not null;
            _downUntil = null;
            return wasDown;
        }
    }
}

/// <summary>
/// Search through the external engine while it is healthy, otherwise (not built yet, too far behind, unreachable, too
/// slow) through the database, so the shop never stops finding products. A failure is logged once and the engine is
/// tried again after <c>Search:Engine:RetrySeconds</c>.
/// </summary>
public sealed partial class EngineProductSearch(
    ISearchEngine engine,
    IDatabaseProductSearch database,
    SearchEngineHealth health,
    TimeProvider clock,
    IOptions<SearchOptions> options,
    ILogger<EngineProductSearch> logger) : IProductSearch
{
    public Task<IReadOnlyDictionary<Guid, double>> MatchAsync(SearchTerms terms, string locale, int limit, CancellationToken cancellationToken = default) =>
        RunAsync(() => engine.MatchAsync(terms, locale, limit, cancellationToken), () => database.MatchAsync(terms, locale, limit, cancellationToken), cancellationToken);

    public Task<string?> SimilarWordAsync(string word, string locale, CancellationToken cancellationToken = default) =>
        RunAsync(() => engine.SimilarWordAsync(word, locale, cancellationToken), () => database.SimilarWordAsync(word, locale, cancellationToken), cancellationToken);

    private async Task<T> RunAsync<T>(Func<Task<T>> fromEngine, Func<Task<T>> fromDatabase, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        if (health.CanServe(now))
        {
            try
            {
                var result = await fromEngine();
                if (health.Recover())
                    LogBack(logger);
                return result;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                if (health.Fail(now, TimeSpan.FromSeconds(options.Value.Engine.RetrySeconds)))
                    LogDown(logger, options.Value.Engine.RetrySeconds, ex);
            }
        }
        return await fromDatabase();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The search engine failed; the database answers searches, the engine is tried again in {Seconds} s.")]
    private static partial void LogDown(ILogger logger, int seconds, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "The search engine answers searches again.")]
    private static partial void LogBack(ILogger logger);
}
