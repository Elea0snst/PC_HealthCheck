using PC_HealthCheck.Core;
using System.Text.Json;

namespace PC_HealthCheck.DAL;

public static class UserSettingsStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private static string FilePath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PC HealthCheck");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "settings.json");
    }

    public static UserAppSettings Load()
    {
        try
        {
            var p = FilePath();
            if (!File.Exists(p))
                return new UserAppSettings();
            var json = File.ReadAllText(p);
            return JsonSerializer.Deserialize<UserAppSettings>(json) ?? new UserAppSettings();
        }
        catch
        {
            return new UserAppSettings();
        }
    }

    public static void Save(UserAppSettings s)
    {
        try
        {
            File.WriteAllText(FilePath(), JsonSerializer.Serialize(s, Json));
        }
        catch
        {
            // ignore
        }
    }
}
