using System.Text.Json;
using RTelemetry.Contracts;

namespace RTelemetry.Server.Storage;

/// <summary>Событие в хранилище: конверт клиента плюс данные пачки и время приёма.</summary>
public sealed record StoredEvent
{
    public DateTimeOffset ReceivedAtUtc { get; init; }
    public string Project { get; init; } = "";
    public Guid InstallId { get; init; }
    public string Platform { get; init; } = "";
    public int SchemaVersion { get; init; }

    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public DateTimeOffset TimestampUtc { get; init; }
    public Guid SessionId { get; init; }
    public long Sequence { get; init; }
    public string AppVersion { get; init; } = "";
    public string? ContentVersion { get; init; }
    public Dictionary<string, JsonElement>? Props { get; init; }

    public static StoredEvent From(TelemetryBatch batch, TelemetryEvent e, DateTimeOffset receivedAt) => new()
    {
        ReceivedAtUtc = receivedAt,
        Project = batch.Project,
        InstallId = batch.InstallId,
        Platform = batch.Platform,
        SchemaVersion = batch.SchemaVersion,
        Id = e.Id,
        Name = e.Name,
        TimestampUtc = e.TimestampUtc,
        SessionId = e.SessionId,
        Sequence = e.Sequence,
        AppVersion = e.AppVersion,
        ContentVersion = e.ContentVersion,
        Props = e.Props,
    };
}

public interface IEventStore
{
    Task AppendAsync(IReadOnlyList<StoredEvent> events, CancellationToken cancellationToken);

    /// <summary>События, принятые в днях [from; to] по UTC.</summary>
    IAsyncEnumerable<StoredEvent> ReadAsync(string? project, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}
