# Подключение к приложению

## Пакет или локальный проект

Пакеты публикуются в NuGet-фид (GitHub Packages или свой). Пока RTelemetry активно меняется,
приложение может подключать проекты напрямую, без публикации на каждое изменение.

Пример для The Book Game — `Game/Game.csproj`:

```xml
<PropertyGroup>
  <!-- true: исходники из соседней папки ../RTelemetry; false: пакет из фида -->
  <UseLocalRTelemetry Condition="'$(UseLocalRTelemetry)' == ''">false</UseLocalRTelemetry>
  <RTelemetryVersion>0.1.*</RTelemetryVersion>
</PropertyGroup>

<ItemGroup Condition="'$(UseLocalRTelemetry)' == 'true'">
  <ProjectReference Include="../../RTelemetry/src/RTelemetry.Maui/RTelemetry.Maui.csproj" />
</ItemGroup>
<ItemGroup Condition="'$(UseLocalRTelemetry)' != 'true'">
  <PackageReference Include="RTelemetry.Maui" Version="$(RTelemetryVersion)" />
</ItemGroup>
```

Локально: `dotnet build -p:UseLocalRTelemetry=true` или то же свойство в `Directory.Build.props`
вне git. CI и облачные сессии берут пакет.

## Версии

SemVer по всему репозиторию (`VersionPrefix` в `Directory.Build.props`):
patch — исправления; minor — новые API и события, обратно совместимо; major — несовместимое
изменение API или формата (`schemaVersion`).

## MAUI

`MauiProgram.cs`:

```csharp
builder.UseRTelemetry(o =>
{
    o.Project = "tbg";
    o.Endpoint = new Uri("https://telemetry.example.com/");
    o.ApiKey = "…";
    o.ContentVersion = StoryInfo.ContentVersion; // версия встроенной истории
#if DEBUG
    o.OnDiagnostic = m => System.Diagnostics.Debug.WriteLine($"[RTelemetry] {m}");
#endif
});
```

`App.xaml.cs`:

```csharp
public App(ClickTracker clicks, ITelemetryClient telemetry)
{
    InitializeComponent();
    _telemetry = telemetry;
    clicks.Attach(this);
}

protected override Window CreateWindow(IActivationState? state)
{
    var window = new Window(new AppShell());
    window.Resumed += (_, _) => _telemetry.StartSession();
    window.Stopped += async (_, _) =>
    {
        _telemetry.EndSession();
        await _telemetry.FlushAsync();
    };
    _telemetry.StartSession();
    return window;
}
```

Согласие — из экрана согласия и настроек: `telemetry.SetConsent(ConsentState.Granted)` /
`Denied`. Пока `Unknown`, клики и события не пишутся (см. `privacy.md`).

## Смысловые события

Адаптер живёт в приложении, не в RTelemetry, например `Game/Telemetry/GameTelemetry.cs`:

```csharp
public sealed class GameTelemetry(ITelemetryClient client)
{
    public void SceneShown(string sceneId) =>
        client.Track("scene.shown", new Dictionary<string, object?> { ["scene_id"] = sceneId });

    public void ChoiceMade(string sceneId, string choiceId, string optionId, int index, TimeSpan latency) =>
        client.Track("choice.made", new Dictionary<string, object?>
        {
            ["scene_id"] = sceneId,
            ["choice_id"] = choiceId,
            ["option_id"] = optionId,
            ["option_index"] = index,
            ["latency_ms"] = latency,
        });
}
```

Каталог — `events.md`. В адаптер передаются только идентификаторы, не тексты.
