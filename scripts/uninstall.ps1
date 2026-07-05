<#
.SYNOPSIS
  install.ps1 で入れた Lukit を削除する（管理者権限不要）。

.DESCRIPTION
  常駐プロセスの終了 → スタートアップ登録の解除 → %LOCALAPPDATA%\Programs\Lukit の削除、を行う。
  ユーザー設定（%APPDATA%\Lukit\settings.json）は既定で残す。消したいときは -PurgeSettings。

.PARAMETER PurgeSettings
  設定フォルダ（%APPDATA%\Lukit）も削除する。

.EXAMPLE
  pwsh scripts/uninstall.ps1
  pwsh scripts/uninstall.ps1 -PurgeSettings
#>
[CmdletBinding()]
param(
    [switch]$PurgeSettings
)

$ErrorActionPreference = 'Stop'

$InstallDir  = Join-Path $env:LOCALAPPDATA 'Programs\Lukit'
$RunKey      = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$SettingsDir = Join-Path $env:APPDATA 'Lukit'

function Write-Step($msg) { Write-Host "==> $msg" -ForegroundColor Cyan }

$running = Get-Process -Name Lukit -ErrorAction SilentlyContinue
if ($running) {
    Write-Step "常駐中の Lukit を終了…"
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}

if (Get-ItemProperty -Path $RunKey -Name 'Lukit' -ErrorAction SilentlyContinue) {
    Write-Step "スタートアップ登録を解除"
    Remove-ItemProperty -Path $RunKey -Name 'Lukit'
}

if (Test-Path $InstallDir) {
    Write-Step "削除: $InstallDir"
    Remove-Item -Recurse -Force $InstallDir
}

if (Test-Path $SettingsDir) {
    if ($PurgeSettings) {
        Write-Step "設定を削除: $SettingsDir"
        Remove-Item -Recurse -Force $SettingsDir
    } else {
        Write-Host "設定は保持: $SettingsDir （消すなら -PurgeSettings）"
    }
}

Write-Host ""
Write-Host "アンインストール完了。" -ForegroundColor Green
