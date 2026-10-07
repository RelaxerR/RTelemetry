namespace RTelemetry.Contracts;

/// <summary>Пачка событий одной установки, отправляемая одним запросом.</summary>
public sealed record TelemetryBatch
{
    public int SchemaVersion { get; init; } = TelemetrySchema.CurrentVersion;

    /// <summary>Код проекта на сервере, например <c>tbg</c>.</summary>
    public string Project { get; init; } = "";

    /// <summary>
    /// Случайный идентификатор установки (не идентификатор устройства или аккаунта).
    /// Создаётся клиентом при первом запуске и стирается вместе с его данными.
    /// </summary>
    public Guid InstallId { get; init; }

    /// <summary>Платформа в общем виде: <c>android 10.0</c>, <c>ios 17.5</c>.</summary>
    public string Platform { get; init; } = "";

    public List<TelemetryEvent> Events { get; init; } = [];
}
