using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RTelemetry.Contracts;

namespace RTelemetry.Server.Storage;

/// <summary>
/// Временное хранилище первой версии: файл на проект и день приёма,
/// <c>{DataDirectory}/{project}/{yyyy-MM-dd}.jsonl</c>, одна строка — одно событие.
/// Дедупликации по Id нет (повторная отправка после обрыва даст дубль) — её даст PostgreSQL.
/// </summary>
public sealed class JsonLinesEventStore : IEventStore
{
    private readonly string _root;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public JsonLinesEventStore(IOptions<TelemetryServerOptions> options, IHostEnvironment environment)
    {
        _root = Path.GetFullPath(options.Value.DataDirectory, environment.ContentRootPath);
        Directory.CreateDirectory(_root);
    }

    public async Task AppendAsync(IReadOnlyList<StoredEvent> events, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            foreach (var group in events.GroupBy(e => (e.Project, Day: DateOnly.FromDateTime(e.ReceivedAtUtc.UtcDateTime))))
            {
                var directory = Path.Combine(_root, group.Key.Project);
                Directory.CreateDirectory(directory);
                var lines = group.Select(e => JsonSerializer.Serialize(e, TelemetryProtocol.JsonOptions));
                await File.AppendAllLinesAsync(FilePath(group.Key.Project, group.Key.Day), lines, cancellationToken);
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async IAsyncEnumerable<StoredEvent> ReadAsync(
        string? project,
        DateOnly from,
        DateOnly to,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string[] projects = project is not null
            ? [project]
            : Directory.EnumerateDirectories(_root).Select(Path.GetFileName).OfType<string>().ToArray();

        foreach (var p in projects)
        {
            if (!TelemetrySchema.IsValidName(p)) continue;

            for (var day = from; day <= to; day = day.AddDays(1))
            {
                var path = FilePath(p, day);
                if (!File.Exists(path)) continue;

                // FileShare.ReadWrite: файл может дописываться приёмом прямо во время чтения.
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                while (await reader.ReadLineAsync(cancellationToken) is { } line)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    StoredEvent? e;
                    try
                    {
                        e = JsonSerializer.Deserialize<StoredEvent>(line, TelemetryProtocol.JsonOptions);
                    }
                    catch (JsonException)
                    {
                        continue; // недописанная последняя строка
                    }

                    if (e is not null) yield return e;
                }
            }
        }
    }

    private string FilePath(string project, DateOnly day) =>
        Path.Combine(_root, project, $"{day:yyyy-MM-dd}.jsonl");
}
