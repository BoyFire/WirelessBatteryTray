# 开发环境与安全约束

开发初期，当前主机的 Windows Smart App Control 曾阻止新编译的 `S99BatteryProbe.dll`，错误码为 `0x800711C7`。Code Integrity 日志事件 3077/3033 记录了这一情况。随后用户自行调整安全设置，Probe 已能在本机运行，S99 电量与充电字段已实机验证。

## Debug 与 Probe

开发过程不自动修改 Defender、WDAC、Code Integrity、注册表或启动配置，也不添加降低整机安全级别的永久例外。构建产物保留在 `tools/S99BatteryProbe/bin/Debug/net10.0/`。

若以后改用专用 Windows 开发环境，并采用虚拟机，必须支持两个 USB 接收器的 HID/USB passthrough：

- VGN S99 2.4G 接收器；
- Logitech MX Vertical Unifying 接收器。

第一阶段只需透传 S99 接收器。确认客体系统能看到 `VID 320F / PID 5088` 后再执行以下步骤：

```powershell
cd C:\Dev\Codex-workspace\WirelessBatteryTray
& 'C:\Program Files\dotnet\dotnet.exe' build WirelessBatteryTray.slnx
& 'C:\Program Files\dotnet\dotnet.exe' tools\S99BatteryProbe\bin\Debug\net10.0\S99BatteryProbe.dll --self-test
& 'C:\Program Files\dotnet\dotnet.exe' tools\S99BatteryProbe\bin\Debug\net10.0\S99BatteryProbe.dll --scan-only
& 'C:\Program Files\dotnet\dotnet.exe' tools\S99BatteryProbe\bin\Debug\net10.0\S99BatteryProbe.dll
```

需记录全部 Collection 的路径、Usage Page、Usage 和三个报告长度，以及成功候选的 Report ID、完整 TX/RX、battery 和 charging 原始值。先在键盘唤醒状态下重复读取，再观察休眠、充电和接收器重连。只有这些实机结果完成后，才能勾选阶段 1。

## Release 规划

Release 阶段为项目自有二进制建立 Authenticode 签名流程，覆盖 `WirelessBatteryTray.exe`、`S99BatteryProbe.exe`、项目自有 DLL、安装器与卸载器。发布流程为：构建 → 在开发环境测试 → 签名 → 验证签名与证书链 → 打包 → 最终验证。签名与正常 Debug 构建分开配置。`.NET Strong Name` 不能代替 Authenticode，也不能据此断定 Smart App Control 会信任文件。
