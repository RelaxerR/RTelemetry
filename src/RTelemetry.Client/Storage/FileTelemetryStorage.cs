using System.Text.Json;
using RTelemetry.Contracts;

namespace RTelemetry.Client.Storage;

/// <summary>Atomic state and bounded queue files. Use one storage instance per directory.</summary>
public sealed class FileTelemetryStorage : ITelemetryStorage
{
    private readonly string _statePath;
    private readonly string _queuePath;
    private readonly long _maxQueueBytes;
    private readonly object _gate = new();

    public FileTelemetryStorage(string directory, long maxQueueBytes = 16 * 1024 * 1024)
    {
        if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Storage directory is required.", nameof(directory));
        if (maxQueueBytes < 2) throw new ArgumentOutOfRangeException(nameof(maxQueueBytes));
        var root = Path.Combine(directory, "rtelemetry");
        Directory.CreateDirectory(root);
        _statePath = Path.Combine(root, "state.json");
        _queuePath = Path.Combine(root, "queue.json");
        _maxQueueBytes = maxQueueBytes;
        // A temp file was never committed. Never resurrect it after consent withdrawal.
        File.Delete(_statePath + ".tmp");
        File.Delete(_queuePath + ".tmp");
    }

    public ClientState? LoadState()
    {
        var state = Read<ClientState>(_statePath, 4096);
        return state is { InstallId: var id } && id != Guid.Empty && Enum.IsDefined(state.Consent) ? state : null;
    }

    public void SaveState(ClientState state)
    {
        lock (_gate) Write(_statePath, JsonSerializer.SerializeToUtf8Bytes(state, TelemetryProtocol.JsonOptions));
    }

    public IReadOnlyList<TelemetryEvent> LoadQueue() =>
        (Read<List<TelemetryEvent>>(_queuePath, _maxQueueBytes) ?? [])
        .Where(e => e is not null && e.Id != Guid.Empty && TelemetrySchema.IsValidName(e.Name)).ToList();

    public void SaveQueue(IReadOnlyList<TelemetryEvent> events)
    {
        lock (_gate)
        {
            // Serialize each event once; retain the newest suffix that fits, including JSON delimiters.
            var records = new List<byte[]>();
            long bytes = 2;
            for (var i = events.Count - 1; i >= 0; i--)
            {
                var record = JsonSerializer.SerializeToUtf8Bytes(events[i], TelemetryProtocol.JsonOptions);
                var next = bytes + record.Length + (records.Count > 0 ? 1 : 0);
                if (next > _maxQueueBytes) break;
                bytes = next;
                records.Add(record);
            }
            if (records.Count == 0) { ClearQueue(); return; }
            using var output = new MemoryStream();
            output.WriteByte((byte)'[');
            for (var i = records.Count - 1; i >= 0; i--)
            {
                if (i != records.Count - 1) output.WriteByte((byte)',');
                output.Write(records[i]);
            }
            output.WriteByte((byte)']');
            Write(_queuePath, output.ToArray());
        }
    }

    public void ClearQueue()
    {
        lock (_gate)
        {
            File.Delete(_queuePath);
            File.Delete(_queuePath + ".tmp");
        }
    }

    private T? Read<T>(string path, long maxBytes)
    {
        lock (_gate)
        {
            if (!File.Exists(path)) return default;
            if (new FileInfo(path).Length > maxBytes) { File.Delete(path); return default; }
            try
            {
                using var stream = File.OpenRead(path);
                return JsonSerializer.Deserialize<T>(stream, TelemetryProtocol.JsonOptions);
            }
            catch (JsonException)
            {
                File.Delete(path);
                return default;
            }
        }
    }

    private static void Write(string path, byte[] data)
    {
        var temp = path + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(data);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, path, overwrite: true);
        }
        finally { File.Delete(temp); }
    }
}
