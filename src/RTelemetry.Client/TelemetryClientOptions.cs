using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace RTelemetry.Client;

/// <summary>Настройки клиента. Обязательны <see cref="Project"/>, <see cref="Endpoint"/>,
/// <see cref="ApiKey"/>, <see cref="AppVersion"/> и <see cref="StorageDirectory"/>.</summary>
public sealed class TelemetryClientOptions
{
    /// <summary>Код проекта на сервере, например <c>tbg</c>.</summary>
    public string Project { get; set; } = "";

    /// <summary>Базовый адрес сервера, например <c>https://telemetry.example.com/</c>.</summary>
    public Uri? Endpoint { get; set; }

    /// <summary>Ключ проекта. Не секрет в строгом смысле (лежит в приложении), но отсекает мусор.</summary>
    public string ApiKey { get; set; } = "";

    public string AppVersion { get; set; } = "";

    /// <summary>Начальная версия контента; меняется через <see cref="ITelemetryClient.SetContentVersion"/>.</summary>
    public string? ContentVersion { get; set; }

    /// <summary>Папка для очереди и состояния клиента (в MAUI — <c>FileSystem.AppDataDirectory</c>).</summary>
    public string StorageDirectory { get; set; } = "";

    /// <summary>Платформа в общем виде; по умолчанию определяется автоматически.</summary>
    public string Platform { get; set; } = DetectPlatform();

    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Сколько событий уходит одним запросом.</summary>
    public int MaxBatchSize { get; set; } = 200;

    /// <summary>Предел очереди; при переполнении отбрасываются самые старые события.</summary>
    public int MaxQueuedEvents { get; set; } = 20_000;

    /// <summary>Куда сообщать о внутренних проблемах клиента. Клиент никогда не бросает исключения в хост.</summary>
    public Action<string>? OnDiagnostic { get; set; }

    public TimeSpan SessionTimeout { get; set; } = TimeSpan.FromMinutes(30);
    public TimeSpan RetryInitialDelay { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan RetryMaxDelay { get; set; } = TimeSpan.FromMinutes(5);
    public long MaxQueueBytes { get; set; } = 16 * 1024 * 1024;
    public ILogger? Logger { get; set; }

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Project)) throw new ArgumentException("Project is required.", nameof(Project));
        if (Endpoint is null) throw new ArgumentException("Endpoint is required.", nameof(Endpoint));
        if (string.IsNullOrWhiteSpace(AppVersion)) throw new ArgumentException("AppVersion is required.", nameof(AppVersion));
        if (MaxBatchSize is < 1 or > Contracts.TelemetrySchema.MaxEventsPerBatch)
            throw new ArgumentOutOfRangeException(nameof(MaxBatchSize));
        if (MaxQueuedEvents < MaxBatchSize) throw new ArgumentOutOfRangeException(nameof(MaxQueuedEvents));
        if (SessionTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(SessionTimeout));
        if (RetryInitialDelay <= TimeSpan.Zero || RetryMaxDelay < RetryInitialDelay)
            throw new ArgumentOutOfRangeException(nameof(RetryInitialDelay));
        if (MaxQueueBytes < 2) throw new ArgumentOutOfRangeException(nameof(MaxQueueBytes));
        if (FlushInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(FlushInterval));
    }

    private static string DetectPlatform()
    {
        var name =
            OperatingSystem.IsAndroid() ? "android" :
            OperatingSystem.IsIOS() ? "ios" :
            OperatingSystem.IsMacCatalyst() ? "maccatalyst" :
            OperatingSystem.IsMacOS() ? "macos" :
            OperatingSystem.IsWindows() ? "windows" :
            OperatingSystem.IsLinux() ? "linux" :
            "other";

        var version = Environment.OSVersion.Version;
        return $"{name} {version.Major}.{Math.Max(version.Minor, 0)} {RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}";
    }
}
