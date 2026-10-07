using System.Text.Json;
using RTelemetry.Contracts;

namespace RTelemetry.Client.Storage;

/// <summary>
/// Хранение в папке приложения: <c>state.json</c> и <c>queue.json</c>.
/// Запись атомарная (временный файл + замена), чтобы обрыв не портил очередь.
/// </summary>
public sealed class FileTelemetryStorage : ITelemetryStorage
{
    private readonly string _statePath;
    private readonly string _queuePath;
    private readonly object _gate = new();

    public FileTelemetryStorage(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("Storage directory is required.", nameof(directory));
        }

        var root = Path.Combine(directory, "rtelemetry");
        Directory.CreateDirectory(root);
        _statePath = Path.Combine(root, "state.json");
        _queuePath = Path.Combine(root, "queue.json");
    }

    public ClientState? LoadState() => Read<ClientState>(_statePath);

    public void SaveState(ClientState state) => Write(_statePath, state);

    public IReadOnlyList<TelemetryEvent> LoadQueue() => Read<List<TelemetryEvent>>(_queuePath) ?? [];

    public void SaveQueue(IReadOnlyList<TelemetryEvent> events)
    {
        if (events.Count == 0)
        {
            ClearQueue();
            return;
        }

        Write(_queuePath, events);
    }

    public void ClearQueue()
    {
        lock (_gate)
        {
            if (File.Exists(_queuePath))
            {
                File.Delete(_queuePath);
            }
        }
    }

    private T? Read<T>(string path)
    {
        lock (_gate)
        {
            if (!File.Exists(path))
            {
                return default;
            }

            try
            {
                using var stream = File.OpenRead(path);
                return JsonSerializer.Deserialize<T>(stream, TelemetryProtocol.JsonOptions);
            }
            catch (JsonException)
            {
                // Повреждённый файл не должен ломать приложение: начинаем с чистого листа.
                return default;
            }
        }
    }

    private void Write<T>(string path, T value)
    {
        lock (_gate)
        {
            var temp = path + ".tmp";
            using (var stream = File.Create(temp))
            {
                JsonSerializer.Serialize(stream, value, TelemetryProtocol.JsonOptions);
            }

            File.Move(temp, path, overwrite: true);
        }
    }
}
