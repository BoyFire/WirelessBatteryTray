using System.Text.Json;
using WirelessBattery.Core;

namespace WirelessBatteryTray.App;

internal sealed record HistoryRecord(DateTimeOffset Timestamp, string DeviceId, string DeviceName,
    int? Battery, bool? Charging, DeviceConnectionState ConnectionState);

internal sealed class HistoryStore(string filePath, int retentionDays)
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WirelessBatteryTray", "history.jsonl");

    private DateOnly lastPruned;

    public void Append(IEnumerable<DeviceSnapshot> snapshots, DateTimeOffset now)
    {
        Prune(now);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        using var writer = new StreamWriter(filePath, append: true);
        foreach (var item in snapshots)
        {
            var record = new HistoryRecord(now, item.LastGood?.DeviceId ?? item.Device.FallbackId,
                item.Device.Name, item.CurrentPercent,
                item.State == DeviceConnectionState.Connected ? item.LastGood?.IsCharging : null, item.State);
            writer.WriteLine(JsonSerializer.Serialize(record));
        }
    }

    public IReadOnlyList<HistoryRecord> ReadRecent(int count)
    {
        if (!File.Exists(filePath)) return [];
        return File.ReadLines(filePath).Select(Parse).Where(record => record is not null)
            .Cast<HistoryRecord>().TakeLast(count).Reverse().ToArray();
    }

    public void Prune(DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.LocalDateTime);
        if (lastPruned == today) return;
        lastPruned = today;
        if (!File.Exists(filePath)) return;
        var cutoff = now.AddDays(-retentionDays);
        var retained = File.ReadLines(filePath).Select(Parse)
            .Where(record => record is not null && record.Timestamp >= cutoff)
            .Select(record => JsonSerializer.Serialize(record)).ToArray();
        var temporary = filePath + ".tmp";
        File.WriteAllLines(temporary, retained);
        File.Move(temporary, filePath, true);
    }

    private static HistoryRecord? Parse(string line)
    {
        try { return JsonSerializer.Deserialize<HistoryRecord>(line); }
        catch (JsonException) { return null; }
    }
}
