using System.Security.Cryptography;
using System.Text;
using WirelessBattery.Core;

namespace WirelessBattery.Provider.Logitech;

public sealed class LogitechHidppProvider(IHidTransport transport) : IBatteryProvider
{
    public string ProviderName => "Logitech HID++";

    public async Task<IReadOnlyList<BatteryState>> ReadAsync(CancellationToken cancellationToken = default)
    {
        var collections = transport.Enumerate(LogitechHidppProtocol.VendorId, LogitechHidppProtocol.UnifyingReceiverPid);
        var shortReport = collections.FirstOrDefault(c => c.UsagePage == 0xFF00 && c.InputReportLength == 7 && c.OutputReportLength == 7);
        var longReport = collections.FirstOrDefault(c => c.UsagePage == 0xFF00 && c.InputReportLength == 20);
        if (shortReport is null || longReport is null) return [];

        for (var slot = 1; slot <= 6; slot++)
        {
            byte[]? pairing;
            try
            {
                pairing = await transport.ExchangeAcrossAsync(shortReport, longReport,
                    LogitechHidppProtocol.PairingInfoRequest(slot), TimeSpan.FromMilliseconds(900), cancellationToken);
            }
            catch (IOException) { continue; }
            if (pairing is null || !LogitechHidppProtocol.TryParsePairingInfo(pairing, slot, out var wpid) ||
                wpid != LogitechHidppProtocol.MxVerticalWpid) continue;

            foreach (var featureId in new ushort[] { 0x1004, 0x1000 })
            {
                byte[]? indexReply;
                try
                {
                    indexReply = await transport.ExchangeAcrossAsync(shortReport, longReport,
                        LogitechHidppProtocol.FeatureIndexRequest(slot, featureId),
                        TimeSpan.FromMilliseconds(1200), cancellationToken);
                }
                catch (IOException) { continue; }
                if (indexReply is null || !LogitechHidppProtocol.TryParseFeatureIndex(indexReply, slot, out var featureIndex) || featureIndex == 0)
                    continue;

                var request = featureId == 0x1004
                    ? LogitechHidppProtocol.UnifiedBatteryRequest(slot, featureIndex)
                    : LogitechHidppProtocol.BatteryStatusRequest(slot, featureIndex);
                byte[]? reply;
                try { reply = await transport.ExchangeAcrossAsync(shortReport, longReport, request, TimeSpan.FromMilliseconds(1200), cancellationToken); }
                catch (IOException) { continue; }
                if (reply is null) continue;
                var parsed = featureId == 0x1004
                    ? LogitechHidppProtocol.TryParseUnifiedBattery(reply, slot, featureIndex, out var unified) ? unified : null
                    : LogitechHidppProtocol.TryParseBatteryStatus(reply, slot, featureIndex, out var standard) ? standard : null;
                if (parsed is null) continue;
                var pathHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(shortReport.Path)))[..12];
                return [new BatteryState($"logitech:c52b:{pathHash}:slot{slot}:407b", "Logitech MX Vertical",
                    parsed.Percent, parsed.IsCharging, DeviceConnectionState.Connected, DateTimeOffset.Now, ProviderName)];
            }
        }
        return [];
    }
}
