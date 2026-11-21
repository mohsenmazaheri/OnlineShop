using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OnlineShopping.Application.Catalog;

namespace OnlineShopping.Infrastructure.Catalog;

/// <summary>Every minute: ends the special offers whose time is up. Failures are logged and retried at the next tick.</summary>
public sealed partial class SaleExpiryWorker(IServiceScopeFactory scopes, TimeProvider clock, ILogger<SaleExpiryWorker> logger) : BackgroundService
{
    internal static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(10);
    internal static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

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
            var ended = await scope.ServiceProvider.GetRequiredService<SaleExpiryService>().EndExpiredAsync(cancellationToken);
            if (ended > 0)
                LogEnded(logger, ended);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogError(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Ended the special offers of {Count} products.")]
    private static partial void LogEnded(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Ending expired special offers failed.")]
    private static partial void LogError(ILogger logger, Exception exception);
}
