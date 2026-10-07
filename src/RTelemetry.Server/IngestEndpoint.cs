using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using RTelemetry.Contracts;
using RTelemetry.Server.Storage;

namespace RTelemetry.Server;

/// <summary><c>POST /v1/batches</c>: проверка ключа проекта и схемы, запись в хранилище.</summary>
public static class IngestEndpoint
{
    public static async Task<IResult> HandleAsync(
        [FromHeader(Name = TelemetryProtocol.ApiKeyHeader)] string? apiKey,
        [FromBody] TelemetryBatch? batch,
        IOptionsSnapshot<TelemetryServerOptions> options,
        IEventStore store,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var error = BatchValidator.Validate(batch);
        if (error is not null)
        {
            return Results.BadRequest(new { error });
        }

        if (!options.Value.Projects.TryGetValue(batch!.Project, out var project) || !KeyMatches(apiKey, project.ApiKey))
        {
            return Results.Unauthorized();
        }

        var receivedAt = time.GetUtcNow();
        var stored = batch.Events.Select(e => StoredEvent.From(batch, e, receivedAt)).ToList();
        await store.AppendAsync(stored, cancellationToken);

        return Results.Ok(new { accepted = stored.Count });
    }

    private static bool KeyMatches(string? provided, string expected)
    {
        if (string.IsNullOrEmpty(provided) || string.IsNullOrEmpty(expected))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(provided),
            Encoding.UTF8.GetBytes(expected));
    }
}
