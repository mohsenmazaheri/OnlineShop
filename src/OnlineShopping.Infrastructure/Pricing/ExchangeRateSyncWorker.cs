using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OnlineShopping.Application.Pricing;

namespace OnlineShopping.Infrastructure.Pricing;

/// <summary>
/// Refreshes the automatic display rates shortly after start-up and then every <c>ExchangeRates:RefreshHours</c>.
/// Failures are logged and retried next time; pages always use the rates already in the database.
/// </summary>
public sealed partial class ExchangeRateSyncWorker(
    IServiceScopeFactory scopes,
    IOptions<ExchangeRateOptions> options,
    TimeProvider clock,
    ILogger<ExchangeRateSyncWorker> logger) : BackgroundService
{
    /// <summary>Lets migrations and seeding finish before the first update.</summary>
    internal static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.Provider == ExchangeRateProvider.None)
            return;

        try
        {
            await Task.Delay(StartDelay, clock, stoppingToken);
            using var timer = new PeriodicTimer(TimeSpan.FromHours(options.Value.RefreshHours), clock);
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

    /// <summary>One update; never throws (a failure must not stop the job or the app).</summary>
    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var result = await scope.ServiceProvider.GetRequiredService<CurrencyService>().SyncRatesAsync(cancellationToken);
            if (result.IsSuccess)
                LogUpdated(logger, result.Value.Updated);
            else
                LogSkipped(logger, result.Error!.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogError(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Exchange rates updated for {Count} currencies.")]
    private static partial void LogUpdated(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Exchange rates not updated: {Reason}")]
    private static partial void LogSkipped(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Exchange rate update failed.")]
    private static partial void LogError(ILogger logger, Exception exception);
}
