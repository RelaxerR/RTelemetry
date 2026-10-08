# Подключение за пять минут

Требуется .NET 10; для MAUI — workload и платформенный SDK. Версия кандидата
`1.0.0-rc.1`. Пакеты пока не опубликованы: сначала создайте локальный feed.

```sh
mkdir -p artifacts/packages
for project in Contracts Client Input Maui; do
  dotnet pack "src/RTelemetry.$project" -c Release -o artifacts/packages
done
# В каталоге своего MAUI-приложения:
dotnet add package RTelemetry.Maui --version 1.0.0-rc.1 --source /absolute/path/RTelemetry/artifacts/packages
```

Локальный pack содержит платформы текущей ОС; CI объединяет Mac и Windows
артефакты в один пакет. После публикации достаточно убрать `--source`.
При активной разработке вместо пакета можно добавить ProjectReference на
`src/RTelemetry.Maui/RTelemetry.Maui.csproj`. Не включайте одновременно оба варианта.

## Сервер для разработки

```sh
dotnet run --project src/RTelemetry.Server
# http://localhost:5180/ — Basic dev:dev, проект dev, ключ dev-key
```

В Android emulator адрес хоста — `10.0.2.2`; для физического устройства нужен
доступный адрес компьютера в сети. В production используйте HTTPS и отдельный
ключ проекта. HTTP-разрешения sample предназначены только для локальной проверки.

## MAUI

В `MauiProgram.cs` после `UseMauiApp<App>()`:

```csharp
using RTelemetry.Maui;

builder.UseRTelemetry(o =>
{
    o.Project = "dev";
    o.Endpoint = new Uri("http://localhost:5180/");
    o.ApiKey = "dev-key";
    o.ContentVersion = "content-1";
    o.SessionTimeout = TimeSpan.FromMinutes(30);
    o.OnDiagnostic = message => System.Diagnostics.Debug.WriteLine(message);
}, input =>
{
    input.DragThreshold = 12; // логические единицы платформы
    input.LongPressThreshold = TimeSpan.FromMilliseconds(600);
});
```

Начало сессии, фон и отправка подключаются автоматически: вызовы в App не нужны.
`AppVersion`, `Platform` и `StorageDirectory` заполняются из MAUI; при необходимости
их можно переопределить. `Logger` принимает ILogger, `OnDiagnostic` можно оставить
вместе с ним. Не передавайте в диагностику персональные данные.

В экране согласия получите `ITelemetryClient` из DI и только после явного решения:

```csharp
telemetry.SetConsent(ConsentState.Granted);
// Отзыв из настроек:
telemetry.SetConsent(ConsentState.Denied);
```

До Granted не создаются события. Denied стирает неотправленное и отменяет текущий
запрос; уже принятые сервером данные удаляет администратор по installId. Политика
согласия и несовершеннолетних остаётся решением приложения: docs/privacy.md.

## Идентификаторы и свои контролы

Задавайте стабильный `AutomationId`, не текст или введённое значение:

```xml
<ContentPage xmlns:telemetry="clr-namespace:RTelemetry.Maui;assembly=RTelemetry.Maui">
  <GraphicsView AutomationId="choice_canvas" telemetry:RTelemetry.IsInteractive="True" />
</ContentPage>
```

Без AutomationId используется StyleId (в том числе назначаемый MAUI из x:Name),
затем имя типа. Для отдельного нестандартного источника доступен
`ClickTracker.TrackClick(element)`. Для обычных касаний он не нужен и даст дубль,
если вызвать его поверх автоматического наблюдения.

## Смысловые события

```csharp
telemetry.Track("scene.shown", new Dictionary<string, object?>
{
    ["scene_id"] = sceneId,
});
telemetry.SetContentVersion("content-2");
```

Адаптер принадлежит приложению. Только id, числа и флаги — никаких реплик и
пользовательского текста. Список событий TBG: docs/events.md.

## Обычное C#-приложение

```csharp
await using var telemetry = TelemetryClient.Create(new TelemetryClientOptions
{
    Project = "dev", Endpoint = new Uri("http://localhost:5180/"), ApiKey = "dev-key",
    AppVersion = "1.0", ContentVersion = "content-1",
    StorageDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyApp", "telemetry")
}).Start();
telemetry.OnForeground();
// Согласие устанавливается вашим UI.
// Перед уходом в фон:
await telemetry.OnBackgroundAsync();
```

Возврат раньше SessionTimeout продолжает sessionId; после таймаута — новая
session.start. session.end может повторяться в одной логической сессии при коротких
уходах в фон; duration_ms накопительная, поэтому её нельзя суммировать.

## Пример и проверка

`samples/RTelemetry.Sample.Maui` содержит все поддержанные типы контролов, модальную
страницу, Popup, список, GraphicsView, согласие и локальную ленту касаний.
Проверки: docs/development/manual-checks.md. Перед публикацией приложения выполните
их на целевых устройствах.
