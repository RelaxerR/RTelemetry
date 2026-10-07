# Сервер

## Настройка

Секция `RTelemetry` в `appsettings.json` или переменные окружения (`RTelemetry__Projects__tbg__ApiKey=…`):

```json
{
  "RTelemetry": {
    "DataDirectory": "/var/lib/rtelemetry",
    "Projects": {
      "tbg": { "ApiKey": "длинная-случайная-строка" }
    },
    "Dashboard": { "User": "relaxerr", "Password": "…" }
  }
}
```

Пустой `Dashboard:Password` — фронт выключен (404), приём работает.

## API

`POST /v1/batches`, заголовок `X-RTelemetry-Key`, тело — `TelemetryBatch` (`events.md`).

| Ответ | Когда | Клиент |
|---|---|---|
| 200 `{ "accepted": n }` | Принято | Удаляет пачку |
| 400 `{ "error": "…" }` | Нарушена схема | Удаляет (повтор не поможет) |
| 401 | Неизвестный проект или ключ | Удаляет |
| 413 | Тело больше 1 МиБ | Удаляет |
| 429, 5xx, сеть | Временная проблема | Повторит позже |

`GET /health` — проверка живости. `GET /` — фронт статистики (HTTP Basic).

## Деплой

Схема как у остальных .NET-сервисов на своём сервере: `dotnet publish -c Release`,
systemd-сервис, nginx с HTTPS впереди. HTTP Basic и ключ без HTTPS не использовать.
В nginx не писать полный IP клиентов в access log для этого хоста (см. `privacy.md`).

## Дальше

1. **PostgreSQL** (`Npgsql.EntityFrameworkCore.PostgreSQL`): таблица событий с `props jsonb`,
   уникальный индекс по `id` (дедупликация повторной отправки), индексы `(project, name, timestamp_utc)`
   и `(project, session_id)`. `JsonLinesEventStore` остаётся для разработки.
2. **Rate limiting** на приём (`Microsoft.AspNetCore.RateLimiting`, по IP и проекту).
3. **Фронт для ВКР**: воронка сцен, распределение выборов по вариантам, время до выбора,
   доля завершивших эпизод — по версиям контента. Выгрузка CSV для анализа.
4. **Удаление по `installId`** по запросу игрока.
5. Интеграционные тесты сервера (`WebApplicationFactory<Program>`).
