# Проверка исходного каркаса

2026-10-07, macOS arm64, .NET SDK 10.0.301, MAUI workload 10.0.110.

- Исходный `dotnet build RTelemetry.slnx`: успешно, 0 warnings / 0 errors;
  собраны Android, iOS, Mac Catalyst, Contracts, Client и Server.
- Исходный `dotnet test RTelemetry.slnx --no-restore`: 6/6 успешно.
- После переноса версий в Directory.Packages.props повторены обе команды:
  0 warnings / 0 errors, 6/6 тестов. `Microsoft.Maui.Controls` использует
  `$(MauiVersion)` и разрешается корректно на всех трёх платформах.

CPM проверен экспериментально, а правила заданы согласно
[NuGet Central Package Management](https://learn.microsoft.com/nuget/consume-packages/central-package-management).
SourceLink предоставляется .NET SDK 8+; механизм описан в
[документации SDK](https://learn.microsoft.com/dotnet/core/whats-new/dotnet-8/sdk).

Это проверка исходной точки, не итоговая проверка всех изменений 1.0.
