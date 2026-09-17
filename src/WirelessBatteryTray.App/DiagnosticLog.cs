namespace WirelessBatteryTray.App;

internal sealed class DiagnosticLog
{
    private readonly object sync = new();
    public static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WirelessBatteryTray", "logs");

    public void Write(string level, string message)
    {
        lock (sync)
        {
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                var path = Path.Combine(DirectoryPath, $"wireless-battery-{DateTime.Now:yyyy-MM-dd}.log");
                File.AppendAllText(path, $"{DateTimeOffset.Now:O} [{level}] {message}{Environment.NewLine}");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
