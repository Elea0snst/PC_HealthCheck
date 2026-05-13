using System.Text;
using System.Text.Json;
using PC_HealthCheck.Core;
using PC_HealthCheck.DAL;

namespace PC_HealthCheck.Business;

public sealed class AiOllamaService
{
    private static double BytesToGbDouble(long bytes) => bytes > 0 ? bytes / 1024d / 1024d / 1024d : 0;
    private static string BytesToGbString(long bytes) => bytes > 0 ? (bytes / 1024d / 1024d / 1024d).ToString("F1") : "0";

    public async Task<(bool Ok, string Detail, HashSet<string> Models)> CheckOllamaAsync(string baseUrl, CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
            using var res = await http.GetAsync("/api/tags", ct);
            var text = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
                return (false, $"Ollama недоступна: HTTP {(int)res.StatusCode}", new HashSet<string>(StringComparer.OrdinalIgnoreCase));

            var models = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("models", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in arr.EnumerateArray())
                {
                    if (m.TryGetProperty("name", out var n))
                    {
                        var name = n.GetString();
                        if (!string.IsNullOrWhiteSpace(name))
                            models.Add(name);
                    }
                }
            }
            return (true, $"Ollama OK. Установлено моделей: {models.Count}.", models);
        }
        catch (Exception ex)
        {
            return (false, $"Ошибка: {ex.Message}", new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }
    }

    public string BuildUserQuestionPrompt(
        IReadOnlyList<SnapshotRow> history,
        DeviceSnapshot current,
        IReadOnlyList<ProcessEntry> recentProcesses,
        string userQuestion)
    {
        var cpuTemp = current.CpuTemperatureC?.ToString("F1") ?? "нет данных";
        var cpuLoad = current.CpuLoadPercent?.ToString("F0") ?? "нет данных";
        var ramUsage = current.RamUsagePercent.ToString("F0");
        var totalRam = BytesToGbString(current.TotalRamBytes);

        var topProcs = string.Join(", ", recentProcesses.Take(5).Select(p =>
        {
            var mb = p.WorkingSetBytes / 1024d / 1024d;
            return $"{p.Name} ({mb:F0}МБ)";
        }));

        var cpuName = current.CpuName.Length > 80 ? current.CpuName.Substring(0, 77) + "..." : current.CpuName;

        return $@"Ты профессиональный компьютерный эксперт с 10-летним опытом. Отвечай ТОЛЬКО на русском языке.

Вопрос пользователя: {userQuestion}

Данные мониторинга ПК:
- Процессор: {cpuName}
- Температура CPU: {cpuTemp}°C
- Загрузка CPU: {cpuLoad}%
- Использование RAM: {ramUsage}%
- Всего RAM: {totalRam} ГБ
- Самые требовательные программы: {topProcs}

ТРЕБОВАНИЯ К ОТВЕТУ:
1. Ответ должен быть ОЧЕНЬ ПОДРОБНЫМ - минимум 40-60 предложений (3000-5000 символов)
2. Обязательно напиши:
   - Сначала краткий ответ на вопрос (5 предложений)
   - Затем подробный разбор причин проблемы (10-15 предложений)
   - Затем пошаговую инструкцию по решению (10-15 предложений)
   - Затем теоретическое объяснение как работает компонент (10 предложений)
   - Затем профилактические советы на будущее (10 предложений)
3. Если видишь проблемы в данных - опиши их и дай рекомендации
4. Пиши простым понятным русским языком
5. НЕ ИСПОЛЬЗУЙ английские слова, если можно сказать по-русски
6. НЕ ОБРЕЗАЙ ОТВЕТ - пиши максимально подробно

Напиши обычный текст, без JSON, без markdown, без кавычек. Просто развёрнутый ответ.";
    }

    public async Task<string> RunAiDiagnosticsAsync(
        string baseUrl,
        string model,
        string prompt,
        CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
            http.Timeout = TimeSpan.FromSeconds(180);

            var body = new
            {
                model,
                prompt,
                stream = false,
                options = new
                {
                    temperature = 0.7,
                    num_predict = 8000,
                    num_ctx = 8192
                }
            };

            var json = JsonSerializer.Serialize(body);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var res = await http.PostAsync("/api/generate", content, ct);
            var text = await res.Content.ReadAsStringAsync(ct);

            if (!res.IsSuccessStatusCode)
                return $"Ошибка HTTP: {(int)res.StatusCode}";

            using var doc = JsonDocument.Parse(text);
            var raw = doc.RootElement.TryGetProperty("response", out var r) ? (r.GetString() ?? "") : text;

            return raw;
        }
        catch (Exception ex)
        {
            return $"Ошибка: {ex.Message}";
        }
    }
}