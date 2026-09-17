param(
    [Parameter(Mandatory = $true)][string]$StageRoot,
    [string]$IsccPath = 'C:\Program Files\Inno Setup 7\ISCC.exe'
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$stagingBase = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts\release-staging'))
$StageRoot = (Resolve-Path -LiteralPath $StageRoot).Path
if (-not $StageRoot.StartsWith($stagingBase + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "安装器输入必须位于 $stagingBase 内"
}
if (-not (Test-Path -LiteralPath $IsccPath -PathType Leaf)) { throw "找不到 Inno Setup：$IsccPath" }
$appFolder = Join-Path $StageRoot 'app'
if (-not (Test-Path -LiteralPath (Join-Path $appFolder 'WirelessBatteryTray.exe') -PathType Leaf)) {
    throw '待打包托盘程序不存在'
}
$manifest = Get-Content -LiteralPath (Join-Path $StageRoot 'manifest.json') -Raw | ConvertFrom-Json
if ($manifest.status -ne 'UNSIGNED - DO NOT DISTRIBUTE') {
    throw '此脚本只用于未签名开发产物的安装器结构验证'
}
$outputFolder = Join-Path $StageRoot 'installer-dev'
New-Item -ItemType Directory -Force -Path $outputFolder | Out-Null
$iss = Join-Path $repoRoot 'installer\WirelessBatteryTray.iss'
& $IsccPath --no-signing --no-ide-signtools "--define=AppSource=$appFolder" "--output-dir=$outputFolder" $iss
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup 开发安装器构建失败' }
$setup = Join-Path $outputFolder 'WirelessBatteryTray-Setup-UNSIGNED.exe'
if (-not (Test-Path -LiteralPath $setup -PathType Leaf)) { throw 'Inno Setup 未输出预期的安装器' }
Write-Output "DEV INSTALLER PASS / UNSIGNED - DO NOT DISTRIBUTE: $setup"
