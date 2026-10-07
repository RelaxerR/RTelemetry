using System.Text.Json;
using RTelemetry.Client.Storage;
using RTelemetry.Client.Transport;
using RTelemetry.Contracts;

namespace RTelemetry.Client;

/// <summary>
/// Клиент телеметрии. Пишет события в очередь в памяти (быстро, без ввода-вывода на вызов),
/// раз в <see cref="TelemetryClientOptions.FlushInterval"/> отправляет пачки и сохраняет остаток на диск.
/// Ни один публичный метод не бросает исключения в хост, кроме ошибок конфигурации в конструкторе.
/// </summary>
public sealed class TelemetryClient : ITelemetryClient, IAsyncDisposable, IDisposable
{
    private readonly TelemetryClientOptions _options;
    private readonly ITelemetryTransport _transport;
    private readonly ITelemetryStorage _storage;
    private readonly TimeProvider _time;

    private readonly object _gate = new();
    private readonly List<TelemetryEvent> _queue = [];
    private readonly SemaphoreSlim _flushLock = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();

    private ClientState _state;
    private Guid _sessionId;
    private DateTimeOffset _sessionStartedAt;
    private long _sequence;
    private string? _contentVersion;
    private Task? _loop;
    private bool _disposed;

    public TelemetryClient(
        TelemetryClientOptions options,
        ITelemetryTransport transport,
        ITelemetryStorage storage,
        TimeProvider? timeProvider = null)
    {
        options.Validate();
        _options = options;
        _transport = transport;
        _storage = storage;
        _time = timeProvider ?? TimeProvider.System;
        _contentVersion = options.ContentVersion;

        _state = SafeLoadState() ?? new ClientState(Guid.NewGuid(), ConsentState.Unknown);
        SafeSaveState(_state);

        if (_state.Consent == ConsentState.Granted)
        {
            _queue.AddRange(SafeLoadQueue());
        }

        _sessionId = Guid.NewGuid();
        _sessionStartedAt = _time.GetUtcNow();
    }

    /// <summary>Клиент с файловым хранилищем и HTTP-доставкой — обычный вариант для приложения.</summary>
    public static TelemetryClient Create(TelemetryClientOptions options, HttpClient? httpClient = null)
    {
        options.Validate();
        var http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var transport = new HttpTelemetryTransport(http, options.Endpoint!, options.ApiKey);
        var storage = new FileTelemetryStorage(options.StorageDirectory);
        return new TelemetryClient(options, transport, storage);
    }

    public ConsentState Consent
    {
        get { lock (_gate) return _state.Consent; }
    }

    public Guid InstallId
    {
        get { lock (_gate) return _state.InstallId; }
    }

    public Guid SessionId
    {
        get { lock (_gate) return _sessionId; }
    }

    /// <summary>Запускает фоновую отправку. Без вызова события копятся до ручного <see cref="FlushAsync"/>.</summary>
    public TelemetryClient Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _loop ??= Task.Run(() => RunLoopAsync(_lifetime.Token));
        }

        return this;
    }

    public void SetConsent(ConsentState consent)
    {
        lock (_gate)
        {
            _state = _state with { Consent = consent };
            if (consent != ConsentState.Granted)
            {
                _queue.Clear();
            }
        }

        if (consent != ConsentState.Granted)
        {
            Safe(_storage.ClearQueue);
        }

        SafeSaveState(_state);
    }

    public void ResetInstallId()
    {
        lock (_gate)
        {
            _state = _state with { InstallId = Guid.NewGuid() };
            _queue.Clear();
        }

        Safe(_storage.ClearQueue);
        SafeSaveState(_state);
    }

    public void SetContentVersion(string? contentVersion)
    {
        lock (_gate) _contentVersion = contentVersion;
    }

    public void StartSession(IReadOnlyDictionary<string, object?>? props = null)
    {
        lock (_gate)
        {
            _sessionId = Guid.NewGuid();
            _sessionStartedAt = _time.GetUtcNow();
            _sequence = 0;
        }

        Track(TelemetryEventNames.SessionStart, props);
    }

    public void EndSession(IReadOnlyDictionary<string, object?>? props = null)
    {
        DateTimeOffset startedAt;
        lock (_gate) startedAt = _sessionStartedAt;

        var merged = new Dictionary<string, object?>(props ?? new Dictionary<string, object?>())
        {
            ["duration_ms"] = (long)(_time.GetUtcNow() - startedAt).TotalMilliseconds,
        };
        Track(TelemetryEventNames.SessionEnd, merged);
    }

    public void Track(string name, IReadOnlyDictionary<string, object?>? props = null)
    {
        if (Consent != ConsentState.Granted)
        {
            return;
        }

        if (!TelemetrySchema.IsValidName(name))
        {
            Diagnostic($"Event dropped: invalid name '{name}'.");
            return;
        }

        var converted = ConvertProps(name, props);
        var flushNow = false;

        lock (_gate)
        {
            if (_disposed || _state.Consent != ConsentState.Granted)
            {
                return;
            }

            _queue.Add(new TelemetryEvent
            {
                Id = Guid.NewGuid(),
                Name = name,
                TimestampUtc = _time.GetUtcNow(),
                SessionId = _sessionId,
                Sequence = ++_sequence,
                AppVersion = _options.AppVersion,
                ContentVersion = _contentVersion,
                Props = converted,
            });

            var overflow = _queue.Count - _options.MaxQueuedEvents;
            if (overflow > 0)
            {
                _queue.RemoveRange(0, overflow);
                Diagnostic($"Queue overflow: dropped {overflow} oldest events.");
            }

            flushNow = _loop is not null && _queue.Count >= _options.MaxBatchSize;
        }

        if (flushNow)
        {
            _ = FlushAsync();
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _flushLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TelemetryBatch batch;
                lock (_gate)
                {
                    if (_queue.Count == 0 || _state.Consent != ConsentState.Granted)
                    {
                        break;
                    }

                    batch = new TelemetryBatch
                    {
                        Project = _options.Project,
                        InstallId = _state.InstallId,
                        Platform = _options.Platform,
                        Events = _queue.Take(_options.MaxBatchSize).ToList(),
                    };
                }

                SendResult result;
                try
                {
                    result = await _transport.SendAsync(batch, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Diagnostic($"Transport failed: {ex.GetType().Name}: {ex.Message}");
                    result = SendResult.RetryLater;
                }

                if (result == SendResult.RetryLater)
                {
                    break;
                }

                if (result == SendResult.Rejected)
                {
                    Diagnostic($"Server rejected a batch of {batch.Events.Count} events; dropped.");
                }

                // Удаляем по Id: пока шла отправка, очередь могли стереть (отзыв согласия) и наполнить снова.
                var sent = batch.Events.Select(e => e.Id).ToHashSet();
                lock (_gate) _queue.RemoveAll(e => sent.Contains(e.Id));
            }

            PersistQueue();
        }
        finally
        {
            _flushLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task? loop;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            loop = _loop;
        }

        _lifetime.Cancel();
        if (loop is not null)
        {
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        PersistQueue();
        _lifetime.Dispose();
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_options.FlushInterval, _time);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private Dictionary<string, JsonElement>? ConvertProps(string eventName, IReadOnlyDictionary<string, object?>? props)
    {
        if (props is null || props.Count == 0)
        {
            return null;
        }

        var result = new Dictionary<string, JsonElement>(Math.Min(props.Count, TelemetrySchema.MaxPropsPerEvent));
        foreach (var (key, value) in props)
        {
            if (result.Count >= TelemetrySchema.MaxPropsPerEvent)
            {
                Diagnostic($"{eventName}: more than {TelemetrySchema.MaxPropsPerEvent} props, rest dropped.");
                break;
            }

            if (!TelemetrySchema.IsValidName(key))
            {
                Diagnostic($"{eventName}: invalid prop name '{key}', dropped.");
                continue;
            }

            JsonElement? element = value switch
            {
                null => JsonSerializer.SerializeToElement<object?>(null),
                string s => JsonSerializer.SerializeToElement(Truncate(s)),
                bool b => JsonSerializer.SerializeToElement(b),
                int or long or short or byte or uint or ushort or sbyte =>
                    JsonSerializer.SerializeToElement(Convert.ToInt64(value)),
                ulong or float or double or decimal =>
                    JsonSerializer.SerializeToElement(Convert.ToDouble(value)),
                Guid g => JsonSerializer.SerializeToElement(g.ToString("N")),
                Enum e => JsonSerializer.SerializeToElement(e.ToString()),
                TimeSpan t => JsonSerializer.SerializeToElement((long)t.TotalMilliseconds),
                _ => null,
            };

            if (element is null)
            {
                Diagnostic($"{eventName}: prop '{key}' has unsupported type {value!.GetType().Name}, dropped.");
                continue;
            }

            result[key] = element.Value;
        }

        return result.Count == 0 ? null : result;
    }

    private static string Truncate(string value) =>
        value.Length <= TelemetrySchema.MaxStringValueLength
            ? value
            : value[..TelemetrySchema.MaxStringValueLength];

    private void PersistQueue()
    {
        List<TelemetryEvent> snapshot;
        lock (_gate)
        {
            if (_state.Consent != ConsentState.Granted)
            {
                return;
            }

            snapshot = [.. _queue];
        }

        Safe(() => _storage.SaveQueue(snapshot));
    }

    private ClientState? SafeLoadState()
    {
        try
        {
            return _storage.LoadState();
        }
        catch (Exception ex)
        {
            Diagnostic($"Load state failed: {ex.Message}");
            return null;
        }
    }

    private IReadOnlyList<TelemetryEvent> SafeLoadQueue()
    {
        try
        {
            return _storage.LoadQueue();
        }
        catch (Exception ex)
        {
            Diagnostic($"Load queue failed: {ex.Message}");
            return [];
        }
    }

    private void SafeSaveState(ClientState state) => Safe(() => _storage.SaveState(state));

    private void Safe(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Diagnostic($"Storage failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void Diagnostic(string message)
    {
        try
        {
            _options.OnDiagnostic?.Invoke(message);
        }
        catch
        {
            // Диагностика не должна ронять хост.
        }
    }
}
