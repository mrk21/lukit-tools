#requires -Version 7
<#
.SYNOPSIS
    Capture Lukit's visual surfaces to PNGs for an AI-in-the-loop review.

.DESCRIPTION
    Produces both halves of the "見た目の確認" loop into one timestamped folder:

      A. Product output — live, tone-mapped screen captures (--shot-fullscreen, --frame-stats).
         Depends on what is on screen and on the display's HDR state, so it is a live check,
         not a deterministic golden.

      B. App UI — the app's own WPF surfaces rendered off-screen (--shot-ui settings|overlay).
         Deterministic and independent of the desktop; grows automatically as the UI grows.

    Then it writes manifest.md listing every artifact. Hand that folder (or the individual
    PNGs) to Claude Code — it can Read PNGs directly — and iterate.

.EXAMPLE
    pwsh scripts/visual-check.ps1
    pwsh scripts/visual-check.ps1 -Only ui -Op aces
    pwsh scripts/visual-check.ps1 -OutDir artifacts/visual/run1
#>
[CmdletBinding()]
param(
    # 'all' (default), 'ui' (surfaces only, deterministic), or 'capture' (live shots only).
    [ValidateSet('all', 'ui', 'capture')]
    [string]$Only = 'all',

    # Tone-map operator for the live full-screen capture.
    [ValidateSet('clip', 'reinhard', 'aces')]
    [string]$Op = 'reinhard',

    # Output folder. Defaults to a timestamped folder under artifacts/visual/ (git-ignored).
    [string]$OutDir,

    # Skip the build and use the existing Debug binary.
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $repo 'src/Lukit/Lukit.csproj'

if (-not $OutDir) {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $OutDir = Join-Path $repo "artifacts/visual/$stamp"
}
New-Item -ItemType Directory -Force $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path

# --- Build once, then invoke the binary directly (one dotnet-run rebuild per shot is slow
#     and would relock the exe between calls). ---
if (-not $NoBuild) {
    Write-Host 'Building (Debug)…' -ForegroundColor Cyan
    dotnet build $proj -c Debug --nologo | Out-Null
}
$exe = Get-ChildItem (Join-Path $repo 'src/Lukit/bin/Debug') -Recurse -Filter 'Lukit.exe' |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
if (-not $exe) { throw 'Lukit.exe not found under src/Lukit/bin/Debug — build first (drop -NoBuild).' }

# Lukit is a WinExe, so its stdout is not attached to this console. Redirect each run's
# stdout/stderr to a log file and surface the exit code instead.
function Invoke-Lukit([string]$Label, [string[]]$CliArgs, [string]$Log) {
    $p = Start-Process -FilePath $exe -ArgumentList $CliArgs -NoNewWindow -Wait -PassThru `
        -RedirectStandardOutput $Log -RedirectStandardError "$Log.err"
    $ok = $p.ExitCode -eq 0
    $color = if ($ok) { 'Green' } else { 'Red' }
    Write-Host ("  [{0}] {1} (exit {2})" -f ($(if ($ok) { 'ok ' } else { 'FAIL' })), $Label, $p.ExitCode) -ForegroundColor $color
    return $ok
}

$artifacts = [System.Collections.Generic.List[object]]::new()

if ($Only -in 'all', 'ui') {
    Write-Host 'UI surfaces (deterministic):' -ForegroundColor Cyan
    foreach ($surface in 'settings', 'overlay') {
        $png = Join-Path $OutDir "ui-$surface.png"
        $ok = Invoke-Lukit "ui:$surface" @('--shot-ui', $surface, $png) (Join-Path $OutDir "ui-$surface.log")
        $artifacts.Add([pscustomobject]@{ kind = 'ui'; name = $surface; file = $png; ok = $ok })
    }
}

if ($Only -in 'all', 'capture') {
    Write-Host 'Live captures (depend on current screen / HDR state):' -ForegroundColor Cyan
    $png = Join-Path $OutDir 'capture-fullscreen.png'
    $ok = Invoke-Lukit "fullscreen ($Op)" @('--shot-fullscreen', $png, '--op', $Op) (Join-Path $OutDir 'capture-fullscreen.log')
    $artifacts.Add([pscustomobject]@{ kind = 'capture'; name = "fullscreen ($Op)"; file = $png; ok = $ok })

    $stats = Join-Path $OutDir 'frame-stats.txt'
    $ok = Invoke-Lukit 'frame-stats' @('--frame-stats') $stats
    $artifacts.Add([pscustomobject]@{ kind = 'capture'; name = 'frame-stats'; file = $stats; ok = $ok })
}

# --- Manifest for the reviewer (human or AI). ---
$lines = @("# Visual check — $(Split-Path -Leaf $OutDir)", '', '| kind | name | file | status |', '| --- | --- | --- | --- |')
foreach ($a in $artifacts) {
    $exists = Test-Path $a.file
    $status = if ($a.ok -and $exists) { 'ok' } else { 'FAILED' }
    $rel = [System.IO.Path]::GetRelativePath($repo, $a.file) -replace '\\', '/'
    $lines += "| $($a.kind) | $($a.name) | ``$rel`` | $status |"
}
$lines += @('', '_UI surfaces are deterministic; live captures reflect whatever was on screen._')
$manifest = Join-Path $OutDir 'manifest.md'
Set-Content -Path $manifest -Value $lines -Encoding utf8

Write-Host ''
Write-Host "Artifacts in: $OutDir" -ForegroundColor Cyan
Write-Host "Manifest:     $manifest"
