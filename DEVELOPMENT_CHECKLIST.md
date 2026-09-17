# Wireless Battery Tray 开发清单

状态约定：`[ ]` 待开始，`[~]` 进行中，`[x]` 已完成。只有满足阶段验收条件才标记完成。

开发期间不通过修改代码完整性策略绕过主机安全限制。早期 Smart App Control 曾阻止 Debug 产物，随后用户自行调整设置；具体经过见下方验证记录。

## 阶段 0：项目准备

- [x] 阅读需求文档并确定 .NET 10、模块边界和分阶段交付顺序。
- [x] 确认 .NET 10 SDK 可用（10.0.401）。
- [x] 创建解决方案、项目引用和可重复构建的配置。

验收：从项目根目录成功构建解决方案。

## 阶段 1：VGN S99 协议验证

- [x] 枚举 320F:5088 的 HID Collection，输出路径、Usage 和报告长度。
- [x] 向确认的厂商 Collection 发送文档中的电量查询命令。
- [x] 实现 500 ms 超时、最多 5 次重试，输出原始 TX/RX 和明确错误；正常路径已实测。
- [x] 验证命令、报告长度和电量范围，保留充电原始字段。
- [x] 固定报文解析自检和 `dotnet build` 均通过。
- [x] 当前 S99 实机连续读取 4 次，均为 61%，`ChargeRaw=0`。
- [x] 实测充电时的原始字段及其含义：`0=未充电`、`1=充电中`；其他值保持未知。

验收：实机报告与预期一致且重复读取稳定；未取得实机响应前保持未完成。

## 阶段 2：Logitech MX Vertical 协议验证

- [x] 枚举 `046D:C52B` Unifying Receiver，读取配对信息并发现槽位 1 的 `WPID 407B`。
- [x] 实现 HID++ 短报文写入、长报文接收和电池 Feature 发现。
- [x] 实机读取 MX Vertical 电量，连续 4 次均为 50%。
- [x] 验证 MX Vertical 关闭与重新开启时的状态和恢复；关闭时不报告错误的电量，重新开启后恢复读取。
- [x] 两台设备通过统一模型同时输出状态，`WirelessBatteryProbe` 返回 `Result: PASS`。

验收：两台设备均能连续、稳定读取。

## 阶段 3：托盘可用版

- [x] 实现单进程托盘、菜单、手动和定时刷新；实机首次刷新成功。
- [x] 实现未知、无响应、断开和过期数据展示；无响应时仅保留上次有效值，不显示为当前电量。
- [x] 实现低电量托盘通知及去重；状态自测覆盖阈值、重复刷新和重置。
- [x] 实现配置持久化与诊断日志；`--debug` 可记录原始 HID TX/RX。
- [x] 实机验证 MX Vertical 接收器拔插：断开显示 `--%`，插回后定时刷新自动恢复。
- [~] 验证普通用户权限下的长期运行与 S99 自然休眠场景。

验收：普通用户权限下长期运行，休眠与拔插不误报电量。

## 阶段 4：产品完善

- [x] 实现开机启动开关与 7 天历史；默认不启用开机启动，历史已在目标目录运行写入。
- [~] 处理系统睡眠恢复和接收器热插拔；热插拔已实测，睡眠恢复代码待实机验证。
- [ ] 完成长时间运行、资源占用、打包和使用说明验证。
- [x] Windows SDK SignTool 已安装，Release 构建、自测和待签名产物准备流程在目标目录通过。
- [x] Inno Setup 7 已安装，未签名的单文件 EXE 安装器已在目标目录构建并核对签名状态。
- [~] 已建立签名、验签与打包脚本；缺少受信任的签名凭据和已签名安装器/卸载器，正式签名未执行。

验收：达到开发文档中的 v1.0 功能与稳定性要求。

## 当前验证记录

- 2026-09-17：.NET SDK `10.0.401`；目标目录 `dotnet build WirelessBatteryTray.slnx` 成功，0 警告、0 错误。阶段 0 完成。
- 2026-09-17：运行 Probe 与 `--self-test` 时，Windows 返回 `0x800711C7`，提示应用程序控制策略阻止 DLL 加载。因此阶段 1 的解析自检和实机通信尚未得到运行结果；不能认定协议已验证。
- 2026-09-17：用户在自己的 PowerShell 中重复运行后仍被拦截。Code Integrity 日志事件 3077/3033 指向 `S99BatteryProbe.dll` 未满足策略 `{0283ac0f-fff1-49ae-ada1-8a933130cad6}`；本机 `VerifiedAndReputablePolicyState=1`。这是代码执行策略限制，阶段 1 保持进行中。
- 当时阶段状态：`BUILD PASS / RUNTIME BLOCKED BY HOST SECURITY POLICY`。受阻文件：`tools/S99BatteryProbe/bin/Debug/net10.0/S99BatteryProbe.dll`；此状态已随用户自行调整设置并完成实机验证而解除。
- 2026-09-17：用户自行调整 Smart App Control 后，本机 `--self-test` 通过；扫描到 `320F:5088` 的 `MI_01/COL04`，Usage Page `FF1C`、Usage `0092`，Input/Output 均为 64 字节。收到 `04 00 00 1A 06 00 00 00 3D 00 ...`，解析为 61%、`ChargeRaw=0`；连续 4 次读取一致。S99 电量查询链路已实机确认，充电状态非零值仍待验证。
- 2026-09-17：用户接上充电线后连续 3 次读取 `04 00 00 1A 06 00 00 00 3D 01 ...`，`ChargeRaw=1`；据此确认 `1=充电中`。阶段 1 验收完成。
- 2026-09-17：Logitech `046D:C52B` 接收器暴露 7/20 字节 HID++ Collection；配对寄存器返回槽位 1、`WPID 407B`。ROOT.GetFeature(`0x1004`) 返回索引 0；`0x1000` 返回索引 8。Battery Status 回复 `11 01 08 08 32 14 00 ...`，解析为 50%、未充电；连续 4 次一致。双设备统一 Probe 返回 `Result: PASS`。阶段 2 的断开/重连项仍待验证。
- 2026-09-17：关闭 MX Vertical 后统一 Probe 报告 `Logitech HID++: no valid battery response`，S99 仍为 78%、充电中；没有把无响应误报为 0%。重新开启后，MX Vertical 恢复为 50%、未充电，双设备 `Result: PASS`。阶段 2 验收完成。
- 2026-09-17：阶段 3 WinForms 托盘、手动/定时刷新、状态缓存、通知策略、设置和日志已实现。目标目录解决方案编译 0 警告、0 错误；`WirelessBatteryTray.dll --self-test` 通过；`--once` 实机读取 S99 79%（充电中）、MX Vertical 50%（未充电）。托盘进程启动并完成首次刷新。长期运行、休眠及接收器拔插验收仍在进行，阶段 3 暂不标记整体完成。
- 2026-09-17：拔下 MX Vertical USB 接收器后，`--once` 显示 `Disconnected, --%`，S99 仍为 80%；运行中的托盘定时日志也记录 MX Vertical 断开并保留上次有效值 50%。等待接收器插回后的自动恢复验证。
- 2026-09-17：插回 MX Vertical 接收器后，运行中的托盘按 60 秒定时刷新自动恢复为 50%，`--once` 再次显示双设备已连接。同步通知阈值修正后，目标目录重新构建 0 警告、0 错误，自测通过，新版托盘进程已启动。长期运行和 S99 自然休眠仍待观察。
- 2026-09-17：阶段 4 加入当前用户开机启动菜单开关（默认关闭）、本地 JSON Lines 7 天历史、最近历史菜单，以及系统恢复后延迟 3 秒重读。目标目录构建 0 警告、0 错误，自测通过；运行中的托盘已将 S99 81% 和 MX Vertical 50% 写入历史。睡眠恢复与更长时间运行待实机观察。当前主机未找到 Windows SDK 的 `signtool.exe`，也尚无受信任的 Authenticode 签名凭据。
- 2026-09-17：用户安装 Windows SDK 后找到 `10.0.28000.0` x64 SignTool，工具帮助命令正常。新增 Release 准备脚本，在开发工作区构建 0 警告、0 错误并通过 S99、Logitech、托盘三个自测，发布托盘和 S99 Probe 的待签名目录。当前用户/本机证书存储未发现代码签名证书；安装器与卸载器仍待实现，不能标记正式 Release 完成。
- 2026-09-17：目标目录执行 `Prepare-Release.ps1` 通过，Release 构建 0 警告、0 错误，三个自测通过；待签名托盘 `--once` 实机读取 S99 83%（充电中）和 MX Vertical 50%。`artifacts/release-staging/20260917-105811/manifest.json` 为 `UNSIGNED - DO NOT DISTRIBUTE`；11 个待发布 EXE/DLL 均为 `NotSigned`。用户确认暂时没有签名证书和安装器工具。
- 2026-09-17：用户安装 Inno Setup 7 并提供 `cert/root.cer`、`root.pvk`、`root.spc`。`root.cer` 是自签名、MD5 签名证书，没有可用于符合 Smart App Control 要求的受信任 Authenticode 代码签名证书；未读取或使用私钥。Inno Setup 7.1.0 已成功编译 `artifacts/release-staging/20260917-105811/installer-dev/WirelessBatteryTray-Setup-UNSIGNED.exe`，约 2.1 MB，SHA-256 为 `9EB5049DFB4932102C01D28847F554806392C530280401F9F517FA3795572D78`，签名状态 `NotSigned`。
