using PC_HealthCheck.Core;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;

namespace PC_HealthCheck.Business;

public enum ReportFormat
{
    Txt,
    Html,
    Json,
    /// <summary>Табличный экспорт (ТЗ 2.4.2).</summary>
    Csv
}

public sealed class ReportService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private static void AppendSensorGroup(StringBuilder sb, string title, IReadOnlyList<SensorReading> list)
    {
        if (list.Count == 0)
            return;
        sb.AppendLine();
        sb.AppendLine($"=== {title} ===");
        foreach (var sr in list.OrderBy(x => x.Name))
            sb.AppendLine($"{sr.Name} = {sr.Value:F2} {sr.Unit}".TrimEnd());
    }

    public string Build(DeviceSnapshot s, ReportFormat format, string? userComment = null)
    {
        return format switch
        {
            ReportFormat.Txt => BuildTxt(s, userComment),
            ReportFormat.Html => BuildHtml(s, userComment),
            ReportFormat.Json => JsonSerializer.Serialize(s, JsonOpts),
            ReportFormat.Csv => BuildCsv(s, userComment),
            _ => BuildTxt(s, userComment)
        };
    }

    public string BuildComparison(
        DeviceSnapshot baseline,
        DeviceSnapshot current,
        ReportFormat format,
        string? baselineLabel = null,
        string? currentLabel = null,
        string? userComment = null)
    {
        var cmp = BuildComparisonData(baseline, current, baselineLabel, currentLabel);
        return format switch
        {
            ReportFormat.Txt => BuildComparisonTxt(cmp, userComment),
            ReportFormat.Html => BuildComparisonHtml(cmp, userComment),
            ReportFormat.Json => JsonSerializer.Serialize(cmp, JsonOpts),
            ReportFormat.Csv => BuildComparisonCsv(cmp, userComment),
            _ => BuildComparisonTxt(cmp, userComment)
        };
    }

    public string Save(string content, ReportFormat format)
    {
        var ext = format switch
        {
            ReportFormat.Txt => "txt",
            ReportFormat.Html => "html",
            ReportFormat.Csv => "csv",
            _ => "json"
        };
        var dir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var path = Path.Combine(dir, $"PCHealthCheck_{DateTime.Now:yyyyMMdd_HHmmss}.{ext}");
        File.WriteAllText(path, content, Encoding.UTF8);
        return path;
    }

    private static string BuildTxt(DeviceSnapshot s, string? userComment)
    {
        var sb = new StringBuilder();
        sb.AppendLine("PC HealthCheck — отчёт о системе");
        sb.AppendLine($"Дата (локальное время): {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        if (!string.IsNullOrWhiteSpace(userComment))
        {
            sb.AppendLine();
            sb.AppendLine("=== Комментарий пользователя (ТЗ 2.4.1) ===");
            sb.AppendLine(userComment.Trim());
        }

        sb.AppendLine();
        sb.AppendLine("=== CPU ===");
        sb.AppendLine(s.CpuName);
        sb.AppendLine($"Производитель: {s.CpuManufacturer}");
        sb.AppendLine($"Ядра / потоки: {s.CpuCores} / {s.CpuThreads}, макс. частота: {s.CpuMaxClockMHz} MHz");
        sb.AppendLine($"Family / Model / Stepping: {s.CpuFamily} / {s.CpuModel} / {s.CpuStepping}");
        if (!string.IsNullOrEmpty(s.CpuSignatureRaw))
            sb.AppendLine($"ProcessorId / сигнатура: {s.CpuSignatureRaw}");
        if (!string.IsNullOrEmpty(s.CpuMicroarchitectureHint))
            sb.AppendLine($"Подсказка по микроархитектуре: {s.CpuMicroarchitectureHint}");
        sb.AppendLine($"Инструкции (видимые процессу): {s.CpuInstructionSets}");
        sb.AppendLine($"Температура: {(s.CpuTemperatureC?.ToString("F1") ?? "N/A")} °C");
        sb.AppendLine($"Загрузка: {(s.CpuLoadPercent?.ToString("F0") ?? "N/A")} %");
        sb.AppendLine();

        sb.AppendLine("=== RAM / ОС ===");
        sb.AppendLine($"Тип (по модулю): {s.RamType}");
        sb.AppendLine($"Видимо ОС: {FmtGb(s.TotalRamBytes)} GB всего, {FmtGb(s.FreeRamBytes)} свободно, использование {s.RamUsagePercent:F1}%");
        foreach (var a in s.MemoryArrays)
            sb.AppendLine($"Массив памяти: {a.Use}, слотов: {a.MemoryDevices}, макс. {FmtGb(a.MaxCapacityBytes)} GB, ECC: {a.ErrorCorrection}");
        sb.AppendLine("Модули (Win32_PhysicalMemory):");
        foreach (var m in s.MemoryModules)
        {
            var cfg = m.ConfiguredClockSpeedMHz > 0 ? $", сконфиг. {m.ConfiguredClockSpeedMHz} МГц" : "";
            var uv = m.ConfiguredVoltageMilliVolts > 0 ? $", U={m.ConfiguredVoltageMilliVolts} мВ" : "";
            sb.AppendLine(
                $"  - {m.DeviceLocator} / {m.BankLabel}: {FmtGb(m.CapacityBytes)} GB @ {m.SpeedMHz} MHz{cfg}{uv}, {m.FormFactor}, {m.Manufacturer}, {m.PartNumber}, S/N {m.SerialNumber}, {m.TypeDetail}");
        }

        if (s.RamTimingSummaryLines.Count > 0)
        {
            sb.AppendLine("Пояснения RAM (WMI, не SPD):");
            foreach (var line in s.RamTimingSummaryLines)
                sb.AppendLine($"  {line}");
        }

        if (s.MemorySlots.Count > 0)
        {
            sb.AppendLine("Слоты (Win32_MemoryDevice):");
            foreach (var x in s.MemorySlots)
            {
                var cap = x.CapacityBytes.HasValue ? $"{FmtGb(x.CapacityBytes.Value)} GB" : "пусто / неизвестно";
                sb.AppendLine($"  - {x.DeviceLocator} / {x.BankLabel}: {cap}, {x.SpeedMHz} MHz, {x.FormFactor}, {x.MemoryType}, {x.TypeDetail}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("=== Плата / BIOS ===");
        sb.AppendLine($"{s.MotherboardManufacturer} {s.MotherboardModel}");
        sb.AppendLine($"BIOS: {s.BiosVersion}, дата: {s.BiosDate}");
        sb.AppendLine();

        sb.AppendLine("=== GPU ===");
        foreach (var g in s.Gpus)
            sb.AppendLine($"{g.Name} | {g.Manufacturer} | драйвер {g.DriverVersion} | VRAM {FmtGb(g.VramBytes)} GB");

        sb.AppendLine();
        sb.AppendLine("=== PCI (Win32_PnPEntity, PCI\\) ===");
        foreach (var p in s.PciDevices)
            sb.AppendLine($"{p.Name} | {p.Manufacturer} | {p.PnpClass} | {p.PnpDeviceId}");

        sb.AppendLine();
        sb.AppendLine("=== Сеть (физические адаптеры) ===");
        foreach (var n in s.NetworkAdapters)
        {
            var mb = n.SpeedBitsPerSec > 0 ? $"{n.SpeedBitsPerSec / 1_000_000} Mbps" : "?";
            sb.AppendLine($"{n.Name} | {n.Manufacturer} | MAC {n.MacAddress} | {mb} | {(n.NetEnabled ? "вкл" : "выкл")} | {n.NetConnectionId} | {n.AdapterType}");
        }

        sb.AppendLine();
        sb.AppendLine("=== Периферия / USB ===");
        foreach (var p in s.Peripherals)
            sb.AppendLine($"[{p.Category}] {p.Name} | {p.Manufacturer}");

        sb.AppendLine();
        sb.AppendLine("=== Принтеры ===");
        foreach (var p in s.Printers)
            sb.AppendLine($"{p.Name} | {p.DriverName} | {p.PortName} | default={p.Default} | network={p.Network}");

        sb.AppendLine();
        sb.AppendLine("=== Диски ===");
        foreach (var d in s.PhysicalDisks)
        {
            sb.AppendLine($"[Физ.] {d.DeviceId}: {d.Model}, S/N {d.SerialNumber}, {d.InterfaceType}, {d.MediaType}, {FmtGb(d.SizeBytes)} GB, разделов: {d.Partitions}");
            if (d.SmartPredictFailure.HasValue)
                sb.AppendLine($"  SMART PredictFailure={d.SmartPredictFailure} {d.SmartReason}");
            foreach (var a in d.SmartAttributes)
                sb.AppendLine($"  SMART {a.Name} (id {a.Id}): current={a.Current} worst={a.Worst} thr={a.Threshold} raw={a.RawValue}");
        }

        foreach (var l in s.LogicalDisks)
        {
            var used = l.SizeBytes > 0 ? (1.0 - (double)l.FreeBytes / l.SizeBytes) * 100.0 : 0;
            sb.AppendLine($"[Лог.] {l.DeviceId} {l.VolumeName} {l.FileSystem} {FmtGb(l.SizeBytes)} GB, свободно {FmtGb(l.FreeBytes)} GB ({used:F1}% занято)");
        }

        sb.AppendLine();
        sb.AppendLine("=== Сенсоры CPU/GPU (LibreHardwareMonitor) ===");
        foreach (var sr in s.CpuSensors.OrderBy(x => x.Kind).ThenBy(x => x.Name))
            sb.AppendLine($"{sr.Kind}: {sr.Name} = {sr.Value:F1}{sr.Unit}");

        AppendSensorGroup(sb, "Частоты ядер CPU (LHM)", s.CpuPerCoreClocks);
        AppendSensorGroup(sb, "Напряжения ядер / VID (LHM)", s.CpuPerCoreVoltages);
        AppendSensorGroup(sb, "VRM / материнская плата / PCH (LHM)", s.VrmChipsetSensors);
        AppendSensorGroup(sb, "Вентиляторы (LHM)", s.FanSensors);
        AppendSensorGroup(sb, "Температура накопителей (LHM)", s.StorageTemperatureSensors);

        return sb.ToString();
    }

    private static string BuildHtml(DeviceSnapshot s, string? userComment)
    {
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html><head><meta charset='utf-8'><title>PC HealthCheck</title>");
        sb.Append("<style>body{font-family:Segoe UI;margin:16px} h2,h3{color:#0D1B2A} table{border-collapse:collapse;margin:8px 0} td,th{border:1px solid #ccc;padding:4px 8px;font-size:13px} .muted{color:#555}</style>");
        sb.Append("</head><body>");
        sb.Append("<h2>PC HealthCheck — отчёт</h2>");
        sb.Append($"<p class='muted'>Дата: {WebUtility.HtmlEncode(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))}</p>");
        if (!string.IsNullOrWhiteSpace(userComment))
        {
            sb.Append("<h3>Комментарий пользователя</h3><p>")
                .Append(WebUtility.HtmlEncode(userComment.Trim())).Append("</p>");
        }

        void h3(string t) => sb.Append("<h3>").Append(WebUtility.HtmlEncode(t)).Append("</h3>");

        h3("CPU");
        sb.Append("<p>").Append(WebUtility.HtmlEncode(s.CpuName)).Append("</p><ul>");
        sb.Append($"<li>{WebUtility.HtmlEncode($"Ядра/потоки: {s.CpuCores}/{s.CpuThreads}, {s.CpuMaxClockMHz} MHz")}</li>");
        sb.Append($"<li>{WebUtility.HtmlEncode($"Family/Model/Stepping: {s.CpuFamily}/{s.CpuModel}/{s.CpuStepping}")}</li>");
        if (!string.IsNullOrEmpty(s.CpuSignatureRaw))
            sb.Append("<li>").Append(WebUtility.HtmlEncode("Сигнатура: " + s.CpuSignatureRaw)).Append("</li>");
        if (!string.IsNullOrEmpty(s.CpuMicroarchitectureHint))
            sb.Append("<li>").Append(WebUtility.HtmlEncode(s.CpuMicroarchitectureHint)).Append("</li>");
        sb.Append("<li>").Append(WebUtility.HtmlEncode("Инструкции: " + s.CpuInstructionSets)).Append("</li>");
        sb.Append($"<li>Температура: {(s.CpuTemperatureC?.ToString("F1") ?? "N/A")} °C, загрузка: {(s.CpuLoadPercent?.ToString("F0") ?? "N/A")} %</li>");
        sb.Append("</ul>");

        h3("RAM");
        sb.Append("<ul>");
        sb.Append($"<li>{WebUtility.HtmlEncode($"{s.RamType}, ОС видит {FmtGb(s.TotalRamBytes)} GB, свободно {FmtGb(s.FreeRamBytes)} GB, {s.RamUsagePercent:F1}%")}</li>");
        sb.Append("</ul>");
        if (s.MemoryArrays.Count > 0)
        {
            sb.Append("<table><tr><th>Массив</th><th>Слотов</th><th>Макс. GB</th><th>ECC</th></tr>");
            foreach (var a in s.MemoryArrays)
                sb.Append("<tr><td>").Append(WebUtility.HtmlEncode(a.Use)).Append("</td><td>").Append(a.MemoryDevices)
                    .Append("</td><td>").Append(FmtGb(a.MaxCapacityBytes)).Append("</td><td>")
                    .Append(WebUtility.HtmlEncode(a.ErrorCorrection)).Append("</td></tr>");
            sb.Append("</table>");
        }

        sb.Append("<table><tr><th>Слот</th><th>Ёмкость</th><th>MHz</th><th>Форм-фактор</th><th>Производитель / партномер</th></tr>");
        foreach (var m in s.MemoryModules)
            sb.Append("<tr><td>").Append(WebUtility.HtmlEncode($"{m.DeviceLocator} {m.BankLabel}")).Append("</td><td>")
                .Append(FmtGb(m.CapacityBytes)).Append("</td><td>").Append(m.SpeedMHz).Append("</td><td>")
                .Append(WebUtility.HtmlEncode(m.FormFactor)).Append("</td><td>")
                .Append(WebUtility.HtmlEncode($"{m.Manufacturer} {m.PartNumber}")).Append("</td></tr>");
        sb.Append("</table>");

        h3("Плата / BIOS / GPU");
        sb.Append("<p>").Append(WebUtility.HtmlEncode($"{s.MotherboardManufacturer} {s.MotherboardModel}")).Append("<br/>");
        sb.Append(WebUtility.HtmlEncode($"BIOS {s.BiosVersion} ({s.BiosDate})")).Append("</p>");
        sb.Append("<ul>");
        foreach (var g in s.Gpus)
            sb.Append("<li>").Append(WebUtility.HtmlEncode($"{g.Name} — {g.Manufacturer}, {g.DriverVersion}, VRAM {FmtGb(g.VramBytes)} GB")).Append("</li>");
        sb.Append("</ul>");

        h3("PCI");
        sb.Append("<table><tr><th>Устройство</th><th>Класс</th><th>PNP ID</th></tr>");
        foreach (var p in s.PciDevices)
            sb.Append("<tr><td>").Append(WebUtility.HtmlEncode(p.Name)).Append("</td><td>")
                .Append(WebUtility.HtmlEncode(p.PnpClass)).Append("</td><td class='muted'>")
                .Append(WebUtility.HtmlEncode(p.PnpDeviceId)).Append("</td></tr>");
        sb.Append("</table>");

        h3("Сеть");
        sb.Append("<table><tr><th>Адаптер</th><th>MAC</th><th>Скорость</th><th>Состояние</th></tr>");
        foreach (var n in s.NetworkAdapters)
        {
            var mb = n.SpeedBitsPerSec > 0 ? $"{n.SpeedBitsPerSec / 1_000_000} Mbps" : "?";
            sb.Append("<tr><td>").Append(WebUtility.HtmlEncode(n.Name)).Append("</td><td>")
                .Append(WebUtility.HtmlEncode(n.MacAddress)).Append("</td><td>").Append(mb).Append("</td><td>")
                .Append(n.NetEnabled ? "вкл" : "выкл").Append("</td></tr>");
        }

        sb.Append("</table>");

        h3("Периферия и принтеры");
        sb.Append("<ul>");
        foreach (var p in s.Peripherals)
            sb.Append("<li>").Append(WebUtility.HtmlEncode($"[{p.Category}] {p.Name}")).Append("</li>");
        foreach (var p in s.Printers)
            sb.Append("<li>").Append(WebUtility.HtmlEncode($"Принтер: {p.Name} ({p.PortName})")).Append("</li>");
        sb.Append("</ul>");

        h3("Диски и SMART");
        sb.Append("<table><tr><th>Диск</th><th>Модель</th><th>Размер GB</th><th>SMART</th></tr>");
        foreach (var d in s.PhysicalDisks)
        {
            var sm = d.SmartPredictFailure is { } b ? (b ? "FAIL " : "OK ") + d.SmartReason : "н/д";
            sb.Append("<tr><td>").Append(WebUtility.HtmlEncode(d.DeviceId)).Append("</td><td>")
                .Append(WebUtility.HtmlEncode(d.Model)).Append("</td><td>").Append(FmtGb(d.SizeBytes)).Append("</td><td>")
                .Append(WebUtility.HtmlEncode(sm)).Append("</td></tr>");
        }

        sb.Append("</table>");
        if (s.PhysicalDisks.Any(d => d.SmartAttributes.Count > 0))
        {
            sb.Append("<table><tr><th>Диск</th><th>Атрибут</th><th>Current</th><th>Worst</th><th>Raw</th></tr>");
            foreach (var d in s.PhysicalDisks)
            foreach (var a in d.SmartAttributes)
                sb.Append("<tr><td>").Append(WebUtility.HtmlEncode(d.Model)).Append("</td><td>")
                    .Append(WebUtility.HtmlEncode(a.Name)).Append("</td><td>").Append(a.Current).Append("</td><td>")
                    .Append(a.Worst).Append("</td><td>").Append(a.RawValue).Append("</td></tr>");
            sb.Append("</table>");
        }

        sb.Append("<h3>Логические тома</h3><table><tr><th>Том</th><th>ФС</th><th>Размер GB</th><th>Свободно GB</th></tr>");
        foreach (var l in s.LogicalDisks)
            sb.Append("<tr><td>").Append(WebUtility.HtmlEncode($"{l.DeviceId} {l.VolumeName}")).Append("</td><td>")
                .Append(WebUtility.HtmlEncode(l.FileSystem)).Append("</td><td>").Append(FmtGb(l.SizeBytes)).Append("</td><td>")
                .Append(FmtGb(l.FreeBytes)).Append("</td></tr>");
        sb.Append("</table>");

        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static string BuildCsv(DeviceSnapshot s, string? userComment)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Section,Key,Value");
        void row(string sec, string key, string? val)
        {
            sb.Append(Csv(sec)).Append(',').Append(Csv(key)).Append(',').AppendLine(Csv(val));
        }

        row("Meta", "ExportedUtc", DateTime.UtcNow.ToString("O"));
        if (!string.IsNullOrWhiteSpace(userComment))
            row("Meta", "UserComment", userComment.Trim());
        row("CPU", "Name", s.CpuName);
        row("CPU", "Manufacturer", s.CpuManufacturer);
        row("CPU", "Cores", s.CpuCores.ToString());
        row("CPU", "Threads", s.CpuThreads.ToString());
        row("CPU", "TempC", s.CpuTemperatureC?.ToString("F1"));
        row("CPU", "LoadPercent", s.CpuLoadPercent?.ToString("F0"));
        row("RAM", "Type", s.RamType);
        row("RAM", "TotalGb", FmtGb(s.TotalRamBytes));
        row("RAM", "UsagePercent", s.RamUsagePercent.ToString("F1"));
        foreach (var d in s.PhysicalDisks)
        {
            row("Disk", "Model", d.Model);
            row("Disk", "SmartPredictFailure", d.SmartPredictFailure?.ToString());
        }

        return sb.ToString();
    }

    private static SnapshotComparisonReport BuildComparisonData(
        DeviceSnapshot baseline,
        DeviceSnapshot current,
        string? baselineLabel,
        string? currentLabel)
    {
        var lines = new List<string>();

        static string Delta(double value) => value >= 0 ? $"+{value:F1}" : $"{value:F1}";
        static string DeltaInt(int value) => value >= 0 ? $"+{value}" : value.ToString();

        if (baseline.CpuTemperatureC is { } bTemp && current.CpuTemperatureC is { } cTemp)
            lines.Add($"CPU температура: {bTemp:F1}°C -> {cTemp:F1}°C ({Delta(cTemp - bTemp)}°C)");
        else
            lines.Add("CPU температура: недостаточно данных для сравнения.");

        if (baseline.CpuLoadPercent is { } bLoad && current.CpuLoadPercent is { } cLoad)
            lines.Add($"CPU загрузка: {bLoad:F0}% -> {cLoad:F0}% ({Delta(cLoad - bLoad)} п.п.)");
        else
            lines.Add("CPU загрузка: недостаточно данных для сравнения.");

        lines.Add($"RAM использование: {baseline.RamUsagePercent:F1}% -> {current.RamUsagePercent:F1}% ({Delta(current.RamUsagePercent - baseline.RamUsagePercent)} п.п.)");
        lines.Add($"RAM свободно: {FmtGb(baseline.FreeRamBytes)} GB -> {FmtGb(current.FreeRamBytes)} GB ({Delta(current.FreeRamBytes / 1024d / 1024d / 1024d - baseline.FreeRamBytes / 1024d / 1024d / 1024d)} GB)");

        var baseDisks = baseline.LogicalDisks.ToDictionary(x => x.DeviceId, StringComparer.OrdinalIgnoreCase);
        var currDisks = current.LogicalDisks.ToDictionary(x => x.DeviceId, StringComparer.OrdinalIgnoreCase);
        foreach (var kv in currDisks.OrderBy(x => x.Key))
        {
            if (!baseDisks.TryGetValue(kv.Key, out var b) || kv.Value.SizeBytes <= 0 || b.SizeBytes <= 0)
                continue;
            var bUsed = (1.0 - (double)b.FreeBytes / b.SizeBytes) * 100.0;
            var cUsed = (1.0 - (double)kv.Value.FreeBytes / kv.Value.SizeBytes) * 100.0;
            lines.Add($"Диск {kv.Key}: занято {bUsed:F1}% -> {cUsed:F1}% ({Delta(cUsed - bUsed)} п.п.)");
        }

        lines.Add($"Сетевые адаптеры: {baseline.NetworkAdapters.Count} -> {current.NetworkAdapters.Count} ({DeltaInt(current.NetworkAdapters.Count - baseline.NetworkAdapters.Count)})");
        lines.Add($"GPU обнаружено: {baseline.Gpus.Count} -> {current.Gpus.Count} ({DeltaInt(current.Gpus.Count - baseline.Gpus.Count)})");

        return new SnapshotComparisonReport
        {
            BaselineUtc = baseline.TimestampUtc,
            CurrentUtc = current.TimestampUtc,
            BaselineLabel = string.IsNullOrWhiteSpace(baselineLabel) ? "До" : baselineLabel.Trim(),
            CurrentLabel = string.IsNullOrWhiteSpace(currentLabel) ? "После" : currentLabel.Trim(),
            Lines = lines
        };
    }

    private static string BuildComparisonTxt(SnapshotComparisonReport cmp, string? userComment)
    {
        var sb = new StringBuilder();
        sb.AppendLine("PC HealthCheck — сравнительный отчет (до/после)");
        sb.AppendLine($"Сформирован: {cmp.GeneratedUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"{cmp.BaselineLabel}: {cmp.BaselineUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"{cmp.CurrentLabel}: {cmp.CurrentUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
        if (!string.IsNullOrWhiteSpace(userComment))
        {
            sb.AppendLine();
            sb.AppendLine("Комментарий:");
            sb.AppendLine(userComment.Trim());
        }

        sb.AppendLine();
        sb.AppendLine("=== Изменения ===");
        foreach (var line in cmp.Lines)
            sb.AppendLine($"- {line}");
        return sb.ToString();
    }

    private static string BuildComparisonHtml(SnapshotComparisonReport cmp, string? userComment)
    {
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html><head><meta charset='utf-8'><title>PC HealthCheck comparison</title>");
        sb.Append("<style>body{font-family:Segoe UI;margin:16px} h2,h3{color:#0D1B2A} li{margin:6px 0} .muted{color:#555}</style>");
        sb.Append("</head><body>");
        sb.Append("<h2>PC HealthCheck — сравнительный отчет</h2>");
        sb.Append("<p class='muted'>Сформирован: ").Append(WebUtility.HtmlEncode(cmp.GeneratedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"))).Append("</p>");
        sb.Append("<p>").Append(WebUtility.HtmlEncode($"{cmp.BaselineLabel}: {cmp.BaselineUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}")).Append("<br/>")
            .Append(WebUtility.HtmlEncode($"{cmp.CurrentLabel}: {cmp.CurrentUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}")).Append("</p>");
        if (!string.IsNullOrWhiteSpace(userComment))
            sb.Append("<p><b>Комментарий:</b> ").Append(WebUtility.HtmlEncode(userComment.Trim())).Append("</p>");
        sb.Append("<h3>Изменения</h3><ul>");
        foreach (var line in cmp.Lines)
            sb.Append("<li>").Append(WebUtility.HtmlEncode(line)).Append("</li>");
        sb.Append("</ul></body></html>");
        return sb.ToString();
    }

    private static string BuildComparisonCsv(SnapshotComparisonReport cmp, string? userComment)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Section,Key,Value");
        sb.AppendLine($"{Csv("Meta")},{Csv("GeneratedUtc")},{Csv(cmp.GeneratedUtc.ToString("O"))}");
        sb.AppendLine($"{Csv("Meta")},{Csv("BaselineUtc")},{Csv(cmp.BaselineUtc.ToString("O"))}");
        sb.AppendLine($"{Csv("Meta")},{Csv("CurrentUtc")},{Csv(cmp.CurrentUtc.ToString("O"))}");
        if (!string.IsNullOrWhiteSpace(userComment))
            sb.AppendLine($"{Csv("Meta")},{Csv("UserComment")},{Csv(userComment.Trim())}");
        foreach (var line in cmp.Lines)
            sb.AppendLine($"{Csv("Diff")},{Csv("Line")},{Csv(line)}");
        return sb.ToString();
    }

    private static string Csv(string? value)
    {
        var v = value ?? "";
        if (v.Contains('"')) v = v.Replace("\"", "\"\"");
        if (v.Contains(',') || v.Contains('\n') || v.Contains('\r'))
            return $"\"{v}\"";
        return string.IsNullOrEmpty(v) ? "\"\"" : v;
    }

    private static string FmtGb(long bytes) => (bytes / 1024d / 1024d / 1024d).ToString("F2");
}
