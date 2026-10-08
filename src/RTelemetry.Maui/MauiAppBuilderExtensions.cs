using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Storage;
using RTelemetry.Client;
using RTelemetry.Input;

namespace RTelemetry.Maui;

public static class MauiAppBuilderExtensions
{
    /// <summary>
    /// Регистрирует <see cref="ITelemetryClient"/> и <see cref="ClickTracker"/> как синглтоны.
    /// Папка хранения, версия приложения и платформа заполняются из MAUI, если не заданы.
    /// Платформенные наблюдатели ввода и жизненного цикла подключаются автоматически. События уходят только после согласия.
    /// </summary>
    public static MauiAppBuilder UseRTelemetry(this MauiAppBuilder builder, Action<TelemetryClientOptions> configure, Action<InputOptions>? configureInput = null)
    {
        builder.Services.AddSingleton<TelemetryClient>(_ =>
        {
            var options = new TelemetryClientOptions
            {
                StorageDirectory = FileSystem.AppDataDirectory,
                AppVersion = $"{AppInfo.VersionString}+{AppInfo.BuildString}",
                Platform = $"{DeviceInfo.Platform.ToString().ToLowerInvariant()} {DeviceInfo.VersionString} {DeviceInfo.Idiom.ToString().ToLowerInvariant()}",
            };
            configure(options);
            return TelemetryClient.Create(options).Start();
        });
        builder.Services.AddSingleton<ITelemetryClient>(sp => sp.GetRequiredService<TelemetryClient>());
        var inputOptions = new InputOptions();
        configureInput?.Invoke(inputOptions);
        inputOptions.Validate();
        builder.Services.AddSingleton(inputOptions);
        builder.Services.AddSingleton<ClickTracker>();
        PlatformInput.Configure(builder);
        return builder;
    }
}
