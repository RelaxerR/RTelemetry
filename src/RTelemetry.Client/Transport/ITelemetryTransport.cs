using RTelemetry.Contracts;

namespace RTelemetry.Client.Transport;

public enum SendResult
{
    /// <summary>Сервер принял пачку — её можно удалить из очереди.</summary>
    Accepted,

    /// <summary>Временная проблема (сеть, 429, 5xx) — пачка остаётся в очереди.</summary>
    RetryLater,

    /// <summary>Сервер отверг пачку как некорректную (4xx) — повтор не поможет, пачка удаляется.</summary>
    Rejected,
}

/// <summary>Доставка пачки на сервер.</summary>
public interface ITelemetryTransport
{
    Task<SendResult> SendAsync(TelemetryBatch batch, CancellationToken cancellationToken);
}
