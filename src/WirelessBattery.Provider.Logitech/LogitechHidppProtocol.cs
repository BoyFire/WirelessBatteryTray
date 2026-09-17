namespace WirelessBattery.Provider.Logitech;

public static class LogitechHidppProtocol
{
    public const ushort VendorId = 0x046D;
    public const ushort UnifyingReceiverPid = 0xC52B;
    public const string MxVerticalWpid = "407B";

    // HID++ 1.0 receiver-info long register read. It does not change pairing or configuration.
    public static byte[] PairingInfoRequest(int slot)
    {
        if (slot is < 1 or > 6) throw new ArgumentOutOfRangeException(nameof(slot));
        return [0x10, 0xFF, 0x83, 0xB5, (byte)(0x20 + slot - 1), 0, 0];
    }

    public static bool TryParsePairingInfo(ReadOnlySpan<byte> report, int slot, out string? wpid)
    {
        wpid = null;
        if (report.Length < 9 || report[0] != 0x11 || report[1] != 0xFF ||
            report[2] != 0x83 || report[3] != 0xB5 || report[4] != 0x20 + slot - 1)
            return false;
        wpid = $"{report[7]:X2}{report[8]:X2}";
        return true;
    }

    public static byte[] FeatureIndexRequest(int slot, ushort featureId)
    {
        if (slot is < 1 or > 6) throw new ArgumentOutOfRangeException(nameof(slot));
        return [0x10, (byte)slot, 0x00, 0x08, (byte)(featureId >> 8), (byte)featureId, 0x00];
    }

    public static bool TryParseFeatureIndex(ReadOnlySpan<byte> report, int slot, out byte index)
    {
        index = 0;
        if (report.Length < 7 || report[0] is not (0x10 or 0x11) || report[1] != slot ||
            report[2] != 0x00 || report[3] != 0x08)
            return false;
        index = report[4];
        return true;
    }

    public static byte[] BatteryStatusRequest(int slot, byte featureIndex)
    {
        if (slot is < 1 or > 6 || featureIndex == 0) throw new ArgumentOutOfRangeException();
        return [0x10, (byte)slot, featureIndex, 0x08, 0x00, 0x00, 0x00];
    }

    public static byte[] UnifiedBatteryRequest(int slot, byte featureIndex)
    {
        if (slot is < 1 or > 6 || featureIndex == 0) throw new ArgumentOutOfRangeException();
        return [0x10, (byte)slot, featureIndex, 0x18, 0x00, 0x00, 0x00];
    }

    public static bool TryParseBatteryStatus(ReadOnlySpan<byte> report, int slot, byte featureIndex,
        out LogitechBatteryStatus? battery)
    {
        battery = null;
        if (report.Length < 7 || report[0] is not (0x10 or 0x11) || report[1] != slot ||
            report[2] != featureIndex || report[3] != 0x08 || report[4] > 100)
            return false;
        var status = report[6];
        bool? charging = Charging(status);
        battery = new LogitechBatteryStatus(report[4] == 0 ? null : report[4], report[5], status, charging);
        return true;
    }

    public static bool TryParseUnifiedBattery(ReadOnlySpan<byte> report, int slot, byte featureIndex,
        out LogitechBatteryStatus? battery)
    {
        battery = null;
        if (report.Length < 8 || report[0] is not (0x10 or 0x11) || report[1] != slot ||
            report[2] != featureIndex || report[3] != 0x18 || report[4] > 100)
            return false;
        var status = report[6];
        battery = new LogitechBatteryStatus(report[4] == 0 ? null : report[4], report[5], status, Charging(status));
        return true;
    }

    private static bool? Charging(byte status) => status switch { 0 => false, 1 or 2 or 3 or 4 => true, _ => null };
}

public sealed record LogitechBatteryStatus(int? Percent, byte NextLevel, byte StatusRaw, bool? IsCharging);
