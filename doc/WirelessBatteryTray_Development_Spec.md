# Wireless Battery Tray 开发文档

> 项目目标：在 Windows 平台统一读取并展示通过 2.4G USB 接收器连接的 VGN S99 键盘与 Logitech MX Vertical 鼠标的剩余电量，并提供系统托盘显示、低电量提醒、电量历史记录与后续多设备扩展能力。

---

# 1. 文档信息

- 文档名称：Wireless Battery Tray 开发文档
- 文档类型：软件需求说明 + 技术设计 + 协议说明 + 开发任务拆分
- 目标平台：Windows 10 / Windows 11
- 推荐技术栈：C# / .NET 8
- 推荐架构：模块化 Provider 架构
- 首批支持设备：
  - VGN S99 键盘，2.4G 接收器模式
  - Logitech MX Vertical 鼠标，Unifying 接收器模式
- 初期重点：先验证 HID 协议，不优先开发 GUI

---

# 2. 项目背景

当前设备均通过 USB 无线接收器连接电脑，而不是通过蓝牙连接：

1. VGN S99 键盘：使用 2.4G USB 接收器。
2. Logitech MX Vertical 鼠标：使用 Logitech Unifying Receiver。

Windows 对此类设备通常只会枚举为 USB HID 设备，系统不会统一提供电量百分比。因此，若想在系统托盘统一显示设备电量，需要直接访问各厂商 HID 协议。

目前已经从现有开源项目中确认：

- VGN S99 的 2.4G 接收器电量查询协议已经存在开源实现。
- Logitech MX Vertical 使用 Logitech HID++ 协议，已有成熟开源实现可参考。

因此，本项目无需从零逆向两种协议，可以进入“验证 + 封装 + 产品化”阶段。

---

# 3. 项目目标

## 3.1 第一阶段目标

第一阶段仅完成协议验证，不做复杂 UI。

要求开发一个命令行程序：

`S99BatteryProbe.exe`

程序需实现：

1. 枚举 Windows HID 设备。
2. 自动发现 VGN S99 2.4G 接收器。
3. 输出 VID、PID、设备路径、Usage Page、Usage、Input/Output/Feature Report 长度。
4. 自动筛选可写入 Output Report 的 HID Collection。
5. 发送 S99 电量查询命令。
6. 等待并读取 Input Report。
7. 输出原始十六进制返回值。
8. 解析电量百分比。
9. 解析充电状态。
10. 在控制台显示明确成功/失败结果。

验收标准：

```text
[SUCCESS] VGN S99 battery protocol confirmed
Battery: 0~100%
```

只要实机返回合理的电量值，即可确认 S99 协议完全打通。

---

## 3.2 第二阶段目标

新增 Logitech MX Vertical 电量读取。

最终命令行测试程序输出类似：

```text
=== Wireless Battery Probe ===

VGN S99
Battery: 76%
Charging: No

Logitech MX Vertical
Battery: 48%
Charging: No

Result: PASS
```

---

## 3.3 第三阶段目标

开发 Windows 托盘程序：

`WirelessBatteryTray.exe`

功能包括：

- 托盘常驻
- 开机自启动
- 自动刷新电量
- 点击托盘查看全部设备
- 托盘图标显示最低电量设备
- Windows Toast 低电量提醒
- 充电状态显示
- 设备断开/重连处理
- 电量历史记录
- 日志

---

# 4. 整体架构

推荐架构：

```text
WirelessBatteryTray
│
├── App
│   ├── Program.cs
│   ├── AppHost.cs
│   └── Startup.cs
│
├── Core
│   ├── Models
│   │   ├── BatteryDevice.cs
│   │   ├── BatteryState.cs
│   │   └── DeviceConnectionState.cs
│   │
│   ├── Interfaces
│   │   ├── IBatteryProvider.cs
│   │   └── IHidTransport.cs
│   │
│   ├── Services
│   │   ├── BatteryManager.cs
│   │   ├── DeviceManager.cs
│   │   ├── NotificationService.cs
│   │   └── HistoryService.cs
│   │
│   └── Utils
│       ├── HexFormatter.cs
│       ├── RetryHelper.cs
│       └── Logger.cs
│
├── Providers
│   ├── Vgn
│   │   ├── VgnBatteryProvider.cs
│   │   ├── VgnProtocol.cs
│   │   └── VgnDeviceDefinitions.cs
│   │
│   └── Logitech
│       ├── LogitechHidppProvider.cs
│       ├── HidppProtocol.cs
│       └── LogitechDeviceDefinitions.cs
│
├── Hid
│   ├── HidEnumerator.cs
│   ├── HidDeviceInfo.cs
│   ├── WindowsHidTransport.cs
│   └── HidCapabilitiesReader.cs
│
├── Tray
│   ├── TrayIconService.cs
│   ├── TrayMenu.cs
│   └── TrayWindow.cs
│
├── Notifications
│   └── WindowsToastService.cs
│
├── Storage
│   ├── AppSettings.cs
│   ├── BatteryHistoryRepository.cs
│   └── JsonSettingsRepository.cs
│
└── Tests
    ├── VgnProtocolTests.cs
    ├── HidppProtocolTests.cs
    └── BatteryManagerTests.cs
```

---

# 5. Provider 抽象设计

核心思想：不同品牌设备使用不同协议，但对上层统一返回 `BatteryState`。

建议定义接口：

```csharp
public interface IBatteryProvider
{
    string ProviderName { get; }

    bool CanHandle(HidDeviceInfo device);

    Task<BatteryState?> ReadBatteryAsync(
        HidDeviceInfo device,
        CancellationToken cancellationToken = default);
}
```

统一返回模型：

```csharp
public sealed class BatteryState
{
    public string DeviceId { get; init; } = string.Empty;
    public string DeviceName { get; init; } = string.Empty;
    public int BatteryPercent { get; init; }
    public bool? IsCharging { get; init; }
    public bool IsConnected { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public string Provider { get; init; } = string.Empty;
}
```

这样后续新增设备时，不需要修改 UI 和 BatteryManager。

---

# 6. VGN S99 硬件识别信息

## 6.1 已知 VID/PID

VGN S99 当前已确认两组 USB 标识：

### 有线模式

```text
VID = 0x320F
PID = 0x5055
```

### 2.4G 接收器模式

```text
VID = 0x320F
PID = 0x5088
```

本项目重点支持：

```text
320F:5088
```

---

# 7. VGN S99 HID 协议信息

## 7.1 协议族

现有开源代码将 VGN S99 归类到：

```text
Family::Weisheng
```

即 Weisheng HID 协议族。

---

## 7.2 Report ID

S99 2.4G 接收器当前已知：

```text
Report ID = 0x04
```

---

## 7.3 电量查询 Payload

查询 Payload：

```text
00 00 1A 06 00 00 00
```

长度：

```text
7 bytes
```

其中：

```text
0x1A = GetBatteryLevel
```

十进制：

```text
26
```

---

## 7.4 hid_write 实际发送数据

hidapi 的 `hid_write()` 第 1 字节为 Report ID，因此实际写入：

```text
04 00 00 1A 06 00 00 00
```

拆分：

```text
04                   Report ID
00 00 1A 06 00 00 00 Payload
```

总长度：

```text
8 bytes
```

---

# 8. VGN S99 电量响应解析

读取 Input Report 时建议至少分配：

```text
16 bytes
```

某些 Windows HID API 返回数据可能包含 Report ID，某些封装层可能已经剥离。

因此需支持：

```text
if response[0] == 0x04
    base = 1
else
    base = 0
```

然后确认：

```text
response[base + 2] == 0x1A
```

只有满足此条件，才认为这是电量查询响应。

---

## 8.1 电量字段

```text
Battery = response[base + 7]
```

有效范围：

```text
0 ~ 100
```

如果超过 100，应视为解析失败。

---

## 8.2 充电状态

```text
Charging = response[base + 8]
```

推荐初步解释：

```text
0   = 未充电
非0 = 正在充电
```

由于不同固件可能存在更多状态值，第一阶段日志必须保留原始字段值。

---

# 9. VGN S99 示例数据

以下仅为解析示例，不表示真实设备一定返回该字节序列：

```text
04 00 00 1A 06 00 00 00 57 00
```

解析：

```text
ReportId = 0x04
Command  = 0x1A
Battery  = 0x57 = 87
Charging = 0x00
```

输出：

```text
Battery: 87%
Charging: No
```

---

# 10. S99 接收器不是唯一使用 320F:5088 的设备

必须特别注意：

```text
VID 320F / PID 5088
```

并不是 VGN S99 唯一占用。

其他键盘平台也可能共享：

```text
320F:5055
320F:5088
```

因此禁止只通过 VID/PID 判断“这是 VGN S99”。

错误实现：

```csharp
if (vid == 0x320F && pid == 0x5088)
{
    return "VGN S99";
}
```

正确思路：

```text
VID/PID 匹配
   ↓
枚举 HID Collection
   ↓
找到可写 Output Report 接口
   ↓
发送 0x1A 电量命令
   ↓
检查返回 command = 0x1A
   ↓
检查 Battery ∈ [0, 100]
   ↓
确认兼容 Weisheng Battery Protocol
```

所以 Provider 的判断原则应当是：

```text
协议确认 > 产品名称确认
```

---

# 11. HID Interface 选择策略

不要硬编码：

```text
MI_01
Interface 1
```

虽然 SignalRGB 针对部分 VGN S99 配置通道使用：

```text
Interface = 1
Usage Page = 0xFF1C
Usage = 0x0092
```

但接收器模式可能存在多个 HID Collection。

因此应动态读取：

- Usage Page
- Usage
- InputReportByteLength
- OutputReportByteLength
- FeatureReportByteLength
- Device Path
- Interface number

推荐使用：

```text
SetupAPI
HidD_GetPreparsedData
HidP_GetCaps
```

或者由 HidSharp / hidapi 封装。

---

# 12. HID Collection 筛选规则

对所有：

```text
VID = 0x320F
PID = 0x5088
```

对应的 HID Collection 逐个检查。

过滤条件：

```text
OutputReportByteLength > 0
```

只有支持 Output Report 的 Collection 才值得尝试。

Input-only Collection 直接跳过。

---

# 13. VGN S99 查询流程

伪代码：

```csharp
foreach (var device in EnumerateHidDevices(0x320F, 0x5088))
{
    if (device.OutputReportLength <= 0)
        continue;

    using var connection = Open(device);

    byte[] request =
    {
        0x04,
        0x00,
        0x00,
        0x1A,
        0x06,
        0x00,
        0x00,
        0x00
    };

    for (int retry = 0; retry < 5; retry++)
    {
        Write(request);

        var response = Read(timeoutMs: 500);

        if (response == null)
            continue;

        int baseIndex = response[0] == 0x04 ? 1 : 0;

        if (response.Length <= baseIndex + 8)
            continue;

        if (response[baseIndex + 2] != 0x1A)
            continue;

        int battery = response[baseIndex + 7];
        int chargeRaw = response[baseIndex + 8];

        if (battery < 0 || battery > 100)
            continue;

        return new BatteryState
        {
            DeviceName = "VGN S99",
            BatteryPercent = battery,
            IsCharging = chargeRaw != 0,
            IsConnected = true,
            UpdatedAt = DateTimeOffset.Now,
            Provider = "VGN/Weisheng"
        };
    }
}
```

---

# 14. 超时与重试策略

建议：

```text
单次等待超时：500 ms
最大重试次数：5
```

原因：

- 2.4G 接收器存在无线延迟。
- 键盘休眠状态下首次查询可能无响应。
- 某些接收器需要设备唤醒后才返回数据。

最终 GUI 版本不建议连续快速重试，应避免影响无线通信。

---

# 15. S99 休眠状态处理

2.4G 键盘为了节能可能进入休眠。

可能出现：

```text
接收器在线
但键盘不响应电量请求
```

此时不能直接显示：

```text
0%
```

应该显示：

```text
Sleeping
Unknown
--%
```

状态模型建议：

```csharp
public enum DeviceConnectionState
{
    Unknown,
    Connected,
    Sleeping,
    Disconnected,
    Error
}
```

---

# 16. S99 第一阶段 Probe 输出要求

程序必须打印：

```text
=== HID Device Scan ===

VID: 320F
PID: 5088

Path:
\\?\hid#vid_320f&pid_5088&mi_xx...

Interface: x
UsagePage: XXXX
Usage: XXXX

InputReportLength: xx
OutputReportLength: xx
FeatureReportLength: xx

Sending:
04 00 00 1A 06 00 00 00

Received:
04 xx xx 1A xx xx xx xx 53 00

Parsed:
Command: 0x1A
Battery: 83%
ChargeRaw: 0
Charging: No

[SUCCESS] VGN S99 battery protocol confirmed
```

失败时必须输出原因。

例如：

```text
[WARN] No writable HID collection found
```

或：

```text
[WARN] Device did not respond within 500 ms
```

或：

```text
[ERROR] Invalid battery value: 184
```

---

# 17. Logitech MX Vertical 支持方案

## 17.1 连接方式

当前设备使用：

```text
Logitech Unifying Receiver
```

MX Vertical 使用 Logitech HID++ 2.0 协议。

---

# 18. HID++ Provider 设计

建议单独实现：

```text
LogitechHidppProvider
```

职责：

1. 枚举 Logitech Receiver。
2. 发现绑定设备。
3. 定位 MX Vertical。
4. 查询 HID++ feature table。
5. 查找电池 Feature。
6. 读取电量。
7. 返回统一 BatteryState。

---

# 19. Logitech 电池相关 Feature

常见 HID++ Feature：

```text
0x1000 Battery Status
0x1001 Battery Voltage
0x1004 Unified Battery
```

推荐优先：

```text
0x1004 Unified Battery
```

如设备不支持，再回退到：

```text
0x1000
```

---

# 20. Logitech Provider 数据模型

最终统一输出：

```csharp
new BatteryState
{
    DeviceName = "Logitech MX Vertical",
    BatteryPercent = 48,
    IsCharging = false,
    IsConnected = true,
    Provider = "Logitech HID++",
    UpdatedAt = DateTimeOffset.Now
};
```

---

# 21. BatteryManager 设计

BatteryManager 负责统一管理多个 Provider。

职责：

1. Provider 注册。
2. 自动扫描设备。
3. 周期刷新电量。
4. 缓存最后一次有效数据。
5. 触发 UI 更新。
6. 触发低电量通知。
7. 写入历史记录。

接口示例：

```csharp
public sealed class BatteryManager
{
    public IReadOnlyList<BatteryState> Devices { get; }

    public Task RefreshAsync();

    public BatteryState? GetLowestBatteryDevice();
}
```

---

# 22. 刷新策略

不建议每秒读取。

推荐：

普通状态：

```text
60 秒刷新一次
```

低电量状态：

```text
30 秒刷新一次
```

设备休眠：

```text
120 秒刷新一次
```

GUI 可提供：

```text
30 秒
1 分钟
5 分钟
```

---

# 23. 托盘设计

托盘右键：

```text
Wireless Battery Tray
------------------------
⌨ VGN S99            76%
🖱 MX Vertical        48%
------------------------
立即刷新
打开详情
设置
开机启动 ✓
------------------------
退出
```

---

# 24. 托盘图标策略

默认：

显示当前电量最低设备。

例如：

```text
S99 76%
MX 48%
```

托盘显示：

```text
48
```

Tooltip：

```text
MX Vertical - 48%
S99 - 76%
```

---

# 25. 低电量通知

默认阈值：

```text
20%
```

建议支持：

```text
关闭
10%
15%
20%
30%
自定义
```

示例：

```text
VGN S99 电量不足
当前剩余 18%，建议及时充电。
```

---

# 26. 防止通知轰炸

同一设备到达低电量阈值后只通知一次。

直到满足任一条件才允许再次通知：

1. 电量重新超过阈值 + 5%。
2. 设备完成充电。
3. 用户手工重置通知状态。

例如：

```text
Threshold = 20
ResetThreshold = 25
```

---

# 27. 电量历史

建议保存：

```text
时间
设备 ID
设备名称
Battery
Charging
ConnectionState
```

例如：

```json
{
  "deviceId": "vgn-s99-320f-5088",
  "timestamp": "2026-09-17T08:30:00+08:00",
  "battery": 72,
  "charging": false
}
```

---

# 28. 历史数据存储

第一版使用：

```text
JSON Lines
```

或者：

```text
SQLite
```

如果只需要 7 天历史，SQLite 更方便。

推荐：

```text
%LOCALAPPDATA%\WirelessBatteryTray\battery.db
```

---

# 29. 配置文件

建议：

```text
%APPDATA%\WirelessBatteryTray\settings.json
```

示例：

```json
{
  "refreshIntervalSeconds": 60,
  "lowBatteryThreshold": 20,
  "startWithWindows": true,
  "trayDisplayMode": "LowestBattery",
  "historyRetentionDays": 7,
  "notificationsEnabled": true
}
```

---

# 30. 日志

建议使用：

```text
Serilog
```

路径：

```text
%LOCALAPPDATA%\WirelessBatteryTray\logs\
```

开发模式必须输出原始 HID 报文。

例如：

```text
2026-09-17 08:15:01 [DBG] VGN TX: 04 00 00 1A 06 00 00 00
2026-09-17 08:15:01 [DBG] VGN RX: 04 00 00 1A 06 00 00 00 53 00
2026-09-17 08:15:01 [INF] VGN S99 Battery=83 Charging=False
```

生产模式不必持续记录全部 HID 包，可由 Debug 开关控制。

---

# 31. HID 写入安全原则

第一阶段仅发送已经确认的只读查询命令：

```text
GetBatteryLevel = 0x1A
```

禁止在未确认协议含义前发送：

- 固件更新命令
- EEPROM 写入命令
- 灯效永久保存命令
- 配置写命令
- 配对命令

Probe 工具应被设计成“只读设备状态”。

---

# 32. 权限要求

优先目标：

```text
普通用户权限运行
```

正常 Windows HID 设备通常无需管理员权限。

如果某个 HID Collection 因驱动占用无法打开，应：

1. 继续尝试其他 Collection。
2. 不强制管理员权限。
3. 日志中记录 AccessDenied / SharingViolation。

---

# 33. 热插拔支持

最终托盘程序需要支持：

- 接收器拔出
- 接收器重新插入
- 键盘睡眠
- 键盘唤醒
- Logitech Receiver 重连

Windows 可通过 Device Notification / WMI / periodic enumeration 实现。

初期可先使用：

```text
定时重新枚举
```

后期优化为事件驱动。

---

# 34. 设备唯一 ID

不能只使用：

```text
VID/PID
```

建议组合：

```text
Provider
VID
PID
SerialNumber
DevicePath
WirelessIndex
```

例如：

```text
vgn:320f:5088:path-hash
```

Logitech Receiver 下还需要加入 Device Index。

---

# 35. 错误模型

建议定义：

```csharp
public enum BatteryReadError
{
    None,
    DeviceNotFound,
    NoWritableInterface,
    OpenFailed,
    WriteFailed,
    Timeout,
    InvalidResponse,
    InvalidBatteryValue,
    DeviceSleeping,
    UnsupportedProtocol
}
```

这样 UI 才能区分：

```text
未连接
休眠
不支持
读取失败
```

---

# 36. 第一阶段开发任务拆分

## T01 建立项目

创建：

```text
WirelessBatteryTray.sln
S99BatteryProbe
WirelessBattery.Core
WirelessBattery.Hid
```

验收：

```text
dotnet build
PASS
```

---

## T02 HID 枚举

实现：

```text
枚举系统所有 HID
过滤 320F:5088
```

输出：

```text
Path
VID
PID
UsagePage
Usage
InputReportLength
OutputReportLength
FeatureReportLength
```

验收：

能够在用户电脑发现 S99 Receiver。

---

## T03 HID 打开测试

逐个 Collection 尝试打开。

验收：

至少找到一个：

```text
OutputReportLength > 0
```

且可以建立句柄。

---

## T04 S99 电量查询

发送：

```text
04 00 00 1A 06 00 00 00
```

等待：

```text
500ms
```

最多：

```text
5次
```

---

## T05 S99 响应解析

实现：

```text
Report ID detection
Command validation
Battery extraction
Charging extraction
```

验收：

输出合理 0~100 电量。

---

## T06 原始协议日志

必须完整打印 TX/RX Hex。

用于确认不同固件是否一致。

---

# 37. 第二阶段任务

## T07 Logitech Receiver 枚举

识别 Logitech Unifying Receiver。

---

## T08 HID++ 通信

实现 HID++ 2.0 基础通信层。

---

## T09 MX Vertical 设备发现

在 Receiver 中定位 MX Vertical。

---

## T10 电池 Feature 查询

优先：

```text
0x1004 Unified Battery
```

备用：

```text
0x1000 Battery Status
```

---

## T11 统一 BatteryManager

S99 和 MX 返回相同 BatteryState。

---

# 38. 第三阶段任务

## T12 系统托盘

实现 NotifyIcon。

---

## T13 托盘菜单

显示全部设备电量。

---

## T14 自动刷新

默认 60 秒。

---

## T15 Windows Toast

低电量提醒。

---

## T16 配置持久化

settings.json。

---

## T17 开机启动

建议使用：

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Run
```

或者 Windows Startup Task。

---

## T18 历史记录

保存 7 天电量历史。

---

# 39. 测试用例

## TC01 S99 正常读取

前置：

键盘已唤醒。

预期：

```text
Battery 0~100
Command 0x1A
```

---

## TC02 S99 休眠

前置：

键盘长时间未使用。

预期：

```text
Timeout / Sleeping
```

不能显示：

```text
0%
```

---

## TC03 S99 拔出 Receiver

预期：

```text
Disconnected
```

程序不崩溃。

---

## TC04 Receiver 重新插入

预期：

自动重新发现。

---

## TC05 Battery=100

必须正确显示 100%。

---

## TC06 无效电量

模拟：

```text
Battery=255
```

预期：

```text
InvalidResponse
```

---

## TC07 MX Vertical 正常读取

预期：

返回合理电量。

---

## TC08 MX 断开

预期：

状态变为 Disconnected。

---

## TC09 低电量通知

例如：

```text
Battery=19
Threshold=20
```

预期 Toast 一次。

---

## TC10 通知防抖

连续刷新 10 次仍为 19%。

预期：

仅一次通知。

---

# 40. 关键验收标准

## S99 协议验收

满足全部：

```text
[ ] 能发现 320F:5088
[ ] 能找到可写 HID Collection
[ ] 能成功发送 04 00 00 1A 06 00 00 00
[ ] 能收到响应
[ ] 响应 command = 0x1A
[ ] battery ∈ 0~100
[ ] 重复读取结果稳定
```

即可判定：

```text
VGN S99 2.4G battery protocol = VERIFIED
```

---

# 41. 产品级验收标准

正式 v1.0：

```text
[ ] 支持 VGN S99 2.4G
[ ] 支持 Logitech MX Vertical
[ ] 不依赖 VGN HUB
[ ] 不依赖 Logi Options+
[ ] 普通用户权限运行
[ ] 开机自动启动
[ ] 托盘显示
[ ] 低电量提醒
[ ] 设备休眠不误报 0%
[ ] 接收器热插拔不崩溃
[ ] CPU 平均占用接近 0
[ ] 内存占用合理
```

---

# 42. 推荐技术选型

## UI

第一推荐：

```text
WinForms + NotifyIcon
```

原因：

- 托盘开发简单。
- 运行轻量。
- .NET 8 支持成熟。

如果后续需要更漂亮 UI：

```text
WPF
```

---

## HID

推荐评估：

```text
HidSharp
```

或：

```text
hidapi native binding
```

如果只针对 Windows，亦可直接使用：

```text
SetupAPI
hid.dll
CreateFile
ReadFile
WriteFile
HidD_*
HidP_*
```

为了快速开发建议第一阶段使用 HidSharp。

---

# 43. 推荐 NuGet 包

可评估：

```text
HidSharp
Serilog
Serilog.Sinks.File
Microsoft.Toolkit.Uwp.Notifications
Microsoft.Data.Sqlite
```

实际选择应在开发时确认最新兼容版本。

---

# 44. 性能目标

后台常驻：

```text
CPU idle < 0.5%
```

读取瞬间允许短时升高。

内存建议：

```text
< 100 MB
```

更理想：

```text
20~50 MB
```

---

# 45. 稳定性要求

以下情况均不能崩溃：

- 接收器突然拔出
- 键盘睡眠
- 接收器被其他程序占用
- HID Write 超时
- HID Read 超时
- 返回包长度异常
- Unknown Report ID
- Battery > 100
- Windows 从睡眠恢复
- USB 设备重新枚举

---

# 46. Windows 睡眠恢复

正式版需处理：

```text
System Suspend
Resume
```

Resume 后应：

1. 清理旧 HID Handle。
2. 延迟 2~5 秒。
3. 重新枚举设备。
4. 重新读取电量。

---

# 47. 调试模式

建议命令行参数：

```text
--debug
--dump-hid
--device vgn
--device logitech
--once
```

例如：

```text
WirelessBatteryTray.exe --debug --dump-hid
```

输出所有 HID 原始数据。

---

# 48. Probe 独立工具建议

不要把所有调试能力放进正式 GUI。

保留：

```text
tools/S99BatteryProbe.exe
```

用于：

- 新固件验证
- 新设备测试
- 用户提交日志
- GitHub issue 排查

---

# 49. 可扩展 Provider

未来可新增：

```text
RazerProvider
AsusRogProvider
SteelSeriesProvider
BluetoothBatteryProvider
```

因此 Provider 不应和 UI 耦合。

---

# 50. 推荐仓库结构

```text
WirelessBatteryTray/
│
├── README.md
├── LICENSE
├── WirelessBatteryTray.sln
│
├── src/
│   ├── WirelessBatteryTray.App/
│   ├── WirelessBattery.Core/
│   ├── WirelessBattery.Hid/
│   ├── WirelessBattery.Provider.Vgn/
│   └── WirelessBattery.Provider.Logitech/
│
├── tools/
│   ├── S99BatteryProbe/
│   └── HidDump/
│
├── tests/
│   ├── WirelessBattery.Core.Tests/
│   ├── WirelessBattery.Vgn.Tests/
│   └── WirelessBattery.Logitech.Tests/
│
└── docs/
    ├── VGN_S99_PROTOCOL.md
    ├── LOGITECH_HIDPP.md
    └── TROUBLESHOOTING.md
```

---

# 51. 建议的第一阶段开发顺序

不要先做 GUI。

正确顺序：

```text
1. HID 枚举
   ↓
2. 找到 320F:5088
   ↓
3. 打开可写 Collection
   ↓
4. 发送 S99 电量查询
   ↓
5. 解析返回
   ↓
6. 实机确认
   ↓
7. Logitech HID++
   ↓
8. 统一 BatteryManager
   ↓
9. Tray
   ↓
10. Notification
   ↓
11. History
```

这样最小化开发风险。

---

# 52. 当前技术结论

目前可以确定：

## VGN S99

```text
2.4G VID: 0x320F
2.4G PID: 0x5088
Protocol: Weisheng
Report ID: 0x04
Battery Command: 0x1A
Request Payload: 00 00 1A 06 00 00 00
Actual HID Write: 04 00 00 1A 06 00 00 00
Battery Field: payload[7]
Charging Field: payload[8]
Timeout: 500ms
Retry: 5
```

这意味着：

```text
S99 电量读取具备直接开发条件。
```

剩余风险不是协议未知，而是：

```text
需要在当前用户这只 S99 Receiver 固件上做一次实机确认。
```

---

# 53. 最终判断

项目技术可行性：

```text
高
```

原因：

1. S99 HID 电量协议已有开源实现。
2. MX Vertical HID++ 已有成熟生态。
3. 两者均可以通过用户态 HID API 访问。
4. 无需驱动开发。
5. 无需内核模块。
6. 无需持续运行厂商官方软件。
7. 适合封装成轻量 Windows Tray 程序。

推荐立即进入：

```text
Phase 1：S99BatteryProbe
```

只有在 Probe 实机成功后，再投入正式 UI 开发。

---

# 54. 开发第一条任务指令

可直接给 Codex / Claude Code：

```text
请在 Windows 11 + .NET 8 环境创建一个名为 S99BatteryProbe 的 C# 控制台程序。

目标：验证 VGN S99 通过 2.4G USB Receiver 的电量查询协议。

要求：

1. 枚举所有 VID=0x320F、PID=0x5088 的 HID Collection。
2. 打印每个 Collection 的 DevicePath、UsagePage、Usage、InputReportByteLength、OutputReportByteLength、FeatureReportByteLength。
3. 仅对 OutputReportByteLength > 0 的 Collection 尝试通信。
4. 发送以下 HID 数据：

   04 00 00 1A 06 00 00 00

5. 单次读取超时 500ms，最多重试 5 次。
6. 完整打印每次 TX/RX 的十六进制数据。
7. 如果返回第一个字节为 0x04，则 base=1，否则 base=0。
8. 验证 response[base+2] == 0x1A。
9. Battery = response[base+7]。
10. ChargingRaw = response[base+8]。
11. Battery 必须在 0~100。
12. 成功时输出：

   [SUCCESS] VGN S99 battery protocol confirmed

13. 任何异常不得导致程序崩溃，应输出明确错误。
14. 不发送任何除 GetBatteryLevel 之外的 HID 命令。
15. 不做 GUI。
16. 代码结构保持可复用，后续将合并入 WirelessBatteryTray。
```

---

# 55. 后续目标

当第一阶段确认成功后，下一份开发任务应是：

```text
MXVerticalBatteryProbe
```

然后进入：

```text
WirelessBatteryTray v0.1
```

第一版产品范围建议严格控制在：

```text
VGN S99
Logitech MX Vertical
托盘
自动刷新
低电量通知
```

暂时不要加入复杂皮肤、云同步、设备控制、灯效控制等功能。

这样可以最快得到稳定可用版本。
