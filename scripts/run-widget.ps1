# Launch the AppBar widget from dist\ (builds if missing).
# Usage (from repo root):
#   .\scripts\run-widget.ps1

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root

$Exe = Join-Path $Root "dist\widget.exe"
if (-not (Test-Path $Exe)) {
    Write-Host "dist\widget.exe not found — building..."
    & (Join-Path $PSScriptRoot "build-release.ps1")
}

Get-Process -Name "widget","Mawaqit Widget","mawaqit-widget" -ErrorAction SilentlyContinue |
    Stop-Process -Force
Start-Sleep -Milliseconds 400

Write-Host "Starting $Exe"
Start-Process -FilePath $Exe -WorkingDirectory (Split-Path $Exe)
Write-Host @"

Widget should appear as a strip above the taskbar (left).

Controls:
  Left-click strip     Open / close today's schedule flyout
  Right-click strip    Menu: Change mosque, Refresh, Hide, Quit
  System tray icon     Same menu (also recovery if the bar is hidden)

Mosque picker needs the Microsoft Edge WebView2 Runtime (usually already on Windows 11).
"@
