using System.Security.Cryptography;
using System.Text;
using WirelessBattery.Core;

namespace WirelessBattery.Provider.Vgn;

public sealed class VgnBatteryProvider(IHidTransport transport) : IBatteryProvider
{
    public string ProviderName => "VGN/Weisheng";

    public async Task<IReadOnlyList<BatteryState>> ReadAsync(CancellationToken cancellationToken = default)
    {
        var candidates = transport.Enumerate(VgnS99Protocol.VendorId, VgnS99Protocol.ProductId)
            .Where(c => c.UsagePage >= 0xFF00 && c.OutputReportLength >= VgnS99Protocol.BatteryRequest.Length && c.InputReportLength >= 10);
        foreach (var collection in candidates)
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    var report = await transport.ExchangeAsync(collection, VgnS99Protocol.BatteryRequest,
                        TimeSpan.FromMilliseconds(500), cancellationToken);
                    if (report is null || !VgnS99Protocol.TryParse(report, out var reading, out _)) continue;
                    var pathHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(collection.Path)))[..12];
                    return [new BatteryState($"vgn:320f:5088:{pathHash}", "VGN S99", reading!.Percent,
                        reading.IsCharging, DeviceConnectionState.Connected, DateTimeOffset.Now, ProviderName)];
                }
                catch (IOException) { break; }
            }
        }
        return [];
    }
}
