# Wireless Battery Tray

安装测试版 EXE：[dist/WirelessBatteryTray-Setup-UNSIGNED.exe](dist/WirelessBatteryTray-Setup-UNSIGNED.exe)。此文件未签名，依赖 .NET 10 Windows Desktop Runtime；正式发布签名仍待完成。

Windows USB 无线接收器电量读取项目。当前已完成 S99 与 MX Vertical 的协议验证和双设备统一 Probe；开发进度见 [DEVELOPMENT_CHECKLIST.md](DEVELOPMENT_CHECKLIST.md)，运行环境要求见 [doc/DEVELOPMENT_ENVIRONMENT.md](doc/DEVELOPMENT_ENVIRONMENT.md)。

## 构建

需要 .NET 10 SDK。在 PowerShell 中从项目根目录运行：

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' build WirelessBatteryTray.slnx
```

## 第一阶段验证

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' tools\S99BatteryProbe\bin\Debug\net10.0\S99BatteryProbe.dll --self-test
& 'C:\Program Files\dotnet\dotnet.exe' tools\S99BatteryProbe\bin\Debug\net10.0\S99BatteryProbe.dll --scan-only
& 'C:\Program Files\dotnet\dotnet.exe' tools\S99BatteryProbe\bin\Debug\net10.0\S99BatteryProbe.dll
```

`--scan-only` 只枚举 HID，不发送请求。完整 Probe 只发送开发文档中的 `04 00 00 1A 06 00 00 00` 查询请求。先运行自检和只读扫描，再唤醒 S99 键盘执行完整 Probe。输出包含每个候选 Collection 的路径和报告长度，以及每次请求/响应。

开发初期，Smart App Control 曾阻止新编译的 DLL，返回 `0x800711C7`；用户调整设置后，S99 Probe 已在当前机器实机读取成功。不要将后续模块的编译成功等同于协议验证成功。

S99 充电字段已实机确认：`0=未充电`、`1=充电中`，其他值显示 `Unknown` 并保留原始值。

## 双设备验证

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' tools\MXVerticalBatteryProbe\bin\Debug\net10.0\MXVerticalBatteryProbe.dll --self-test
& 'C:\Program Files\dotnet\dotnet.exe' tools\MXVerticalBatteryProbe\bin\Debug\net10.0\MXVerticalBatteryProbe.dll
& 'C:\Program Files\dotnet\dotnet.exe' tools\WirelessBatteryProbe\bin\Debug\net10.0\WirelessBatteryProbe.dll
```

MX Vertical 已在 Unifying Receiver 槽位 1、WPID `407B` 实机确认。当前设备使用 HID++ `0x1000 Battery Status`，`0x1004 Unified Battery` 不支持。统一 Probe 已同时读取 S99 与 MX Vertical 并返回 `Result: PASS`。
关闭 MX Vertical 时，统一 Probe 会报告 Logitech 无有效电量响应，而 S99 仍可读取；重新开启鼠标后双设备读取恢复，阶段 2 已通过。

## 阶段 3 托盘程序

构建后运行 `src\WirelessBatteryTray.App\bin\Debug\net10.0-windows\WirelessBatteryTray.exe`。托盘左键查看详情，右键查看两台设备状态、立即刷新、调整刷新间隔和低电量阈值、打开日志目录或退出。默认每 60 秒刷新；图标显示当前电量最低设备的百分比。无响应或断开时显示状态及上次有效数据，不把旧值当作当前电量。

配置写入 `%APPDATA%\WirelessBatteryTray\settings.json`，日志写入 `%LOCALAPPDATA%\WirelessBatteryTray\logs\`。低电量提醒使用 Windows 托盘通知，同一设备在阈值内只提醒一次；电量高于阈值 5 个百分点或开始充电后重新允许提醒。运行 `WirelessBatteryTray.exe --debug` 可在日志中记录原始 HID TX/RX；日常使用不建议持续开启。

验证命令（从项目根目录执行）：

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' src\WirelessBatteryTray.App\bin\Debug\net10.0-windows\WirelessBatteryTray.dll --self-test
& 'C:\Program Files\dotnet\dotnet.exe' src\WirelessBatteryTray.App\bin\Debug\net10.0-windows\WirelessBatteryTray.dll --once
```

阶段 3 的短时启动、实机读取和 MX Vertical 接收器拔插已通过；长期运行与 S99 自然休眠仍在观察。

## 阶段 4 功能

托盘菜单提供“开机启动”开关，默认关闭；开启时仅写入当前用户的 Windows 启动项，关闭时删除该启动项。建议先将程序放到固定位置，再开启此项。

每次刷新会把当前设备状态写入 `%LOCALAPPDATA%\WirelessBatteryTray\history.jsonl`，默认保留 7 天；断开或无响应的记录将电量写为 `null`，以免把上次读数当成当前电量。托盘菜单的“最近历史”可查看最近 12 条记录。系统从睡眠恢复后，程序会等待 3 秒并重新枚举设备、刷新状态。睡眠恢复仍需实机验收。

发布版所需的签名、安装器和验收条件见 [doc/RELEASE.md](doc/RELEASE.md)。

Windows SDK SignTool 已安装。当前可运行 `scripts\Prepare-Release.ps1` 完成 Release 构建、自测和待签名文件准备；输出位于 `artifacts\release-staging\`，明确标记为未签名，不供分发。正式签名仍需要受信任的代码签名证书，以及签名安装器和卸载器的构建验证。

Inno Setup 7 已安装。运行 `scripts\Build-DevInstaller.ps1 -StageRoot <待签名目录>` 可生成单文件 `WirelessBatteryTray-Setup-UNSIGNED.exe` 供本机安装测试。这个安装器尚未做 Authenticode 签名，安装后的程序依赖 .NET 10 Windows Desktop Runtime。`cert/` 中现有自签名 MD5 证书不能用于符合 Smart App Control 要求的正式发布签名。
