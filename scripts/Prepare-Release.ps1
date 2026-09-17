param(
    [string]$DotnetPath = 'C:\Program Files\dotnet\dotnet.exe',
    [string]$OutputRoot
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not (Test-Path -LiteralPath $DotnetPath -PathType Leaf)) { throw "找不到 dotnet：$DotnetPath" }
if (-not $OutputRoot) {
    $OutputRoot = Join-Path $repoRoot ('artifacts\release-staging\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
$stagingBase = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts\release-staging'))
if (-not $OutputRoot.StartsWith($stagingBase + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "输出目录必须位于 $stagingBase 内"
}
if (Test-Path -LiteralPath $OutputRoot) { throw "输出目录已存在：$OutputRoot" }
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

$originalDotnetHome = $env:DOTNET_CLI_HOME
$originalAppData = $env:APPDATA
$env:DOTNET_CLI_HOME = $repoRoot
$env:APPDATA = $repoRoot
Push-Location $repoRoot
try {
    & $DotnetPath restore WirelessBatteryTray.slnx --configfile (Join-Path $repoRoot 'NuGet.Config')
    if ($LASTEXITCODE -ne 0) { throw 'Release 依赖还原失败' }
    & $DotnetPath build WirelessBatteryTray.slnx -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Release 构建失败' }
    $tests = @(
        'tools\S99BatteryProbe\bin\Release\net10.0\S99BatteryProbe.dll',
        'tools\MXVerticalBatteryProbe\bin\Release\net10.0\MXVerticalBatteryProbe.dll',
        'src\WirelessBatteryTray.App\bin\Release\net10.0-windows\WirelessBatteryTray.dll'
    )
    foreach ($test in $tests) {
        & $DotnetPath $test --self-test
        if ($LASTEXITCODE -ne 0) { throw "自测失败：$test" }
    }

    $projects = @(
        @{ Project = 'src\WirelessBatteryTray.App\WirelessBatteryTray.App.csproj'; Folder = 'app' },
        @{ Project = 'tools\S99BatteryProbe\S99BatteryProbe.csproj'; Folder = 'S99BatteryProbe' }
    )
    foreach ($project in $projects) {
        $destination = Join-Path $OutputRoot $project.Folder
        & $DotnetPath publish $project.Project -c Release --no-restore --no-self-contained -o $destination
        if ($LASTEXITCODE -ne 0) { throw "发布输出失败：$($project.Project)" }
    }

    foreach ($required in @('app\WirelessBatteryTray.exe', 'app\WirelessBatteryTray.dll',
            'S99BatteryProbe\S99BatteryProbe.exe', 'S99BatteryProbe\S99BatteryProbe.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $OutputRoot $required) -PathType Leaf)) {
            throw "缺少发布产物：$required"
        }
    }
    $manifest = [ordered]@{
        createdAt = (Get-Date).ToString('o')
        status = 'UNSIGNED - DO NOT DISTRIBUTE'
        runtime = '.NET 10 Windows Desktop Runtime required'
        tested = @('S99BatteryProbe --self-test', 'MXVerticalBatteryProbe --self-test', 'WirelessBatteryTray --self-test')
    }
    $manifest | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $OutputRoot 'manifest.json') -Encoding utf8
    Write-Output "PREPARE PASS / UNSIGNED: $OutputRoot"
}
finally {
    Pop-Location
    $env:DOTNET_CLI_HOME = $originalDotnetHome
    $env:APPDATA = $originalAppData
}
