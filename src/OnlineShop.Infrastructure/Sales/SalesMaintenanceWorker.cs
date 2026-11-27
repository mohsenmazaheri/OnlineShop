using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OnlineShop.Application.Sales;

namespace OnlineShop.Infrastructure.Sales;

/// <summary>
/// Every minute: cancels the unpaid orders whose payment time ran out; once an hour: deletes guest carts nobody touched
/// for <see cref="CartService.AbandonedGuestCartDays"/> days. Failures are logged and retried at the next tick.
/// </summary>
public sealed partial class SalesMaintenanceWorker(IServiceScopeFactory scopes, TimeProvider clock, ILogger<SalesMaintenanceWorker> logger) : BackgroundService
{
    internal static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(15);
    internal static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    internal static readonly TimeSpan CartCleanupInterval = TimeSpan.FromHours(1);

    private DateTimeOffset _lastCartCleanup = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartDelay, clock, stoppingToken);
            using var timer = new PeriodicTimer(Interval, clock);
            do
            {
                await RunOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    /// <summary>One pass; never throws (a failure must not stop the job or the app).</summary>
    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var canceled = await scope.ServiceProvider.GetRequiredService<OrderLifecycleService>().CancelExpiredAsync(cancellationToken);
            if (canceled > 0)
                LogCanceled(logger, canceled);

            var now = clock.GetUtcNow();
            if (now - _lastCartCleanup >= CartCleanupInterval)
            {
                _lastCartCleanup = now;
                var deleted = await scope.ServiceProvider.GetRequiredService<CartService>()
                    .DeleteAbandonedGuestCartsAsync(now.UtcDateTime.AddDays(-CartService.AbandonedGuestCartDays), cancellationToken);
                if (deleted > 0)
                    LogCartsDeleted(logger, deleted);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogError(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Canceled {Count} unpaid orders whose payment time ran out.")]
    private static partial void LogCanceled(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted {Count} abandoned guest carts.")]
    private static partial void LogCartsDeleted(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Sales maintenance (expired orders, abandoned carts) failed.")]
    private static partial void LogError(ILogger logger, Exception exception);
}
