# Сервер

## Запуск и провайдеры

```sh
dotnet run --project src/RTelemetry.Server
```

Development: `http://localhost:5180/`, Basic `dev:dev`, проект `dev`, ключ
`dev-key`. Production-конфигурация не содержит ключей. Пустой Dashboard:Password
отключает фронт и CSV (404); пустой AdminKey запрещает авторизацию удаления.

`RTelemetry:Storage:Provider`: `JsonLines` (по умолчанию, один процесс разработки)
или `Postgres`. PostgreSQL использует EF Core/Npgsql, применяет миграции при старте,
дедуплицирует по event Id через `ON CONFLICT DO NOTHING`. Сохраняется полный
конверт в jsonb и индексируемые поля. Индексы: проект/дата/версии,
проект/имя/время, проект/сессия/sequence, installId и время приёма.
JSON Lines не дедуплицирует повторные доставки; при удалении переписывает файлы.
Не запускайте несколько процессов на одном файловом каталоге.

Пример production EnvironmentFile (задайте реальные значения вне репозитория):

```ini
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://127.0.0.1:5180
RTelemetry__Storage__Provider=Postgres
RTelemetry__Storage__ConnectionString=Host=127.0.0.1;Database=rtelemetry;Username=rtelemetry;Password=REPLACE_ME
RTelemetry__Projects__app__ApiKey=REPLACE_ME
RTelemetry__Dashboard__User=admin
RTelemetry__Dashboard__Password=REPLACE_ME
RTelemetry__AdminKey=REPLACE_ME
RTelemetry__RetentionDays=90
RTelemetry__RateLimit__RequestsPerMinutePerIp=120
RTelemetry__RateLimit__RequestsPerMinutePerProject=600
```

Для JsonLines задайте `RTelemetry__DataDirectory=/var/lib/rtelemetry`.
Срок хранения — 90 дней по receivedAtUtc. Очистка при старте и каждые 6 часов.
Старое офлайн-событие хранится этот период с момента приёма. Бэкапы требуют
отдельной политики удаления.

## API и защита

`POST /v1/batches`, заголовок `X-RTelemetry-Key`, тело — TelemetryBatch схемы 1.

| Ответ | Значение | Действие клиента |
|---|---|---|
| 200 | Пачка обработана; PostgreSQL не вставляет повторные Id | Удалить пачку |
| 400 | Некорректная схема | Удалить с диагностикой |
| 401 | Неизвестный проект / неверный ключ | Удалить с диагностикой |
| 413 | Тело более 1 МиБ | Удалить с диагностикой |
| 429 | Превышен лимит IP или авторизованного проекта | Повторить с задержкой |
| 5xx / сеть | Временная ошибка | Повторить с задержкой |

Лимит IP применяется до разбора тела; лимит проекта — после авторизации.
Ключ клиентского приложения не является секретом. Админ-ключ и пароль Basic
не включайте в приложение. `GET /health` проверяет процесс, а не доступность БД.

Удалить данные установки во всех проектах:

```sh
curl -X DELETE "https://telemetry.example.com/v1/admin/installs/INSTALL_GUID" \
  -H "X-RTelemetry-Admin-Key: $RTELEMETRY_ADMIN_KEY"
```

Ответ содержит deleted. Это не блокирует новую отправку: сначала отзовите согласие
и очистите очередь на клиенте.

## Статистика и CSV

`GET /` и `GET /?handler=Csv` защищены HTTP Basic. Фильтры: project,
from/to (UTC, включительно, по времени приёма), appVersion, contentVersion,
session. screen выбирает страницу карты, не сужает общую сводку и CSV.

Страница показывает количество событий/установок/сессий, имена событий,
SVG-карту click/miss, топ ближайших элементов для промахов, список сессий
и ленту выбранной сессии по sequence. Карта ограничена первыми 5000 точками.
Период не более 366 дней; свыше 100 000 событий сервер возвращает 413 с просьбой
сузить фильтры, включая экспорт. CSV содержит идентификаторы, версии, обе временные
метки и JSON props. Формулы табличных программ экранируются.

## Docker Compose

Задайте POSTGRES_PASSWORD, RTELEMETRY_PROJECT_KEY, RTELEMETRY_ADMIN_KEY и
RTELEMETRY_DASHBOARD_PASSWORD в окружении или локальном `.env` (не коммитить).

```sh
docker compose up --build -d
```

Postgres 16 хранит данные в volume. Сервер без root слушает loopback хоста
127.0.0.1:5180, проект — app. Для HTTPS нужен reverse proxy.
Docker в текущей среде отсутствует, compose не проверен запуском.

## systemd и nginx с HTTPS

```sh
dotnet publish src/RTelemetry.Server -c Release -o artifacts/server
```

Разместите publish в /opt/rtelemetry, создайте системного пользователя rtelemetry
и /etc/rtelemetry.env с правами 600. Пример unit:

```ini
[Unit]
Description=RTelemetry
After=network-online.target
Wants=network-online.target

[Service]
User=rtelemetry
Group=rtelemetry
WorkingDirectory=/opt/rtelemetry
EnvironmentFile=/etc/rtelemetry.env
ExecStart=/usr/bin/dotnet /opt/rtelemetry/RTelemetry.Server.dll
Restart=on-failure
StateDirectory=rtelemetry
NoNewPrivileges=true
ProtectSystem=strict
ProtectHome=true
PrivateTmp=true

[Install]
WantedBy=multi-user.target
```

Пример nginx после получения сертификата для реального домена:

```nginx
server {
    listen 80;
    server_name telemetry.example.com;
    return 301 https://$host$request_uri;
}
server {
    listen 443 ssl;
    server_name telemetry.example.com;
    ssl_certificate /etc/letsencrypt/live/telemetry.example.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/telemetry.example.com/privkey.pem;
    access_log off;
    client_max_body_size 1m;
    location / {
        proxy_pass http://127.0.0.1:5180;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $remote_addr;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

ForwardedHeaders доверяет loopback proxy по умолчанию ASP.NET. Для Docker или
внешнего proxy задайте точные IP через `RTelemetry__TrustedProxies__0` (и следующие индексы)
и ограничьте сетевой доступ. Не доверяйте произвольному X-Forwarded-For из интернета.
Проверяйте журналы nginx/systemd: IP и сырые события не должны сохраняться.
Реальный деплой HTTPS, systemd и nginx не выполнялся.

## Тесты

`dotnet test tests/RTelemetry.Server.Tests` использует WebApplicationFactory и
временный JSON-каталог. PostgreSQL-тест включается переменной окружения `RTELEMETRY_TEST_POSTGRES` (строка подключения), и требует отдельную тестовую базу. Не задавайте production
connection string: тест выполняет операции очистки.
