using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OnlineShopping.Application.Inventory;

namespace OnlineShopping.Infrastructure.Inventory;

/// <summary>
/// Puts the units of abandoned checkouts back on sale: every <c>Inventory:ExpiryCheckSeconds</c> it releases the
/// active reservations whose time ran out. Failures are logged and retried at the next tick.
/// </summary>
public sealed partial class ReservationExpiryWorker(
    IServiceScopeFactory scopes,
    IOptions<InventoryOptions> options,
    TimeProvider clock,
    ILogger<ReservationExpiryWorker> logger) : BackgroundService
{
    /// <summary>Lets migrations and seeding finish before the first run.</summary>
    internal static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartDelay, clock, stoppingToken);
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.ExpiryCheckSeconds), clock);
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
            var released = await scope.ServiceProvider.GetRequiredService<StockReservationService>().ReleaseExpiredAsync(cancellationToken);
            if (released > 0)
                LogReleased(logger, released);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogError(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Released {Count} expired stock reservations.")]
    private static partial void LogReleased(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Releasing expired stock reservations failed.")]
    private static partial void LogError(ILogger logger, Exception exception);
}
