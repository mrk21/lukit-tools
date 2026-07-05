<#
.SYNOPSIS
  Lukit を自分の PC にインストールする簡易スクリプト（管理者権限不要）。

.DESCRIPTION
  self-contained 単一 exe をビルドし、常駐用の %LOCALAPPDATA%\Programs\Lukit へ配置する。
  常駐中のインスタンスがあれば先に終了してから上書きする。
  他人へ配る「setup.exe」形式は installer\Lukit.iss（Inno Setup）を参照。

.PARAMETER Startup
  ログオン時に自動起動するよう HKCU の Run キーへ登録する。

.PARAMETER NoLaunch
  インストール後にアプリを起動しない。

.PARAMETER SkipBuild
  ビルドせず、直近の publish 成果物（artifacts\publish\Lukit.exe）をそのまま配置する。

.EXAMPLE
  pwsh scripts/install.ps1
  pwsh scripts/install.ps1 -Startup
  pwsh scripts/install.ps1 -SkipBuild -NoLaunch
#>
[CmdletBinding()]
param(
    [switch]$Startup,
    [switch]$NoLaunch,
    [switch]$SkipBuild,
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$RepoRoot   = Split-Path -Parent $PSScriptRoot
$Project    = Join-Path $RepoRoot 'src\Lukit\Lukit.csproj'
$PublishDir = Join-Path $RepoRoot 'artifacts\publish'
$InstallDir = Join-Path $env:LOCALAPPDATA 'Programs\Lukit'
$ExePath    = Join-Path $InstallDir 'Lukit.exe'
$RunKey     = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

function Write-Step($msg) { Write-Host "==> $msg" -ForegroundColor Cyan }

if (-not $SkipBuild) {
    Write-Step "ビルド中 (self-contained single-file / win-x64)…"
    dotnet publish $Project -c $Configuration -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -o $PublishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish が失敗しました (exit $LASTEXITCODE)。" }
}

$Source = Join-Path $PublishDir 'Lukit.exe'
if (-not (Test-Path $Source)) {
    throw "publish 成果物が見つかりません: $Source（-SkipBuild を外して実行してください）。"
}

# 常駐中のインスタンスは exe を握っているので、先に終了しないと上書きできない。
$running = Get-Process -Name Lukit -ErrorAction SilentlyContinue
if ($running) {
    Write-Step "常駐中の Lukit を終了…"
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}

Write-Step "配置: $ExePath"
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null

# プロセス終了後もファイルハンドルの解放が一瞬遅れることがあるためリトライする。
$copied = $false
for ($i = 0; $i -lt 10 -and -not $copied; $i++) {
    try {
        Copy-Item $Source $ExePath -Force
        $copied = $true
    } catch {
        if ($i -eq 9) { throw }
        Start-Sleep -Milliseconds 300
    }
}

if ($Startup) {
    Write-Step "スタートアップ登録 (HKCU\...\Run)"
    Set-ItemProperty -Path $RunKey -Name 'Lukit' -Value "`"$ExePath`""
}

Write-Host ""
Write-Host "インストール完了: $ExePath" -ForegroundColor Green
if ($Startup) {
    Write-Host "ログオン時に自動起動します（解除は scripts\uninstall.ps1）。"
}

if (-not $NoLaunch) {
    Write-Step "起動…"
    Start-Process $ExePath
}
