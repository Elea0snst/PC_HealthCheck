using Microsoft.Data.Sqlite;
using PC_HealthCheck.Core;
using System.Text.Json;
using System.Threading;

namespace PC_HealthCheck.DAL;

public sealed class DatabaseService : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SemaphoreSlim _dbLock = new(1, 1);

    public DatabaseService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PC HealthCheck");
        Directory.CreateDirectory(dir);
        var dbPath = Path.Combine(dir, "healthcheck_v2.db");

        _connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadWriteCreate");
        _connection.Open();
        Init();
    }

    private void Init()
    {
        // WAL + busy_timeout уменьшают вероятность блокировок при частых событиях мониторинга/AI.
        using (var pragma = _connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";
            pragma.ExecuteNonQuery();
        }

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS Snapshots(
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  TsUtc TEXT NOT NULL,
  CpuTemp REAL,
  CpuLoad REAL,
  RamUsage REAL,
  Json TEXT
);
CREATE TABLE IF NOT EXISTS Events(
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  TsUtc TEXT NOT NULL,
  Level TEXT NOT NULL,
  Kind TEXT NOT NULL,
  Message TEXT NOT NULL,
  Json TEXT
);
CREATE TABLE IF NOT EXISTS TestRuns(
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  TsUtc TEXT NOT NULL,
  TestName TEXT,
  Status TEXT,
  Error TEXT,
  Json TEXT
);";
        cmd.ExecuteNonQuery();
    }

    public async Task SaveSnapshotAsync(DeviceSnapshot s)
    {
        await _dbLock.WaitAsync().ConfigureAwait(false);
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText =
                "INSERT INTO Snapshots(TsUtc,CpuTemp,CpuLoad,RamUsage,Json) VALUES($t,$ct,$cl,$ru,$j)";
            cmd.Parameters.AddWithValue("$t", s.TimestampUtc.ToString("O"));
            cmd.Parameters.AddWithValue("$ct", s.CpuTemperatureC ?? 0d);
            cmd.Parameters.AddWithValue("$cl", s.CpuLoadPercent ?? 0d);
            cmd.Parameters.AddWithValue("$ru", s.RamUsagePercent);
            cmd.Parameters.AddWithValue("$j", JsonSerializer.Serialize(s));
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        catch
        {
            // best-effort: чтобы UI/приложение не падало из-за lock/file-busy SQLite.
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task SaveTestResultAsync(StressResult r)
    {
        await _dbLock.WaitAsync().ConfigureAwait(false);
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText =
                "INSERT INTO TestRuns(TsUtc,TestName,Status,Error,Json) VALUES($t,$n,$s,$e,$j)";
            cmd.Parameters.AddWithValue("$t", r.StartedUtc.ToString("O"));
            cmd.Parameters.AddWithValue("$n", r.TestName);
            cmd.Parameters.AddWithValue("$s", r.Status.ToString());
            cmd.Parameters.AddWithValue("$e", r.Error);
            cmd.Parameters.AddWithValue("$j", JsonSerializer.Serialize(r));
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        catch
        {
            // best-effort
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<List<SnapshotRow>> GetSnapshotsSinceUtcAsync(DateTime sinceUtc, int limit = 2000)
    {
        limit = Math.Clamp(limit, 1, 200_000);
        var list = new List<SnapshotRow>(Math.Min(limit, 4096));

        await _dbLock.WaitAsync().ConfigureAwait(false);
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
SELECT TsUtc, CpuTemp, CpuLoad, RamUsage
FROM Snapshots
WHERE TsUtc >= $t
ORDER BY TsUtc ASC
LIMIT $limit;";
            cmd.Parameters.AddWithValue("$t", sinceUtc.ToString("O"));
            cmd.Parameters.AddWithValue("$limit", limit);

            using var r = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
            while (await r.ReadAsync().ConfigureAwait(false))
            {
                var ts = r.GetString(0);
                _ = DateTime.TryParse(ts, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt);
                list.Add(new SnapshotRow
                {
                    TsUtc = dt == default ? DateTime.UtcNow : dt,
                    CpuTemp = r.IsDBNull(1) ? null : r.GetDouble(1),
                    CpuLoad = r.IsDBNull(2) ? null : r.GetDouble(2),
                    RamUsage = r.IsDBNull(3) ? null : r.GetDouble(3)
                });
            }

            return list;
        }
        catch
        {
            return new List<SnapshotRow>();
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task SaveEventAsync(EventRow e)
    {
        await _dbLock.WaitAsync().ConfigureAwait(false);
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "INSERT INTO Events(TsUtc,Level,Kind,Message,Json) VALUES($t,$l,$k,$m,$j)";
            cmd.Parameters.AddWithValue("$t", e.TsUtc.ToString("O"));
            cmd.Parameters.AddWithValue("$l", e.Level ?? "INFO");
            cmd.Parameters.AddWithValue("$k", e.Kind ?? "Event");
            cmd.Parameters.AddWithValue("$m", e.Message ?? "");
            cmd.Parameters.AddWithValue("$j", string.IsNullOrWhiteSpace(e.Json) ? "" : e.Json);
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        catch
        {
            // best-effort
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<List<EventRow>> GetRecentEventsUtcAsync(DateTime sinceUtc, int limit = 2000)
    {
        limit = Math.Clamp(limit, 1, 200_000);
        var list = new List<EventRow>(Math.Min(limit, 2048));
        await _dbLock.WaitAsync().ConfigureAwait(false);
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
SELECT TsUtc, Level, Kind, Message, Json
FROM Events
WHERE TsUtc >= $t
ORDER BY TsUtc DESC
LIMIT $limit;";
            cmd.Parameters.AddWithValue("$t", sinceUtc.ToString("O"));
            cmd.Parameters.AddWithValue("$limit", limit);

            using var r = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
            while (await r.ReadAsync().ConfigureAwait(false))
            {
                var ts = r.GetString(0);
                _ = DateTime.TryParse(ts, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt);
                list.Add(new EventRow
                {
                    TsUtc = dt == default ? DateTime.UtcNow : dt,
                    Level = r.IsDBNull(1) ? "INFO" : r.GetString(1),
                    Kind = r.IsDBNull(2) ? "Event" : r.GetString(2),
                    Message = r.IsDBNull(3) ? "" : r.GetString(3),
                    Json = r.IsDBNull(4) ? "" : r.GetString(4)
                });
            }

            return list;
        }
        catch
        {
            return new List<EventRow>();
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public void Dispose() => _connection.Dispose();
}

public sealed class SnapshotRow
{
    public DateTime TsUtc { get; set; }
    public double? CpuTemp { get; set; }
    public double? CpuLoad { get; set; }
    public double? RamUsage { get; set; }
}

public sealed class EventRow
{
    public DateTime TsUtc { get; set; } = DateTime.UtcNow;
    public string Level { get; set; } = "INFO";
    public string Kind { get; set; } = "Event";
    public string Message { get; set; } = "";
    public string Json { get; set; } = "";
}

