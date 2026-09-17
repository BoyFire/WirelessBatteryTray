using WirelessBattery.Core;
using WirelessBattery.Hid;
using WirelessBattery.Provider.Logitech;
using WirelessBattery.Provider.Vgn;

Console.WriteLine("=== Wireless Battery Probe ===");
var transport = new WindowsHidTransport();
IBatteryProvider[] providers = [new VgnBatteryProvider(transport), new LogitechHidppProvider(transport)];
var all = new List<BatteryState>();
foreach (var provider in providers)
{
    try
    {
        var states = await provider.ReadAsync();
        if (states.Count == 0) Console.WriteLine($"{provider.ProviderName}: no valid battery response");
        all.AddRange(states);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"{provider.ProviderName}: {ex.GetType().Name}: {ex.Message}");
    }
}

foreach (var state in all)
{
    Console.WriteLine($"\n{state.DeviceName}");
    Console.WriteLine($"Battery: {(state.BatteryPercent is null ? "Unknown" : $"{state.BatteryPercent}%")}");
    Console.WriteLine($"Charging: {(state.IsCharging is null ? "Unknown" : state.IsCharging.Value ? "Yes" : "No")}");
    Console.WriteLine($"DeviceId: {state.DeviceId}");
}
var pass = all.Any(x => x.Provider == "VGN/Weisheng" && x.BatteryPercent is >= 0 and <= 100) &&
    all.Any(x => x.Provider == "Logitech HID++" && x.BatteryPercent is >= 0 and <= 100);
Console.WriteLine($"\nResult: {(pass ? "PASS" : "FAIL")}");
return pass ? 0 : 1;
