using WirelessBattery.Core;

namespace WirelessBatteryTray.App;

internal sealed record MonitoredDevice(string Name, string FallbackId, ushort VendorId, ushort ProductId,
    IBatteryProvider Provider, DeviceConnectionState NoResponseState);

internal sealed record DeviceSnapshot(MonitoredDevice Device, BatteryState? LastGood,
    DeviceConnectionState State, DateTimeOffset CheckedAt, string? Error)
{
    public bool IsStale(TimeSpan maxAge, DateTimeOffset now) => LastGood is not null && now - LastGood.UpdatedAt > maxAge;
    public int? CurrentPercent => State == DeviceConnectionState.Connected ? LastGood?.BatteryPercent : null;
}

internal sealed class BatteryMonitor(IHidTransport transport, IReadOnlyList<MonitoredDevice> devices,
    Action<string, string> log)
{
    private readonly Dictionary<string, DeviceSnapshot> snapshots = new();
    private readonly SemaphoreSlim gate = new(1, 1);
    public IReadOnlyList<DeviceSnapshot> Snapshots => devices.Select(device => snapshots.TryGetValue(device.FallbackId, out var value)
        ? value : new DeviceSnapshot(device, null, DeviceConnectionState.Unknown, DateTimeOffset.MinValue, null)).ToArray();

    public async Task<IReadOnlyList<DeviceSnapshot>> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            foreach (var device in devices)
            {
                cancellationToken.ThrowIfCancellationRequested();
                snapshots.TryGetValue(device.FallbackId, out var previous);
                BatteryState? reading = null;
                string? error = null;
                try
                {
                    reading = (await device.Provider.ReadAsync(cancellationToken)).FirstOrDefault();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    error = ex.Message;
                    log("ERR", $"{device.Name} 读取失败: {ex}");
                }

                DeviceConnectionState state;
                if (reading is not null)
                {
                    state = DeviceConnectionState.Connected;
                    log("INF", $"{device.Name} Battery={reading.BatteryPercent} Charging={reading.IsCharging}");
                }
                else
                {
                    try
                    {
                        state = transport.Enumerate(device.VendorId, device.ProductId).Count == 0
                            ? DeviceConnectionState.Disconnected : device.NoResponseState;
                    }
                    catch (Exception ex)
                    {
                        state = DeviceConnectionState.Error;
                        error ??= ex.Message;
                    }
                    log("WRN", $"{device.Name} 无有效响应，状态={state}，上次有效电量={previous?.LastGood?.BatteryPercent?.ToString() ?? "无"}");
                }
                snapshots[device.FallbackId] = new DeviceSnapshot(device, reading ?? previous?.LastGood,
                    state, DateTimeOffset.Now, error);
            }
            return Snapshots;
        }
        finally { gate.Release(); }
    }
}
