namespace WirelessBattery.Core;

public enum DeviceConnectionState { Unknown, Connected, Sleeping, Disconnected, Error }

public sealed record BatteryState(
    string DeviceId,
    string DeviceName,
    int? BatteryPercent,
    bool? IsCharging,
    DeviceConnectionState ConnectionState,
    DateTimeOffset UpdatedAt,
    string Provider);

public interface IBatteryProvider
{
    string ProviderName { get; }
    Task<IReadOnlyList<BatteryState>> ReadAsync(CancellationToken cancellationToken = default);
}
