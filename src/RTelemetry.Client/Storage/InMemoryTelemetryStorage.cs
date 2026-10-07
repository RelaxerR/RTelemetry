using RTelemetry.Contracts;

namespace RTelemetry.Client.Storage;

/// <summary>Хранение в памяти: для тестов и сред без файловой системы.</summary>
public sealed class InMemoryTelemetryStorage : ITelemetryStorage
{
    private readonly object _gate = new();
    private ClientState? _state;
    private List<TelemetryEvent> _queue = [];

    public ClientState? LoadState()
    {
        lock (_gate) return _state;
    }

    public void SaveState(ClientState state)
    {
        lock (_gate) _state = state;
    }

    public IReadOnlyList<TelemetryEvent> LoadQueue()
    {
        lock (_gate) return [.. _queue];
    }

    public void SaveQueue(IReadOnlyList<TelemetryEvent> events)
    {
        lock (_gate) _queue = [.. events];
    }

    public void ClearQueue()
    {
        lock (_gate) _queue = [];
    }
}
