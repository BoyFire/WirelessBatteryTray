param(
    [Parameter(Mandatory = $true)][string]$StageRoot,
    [Parameter(Mandatory = $true)][string]$CertificateThumbprint,
    [Parameter(Mandatory = $true)][string]$TimestampUrl,
    [Parameter(Mandatory = $true)][string]$InstallerBuildScript,
    [string]$SignToolPath = 'C:\Program Files (x86)\Windows Kits\10\bin\10.0.28000.0\x64\signtool.exe',
    [switch]$MachineStore
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$stagingBase = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts\release-staging'))
$StageRoot = (Resolve-Path -LiteralPath $StageRoot).Path
if (-not $StageRoot.StartsWith($stagingBase + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "待签名目录必须位于 $stagingBase 内"
}
if (-not (Test-Path -LiteralPath $SignToolPath -PathType Leaf)) { throw "找不到 SignTool：$SignToolPath" }
if (-not (Test-Path -LiteralPath $InstallerBuildScript -PathType Leaf)) {
    throw "找不到从已签名文件构建安装器的脚本：$InstallerBuildScript"
}
$CertificateThumbprint = $CertificateThumbprint.Replace(' ', '').ToUpperInvariant()
if ($CertificateThumbprint -notmatch '^[0-9A-F]{40}$') { throw '证书指纹必须是 40 位十六进制 SHA-1 指纹。' }
$store = if ($MachineStore) { 'LocalMachine' } else { 'CurrentUser' }
$certificate = Get-Item -LiteralPath "Cert:\$store\My\$CertificateThumbprint" -ErrorAction SilentlyContinue
if (-not $certificate -or -not $certificate.HasPrivateKey -or $certificate.NotAfter -le (Get-Date)) {
    throw "证书存储 $store\My 中没有可用的私钥证书。"
}
if ($certificate.PublicKey.Oid.Value -ne '1.2.840.113549.1.1.1') {
    throw 'Smart App Control 发布签名需要 RSA 证书。'
}
if (-not ($certificate.EnhancedKeyUsageList | Where-Object { $_.ObjectId -eq '1.3.6.1.5.5.7.3.3' })) {
    throw '证书缺少 Code Signing EKU。'
}

$binaries = @(Get-ChildItem -LiteralPath $StageRoot -Recurse -File |
    Where-Object { $_.Extension -in @('.exe', '.dll') })
if ($binaries.Count -lt 8) { throw '待签名 EXE/DLL 数量异常，请检查发布输出。' }
foreach ($binary in $binaries) {
    $arguments = @('sign', '/sha1', $CertificateThumbprint, '/fd', 'SHA256',
        '/tr', $TimestampUrl, '/td', 'SHA256')
    if ($MachineStore) { $arguments += '/sm' }
    $arguments += $binary.FullName
    & $SignToolPath @arguments
    if ($LASTEXITCODE -ne 0) { throw "签名失败：$($binary.FullName)" }
    & $SignToolPath verify /pa /v /tw $binary.FullName
    if ($LASTEXITCODE -ne 0) { throw "验签失败：$($binary.FullName)" }
    if ((Get-AuthenticodeSignature -LiteralPath $binary.FullName).Status -ne 'Valid') {
        throw "Windows Authenticode 信任验证失败：$($binary.FullName)"
    }
}

$installerFolder = Join-Path $StageRoot 'installer'
New-Item -ItemType Directory -Force -Path $installerFolder | Out-Null
& $InstallerBuildScript -StageRoot $StageRoot -OutputDirectory $installerFolder
if (-not $?) { throw '安装器构建脚本失败。' }
$installerCopy = Join-Path $installerFolder 'WirelessBatteryTray-Setup.exe'
$uninstallerCopy = Join-Path $installerFolder 'WirelessBatteryTray-Uninstall.exe'
foreach ($installerBinary in @($installerCopy, $uninstallerCopy)) {
    if (-not (Test-Path -LiteralPath $installerBinary -PathType Leaf)) {
        throw "安装器构建脚本未生成：$installerBinary"
    }
    $arguments = @('sign', '/sha1', $CertificateThumbprint, '/fd', 'SHA256',
        '/tr', $TimestampUrl, '/td', 'SHA256')
    if ($MachineStore) { $arguments += '/sm' }
    $arguments += $installerBinary
    & $SignToolPath @arguments
    if ($LASTEXITCODE -ne 0) { throw "签名失败：$installerBinary" }
    & $SignToolPath verify /pa /v /tw $installerBinary
    if ($LASTEXITCODE -ne 0) { throw "验签失败：$installerBinary" }
    if ((Get-AuthenticodeSignature -LiteralPath $installerBinary).Status -ne 'Valid') {
        throw "Windows Authenticode 信任验证失败：$installerBinary"
    }
}
$binaries = @(Get-ChildItem -LiteralPath $StageRoot -Recurse -File |
    Where-Object { $_.Extension -in @('.exe', '.dll') })

$manifestPath = Join-Path $StageRoot 'manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$manifest.status = 'SIGNED AND VERIFIED'
$manifest | Add-Member -NotePropertyName signer -NotePropertyValue $certificate.Subject -Force
$manifest | Add-Member -NotePropertyName signedAt -NotePropertyValue (Get-Date).ToString('o') -Force
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding utf8

$packageBase = Join-Path $repoRoot 'artifacts\release-packages'
New-Item -ItemType Directory -Force -Path $packageBase | Out-Null
$packageName = 'WirelessBatteryTray-' + (Get-Date -Format 'yyyyMMdd-HHmmss')
$zipPath = Join-Path $packageBase ($packageName + '.zip')
if (Test-Path -LiteralPath $zipPath) { throw "发布包已存在：$zipPath" }
Compress-Archive -Path (Join-Path $StageRoot '*') -DestinationPath $zipPath
$verifyFolder = Join-Path $packageBase ($packageName + '-verification')
Expand-Archive -LiteralPath $zipPath -DestinationPath $verifyFolder
$verifiedBinaries = @(Get-ChildItem -LiteralPath $verifyFolder -Recurse -File |
    Where-Object { $_.Extension -in @('.exe', '.dll') })
if ($verifiedBinaries.Count -ne $binaries.Count) { throw '打包后的二进制文件数量与签名前不一致。' }
foreach ($binary in $verifiedBinaries) {
    & $SignToolPath verify /pa /v /tw $binary.FullName
    if ($LASTEXITCODE -ne 0) { throw "打包后验签失败：$($binary.FullName)" }
}
Write-Output "RELEASE SIGN/VERIFY/PACKAGE PASS: $zipPath"
