using System.Globalization;
using WirelessBattery.Hid;

if (args.Length != 2 || !ushort.TryParse(args[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var vid) ||
    !ushort.TryParse(args[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var pid))
{
    Console.Error.WriteLine("Usage: HidDump <VID hex> <PID hex>   Example: HidDump 046D C52B");
    return 1;
}

try
{
    var collections = new WindowsHidTransport().Enumerate(vid, pid);
    Console.WriteLine($"Collections for {vid:X4}:{pid:X4}: {collections.Count}");
    foreach (var item in collections)
    {
        Console.WriteLine($"Path: {item.Path}");
        Console.WriteLine($"UsagePage: {item.UsagePage:X4} Usage: {item.Usage:X4} Input: {item.InputReportLength} Output: {item.OutputReportLength} Feature: {item.FeatureReportLength}");
    }
    return collections.Count == 0 ? 2 : 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[ERROR] {ex.GetType().Name}: {ex.Message}");
    return 3;
}
