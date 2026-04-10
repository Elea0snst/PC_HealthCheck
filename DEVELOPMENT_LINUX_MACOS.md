# PC HealthCheck: разработка под Linux и macOS

## 1) Что уже работает кроссплатформенно

- UI на Avalonia.
- Базовый провайдер `UnixHardwareProvider` для Linux/macOS.
- Общая бизнес-логика: отчеты, диагностика, бенчмарк, БД, настройки.
- Мониторинг в ограниченном режиме:
  - логические диски;
  - сетевые интерфейсы;
  - системная информация по ОС/архитектуре.

Ограничения текущей реализации: нет WMI/LibreHardwareMonitor датчиков (температуры, вентиляторы, VRM, SMART-детали) на Linux/macOS.

## 2) Установка SDK

Проект использует `net9.0`, поэтому нужен .NET SDK 9.

Проверка:

```bash
dotnet --info
```

Если SDK 9 не установлен:

- Linux: установить через пакетный менеджер дистрибутива или [официальный install script](https://learn.microsoft.com/dotnet/core/install/linux).
- macOS: установить через [официальный пакет](https://learn.microsoft.com/dotnet/core/install/macos) или Homebrew.

## 3) Зависимости для Linux

Минимально:

- `libfontconfig1`
- `libfreetype6`
- `libx11-6`
- `libxkbcommon0`

Для Wayland/X11 в зависимости от окружения могут понадобиться дополнительные пакеты GUI.

## 4) Запуск приложения

Из корня проекта:

```bash
dotnet restore
dotnet build
dotnet run
```

## 5) Публикация self-contained

### Linux x64

```bash
dotnet publish -c Release -r linux-x64 --self-contained true
```

### macOS Intel

```bash
dotnet publish -c Release -r osx-x64 --self-contained true
```

### macOS Apple Silicon

```bash
dotnet publish -c Release -r osx-arm64 --self-contained true
```

Артефакты появятся в `bin/Release/net9.0/<rid>/publish`.

## 6) Что нужно сделать дальше для полноценного ТЗ на Linux/macOS

Чтобы приблизить паритет с Windows:

1. Реализовать `LinuxHardwareProvider`:
   - чтение `/proc`, `/sys/class/hwmon`, `lm-sensors`;
   - SMART через `smartctl`;
   - GPU через NVML/ROCm.
2. Реализовать `MacHardwareProvider`:
   - метрики через `powermetrics`, `sysctl`, IOKit.
3. Вынести платформозависимые утилиты (автозагрузка, очистка temp) в отдельные сервисы по ОС.
4. Добавить автотесты для провайдеров и контрактные тесты `IHardwareProvider`.

## 7) Режим прав доступа

Часть датчиков на Linux/macOS требует `sudo` или membership в системных группах (например, `video`, `disk`), это нужно учитывать при запуске и CI.
