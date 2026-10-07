using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RTelemetry.Contracts;

namespace RTelemetry.Server.Storage;

public sealed class PostgresEventStore(IDbContextFactory<TelemetryDbContext> factory) : IEventStore
{
    public async Task AppendAsync(IReadOnlyList<StoredEvent> events, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var e in events)
        {
            var payload = JsonSerializer.Serialize(e, TelemetryProtocol.JsonOptions);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Events" ("Id", "Project", "InstallId", "SessionId", "Sequence", "Name", "AppVersion", "ContentVersion", "ReceivedAtUtc", "TimestampUtc", "Payload")
                VALUES ({e.Id}, {e.Project}, {e.InstallId}, {e.SessionId}, {e.Sequence}, {e.Name}, {e.AppVersion}, {e.ContentVersion}, {e.ReceivedAtUtc.ToUniversalTime()}, {e.TimestampUtc.ToUniversalTime()}, CAST({payload} AS jsonb))
                ON CONFLICT ("Id") DO NOTHING
                """, cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async IAsyncEnumerable<StoredEvent> ReadAsync(string? project, DateOnly from, DateOnly to,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var end = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var query = db.Events.AsNoTracking().Where(e => e.ReceivedAtUtc >= start && e.ReceivedAtUtc < end);
        if (project is not null) query = query.Where(e => e.Project == project);
        await foreach (var payload in query.Select(e => e.Payload).AsAsyncEnumerable().WithCancellation(cancellationToken))
            yield return JsonSerializer.Deserialize<StoredEvent>(payload, TelemetryProtocol.JsonOptions)!;
    }

    public async Task<int> DeleteInstallAsync(Guid installId, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.Events.Where(e => e.InstallId == installId).ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<int> DeleteBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.Events.Where(e => e.ReceivedAtUtc < cutoff).ExecuteDeleteAsync(cancellationToken);
    }
}
