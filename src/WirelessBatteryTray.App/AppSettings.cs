using System.Text.Json;

namespace WirelessBatteryTray.App;

internal sealed class AppSettings
{
    public int RefreshIntervalSeconds { get; set; } = 60;
    public int LowBatteryThreshold { get; set; } = 20;
    public bool NotificationsEnabled { get; set; } = true;
    public bool DebugLogging { get; set; }
    public int HistoryRetentionDays { get; set; } = 7;

    public void Normalize()
    {
        RefreshIntervalSeconds = Math.Clamp(RefreshIntervalSeconds, 30, 300);
        LowBatteryThreshold = Math.Clamp(LowBatteryThreshold, 0, 100);
        HistoryRetentionDays = Math.Clamp(HistoryRetentionDays, 1, 30);
    }
}

internal static class SettingsStore
{
    public static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WirelessBatteryTray");
    public static string FilePath => Path.Combine(DirectoryPath, "settings.json");

    public static AppSettings Load(Action<string> log)
    {
        try
        {
            if (!File.Exists(FilePath)) return new AppSettings();
            var value = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
            value.Normalize();
            return value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            log($"配置读取失败，使用默认值: {ex.Message}");
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        settings.Normalize();
        Directory.CreateDirectory(DirectoryPath);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, FilePath, true);
    }
}
