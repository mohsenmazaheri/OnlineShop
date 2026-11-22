using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnlineShop.Application.Common;
using OnlineShop.Domain.Search;

namespace OnlineShop.Application.Search;

/// <summary>What the admin sees of the external engine.</summary>
/// <param name="Provider">The engine in use.</param>
/// <param name="Serving">Whether it answers searches now (otherwise the database does).</param>
/// <param name="Pending">Products waiting to reach it.</param>
public sealed record SearchEngineStatus(SearchProvider Provider, bool Serving, int Pending);

/// <summary>
/// Copies the search documents (PostgreSQL stays the source of truth) to the external engine through its outbox: a
/// changed product is written to (or removed from) every language's alias; a full rebuild fills new indexes and then
/// switches the aliases in one step, so searches never see a half-built index. A failure leaves the outbox as it is,
/// to be retried.
/// </summary>
public sealed class SearchEngineSyncService(
    IApplicationDbContext db,
    ISearchEngine engine,
    SearchEngineHealth health,
    TimeProvider clock,
    IOptions<StoreOptions> store,
    IOptions<SearchOptions> options)
{
    /// <summary>Queued products per incremental write.</summary>
    public const int Batch = 200;

    /// <summary>Documents per write of a full rebuild.</summary>
    public const int Page = 500;

    /// <summary>
    /// Whether every language has its index; when one is missing (a new engine, a wiped cluster) a full rebuild is
    /// queued once.
    /// </summary>
    public async Task<bool> EnsureIndexAsync(CancellationToken cancellationToken = default)
    {
        foreach (var locale in store.Value.LocaleList)
        {
            if (await engine.IndexExistsAsync(locale, cancellationToken))
                continue;
            if (!await db.SearchEngineOutbox.AnyAsync(e => e.ProductId == Guid.Empty, cancellationToken))
            {
                db.SearchEngineOutbox.Add(new SearchEngineOutboxEntry { ProductId = Guid.Empty, EnqueuedUtc = clock.GetUtcNow().UtcDateTime });
                await db.SaveChangesAsync(cancellationToken);
                db.ChangeTracker.Clear();
            }
            return false;
        }
        return true;
    }

    /// <summary>Sends up to <see cref="Batch"/> queued products (or a full rebuild); returns how many entries it took.</summary>
    public async Task<int> ProcessAsync(CancellationToken cancellationToken = default)
    {
        var entries = await db.SearchEngineOutbox.AsNoTracking().OrderBy(e => e.Id).Take(Batch)
            .Select(e => new { e.Id, e.ProductId }).ToListAsync(cancellationToken);
        if (entries.Count == 0)
            return 0;
        var last = entries[^1].Id;
        // A queued full rebuild goes first (writes to an alias that does not exist yet would fail).
        if (await db.SearchEngineOutbox.AnyAsync(e => e.ProductId == Guid.Empty, cancellationToken))
        {
            // Everything queued so far is covered by the rebuild; later changes stay queued.
            last = await db.SearchEngineOutbox.MaxAsync(e => e.Id, cancellationToken);
            await RebuildAllAsync(cancellationToken);
        }
        else
        {
            await WriteAsync(entries.Select(e => e.ProductId).Distinct().ToList(), cancellationToken);
        }
        return await db.SearchEngineOutbox.Where(e => e.Id <= last).ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>Whether the oldest waiting change is older than <c>Search:Engine:MaxLagSeconds</c>.</summary>
    public async Task<bool> IsBehindAsync(CancellationToken cancellationToken = default)
    {
        var oldest = await db.SearchEngineOutbox.OrderBy(e => e.Id).Select(e => (DateTime?)e.EnqueuedUtc).FirstOrDefaultAsync(cancellationToken);
        return oldest is { } since && clock.GetUtcNow().UtcDateTime - since > TimeSpan.FromSeconds(options.Value.Engine.MaxLagSeconds);
    }

    /// <summary>The engine's state for the admin panel, or null when the database searches.</summary>
    public async Task<SearchEngineStatus?> StatusAsync(CancellationToken cancellationToken = default)
    {
        if (!options.Value.UsesEngine)
            return null;
        return new SearchEngineStatus(options.Value.Provider, health.CanServe(clock.GetUtcNow().UtcDateTime),
            await db.SearchEngineOutbox.CountAsync(cancellationToken));
    }

    private async Task WriteAsync(List<Guid> ids, CancellationToken cancellationToken)
    {
        var documents = await db.SearchDocuments.AsNoTracking().Where(d => ids.Contains(d.ProductId)).ToListAsync(cancellationToken);
        foreach (var locale in store.Value.LocaleList)
        {
            var current = documents.Where(d => d.Locale == locale).ToList();
            var gone = ids.Except(current.Select(d => d.ProductId)).ToList();
            await engine.WriteAsync(engine.AliasFor(locale), current, gone, cancellationToken);
        }
    }

    private async Task RebuildAllAsync(CancellationToken cancellationToken)
    {
        foreach (var locale in store.Value.LocaleList)
        {
            var index = await engine.CreateIndexAsync(locale, cancellationToken);
            for (var page = 0; ; page++)
            {
                var documents = await db.SearchDocuments.AsNoTracking().Where(d => d.Locale == locale)
                    .OrderBy(d => d.ProductId).Skip(page * Page).Take(Page).ToListAsync(cancellationToken);
                if (documents.Count > 0)
                    await engine.WriteAsync(index, documents, [], cancellationToken);
                if (documents.Count < Page)
                    break;
            }
            await engine.PublishAsync(locale, index, cancellationToken);
        }
    }
}
