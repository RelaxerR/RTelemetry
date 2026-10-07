using System.Text.Json;

namespace RTelemetry.Contracts;

/// <summary>
/// Одно событие телеметрии. Конверт общий для всех проектов: смысл события задают
/// <see cref="Name"/> и <see cref="Props"/>, клиент и сервер о предметной области не знают.
/// </summary>
public sealed record TelemetryEvent
{
    /// <summary>Уникальный идентификатор события — для дедупликации повторной отправки.</summary>
    public Guid Id { get; init; }

    /// <summary>Имя события: <c>[a-z0-9._-]</c>, до 64 символов, например <c>ui.click</c>.</summary>
    public string Name { get; init; } = "";

    /// <summary>Время события по часам устройства, UTC.</summary>
    public DateTimeOffset TimestampUtc { get; init; }

    /// <summary>Сессия, в которой произошло событие.</summary>
    public Guid SessionId { get; init; }

    /// <summary>Порядковый номер в сессии: упорядочивает события с одинаковым временем.</summary>
    public long Sequence { get; init; }

    /// <summary>Версия приложения на момент события.</summary>
    public string AppVersion { get; init; } = "";

    /// <summary>Версия контента (истории, уровня, данных), если она у приложения есть.</summary>
    public string? ContentVersion { get; init; }

    /// <summary>
    /// Плоские свойства события: только строки, числа и bool.
    /// Никаких текстов, введённых или прочитанных игроком, и персональных данных.
    /// </summary>
    public Dictionary<string, JsonElement>? Props { get; init; }
}
