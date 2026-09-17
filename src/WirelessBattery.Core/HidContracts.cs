namespace WirelessBattery.Core;

public sealed record HidCollection(
    string Path, ushort VendorId, ushort ProductId, ushort UsagePage, ushort Usage,
    short InputReportLength, short OutputReportLength, short FeatureReportLength);

public interface IHidTransport
{
    IReadOnlyList<HidCollection> Enumerate(ushort vendorId, ushort productId);
    Task<byte[]?> ExchangeAsync(HidCollection collection, byte[] request, TimeSpan timeout,
        CancellationToken cancellationToken = default);
    Task<byte[]?> ExchangeAcrossAsync(HidCollection outputCollection, HidCollection inputCollection,
        byte[] request, TimeSpan timeout, CancellationToken cancellationToken = default);
}

public sealed record BatteryReading(int Percent, byte ChargeRaw, bool? IsCharging);
