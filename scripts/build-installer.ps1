<#
.SYNOPSIS
  配布物（setup.exe / portable zip）をローカルでビルドする。

.DESCRIPTION
  self-contained 単一 exe を publish し、Inno Setup（installer\Lukit.iss）をコンパイルして
  dist\Lukit-Setup-<version>-x64.exe を出力する。GitHub Actions の release ワークフローと
  同じ成果物をローカルで作るためのスクリプトで、ワークフローもこれを呼ぶ（手順の二重管理を防ぐ）。

  Inno Setup 6 が必要。未導入なら `winget install JRSoftware.InnoSetup` を実行するか、-InstallInno を付ける。

.PARAMETER Version
  setup.exe に埋め込むバージョン（例 1.2.3）。出力ファイル名にも入る。既定は 0.0.0-dev。

.PARAMETER SkipBuild
  publish を省略し、既存の artifacts\publish\Lukit.exe をそのまま使う。

.PARAMETER Portable
  setup.exe に加えて portable zip（Lukit-portable-<version>-x64.zip）も作る。

.PARAMETER InstallInno
  Inno Setup が見つからなければ winget で導入してから続行する。

.EXAMPLE
  pwsh scripts/build-installer.ps1 -Version 0.1.0
  pwsh scripts/build-installer.ps1 -Version 0.1.0 -SkipBuild -Portable
#>
[CmdletBinding()]
param(
    [string]$Version = '0.0.0-dev',
    [switch]$SkipBuild,
    [switch]$Portable,
    [switch]$InstallInno
)

$ErrorActionPreference = 'Stop'

$RepoRoot   = Split-Path -Parent $PSScriptRoot
$Project    = Join-Path $RepoRoot 'src\Lukit\Lukit.csproj'
$PublishDir = Join-Path $RepoRoot 'artifacts\publish'
$Iss        = Join-Path $RepoRoot 'installer\Lukit.iss'
$DistDir    = Join-Path $RepoRoot 'dist'

function Write-Step($msg) { Write-Host "==> $msg" -ForegroundColor Cyan }

function Find-Iscc {
    # マシン単位（choco 等）と、winget のユーザー単位インストール先の両方を見る。
    $candidates = @()
    if (${env:ProgramFiles(x86)}) { $candidates += Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe' }
    if ($env:ProgramFiles)        { $candidates += Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe' }
    if ($env:LOCALAPPDATA)        { $candidates += Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe' }
    $hit = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if ($hit) { return $hit }
    $cmd = Get-Command iscc -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    return $null
}

# --- Inno Setup (ISCC.exe) の在り処を先に確定する（無ければ publish 前に止める）---
$iscc = Find-Iscc
if (-not $iscc -and $InstallInno) {
    Write-Step "Inno Setup を winget で導入…"
    winget install --id JRSoftware.InnoSetup -e --accept-package-agreements --accept-source-agreements
    $iscc = Find-Iscc
}
if (-not $iscc) {
    throw @"
Inno Setup (ISCC.exe) が見つかりません。次のいずれかで導入してください:
  winget install JRSoftware.InnoSetup
  （または本スクリプトに -InstallInno を付けて再実行）
"@
}
Write-Step "ISCC: $iscc"

# --- self-contained 単一 exe を publish ---
if (-not $SkipBuild) {
    Write-Step "publish 中 (self-contained single-file / win-x64, v$Version)…"
    dotnet publish $Project -c Release -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:Version=$Version `
        -o $PublishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish が失敗しました (exit $LASTEXITCODE)。" }
}
if (-not (Test-Path (Join-Path $PublishDir 'Lukit.exe'))) {
    throw "artifacts\publish\Lukit.exe が見つかりません（-SkipBuild を外して実行してください）。"
}

# --- インストーラをコンパイル（.iss 内の相対パスは .iss の場所を基準に解決される）---
Write-Step "インストーラをビルド (installer\Lukit.iss)…"
& $iscc "/DMyAppVersion=$Version" $Iss
if ($LASTEXITCODE -ne 0) { throw "iscc が失敗しました (exit $LASTEXITCODE)。" }

# --- 任意: portable zip（ワークフローと同じ中身）---
if ($Portable) {
    Write-Step "portable zip を作成…"
    $stage = Join-Path $RepoRoot 'artifacts\portable'
    if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
    New-Item -ItemType Directory -Force $stage | Out-Null
    Copy-Item (Join-Path $PublishDir 'Lukit.exe') $stage
    Copy-Item (Join-Path $RepoRoot 'README.md'), (Join-Path $RepoRoot 'LICENSE') $stage
    New-Item -ItemType Directory -Force $DistDir | Out-Null
    $zip = Join-Path $DistDir "Lukit-portable-$Version-x64.zip"
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
}

Write-Host ""
Write-Host "完成 (dist\):" -ForegroundColor Green
Get-ChildItem $DistDir -Filter "*$Version*" | ForEach-Object { Write-Host "  $($_.Name)" }
