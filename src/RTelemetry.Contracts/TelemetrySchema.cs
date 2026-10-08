using System.Text.Json;

namespace RTelemetry.Contracts;

/// <summary>Версия схемы и ограничения, общие для клиента и сервера.</summary>
public static class TelemetrySchema
{
    public const int CurrentVersion = 1;

    public const int MaxEventsPerBatch = 500;
    public const int MaxPropsPerEvent = 32;
    public const int MaxNameLength = 64;
    public const int MaxStringValueLength = 256;

    /// <summary>
    /// Имя проекта, события или свойства: строчные латинские буквы, цифры, <c>. _ -</c>;
    /// первый символ — буква или цифра (так имя безопасно и как часть пути на сервере).
    /// </summary>
    public static bool IsValidName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength)
        {
            return false;
        }

        if (name[0] is not (>= 'a' and <= 'z' or >= '0' and <= '9'))
        {
            return false;
        }

        foreach (var c in name)
        {
            var ok = c is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_' or '-';
            if (!ok)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Допустимое значение свойства: строка (с ограничением длины), число или bool.</summary>
    public static bool IsValidPropValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString()!.Length <= MaxStringValueLength,
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null => true,
        _ => false,
    };
}

/// <summary>Стандартные имена событий, которые понимает фронт статистики.</summary>
public static class TelemetryEventNames
{
    public const string SessionStart = "session.start";
    public const string SessionEnd = "session.end";
    public const string UiClick = "ui.click";
    public const string UiMiss = "ui.miss";
    public const string ScreenShown = "ui.screen";
}

/// <summary>HTTP-детали протокола.</summary>
public static class TelemetryProtocol
{
    public const string ApiKeyHeader = "X-RTelemetry-Key";
    public const string BatchesPath = "v1/batches";

    /// <summary>Настройки JSON для обеих сторон: camelCase, как в веб-API по умолчанию.</summary>
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);
}
