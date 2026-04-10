# PC HealthCheck

Desktop-приложение для диагностики и мониторинга компонентов ПК (Avalonia + .NET).

## Статус реализации

- Windows: расширенный режим (WMI + LibreHardwareMonitor + SMART parsing).
- Linux/macOS: ограниченный базовый режим через `UnixHardwareProvider`.

## Быстрый старт

```bash
dotnet restore
dotnet build
dotnet run
```

## Документация для Linux/macOS

Подробная инструкция по разработке, запуску и публикации:

- `DEVELOPMENT_LINUX_MACOS.md`
