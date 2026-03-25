using Microsoft.Data.Sqlite;
using PC_HealthCheck.Core;
using System.Text.Json;

namespace PC_HealthCheck.DAL;

public sealed class DatabaseService : IDisposable
{
    private readonly SqliteConnection _connection;

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
        using var cmd = _connection.CreateCommand();
        cmd.CommandText =
            "INSERT INTO Snapshots(TsUtc,CpuTemp,CpuLoad,RamUsage,Json) VALUES($t,$ct,$cl,$ru,$j)";
        cmd.Parameters.AddWithValue("$t", s.TimestampUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$ct", s.CpuTemperatureC ?? 0d);
        cmd.Parameters.AddWithValue("$cl", s.CpuLoadPercent ?? 0d);
        cmd.Parameters.AddWithValue("$ru", s.RamUsagePercent);
        cmd.Parameters.AddWithValue("$j", JsonSerializer.Serialize(s));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task SaveTestResultAsync(StressResult r)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText =
            "INSERT INTO TestRuns(TsUtc,TestName,Status,Error,Json) VALUES($t,$n,$s,$e,$j)";
        cmd.Parameters.AddWithValue("$t", r.StartedUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$n", r.TestName);
        cmd.Parameters.AddWithValue("$s", r.Status.ToString());
        cmd.Parameters.AddWithValue("$e", r.Error);
        cmd.Parameters.AddWithValue("$j", JsonSerializer.Serialize(r));
        await cmd.ExecuteNonQueryAsync();
    }

    public void Dispose() => _connection.Dispose();
}

