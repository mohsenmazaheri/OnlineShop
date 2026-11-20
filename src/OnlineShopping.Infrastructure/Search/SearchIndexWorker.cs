using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OnlineShopping.Application.Search;

namespace OnlineShopping.Infrastructure.Search;

/// <summary>
/// Brings catalog changes into the search index: at start-up it queues a full build for a shop without documents, then
/// every <c>Search:IndexIntervalSeconds</c> it works through the queued changes. Failures are logged and retried at the
/// next tick (the queue keeps them).
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

    /// <summary>Works through the queue (and, at start, makes sure the index exists); never throws.</summary>
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
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The search index is empty: a full build was queued.")]
    private static partial void LogFullBuild(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Search index updated for {Count} queued changes.")]
    private static partial void LogProcessed(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Updating the search index failed.")]
    private static partial void LogError(ILogger logger, Exception exception);
}
