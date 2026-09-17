using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using WirelessBattery.Core;

namespace WirelessBatteryTray.App;

internal sealed class TrayContext : ApplicationContext
{
    private readonly BatteryMonitor monitor;
    private readonly AppSettings settings;
    private readonly DiagnosticLog log;
    private readonly HistoryStore history;
    private readonly Control dispatcher = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly LowBatteryPolicy notifications = new();
    private readonly NotifyIcon notifyIcon = new();
    private readonly ContextMenuStrip menu = new();
    private readonly System.Windows.Forms.Timer timer = new();
    private Icon? generatedIcon;
    private bool refreshing;
    private bool started;
    private bool suspended;

    public TrayContext(BatteryMonitor monitor, AppSettings settings, DiagnosticLog log, HistoryStore history)
    {
        this.monitor = monitor;
        this.settings = settings;
        this.log = log;
        this.history = history;
        _ = dispatcher.Handle;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        notifyIcon.Icon = SystemIcons.Application;
        notifyIcon.Text = "Wireless Battery Tray：等待首次读取";
        notifyIcon.ContextMenuStrip = menu;
        notifyIcon.Visible = true;
        notifyIcon.MouseClick += (_, args) => { if (args.Button == MouseButtons.Left) ShowDetails(); };
        timer.Interval = settings.RefreshIntervalSeconds * 1000;
        timer.Tick += async (_, _) => await RefreshAsync();
        timer.Start();
        BuildMenu();
        Application.Idle += OnFirstIdle;
    }

    private async void OnFirstIdle(object? sender, EventArgs e)
    {
        if (started) return;
        started = true;
        Application.Idle -= OnFirstIdle;
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (refreshing) return;
        refreshing = true;
        timer.Stop();
        try
        {
            var states = await monitor.RefreshAsync();
            try { history.Append(states, DateTimeOffset.Now); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { log.Write("ERR", $"历史记录写入失败: {ex}"); }
            UpdateIcon(states);
            BuildMenu();
            foreach (var item in notifications.Evaluate(states, settings.LowBatteryThreshold, settings.NotificationsEnabled))
            {
                notifyIcon.BalloonTipIcon = ToolTipIcon.Warning;
                notifyIcon.BalloonTipTitle = $"{item.Device.Name} 电量不足";
                notifyIcon.BalloonTipText = $"当前剩余 {item.CurrentPercent}%，建议及时充电。";
                notifyIcon.ShowBalloonTip(10000);
                log.Write("INF", $"低电量通知: {item.Device.Name} {item.CurrentPercent}%");
            }
        }
        catch (Exception ex)
        {
            log.Write("ERR", $"刷新失败: {ex}");
            notifyIcon.Text = "Wireless Battery Tray：读取失败";
        }
        finally
        {
            timer.Interval = settings.RefreshIntervalSeconds * 1000;
            if (!suspended) timer.Start();
            refreshing = false;
        }
    }

    private void BuildMenu()
    {
        menu.Items.Clear();
        menu.Items.Add(new ToolStripMenuItem("Wireless Battery Tray") { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());
        foreach (var item in monitor.Snapshots)
        {
            var snapshot = item;
            menu.Items.Add(new ToolStripMenuItem($"{item.Device.Name}    {StatusText(item)}", null,
                (_, _) => ShowDetails(snapshot)));
        }
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("立即刷新", null, async (_, _) => await RefreshAsync()));
        menu.Items.Add(new ToolStripMenuItem("查看详情", null, (_, _) => ShowDetails()));
        menu.Items.Add(new ToolStripMenuItem("最近历史", null, (_, _) => ShowHistory()));
        var settingsMenu = new ToolStripMenuItem("设置");
        foreach (var seconds in new[] { 30, 60, 300 })
        {
            var interval = seconds;
            settingsMenu.DropDownItems.Add(new ToolStripMenuItem($"刷新间隔：{(seconds == 300 ? "5 分钟" : $"{seconds} 秒")}", null,
                (_, _) => { settings.RefreshIntervalSeconds = interval; SaveSettings(); BuildMenu(); })
            { Checked = settings.RefreshIntervalSeconds == seconds });
        }
        settingsMenu.DropDownItems.Add(new ToolStripSeparator());
        foreach (var threshold in new[] { 0, 10, 15, 20, 30 })
        {
            var value = threshold;
            settingsMenu.DropDownItems.Add(new ToolStripMenuItem(threshold == 0 ? "低电量阈值：关闭" : $"低电量阈值：{threshold}%", null,
                (_, _) => { settings.LowBatteryThreshold = value; SaveSettings(); BuildMenu(); })
            { Checked = settings.LowBatteryThreshold == threshold });
        }
        settingsMenu.DropDownItems.Add(new ToolStripSeparator());
        settingsMenu.DropDownItems.Add(new ToolStripMenuItem("启用低电量通知", null,
            (_, _) => { settings.NotificationsEnabled = !settings.NotificationsEnabled; SaveSettings(); BuildMenu(); })
        { Checked = settings.NotificationsEnabled });
        menu.Items.Add(settingsMenu);
        var autoStart = false;
        try { autoStart = StartupManager.IsEnabled(); }
        catch (Exception ex) { log.Write("WRN", $"启动项读取失败: {ex.Message}"); }
        menu.Items.Add(new ToolStripMenuItem("开机启动", null, (_, _) =>
        {
            try { StartupManager.SetEnabled(!StartupManager.IsEnabled()); BuildMenu(); }
            catch (Exception ex)
            {
                log.Write("ERR", $"启动项设置失败: {ex}");
                MessageBox.Show(ex.Message, "开机启动设置失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }) { Checked = autoStart });
        menu.Items.Add(new ToolStripMenuItem("打开日志目录", null, (_, _) =>
        {
            Directory.CreateDirectory(DiagnosticLog.DirectoryPath);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            { FileName = DiagnosticLog.DirectoryPath, UseShellExecute = true });
        }));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("退出", null, (_, _) => ExitThread()));
    }

    private void SaveSettings()
    {
        try { SettingsStore.Save(settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { log.Write("ERR", $"配置保存失败: {ex}"); }
    }

    private void ShowDetails(DeviceSnapshot? selected = null)
    {
        var lines = (selected is null ? monitor.Snapshots : [selected]).Select(item =>
        {
            var last = item.LastGood is null ? "无" : $"{item.LastGood.BatteryPercent}%（{item.LastGood.UpdatedAt:yyyy-MM-dd HH:mm:ss}）";
            var charging = item.State == DeviceConnectionState.Connected
                ? item.LastGood?.IsCharging switch { true => "充电中", false => "未充电", _ => "未知" }
                : "未知";
            return $"{item.Device.Name}\n状态：{StatusText(item)}\n充电：{charging}\n上次有效电量：{last}";
        });
        MessageBox.Show(string.Join("\n\n", lines), "Wireless Battery Tray", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ShowHistory()
    {
        try
        {
            var rows = history.ReadRecent(12).Select(item =>
                $"{item.Timestamp:MM-dd HH:mm}  {item.DeviceName}  {item.ConnectionState}  {(item.Battery is int percent ? $"{percent}%" : "--%")}");
            var display = string.Join("\n", rows);
            MessageBox.Show(string.IsNullOrWhiteSpace(display) ? "暂无历史记录" : display,
                "最近历史", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            log.Write("ERR", $"历史记录读取失败: {ex}");
            MessageBox.Show(ex.Message, "历史记录读取失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (dispatcher.IsDisposed) return;
        try
        {
            dispatcher.BeginInvoke((Action)(async () =>
            {
                if (e.Mode == PowerModes.Suspend)
                {
                    suspended = true;
                    timer.Stop();
                    log.Write("INF", "系统进入睡眠，暂停刷新");
                }
                else if (e.Mode == PowerModes.Resume)
                {
                    timer.Stop();
                    log.Write("INF", "系统恢复，3 秒后重新枚举设备");
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(3), lifetime.Token);
                        suspended = false;
                        await RefreshAsync();
                    }
                    catch (OperationCanceledException) { }
                }
            }));
        }
        catch (InvalidOperationException) { }
    }

    private static string StatusText(DeviceSnapshot item)
    {
        var state = item.State switch
        {
            DeviceConnectionState.Connected => item.CurrentPercent is int percent ? $"{percent}%" : "电量未知",
            DeviceConnectionState.Sleeping => "无响应 / 可能休眠",
            DeviceConnectionState.Disconnected => "已断开",
            DeviceConnectionState.Error => "读取失败",
            _ => "未知"
        };
        if (item.State != DeviceConnectionState.Connected && item.LastGood is not null)
            state += item.IsStale(TimeSpan.FromMinutes(5), DateTimeOffset.Now) ? "（上次数据已过期）" : "（上次数据保留）";
        return state;
    }

    private void UpdateIcon(IReadOnlyList<DeviceSnapshot> states)
    {
        var lowest = states.Where(x => x.CurrentPercent.HasValue).MinBy(x => x.CurrentPercent);
        var text = lowest?.CurrentPercent?.ToString() ?? "--";
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(lowest?.CurrentPercent <= settings.LowBatteryThreshold ? Color.Firebrick : Color.FromArgb(32, 102, 72));
            g.FillEllipse(brush, 1, 1, 30, 30);
            using var font = new Font("Segoe UI", text.Length > 2 ? 11 : 14, FontStyle.Bold, GraphicsUnit.Pixel);
            var size = g.MeasureString(text, font);
            g.DrawString(text, font, Brushes.White, (32 - size.Width) / 2, (32 - size.Height) / 2);
        }
        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            var replacement = (Icon)temporary.Clone();
            notifyIcon.Icon = replacement;
            generatedIcon?.Dispose();
            generatedIcon = replacement;
        }
        finally { DestroyIcon(handle); }
        var tooltip = string.Join(" | ", states.Select(s => $"{s.Device.Name}: {StatusText(s)}"));
        notifyIcon.Text = tooltip.Length > 63 ? tooltip[..63] : tooltip;
    }

    protected override void ExitThreadCore()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        lifetime.Cancel();
        lifetime.Dispose();
        dispatcher.Dispose();
        timer.Stop();
        timer.Dispose();
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
        generatedIcon?.Dispose();
        menu.Dispose();
        base.ExitThreadCore();
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
