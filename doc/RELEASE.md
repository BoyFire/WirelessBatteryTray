# Release 准备与验收

当前已能构建待签名 Release 产物。发布流程与日常 Debug 构建分开；在安装器和签名凭据齐备前，不把未签名文件标记为正式 Release。

## 目标流程

1. 运行 `scripts/Prepare-Release.ps1`：构建 Release，执行协议、状态和通知自测，并发布待签名的托盘与 S99 Probe；在可访问两台设备的 Windows 环境运行实机读取。
2. 运行 `scripts/Finalize-Release.ps1`：对项目自有 `WirelessBatteryTray.exe`、`S99BatteryProbe.exe` 和项目自有 DLL 执行 Authenticode 签名并验签。安装器须从这些已签名文件构建；随后对安装器和卸载器签名并验签。
3. 仅打包已验签的文件；脚本会解压包并再次验签。还需在干净 Windows 环境安装、运行、卸载，检查签名与设备读取。

推荐使用 RSA 代码签名证书，并确认证书由 Windows 信任的提供者签发；Strong Name 不能代替 Authenticode。签名命令应明确使用 SHA-256 文件摘要和时间戳摘要，例如 `signtool sign /fd SHA256 /tr <timestamp-url> /td SHA256 ...`，验签时使用 `signtool verify /pa /v ...`。证书、私钥和访问凭据不得提交到仓库。

## 当前缺口

- Windows SDK `10.0.28000.0` x64 SignTool 已找到并可运行。
- `Prepare-Release.ps1` 已在目标项目目录跑通：Release 构建 0 警告、0 错误，三个自测均通过。待签名托盘实机读取 S99 83%（充电中）、MX Vertical 50%；11 个 EXE/DLL 目前均为 `NotSigned`，清单状态为 `UNSIGNED - DO NOT DISTRIBUTE`。
- Inno Setup 7.1.0 已安装。`scripts/Build-DevInstaller.ps1` 已从待签名托盘产物构建单文件 `WirelessBatteryTray-Setup-UNSIGNED.exe`；仅用于安装器结构验证，签名状态为 `NotSigned`，不供正式分发。
- `cert/` 中的 `root.cer` 是自签名、MD5 签名证书；`root.pvk` 和 `root.spc` 不应当用来冒充符合 Smart App Control 要求的受信任 Authenticode 证书。该目录已加入 `.gitignore`，私钥未被读取或打包。正式签名仍需受信任提供者签发的 RSA 代码签名证书或等效签名服务。
- `Finalize-Release.ps1` 仍需接入从已签名应用文件构建安装器、并签名/验签卸载器的流程；正式签名、打包和最终验收未执行。
- 当前程序是依赖 .NET 10 Windows Desktop Runtime 的框架依赖构建。打包前需确定安装器如何检查或提供该运行时。

参考：[Microsoft 的 Smart App Control 签名说明](https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/code-signing-for-smart-app-control)、[SignTool 文档](https://learn.microsoft.com/en-us/windows/win32/seccrypto/signtool)、[Inno Setup 签名安装器和卸载器说明](https://jrsoftware.org/ishelp/topic_setup_signeduninstaller.htm)。
