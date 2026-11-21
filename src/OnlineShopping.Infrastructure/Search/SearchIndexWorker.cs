using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OnlineShopping.Application.Search;

namespace OnlineShopping.Infrastructure.Search;

/// <summary>
/// Brings catalog changes into the search index: at start-up it queues a full build for a shop without documents, then
/// every <c>Search:IndexIntervalSeconds</c> it works through the queued changes. With an external engine it then sends
/// the rebuilt documents there (building its indexes when they are missing) and tells <see cref="SearchEngineHealth"/>
/// whether the engine may answer searches. Failures are logged and retried at a later tick (the queues keep them); an
/// unreachable engine is left alone for <c>Search:Engine:RetrySeconds</c>.
/// </summary>
public sealed partial class SearchIndexWorker(
    IServiceScopeFactory scopes,
    IOptions<SearchOptions> options,
    TimeProvider clock,
    ILogger<SearchIndexWorker> logger) : BackgroundService
{
    /// <summary>Lets migrations and seeding finish first.</summary>
    internal static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartDelay, clock, stoppingToken);
            await RunAsync(ensureBuilt: true, stoppingToken);
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.IndexIntervalSeconds), clock);
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await RunAsync(ensureBuilt: false, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    /// <summary>Works through the queues (and, at start, makes sure the index exists); never throws.</summary>
    public async Task RunAsync(bool ensureBuilt, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var index = scope.ServiceProvider.GetRequiredService<SearchIndexService>();
            if (ensureBuilt && await index.EnsureBuiltAsync(cancellationToken))
                LogFullBuild(logger);
            var total = 0;
            for (var handled = await index.ProcessOutboxAsync(cancellationToken); handled > 0; handled = await index.ProcessOutboxAsync(cancellationToken))
                total += handled;
            if (total > 0)
                LogProcessed(logger, total);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogError(logger, ex);
        }
        if (options.Value.UsesEngine)
            await SyncEngineAsync(cancellationToken);
    }

    /// <summary>Sends queued documents to the external engine; never throws.</summary>
    public async Task SyncEngineAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        try
        {
            using var scope = scopes.CreateScope();
            var health = scope.ServiceProvider.GetRequiredService<SearchEngineHealth>();
            if (!health.CanTry(now))
                return;
            try
            {
                var sync = scope.ServiceProvider.GetRequiredService<SearchEngineSyncService>();
                var wasReady = health.Ready;
                // A missing index queues a full rebuild, which the loop below carries out.
                if (!wasReady)
                    health.SetReady(await sync.EnsureIndexAsync(cancellationToken));
                var total = 0;
                for (var handled = await sync.ProcessAsync(cancellationToken); handled > 0; handled = await sync.ProcessAsync(cancellationToken))
                    total += handled;
                if (total > 0)
                    LogEngineProcessed(logger, total);
                if (!health.Ready)
                    health.SetReady(await sync.EnsureIndexAsync(cancellationToken));
                if (!wasReady && health.Ready)
                    LogEngineReady(logger, options.Value.Provider);
                var behind = await sync.IsBehindAsync(cancellationToken);
                if (health.SetBehind(behind) && behind)
                    LogEngineBehind(logger);
                if (health.Recover())
                    LogEngineBack(logger);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Checked again at the next attempt: a lost index (a wiped cluster) is then rebuilt.
                health.SetReady(false);
                if (health.Fail(now, TimeSpan.FromSeconds(options.Value.Engine.RetrySeconds)))
                    LogEngineDown(logger, options.Value.Engine.RetrySeconds, ex);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogError(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The search index is empty: a full build was queued.")]
    private static partial void LogFullBuild(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Search index updated for {Count} queued changes.")]
    private static partial void LogProcessed(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Updating the search index failed.")]
    private static partial void LogError(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Search engine updated for {Count} queued products.")]
    private static partial void LogEngineProcessed(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "The {Provider} indexes are built; the engine answers searches.")]
    private static partial void LogEngineReady(ILogger logger, SearchProvider provider);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The search engine is behind the catalog; the database answers searches until it catches up.")]
    private static partial void LogEngineBehind(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "The search engine is reachable again.")]
    private static partial void LogEngineBack(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Updating the search engine failed; the database answers searches, the engine is tried again in {Seconds} s.")]
    private static partial void LogEngineDown(ILogger logger, int seconds, Exception exception);
}
