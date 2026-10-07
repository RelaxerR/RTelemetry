namespace RTelemetry.Client;

/// <summary>Публичный API клиента телеметрии. Все методы безопасны для вызова из UI-потока.</summary>
public interface ITelemetryClient
{
    /// <summary>Resume the session, rotating its ID after SessionTimeout in the background.</summary>
    void OnForeground();
    /// <summary>Record session end and flush when the application enters the background.</summary>
    Task OnBackgroundAsync(CancellationToken cancellationToken = default);

    ConsentState Consent { get; }

    /// <summary>Случайный идентификатор установки (не устройства и не аккаунта).</summary>
    Guid InstallId { get; }

    Guid SessionId { get; }

    /// <summary>
    /// Меняет согласие. <see cref="ConsentState.Denied"/> немедленно стирает неотправленную очередь.
    /// </summary>
    void SetConsent(ConsentState consent);

    /// <summary>Отзыв согласия с новым <see cref="InstallId"/>: прошлые события больше не связываются с будущими.</summary>
    void ResetInstallId();

    /// <summary>Версия контента для следующих событий (например, после загрузки другой истории).</summary>
    void SetContentVersion(string? contentVersion);

    /// <summary>Начинает новую сессию и пишет <c>session.start</c>.</summary>
    void StartSession(IReadOnlyDictionary<string, object?>? props = null);

    /// <summary>Пишет <c>session.end</c> с длительностью сессии.</summary>
    void EndSession(IReadOnlyDictionary<string, object?>? props = null);

    /// <summary>
    /// Записывает событие. Без согласия ничего не делает. Значения свойств — только строки
    /// (до 256 символов), числа и bool; остальное отбрасывается с диагностикой.
    /// </summary>
    void Track(string name, IReadOnlyDictionary<string, object?>? props = null);

    /// <summary>Отправляет накопленное и сохраняет остаток очереди на диск.</summary>
    Task FlushAsync(CancellationToken cancellationToken = default);
}
