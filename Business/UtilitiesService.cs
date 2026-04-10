using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using PC_HealthCheck.Core;

namespace PC_HealthCheck.Business;

public static class UtilitiesService
{
    public static List<ProcessEntry> GetTopProcessesByWorkingSet(int maxCount = 100)
    {
        var list = new List<ProcessEntry>(maxCount);
        Process[]? all = null;
        try
        {
            all = Process.GetProcesses();
            foreach (var p in all.OrderByDescending(x =>
                     {
                         try
                         {
                             return x.WorkingSet64;
                         }
                         catch
                         {
                             return 0L;
                         }
                     }).Take(maxCount))
            {
                try
                {
                    list.Add(new ProcessEntry
                    {
                        ProcessId = p.Id,
                        Name = p.ProcessName,
                        WorkingSetBytes = p.WorkingSet64,
                        ThreadCount = p.Threads.Count
                    });
                }
                catch
                {
                    // access denied etc.
                }
            }
        }
        finally
        {
            if (all is not null)
            {
                foreach (var p in all)
                {
                    try
                    {
                        p.Dispose();
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }
        }

        return list;
    }

    public static List<NetworkInterfaceStat> GetNetworkCounters()
    {
        var list = new List<NetworkInterfaceStat>();
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up)
                continue;
            if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback)
                continue;
            try
            {
                var s = ni.GetIPStatistics();
                list.Add(new NetworkInterfaceStat
                {
                    Name = ni.Name,
                    Description = ni.Description,
                    BytesReceived = s.BytesReceived,
                    BytesSent = s.BytesSent
                });
            }
            catch
            {
                // ignore
            }
        }

        return list;
    }

    public static List<StartupEntry> GetStartupEntries()
    {
        var list = new List<StartupEntry>();
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return list;

        AddRunKey(list, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "HKCU\\…\\Run");
        AddRunKey(list, Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run", "HKLM\\…\\Run");
        AddRunKey(list, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\RunOnce", "HKCU\\…\\RunOnce");
        AddRunKey(list, Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\RunOnce", "HKLM\\…\\RunOnce");

        AddStartupFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Папка автозагрузки (пользователь)");
        AddStartupFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "Общая папка автозагрузки");
        return list;
    }

    private static void AddRunKey(List<StartupEntry> list, RegistryKey root, string subKey, string label)
    {
        try
        {
            using var k = root.OpenSubKey(subKey, writable: false);
            if (k is null) return;
            foreach (var name in k.GetValueNames())
            {
                var cmd = k.GetValue(name)?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(cmd))
                    continue;
                list.Add(new StartupEntry { Location = label, Name = name, Command = cmd });
            }
        }
        catch
        {
            // ignore
        }
    }

    private static void AddStartupFolder(List<StartupEntry> list, string folder, string label)
    {
        try
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                return;
            foreach (var path in Directory.EnumerateFileSystemEntries(folder))
            {
                var name = Path.GetFileName(path);
                list.Add(new StartupEntry { Location = label, Name = name, Command = path });
            }
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>Удаляет файлы во временных каталогах старше указанного срока (осторожный режим).</summary>
    public static (long BytesFreed, int FilesDeleted, string Detail) CleanupOldTempFiles(int minAgeDays = 7)
    {
        long freed = 0;
        var deleted = 0;
        var errors = 0;
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetTempPath(),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp")
        };

        var cutoff = DateTime.UtcNow.AddDays(-minAgeDays);
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                continue;
            try
            {
                foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        var fi = new FileInfo(file);
                        if (fi.LastWriteTimeUtc >= cutoff)
                            continue;
                        var len = fi.Length;
                        File.Delete(file);
                        freed += len;
                        deleted++;
                    }
                    catch
                    {
                        errors++;
                    }
                }
            }
            catch
            {
                errors++;
            }
        }

        var msg = $"Удалено файлов: {deleted}, освобождено ~{freed / 1024 / 1024} МБ. Не удалось обработать элементов: {errors}.";
        return (freed, deleted, msg);
    }
}
