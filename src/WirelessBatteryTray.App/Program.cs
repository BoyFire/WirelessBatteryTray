using WirelessBattery.Core;
using WirelessBattery.Hid;
using WirelessBattery.Provider.Logitech;
using WirelessBattery.Provider.Vgn;

namespace WirelessBatteryTray.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--self-test")) return RunSelfTest();
        var log = new DiagnosticLog();
        var settings = SettingsStore.Load(message => log.Write("WRN", message));
        if (args.Contains("--debug")) settings.DebugLogging = true;
        var transport = new WindowsHidTransport(settings.DebugLogging ? message => log.Write("DBG", message) : null);
        var monitor = CreateMonitor(transport, (level, message) => log.Write(level, message));
        var history = new HistoryStore(HistoryStore.DefaultPath, settings.HistoryRetentionDays);
        if (args.Contains("--once"))
        {
            try
            {
                foreach (var item in monitor.RefreshAsync().GetAwaiter().GetResult())
                    Console.WriteLine($"{item.Device.Name}: {item.State}, {item.CurrentPercent?.ToString() ?? "--"}%, charging={item.LastGood?.IsCharging?.ToString() ?? "Unknown"}");
                return 0;
            }
            catch (Exception ex)
            {
                log.Write("ERR", ex.ToString());
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        using var mutex = new Mutex(true, "Local\\WirelessBatteryTray", out var createdNew);
        if (!createdNew) return 0;
        try
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new TrayContext(monitor, settings, log, history));
            return 0;
        }
        catch (Exception ex)
        {
            log.Write("ERR", $"托盘主循环异常: {ex}");
            MessageBox.Show($"Wireless Battery Tray 启动失败：{ex.Message}", "Wireless Battery Tray",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static BatteryMonitor CreateMonitor(IHidTransport transport, Action<string, string> log) => new(transport,
    [
        new MonitoredDevice("VGN S99", "vgn-s99", 0x320F, 0x5088,
            new VgnBatteryProvider(transport), DeviceConnectionState.Sleeping),
        new MonitoredDevice("Logitech MX Vertical", "logitech-mx-vertical", 0x046D, 0xC52B,
            new LogitechHidppProvider(transport), DeviceConnectionState.Disconnected)
    ], log);

    private static int RunSelfTest()
    {
        try
        {
            var transport = new FakeTransport { ReceiverPresent = true };
            var provider = new FakeProvider();
            var device = new MonitoredDevice("Test Mouse", "test", 1, 2, provider, DeviceConnectionState.Disconnected);
            var monitor = new BatteryMonitor(transport, [device], (_, _) => { });
            var policy = new LowBatteryPolicy();
            var now = DateTimeOffset.Now;
            provider.Reading = new BatteryState("test-device", "Test Mouse", 19, false,
                DeviceConnectionState.Connected, now, "test");
            var connected = monitor.RefreshAsync().GetAwaiter().GetResult();
            Assert(connected[0].CurrentPercent == 19, "initial battery");
            Assert(policy.Evaluate(connected, 20, true).Count == 1, "first low battery notification");
            Assert(policy.Evaluate(connected, 20, true).Count == 0, "duplicate notification");
            Assert(new LowBatteryPolicy().Evaluate(connected, 0, true).Count == 0, "disabled threshold");

            provider.Reading = null;
            var missing = monitor.RefreshAsync().GetAwaiter().GetResult();
            Assert(missing[0].State == DeviceConnectionState.Disconnected && missing[0].CurrentPercent is null &&
                missing[0].LastGood?.BatteryPercent == 19, "wireless disconnect keeps only last-good battery");
            Assert(policy.Evaluate(missing, 20, true).Count == 0, "no notification while disconnected");
            var sleepingDevice = device with { FallbackId = "sleeping", NoResponseState = DeviceConnectionState.Sleeping };
            var sleepingMonitor = new BatteryMonitor(transport, [sleepingDevice], (_, _) => { });
            Assert(sleepingMonitor.RefreshAsync().GetAwaiter().GetResult()[0].State == DeviceConnectionState.Sleeping,
                "receiver present but device not responding");
            Assert(new DeviceSnapshot(device, provider.Reading, DeviceConnectionState.Disconnected, now, null)
                .IsStale(TimeSpan.FromMinutes(5), now.AddMinutes(6)) == false, "empty reading is not stale");
            Assert(new DeviceSnapshot(device, missing[0].LastGood, DeviceConnectionState.Disconnected, now, null)
                .IsStale(TimeSpan.FromMinutes(5), now.AddMinutes(6)), "old reading is stale");

            transport.ReceiverPresent = false;
            Assert(monitor.RefreshAsync().GetAwaiter().GetResult()[0].State == DeviceConnectionState.Disconnected,
                "receiver disconnected");
            transport.ReceiverPresent = true;
            provider.Reading = new BatteryState("test-device", "Test Mouse", 26, false,
                DeviceConnectionState.Connected, now, "test");
            Assert(policy.Evaluate(monitor.RefreshAsync().GetAwaiter().GetResult(), 20, true).Count == 0,
                "recovery above reset threshold");
            provider.Reading = provider.Reading with { BatteryPercent = 19 };
            Assert(policy.Evaluate(monitor.RefreshAsync().GetAwaiter().GetResult(), 20, true).Count == 1,
                "notification after rearm");
            var testDirectory = Path.Combine(Path.GetTempPath(), "WirelessBatteryTray-selftest-" + Guid.NewGuid().ToString("N"));
            var historyPath = Path.Combine(testDirectory, "history.jsonl");
            try
            {
                var history = new HistoryStore(historyPath, 7);
                var current = monitor.Snapshots;
                history.Append(current, now.AddDays(-8));
                history.Append(current, now);
                Assert(history.ReadRecent(10).Count == 1, "seven day retention");
                provider.Reading = null;
                var disconnected = monitor.RefreshAsync().GetAwaiter().GetResult();
                history.Append(disconnected, now.AddMinutes(1));
                Assert(history.ReadRecent(1)[0].Battery is null, "disconnected history has no current battery");
            }
            finally
            {
                if (File.Exists(historyPath)) File.Delete(historyPath);
                if (Directory.Exists(testDirectory)) Directory.Delete(testDirectory);
            }
            Console.WriteLine("[PASS] tray state and notification policy");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[FAIL] {ex}");
            return 1;
        }
    }

    private static void Assert(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
    }

    private sealed class FakeProvider : IBatteryProvider
    {
        public string ProviderName => "test";
        public BatteryState? Reading { get; set; }
        public Task<IReadOnlyList<BatteryState>> ReadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BatteryState>>(Reading is null ? [] : [Reading]);
    }

    private sealed class FakeTransport : IHidTransport
    {
        public bool ReceiverPresent { get; set; }
        public IReadOnlyList<HidCollection> Enumerate(ushort vendorId, ushort productId) => ReceiverPresent
            ? [new HidCollection("test", vendorId, productId, 0, 0, 1, 1, 1)] : [];
        public Task<byte[]?> ExchangeAsync(HidCollection collection, byte[] request, TimeSpan timeout,
            CancellationToken cancellationToken = default) => Task.FromResult<byte[]?>(null);
        public Task<byte[]?> ExchangeAcrossAsync(HidCollection outputCollection, HidCollection inputCollection,
            byte[] request, TimeSpan timeout, CancellationToken cancellationToken = default) => Task.FromResult<byte[]?>(null);
    }
}
