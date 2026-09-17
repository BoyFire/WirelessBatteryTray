using WirelessBattery.Core;
using WirelessBattery.Hid;
using WirelessBattery.Provider.Logitech;

if (args.Contains("--self-test"))
{
    var request = LogitechHidppProtocol.PairingInfoRequest(1);
    var valid = request.SequenceEqual(new byte[] { 0x10, 0xFF, 0x83, 0xB5, 0x20, 0, 0 });
    var sample = new byte[] { 0x11, 0xFF, 0x83, 0xB5, 0x20, 0, 0, 0x40, 0x7B };
    valid &= LogitechHidppProtocol.TryParsePairingInfo(sample, 1, out var wpid) && wpid == "407B";
    var feature = new byte[] { 0x11, 0x01, 0x00, 0x08, 0x08, 0, 1 };
    valid &= LogitechHidppProtocol.TryParseFeatureIndex(feature, 1, out var featureIndex) && featureIndex == 8;
    var battery = new byte[] { 0x11, 0x01, 0x08, 0x08, 0x32, 0x14, 0x00 };
    valid &= LogitechHidppProtocol.TryParseBatteryStatus(battery, 1, 8, out var reading) &&
        reading is { Percent: 50, IsCharging: false };
    battery[4] = 255;
    valid &= !LogitechHidppProtocol.TryParseBatteryStatus(battery, 1, 8, out _);
    var unified = new byte[] { 0x11, 0x01, 0x08, 0x18, 0x32, 0x04, 0x01, 0x00 };
    valid &= LogitechHidppProtocol.TryParseUnifiedBattery(unified, 1, 8, out reading) &&
        reading is { Percent: 50, IsCharging: true };
    Console.WriteLine(valid ? "[PASS] Logitech protocol parser fixtures" : "[FAIL] Logitech protocol parser fixtures");
    return valid ? 0 : 1;
}

try
{
    var transport = new WindowsHidTransport();
    var collections = transport.Enumerate(LogitechHidppProtocol.VendorId, LogitechHidppProtocol.UnifyingReceiverPid);
    Console.WriteLine($"Unifying 046D:C52B collections: {collections.Count}");
    foreach (var c in collections)
        Console.WriteLine($"UsagePage={c.UsagePage:X4} Usage={c.Usage:X4} Input={c.InputReportLength} Output={c.OutputReportLength} Path={c.Path}");
    if (args.Contains("--scan-only")) return collections.Count == 0 ? 2 : 0;
    HidCollection? shortReport = collections.FirstOrDefault(c => c.UsagePage == 0xFF00 && c.InputReportLength == 7 && c.OutputReportLength == 7);
    HidCollection? longReport = collections.FirstOrDefault(c => c.UsagePage == 0xFF00 && c.InputReportLength == 20);
    if (shortReport is null || longReport is null)
    {
        Console.WriteLine("[ERROR] HID++ short or long Collection not found");
        return 3;
    }
    var targetSlot = 0;
    for (var slot = 1; slot <= 6; slot++)
    {
        var request = LogitechHidppProtocol.PairingInfoRequest(slot);
        Console.WriteLine($"Slot {slot} TX: {Hex(request)}");
        try
        {
            var reply = await transport.ExchangeAcrossAsync(shortReport, longReport, request, TimeSpan.FromMilliseconds(900));
            if (reply is null)
            {
                Console.WriteLine($"Slot {slot}: timeout");
                continue;
            }
            Console.WriteLine($"Slot {slot} RX: {Hex(reply)}");
            if (LogitechHidppProtocol.TryParsePairingInfo(reply, slot, out var wpid))
            {
                Console.WriteLine($"Slot {slot} WPID: {wpid}");
                if (wpid == LogitechHidppProtocol.MxVerticalWpid)
                {
                    Console.WriteLine($"[SUCCESS] MX Vertical found in slot {slot}");
                    targetSlot = slot;
                    break;
                }
            }
            else Console.WriteLine($"Slot {slot}: unrelated or invalid reply");
        }
        catch (IOException ex) { Console.WriteLine($"Slot {slot}: {ex.Message}"); }
    }
    if (targetSlot == 0)
    {
        Console.WriteLine("[WARN] MX Vertical WPID 407B not found");
        return 4;
    }
    foreach (var featureId in new ushort[] { 0x1004, 0x1000 })
    {
        var request = LogitechHidppProtocol.FeatureIndexRequest(targetSlot, featureId);
        Console.WriteLine($"Feature 0x{featureId:X4} TX: {Hex(request)}");
        var reply = await transport.ExchangeAcrossAsync(shortReport, longReport, request, TimeSpan.FromMilliseconds(1200));
        Console.WriteLine(reply is null ? "Feature RX: timeout" : $"Feature RX: {Hex(reply)}");
        if (reply is null || !LogitechHidppProtocol.TryParseFeatureIndex(reply, targetSlot, out var featureIndex) || featureIndex == 0)
            continue;
        Console.WriteLine($"Feature 0x{featureId:X4} index: {featureIndex}");
        if (featureId == 0x1000)
        {
            request = LogitechHidppProtocol.BatteryStatusRequest(targetSlot, featureIndex);
            Console.WriteLine($"Battery TX: {Hex(request)}");
            reply = await transport.ExchangeAcrossAsync(shortReport, longReport, request, TimeSpan.FromMilliseconds(1200));
            Console.WriteLine(reply is null ? "Battery RX: timeout" : $"Battery RX: {Hex(reply)}");
            if (reply is null || !LogitechHidppProtocol.TryParseBatteryStatus(reply, targetSlot, featureIndex, out var battery))
                continue;
            Console.WriteLine($"Battery: {(battery!.Percent is null ? "Unknown" : $"{battery.Percent}%")}");
            Console.WriteLine($"StatusRaw: 0x{battery.StatusRaw:X2} Charging: {(battery.IsCharging is null ? "Unknown" : battery.IsCharging.Value ? "Yes" : "No")}");
            Console.WriteLine("[SUCCESS] MX Vertical battery protocol confirmed");
            return 0;
        }
        Console.WriteLine("[INFO] Unified Battery feature discovered; status parsing pending");
    }
    Console.WriteLine("[WARN] MX Vertical battery status not available");
    return 5;
}
catch (Exception ex)
{
    Console.WriteLine($"[ERROR] {ex.GetType().Name}: {ex.Message}");
    return 1;
}

static string Hex(byte[] bytes) => string.Join(" ", bytes.Select(x => x.ToString("X2")));
