namespace RTelemetry.Server;

/// <summary>Секция <c>RTelemetry</c> в appsettings.</summary>
public sealed class TelemetryServerOptions
{
    public const string SectionName = "RTelemetry";

    /// <summary>Папка сырых событий (относительно рабочей папки процесса).</summary>
    public string DataDirectory { get; set; } = "data";

    /// <summary>Проекты, которым разрешено присылать события: код → настройки.</summary>
    public Dictionary<string, ProjectOptions> Projects { get; set; } = new(StringComparer.Ordinal);

    public DashboardOptions Dashboard { get; set; } = new();
}

public sealed class ProjectOptions
{
    public string ApiKey { get; set; } = "";
}

/// <summary>Доступ к фронту статистики по HTTP Basic. Пустой пароль — фронт выключен (404).</summary>
public sealed class DashboardOptions
{
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
}
