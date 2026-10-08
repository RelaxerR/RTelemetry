# RTelemetry

Consent-first telemetry for .NET 10 applications, with optional .NET MAUI input
capture and a self-hosted ASP.NET Core analytics server. Domain-neutral events:
a name, versioned envelope and flat properties. No UI text is read by the tracker.

**Release candidate: 1.0.0-rc.1.** Packages are prepared locally; publishing is a
separate owner action. See [verification and limitations](docs/development/release-1.0.md)
and the [device checklist](docs/development/manual-checks.md).

| Component | Purpose |
|---|---|
| RTelemetry.Contracts | Wire schema and event names |
| RTelemetry.Client | Consent, bounded offline queue, sessions, HTTP retries |
| RTelemetry.Input | UI-independent hit testing and gesture classification |
| RTelemetry.Maui | Platform touch observers, click/miss events and lifecycle |
| RTelemetry.Server | JSON Lines or PostgreSQL, Razor dashboard, heatmaps and CSV |
| samples/RTelemetry.Sample.Maui | Controls, modal page, Toolkit Popup and event stream |

## Quick start

```sh
# .NET 10 SDK; core tests do not need MAUI workloads.
dotnet test tests/RTelemetry.Client.Tests
dotnet test tests/RTelemetry.Input.Tests
dotnet test tests/RTelemetry.Server.Tests
dotnet run --project src/RTelemetry.Server
```

Open http://localhost:5180/ (Development only: Basic `dev:dev`, project `dev`,
ingest key `dev-key`). Use HTTPS and private server credentials in production.
[Integration guide](docs/integration.md) covers local packages, application setup
and explicit consent. [Server guide](docs/server.md) covers Docker and deployment.

No events are queued or sent without Granted consent. Revocation clears pending
data and cancels delivery; it cannot undo a request already accepted by the server.
Use the separate authenticated admin endpoint to remove stored installation data.

Licensed under [MIT](LICENSE). [Contributing](CONTRIBUTING.md) · [Security](SECURITY.md).

## По-русски

RTelemetry — телеметрия RelaxerR Games для C#/.NET 10 и MAUI с собственным сервером.
Первый потребитель — The Book Game; клиент и сервер не знают об играх.

- Без Granted события не попадают в память или на диск. Отзыв стирает очередь.
- Автоматически собираются касания интерактивных элементов (`ui.click`) и промахи
  (`ui.miss`). Перетаскивание отбрасывается, долгое нажатие помечается.
- Текст контролов, Placeholder и введённое содержимое трекер не читает.
- В каждом событии — версии приложения и контента. installId — случайный GUID.
- Смысловые события добавляет приложение: клики сами по себе не объясняют контекст.
- Ошибки клиента уходят в OnDiagnostic/ILogger, не должны ронять приложение.

Ветка готовит кандидат 1.0.0-rc.1. Фактически выполненные проверки и ограничения
смотрите в отчёте; наличие платформенного обработчика не заменяет проверку на устройстве.

## Документация

| Документ | Содержание |
|---|---|
| [Архитектура](docs/architecture.md) | Поток данных, очереди, ввод и сервер |
| [События](docs/events.md) | Схема, точные определения click/miss, профиль TBG |
| [Приватность](docs/privacy.md) | Согласие и открытые вопросы исследования |
| [Подключение](docs/integration.md) | Быстрый старт и API приложения |
| [Сервер](docs/server.md) | Конфигурация, API, PostgreSQL, Docker, systemd/nginx |
| [Память проекта](docs/AI_MEMORY.md) | Обязательные инварианты и решения |
| [План 1.0](docs/development/plan-1.0.md) | Потоки и порядок интеграции |
| [Исходная проверка](docs/development/baseline.md) | Сборка начального каркаса |
| [Ручные проверки](docs/development/manual-checks.md) | Устройства и поведение UI |
| [Отчёт о выпуске](docs/development/release-1.0.md) | Проверки, ограничения и коммиты |
| [Изменения](CHANGELOG.md) | История версий |
