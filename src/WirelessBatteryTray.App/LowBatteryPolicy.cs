namespace WirelessBatteryTray.App;

internal sealed class LowBatteryPolicy
{
    private readonly HashSet<string> notified = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<DeviceSnapshot> Evaluate(IEnumerable<DeviceSnapshot> snapshots, int threshold, bool enabled)
    {
        var result = new List<DeviceSnapshot>();
        foreach (var item in snapshots)
        {
            var key = item.LastGood?.DeviceId ?? item.Device.FallbackId;
            if (item.State != WirelessBattery.Core.DeviceConnectionState.Connected || item.CurrentPercent is not int percent)
                continue;
            if (item.LastGood?.IsCharging == true || percent > threshold + 5)
            {
                notified.Remove(key);
                continue;
            }
            if (enabled && threshold > 0 && item.LastGood?.IsCharging != true && percent <= threshold && notified.Add(key)) result.Add(item);
        }
        return result;
    }
}
