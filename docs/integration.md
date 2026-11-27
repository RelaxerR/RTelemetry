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

## Временная проверка разрешения приложения

Для приложения, которое проверяет статус участия на сервере при каждом запуске/возврате,
задайте при создании клиента:

```csharp
o.InitialCollectionEnabled = false;
o.PauseOnBackground = true;
```

Даже сохранённое `Granted` тогда не разрешает события и отправку до
`telemetry.SetCollectionEnabled(true)`. Этот вызов делает приложение после успешной проверки;
он не выдаёт согласие. До явного `Granted` сбор по-прежнему невозможен.
`SetCollectionEnabled(false)` временно закрывает сбор и отправку, отменяет текущий запрос,
сохраняет очередь и `installId`; разрешение не записывается на диск. Сетевой сбой при проверке
оставляет клиента на паузе. Уже принятый сервером запрос отменить задним числом нельзя.

При `PauseOnBackground` событие конца сессии помещается в очередь, затем клиент ставится
на паузу. Очередь сохраняется на диск и отправится после следующего подтверждения.
Короткий фон сохраняет sessionId, таймаут начинает новую сессию только после разрешения.
MAUI-трекер не захватывает снимок интерфейса и не публикует локальные InputCaptured при паузе.
Отзыв — отдельное действие `SetConsent(Denied)`, он стирает очередь и отменяет отправку.
Перевод в `Unknown` тоже очищает очередь, поэтому для временной сетевой проверки он не подходит.

По умолчанию `InitialCollectionEnabled = true`, `PauseOnBackground = false`: существующие
потребители сохраняют прежнее поведение. Конверт событий и состояние согласия не менялись.

Для пилота с локальной очередью включите `RequireUploadValidation = true` и задайте
`MaxPendingEventAge = TimeSpan.FromDays(7)`. После подтверждённого вступления приложение
привязывает выданный сайтом UUID через `SetInstallId`, сохраняет согласие и передаёт
исключительную границу локального сбора в `SetCollectionDeadline`. Каждый запуск
начинается без разрешения на сбор. Приложение может восстановить его из ранее подтверждённой
и ещё действующей записи участия. При смене UUID SDK очищает очередь и сбрасывает согласие.
Файловая очередь режима с проверкой привязана к UUID и сохраняется при перезапуске
той же установки. При переходе со старого формата SDK очищает прежнюю общую очередь.

Приложение один раз вызывает `ConfigureConsentGate` и передаёт асинхронную проверку статуса.
SDK ждёт её перед каждой пачкой, включая повтор после временной ошибки, и отправляет
данные только при результате `true` и действующем разрешении. Ошибка связи сохраняет
допустимые события локально. SDK прекращает запись при достижении границы UTC
или исчерпании монотонного времени, исключает события предельного возраста, а при
`SetConsent(Denied)` очищает очередь и отменяет проверку или запрос в полёте.
Разрешение приложения и локальный срок действуют совместно.

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
