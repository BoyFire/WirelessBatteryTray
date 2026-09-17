using WirelessBattery.Core;

namespace WirelessBattery.Provider.Vgn;

public static class VgnS99Protocol
{
    public const ushort VendorId = 0x320F;
    public const ushort ProductId = 0x5088;
    public static readonly byte[] BatteryRequest = [0x04, 0x00, 0x00, 0x1A, 0x06, 0x00, 0x00, 0x00];

    public static bool TryParse(ReadOnlySpan<byte> report, out BatteryReading? reading, out string error)
    {
        reading = null;
        var offset = report.Length > 0 && report[0] == 0x04 ? 1 : 0;
        if (report.Length <= offset + 8)
        {
            error = $"Response too short: {report.Length} bytes";
            return false;
        }
        if (report[offset + 2] != 0x1A)
        {
            error = $"Unexpected command: 0x{report[offset + 2]:X2}";
            return false;
        }
        var percent = report[offset + 7];
        if (percent > 100)
        {
            error = $"Invalid battery value: {percent}";
            return false;
        }
        var chargeRaw = report[offset + 8];
        reading = new BatteryReading(percent, chargeRaw, chargeRaw switch { 0 => false, 1 => true, _ => null });
        error = string.Empty;
        return true;
    }
}
