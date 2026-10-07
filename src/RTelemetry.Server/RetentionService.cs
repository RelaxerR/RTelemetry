using Microsoft.Extensions.Options;
using RTelemetry.Server.Storage;

namespace RTelemetry.Server;

public sealed class RetentionService(IEventStore store, IOptions<TelemetryServerOptions> options,
    TimeProvider time, ILogger<RetentionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await store.DeleteBeforeAsync(time.GetUtcNow().AddDays(-options.Value.RetentionDays), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Retention cleanup failed"); }
            try { await Task.Delay(TimeSpan.FromHours(6), time, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
