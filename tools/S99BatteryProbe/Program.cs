using WirelessBattery.Hid;
using WirelessBattery.Provider.Vgn;
using System.Text.RegularExpressions;

if (args.Contains("--self-test"))
{
    var sample = new byte[] { 0x04, 0x00, 0x00, 0x1A, 0x06, 0x00, 0x00, 0x00, 0x57, 0x00 };
    var valid = VgnS99Protocol.TryParse(sample, out var reading, out _) &&
        reading is { Percent: 87, ChargeRaw: 0, IsCharging: false };
    valid &= VgnS99Protocol.TryParse(sample.AsSpan(1), out reading, out _) && reading?.Percent == 87;
    sample[8] = 100;
    valid &= VgnS99Protocol.TryParse(sample, out reading, out _) && reading?.Percent == 100;
    sample[9] = 1;
    valid &= VgnS99Protocol.TryParse(sample, out reading, out _) && reading?.IsCharging == true;
    sample[9] = 2;
    valid &= VgnS99Protocol.TryParse(sample, out reading, out _) && reading?.IsCharging is null;
    sample[9] = 0;
    sample[8] = 255;
    valid &= !VgnS99Protocol.TryParse(sample, out _, out _);
    sample[8] = 50;
    sample[3] = 0x19;
    valid &= !VgnS99Protocol.TryParse(sample, out _, out _);
    valid &= !VgnS99Protocol.TryParse(sample.AsSpan(0, 8), out _, out _);
    Console.WriteLine(valid ? "[PASS] S99 parser fixtures" : "[FAIL] S99 parser fixtures");
    return valid ? 0 : 1;
}

Console.WriteLine("=== HID Device Scan ===");
try
{
    var transport = new WindowsHidTransport();
    var collections = transport.Enumerate(VgnS99Protocol.VendorId, VgnS99Protocol.ProductId);
    if (collections.Count == 0)
    {
        Console.WriteLine("[WARN] 320F:5088 receiver not found");
        return 2;
    }
    foreach (var item in collections)
    {
        Console.WriteLine($"\nVID: {item.VendorId:X4} PID: {item.ProductId:X4}\nPath: {item.Path}");
        var interfaceMatch = Regex.Match(item.Path, @"&mi_([0-9a-f]{2})", RegexOptions.IgnoreCase);
        Console.WriteLine($"Interface: {(interfaceMatch.Success ? interfaceMatch.Groups[1].Value : "unknown")}");
        Console.WriteLine($"UsagePage: {item.UsagePage:X4} Usage: {item.Usage:X4}");
        Console.WriteLine($"InputReportLength: {item.InputReportLength} OutputReportLength: {item.OutputReportLength} FeatureReportLength: {item.FeatureReportLength}");
    }
    if (args.Contains("--scan-only")) return 0;
    var candidates = collections.Where(x => x.UsagePage >= 0xFF00 &&
        x.OutputReportLength >= VgnS99Protocol.BatteryRequest.Length && x.InputReportLength >= 10).ToArray();
    if (candidates.Length == 0)
    {
        Console.WriteLine("[WARN] No suitable writable HID collection found");
        return 3;
    }
    foreach (var item in candidates)
    {
        Console.WriteLine($"\nTrying: {item.Path}");
        Console.WriteLine("ReportID: 0x04");
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                var transmitted = new byte[item.OutputReportLength];
                VgnS99Protocol.BatteryRequest.CopyTo(transmitted, 0);
                Console.WriteLine($"TX #{attempt} ({transmitted.Length} bytes): {Hex(transmitted)}");
                var response = await transport.ExchangeAsync(item, VgnS99Protocol.BatteryRequest, TimeSpan.FromMilliseconds(500));
                if (response is null)
                {
                    Console.WriteLine("[WARN] Device did not respond within 500 ms");
                    continue;
                }
                Console.WriteLine($"RX #{attempt} ({response.Length} bytes): {Hex(response)}");
                if (!VgnS99Protocol.TryParse(response, out var reading, out var error))
                {
                    Console.WriteLine($"[WARN] {error}");
                    continue;
                }
                Console.WriteLine($"Battery: {reading!.Percent}%\nChargeRaw: {reading.ChargeRaw}\nCharging: {(reading.IsCharging is null ? "Unknown" : reading.IsCharging.Value ? "Yes" : "No")}");
                Console.WriteLine("[SUCCESS] VGN S99 battery protocol confirmed");
                return 0;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
            {
                Console.WriteLine($"[WARN] {ex.GetType().Name}: {ex.Message}");
                break;
            }
        }
    }
    Console.WriteLine("[ERROR] No collection returned a valid S99 battery response");
    return 4;
}

catch (Exception ex)
{
    Console.WriteLine($"[ERROR] {ex.GetType().Name}: {ex.Message}");
    return 1;
}

static string Hex(byte[] data) => string.Join(" ", data.Select(x => x.ToString("X2")));
