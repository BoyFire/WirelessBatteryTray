using Microsoft.Win32;

namespace WirelessBatteryTray.App;

internal static class StartupManager
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WirelessBatteryTray";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return string.Equals(key?.GetValue(ValueName) as string, LaunchCommand(), StringComparison.OrdinalIgnoreCase);
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath)
            ?? throw new IOException("无法打开当前用户的启动项注册表键");
        if (enabled) key.SetValue(ValueName, LaunchCommand(), RegistryValueKind.String);
        else key.DeleteValue(ValueName, false);
    }

    private static string LaunchCommand()
    {
        var path = Environment.ProcessPath ?? throw new InvalidOperationException("无法确定程序路径");
        if (!path.EndsWith("WirelessBatteryTray.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("请使用 WirelessBatteryTray.exe 启动托盘后再设置开机启动");
        return $"\"{path}\"";
    }
}
