using RTelemetry.Contracts;

namespace RTelemetry.Client.Storage;

/// <summary>Состояние клиента, которое переживает перезапуск приложения.</summary>
public sealed record ClientState(Guid InstallId, ConsentState Consent);

/// <summary>Где клиент хранит состояние и неотправленную очередь.</summary>
public interface ITelemetryStorage
{
    ClientState? LoadState();
    void SaveState(ClientState state);

    IReadOnlyList<TelemetryEvent> LoadQueue();
    void SaveQueue(IReadOnlyList<TelemetryEvent> events);
    void ClearQueue();
}
