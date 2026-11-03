# Rebuild the WinUI taskbar widget into dist\winui and launch it.
# Usage (from repo root):
#   .\scripts\run-widget.ps1

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root

$Exe = Join-Path $Root "dist\winui\Mawaqit Widget.exe"

# Always rebuild: launching an old dist\ build hides every change made since.
# (build-release.ps1 also stops a running widget first.)
Write-Host "Building the latest code..."
& (Join-Path $PSScriptRoot "build-release.ps1")

Write-Host "Starting $Exe"
Start-Process -FilePath $Exe -WorkingDirectory (Split-Path $Exe)
Write-Host @"

Widget should appear as a chip on the taskbar (next to the clock / Widgets).

Controls:
  Left-click chip      Open / close today's schedule flyout
  Right-click chip     Menu: Preferences, Hide, Quit
  System tray icon     Show / Hide / Re-position / Manual position / Quit

Mosque picker needs the Microsoft Edge WebView2 Runtime (usually already on Windows 11).
Windows App SDK 1.5 is bundled in this build (self-contained).
"@
