using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace RTelemetry.Server;

// Отдельные бюджеты: IP ограничивается до чтения тела, проект — после проверки ключа.
public sealed class IngestRateLimiter(IOptions<TelemetryServerOptions> options) : IDisposable
{
    private readonly PartitionedRateLimiter<string> _projects = PartitionedRateLimiter.Create<string, string>(project =>
        RateLimitPartition.GetFixedWindowLimiter(project, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = options.Value.RateLimit.RequestsPerMinutePerProject,
            Window = TimeSpan.FromMinutes(1), QueueLimit = 0
        }));
    public RateLimitLease Acquire(string project) => _projects.AttemptAcquire(project);
    public void Dispose() => _projects.Dispose();
}
