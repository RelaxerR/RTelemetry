using System.Data;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RTelemetry.Contracts;

namespace RTelemetry.Server.Storage;

public sealed class PostgresEventStore(IDbContextFactory<TelemetryDbContext> factory) : IEventStore
{
    public async Task<int> AppendAsync(IReadOnlyList<StoredEvent> events, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var installIds = events.Select(e => e.InstallId).Distinct().Order().ToArray();
        foreach (var installId in installIds)
            await LockInstallAsync(db, installId, cancellationToken);

        var deleted = await db.DeletedInstalls.Where(x => installIds.Contains(x.InstallId))
            .Select(x => x.InstallId).ToListAsync(cancellationToken);
        var blocked = deleted.ToHashSet();
        var accepted = 0;
        foreach (var e in events)
        {
            if (blocked.Contains(e.InstallId)) continue;
            accepted++;
            var payload = JsonSerializer.Serialize(e, TelemetryProtocol.JsonOptions);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Events" ("Id", "Project", "InstallId", "SessionId", "Sequence", "Name", "AppVersion", "ContentVersion", "ReceivedAtUtc", "TimestampUtc", "Payload")
                VALUES ({e.Id}, {e.Project}, {e.InstallId}, {e.SessionId}, {e.Sequence}, {e.Name}, {e.AppVersion}, {e.ContentVersion}, {e.ReceivedAtUtc.ToUniversalTime()}, {e.TimestampUtc.ToUniversalTime()}, CAST({payload} AS jsonb))
                ON CONFLICT ("Id") DO NOTHING
                """, cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return accepted;
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
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await LockInstallAsync(db, installId, cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "DeletedInstalls" ("InstallId") VALUES ({installId}) ON CONFLICT DO NOTHING
            """, cancellationToken);
        var deleted = await db.Events.Where(e => e.InstallId == installId).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return deleted;
    }

    // Запись пачки и удаление одной установки должны завершаться последовательно даже в двух экземплярах сервера.
    private static Task<int> LockInstallAsync(TelemetryDbContext db, Guid installId, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT pg_advisory_xact_lock(hashtextextended(CAST({installId} AS text), 0))
            """, cancellationToken);

    public async Task<int> DeleteBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.Events.Where(e => e.ReceivedAtUtc < cutoff).ExecuteDeleteAsync(cancellationToken);
    }
}
