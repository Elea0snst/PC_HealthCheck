# Руководство программиста

**Проект:** PC HealthCheck  
**Стек:** C# / .NET 9, Avalonia UI 11.3, SQLite, LibreHardwareMonitor  
**Репозиторий:** структура ниже относится к корню solution

---

## 1. Общие сведения

PC HealthCheck — настольное клиентское приложение с многослойной архитектурой и паттерном MVVM. Бизнес-логика не зависит от Avalonia; доступ к оборудованию инкапсулирован в `IHardwareProvider`. Целевая платформа релизных сборок — **win-x64**; Linux/macOS поддерживаются для разработки и демонстрации кроссплатформенного каркаса.

---

## 2. Структура репозитория

```
PC_HealthCheck/
├── Program.cs                 # Точка входа (Avalonia host)
├── App.axaml(.cs)             # Application, главное окно
├── ViewLocator.cs             # Сопоставление ViewModel → View
├── Core/                      # Доменные модели, настройки, enum
│   ├── Models.cs              # DeviceSnapshot, SensorReading, …
│   ├── UserAppSettings.cs
│   ├── StressTestProfile.cs
│   └── AiDiagnosticsModels.cs
├── DAL/                       # Данные и оборудование
│   ├── IHardwareProvider.cs
│   ├── HardwareProviderFactory.cs
│   ├── WindowsHardwareProvider.cs
│   ├── LinuxHardwareProvider.cs
│   ├── MacHardwareProvider.cs
│   ├── UnixHardwareProvider.cs
│   ├── WindowsMonitoringFallback.cs
│   ├── HardwareSensorCategorizer.cs
│   ├── DeviceSnapshotCloner.cs
│   ├── DatabaseService.cs
│   └── UserSettingsStore.cs
├── Business/                  # Сервисы приложения
│   ├── MonitoringService.cs
│   ├── SnapshotEnricher.cs
│   ├── MonitoringSensorAggregator.cs
│   ├── DiagnosticService.cs
│   ├── TestingService.cs
│   ├── GpuStressWorkload.cs
│   ├── WindowsD3D11GpuStress.cs
│   ├── BenchmarkService.cs
│   ├── ReportService.cs
│   ├── ReportSnapshotExtras.cs
│   ├── ReferenceScoreTable.cs
│   ├── AiOllamaService.cs
│   └── UtilitiesService.cs
├── ViewModels/
│   └── MainWindowViewModel.cs
├── Views/
│   ├── MainWindow.axaml(.cs)
│   └── MonitoringChartPanel.axaml(.cs)
├── Visualization/
│   ├── MonitoringSeriesBuffer.cs
│   └── MonitoringChartSnapshot.cs
├── docs/                      # USER_GUIDE, PROGRAMMER_GUIDE
└── .github/workflows/         # build.yml, release.yml
```

---

## 3. Сборка и публикация

### 3.1. Разработка

```bash
dotnet restore
dotnet build
dotnet run
```

Требуется **.NET SDK 9**.

### 3.2. Публикация Windows x64

**Framework-dependent** (нужен Desktop Runtime 9 у пользователя):

```powershell
dotnet publish -c Release -r win-x64 --self-contained false `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o ./artifacts/framework/publish
```

**Self-contained** (Runtime встроен):

```powershell
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o ./artifacts/selfcontained/publish
```

Артефакты CI упаковываются в:

- `PC_HealthCheck_win-x64_framework.zip`
- `PC_HealthCheck_win-x64_selfcontained.zip`

### 3.3. Версионирование

Версия задаётся в `PC_HealthCheck.csproj` (`Version`, `AssemblyVersion`, `FileVersion`). Для GitHub Release создайте тег:

```bash
git tag v1.0.0
git push origin v1.0.0
```

Workflow `release.yml` прикрепит оба zip к Release.

---

## 4. Точка входа и жизненный цикл

1. `Program.cs` — `BuildAvaloniaApp().StartWithClassicDesktopLifetime(args)`.
2. `App.axaml.cs` — инициализация Avalonia, `DataContext = new MainWindowViewModel()`.
3. `MainWindow.axaml` — привязки к ViewModel; `MainWindow.axaml.cs` подписывается на `ChartRefreshRequested` и вызывает `MonitoringChartPanel.Render`.
4. `MainWindowViewModel` — создаёт `HardwareProviderFactory.CreateDefault()`, `MonitoringService`, `TestingService`, `DatabaseService`, загружает настройки, запускает `InitializeAsync()`.

Длительные операции не блокируют UI: обновления коллекций — через `Dispatcher.UIThread.Post`.

---

## 5. Расширение: новый провайдер оборудования

1. Реализуйте `IHardwareProvider` в `DAL/`:
   - `InitializeAsync()` — однократная инициализация;
   - `ReadSnapshotAsync()` — заполнение `DeviceSnapshot`;
   - `Dispose()` — освобождение ресурсов.
2. Зарегистрируйте в `HardwareProviderFactory.CreateDefault()` по `RuntimeInformation.IsOSPlatform`.
3. Убедитесь, что `SnapshotEnricher` и `MonitoringSensorAggregator` корректно работают с вашими полями (или расширьте fallback).

**Windows + LHM:** доступ к `LibreHardwareMonitor` сериализуйте `lock` — библиотека не полностью потокобезопасна.

---

## 6. Подсистема мониторинга

Цепочка данных:

```
PeriodicTimer (MonitoringService)
  → IHardwareProvider.ReadSnapshotAsync()
  → SnapshotEnricher.Enrich()
  → событие SnapshotUpdated
  → MainWindowViewModel.UpdateView()
  → MonitoringSensorAggregator.Analyze()  // адаптивные каналы графика
  → MonitoringSeriesBuffer.Append()
  → BuildChartSnapshot() → MonitoringChartPanel.Render()
  → DatabaseService.SaveSnapshotAsync()   // best-effort
```

| Компонент | Назначение |
|-----------|------------|
| `SnapshotEnricher` | WMI fallback (Windows), заполнение CpuTemp/CpuLoad из агрегатора |
| `MonitoringSensorAggregator` | Извлечение метрик из `AllHardwareSensors`, `MonitoringChartCapabilities` |
| `MonitoringSeriesBuffer` | Кольцевой буфер до 720 точек, 7 каналов |
| `MonitoringChartPanel` | Отрисовка на `Canvas`, зоны warn/crit, две шкалы |

Графики реализованы **без OxyPlot** (снижение зависимостей и полный контроль отрисовки).

---

## 7. GPU-стресс

| Файл | Роль |
|------|------|
| `GpuStressWorkload.cs` | Публичный фасад `RunAsync` |
| `WindowsD3D11GpuStress.cs` | HLSL compute `cs_5_0`, UAV buffer, `Vortice.Direct3D11` |
| `TestingService.StartGpuTestAsync` | Интеграция, прогресс, `StressResult` |

На Windows сначала вызывается D3D11; при исключении — **SIMD fallback** на всех логических ядрах (`Vector<float>`).

**Внимание:** изменение шейдера, размера буфера или цикла dispatch влияет на нагрев GPU; тестируйте на VM/тестовом ПК. Не меняйте логику отмены (`CancellationToken`) без проверки UI.

---

## 8. База данных

**Файл:** `%LocalAppData%\PC HealthCheck\healthcheck_v2.db`  
**Класс:** `DAL/DatabaseService.cs`

| Таблица | Содержимое |
|---------|------------|
| `Snapshots` | Скаляры + JSON (`DeviceSnapshot`) |
| `Events` | Алерты, автоостановки |
| `TestRuns` | JSON (`StressResult`) |

PRAGMA: `journal_mode=WAL`, `busy_timeout=5000`.  
Синхронизация: `SemaphoreSlim`. Ошибки записи **подавляются** (best-effort), UI не падает.

---

## 9. Отчёты и сравнение снимков

- `ReportService.Build` / `BuildComparison` — всегда через `SnapshotEnricher.Enrich`.
- `ReportSnapshotExtras` — строки об ОС и ограничениях мониторинга.
- `DeviceSnapshotCloner.Clone` — базовый снимок для сравнения.
- Форматы: `ReportFormat` — Txt, Html, Json, Md, Csv.

Добавление формата: расширить enum и ветку в `ReportService.Build`.

---

## 10. CI/CD

### build.yml

Триггер: `push`, `pull_request`.  
Шаги: restore → build Release → publish (framework + self-contained) → zip → upload artifacts.

### release.yml

Триггер: тег `v*`.  
Создаёт GitHub Release с обоими zip и autogenerated release notes.

Локальная проверка перед тегом:

```powershell
dotnet build -c Release
dotnet publish ... # оба варианта, запуск exe
```

---

## 11. Отладка (типичные проблемы)

| Проблема | Причина | Подход |
|----------|---------|--------|
| Пустые температуры | OEM / нет LHM | `WindowsMonitoringFallback`, агрегатор |
| Исключение в LHM | Потоки | `lock` в `ReadSnapshotAsync` |
| SQLite busy | Частые INSERT | WAL, busy_timeout, SemaphoreSlim |
| UI не обновляется | Неверный поток | Только `Dispatcher.UIThread.Post` |
| GPU всегда SIMD | Нет D3D11 / драйвер | Ожидаемый fallback, проверить Видеоадаптер |
| CI publish fail | SDK / RID | `windows-latest`, `-r win-x64` |

---

## 12. Зависимости (NuGet)

| Пакет | Назначение |
|-------|------------|
| Avalonia 11.3.x | UI |
| CommunityToolkit.Mvvm | MVVM, команды |
| LibreHardwareMonitorLib | Сенсоры Windows |
| Microsoft.Data.Sqlite | Локальная БД |
| System.Management | WMI |
| System.Diagnostics.PerformanceCounter | Fallback CPU/RAM |
| Vortice.Direct3D11 / D3DCompiler | GPU stress |
| Ollama.NET | HTTP к Ollama |

---

## 13. Рекомендации по изменениям

- Не смешивайте бизнес-правила в `Views` — только в `Business` / `ViewModels`.
- Новые поля `DeviceSnapshot` — обратная совместимость JSON в SQLite (nullable/default).
- Перед PR: `dotnet build -c Release`, ручной прогон тест-кейсов из USER_GUIDE (контрольный пример).
- Не коммитить `bin/`, `obj/`, `.vs/`, `artifacts/`, `temp_build_out/`.

---

*Конец руководства программиста.*
