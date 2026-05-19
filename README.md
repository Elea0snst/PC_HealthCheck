# PC HealthCheck

Настольное приложение для диагностики, мониторинга и нагрузочного тестирования компонентов ПК (Avalonia UI + .NET 9).

## Скачать (Windows 10/11 x64)

Готовые сборки публикуются в разделе **Releases** вашего репозитория на GitHub (после push тега `v1.0.0` workflow `release.yml` создаст Release автоматически).

| Архив | Нужен .NET 9 Desktop Runtime? | Когда выбирать |
|-------|-------------------------------|----------------|
| **PC_HealthCheck_win-x64_framework.zip** | **Да** | Меньший размер exe (~37 МБ), Runtime уже установлен или можно установить отдельно |
| **PC_HealthCheck_win-x64_selfcontained.zip** | **Нет** | «Скачал и запустил» без установки Runtime (~80–120+ МБ) |

### Установка .NET 9 Desktop Runtime (только для framework-варианта)

1. Скачайте [**.NET Desktop Runtime 9.x**](https://dotnet.microsoft.com/download/dotnet/9.0) для Windows x64.
2. Установите и перезапустите `PC_HealthCheck.exe`.

### Первый запуск

1. Распакуйте zip в любую папку (например `C:\Tools\PC_HealthCheck\`).
2. Запустите `PC_HealthCheck.exe`.
3. При предупреждении SmartScreen: **Подробнее** → **Выполнить в любом случае** (для неподписанной учебной сборки это нормально).
4. При отсутствии датчиков температуры на ноутбуке запустите от имени администратора (по необходимости).

Подробная инструкция: [docs/USER_GUIDE.md](docs/USER_GUIDE.md).

## Документация

| Файл | Назначение |
|------|------------|
| [docs/USER_GUIDE.md](docs/USER_GUIDE.md) | Руководство пользователя (ГОСТ 19.505) |
| [docs/PROGRAMMER_GUIDE.md](docs/PROGRAMMER_GUIDE.md) | Руководство программиста |
| [DEVELOPMENT_LINUX_MACOS.md](DEVELOPMENT_LINUX_MACOS.md) | Сборка и запуск на Linux/macOS |

## Матрица платформ

| Платформа | Режим | Источники данных |
|-----------|--------|------------------|
| **Windows 10/11 x64** | Полный | WMI, LibreHardwareMonitor, WMI fallback, D3D11 GPU-стресс |
| **Linux x64** | Ограниченный | `/proc`, `lm-sensors`, `nvidia-smi` (при наличии) |
| **macOS** | Ограниченный | `sysctl`, `system_profiler`, частичная температура |
| **Прочие ОС** | Минимальный | `UnixHardwareProvider` |

Основная целевая платформа для релизных **exe** — **Windows x64**.

## Возможности

- Снимок системы (`DeviceSnapshot`): CPU, RAM, GPU, диски, сеть, сенсоры.
- Мониторинг в реальном времени с адаптивным графиком (без OxyPlot).
- Диагностика по порогам, SMART, эвристики узких мест.
- Стресс-тесты: CPU, RAM, GPU (D3D11 на Windows / SIMD fallback), диск.
- Бенчмарки CPU/RAM с сравнением с референсной таблицей.
- Отчёты: TXT, HTML, JSON, MD, CSV; сравнение двух снимков.
- Локальная история: SQLite (`%LocalAppData%\PC HealthCheck\`).
- AI-аналитика через локальный [Ollama](https://ollama.com).

## Быстрый старт (из исходников)

```bash
dotnet restore
dotnet build
dotnet run
```

### Публикация win-x64 локально

**Framework-dependent** (нужен Runtime 9):

```powershell
dotnet publish -c Release -r win-x64 --self-contained false `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o ./artifacts/framework/publish
```

**Self-contained** (Runtime не нужен):

```powershell
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o ./artifacts/selfcontained/publish
```

## CI и релизы

- **build.yml** — на каждый push/PR: сборка и артефакты `PC_HealthCheck_win-x64_framework.zip` и `PC_HealthCheck_win-x64_selfcontained.zip`.
- **release.yml** — при push тега `v*` (например `v1.0.0`): те же zip прикрепляются к GitHub Release.

```bash
git tag v1.0.0
git push origin v1.0.0
```

## AI-аналитика (Ollama)

```powershell
winget install Ollama.Ollama
ollama pull llama3.2
```

В приложении: вкладка **AI Аналитика** → **Проверить установку Ollama/модели** → ввод вопроса → **Запустить AI анализ**.

## FAQ

- **«Для запуска приложения требуется .NET»** — установите Desktop Runtime 9 или скачайте **selfcontained** zip.
- **Нет температуры CPU** — на части ноутбуков OEM; смотрите загрузку CPU % и RAM; при необходимости запуск от администратора.
- **GPU-стресс показывает «SIMD CPU»** — D3D11 недоступен; нагрузка идёт на процессор (fallback), не на видеокарту.
- **AI не работает** — не запущен Ollama или не скачана модель.
- **Ошибка БД** — не удаляйте `healthcheck_v2.db` при работе приложения; проверьте права на `%LocalAppData%\PC HealthCheck\`.

## Лицензия

Учебный дипломный проект. См. [LICENSE](LICENSE).
