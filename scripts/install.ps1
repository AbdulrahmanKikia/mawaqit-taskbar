# Publish and install the WinUI taskbar widget for daily use + Windows startup.
# Usage (from repo root, PowerShell):
#   .\scripts\install.ps1
#
# Requirements: .NET 8 SDK, Windows 11, WebView2 Runtime.

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root

$ProductName = "Mawaqit Widget"
$InstallDir = Join-Path $env:LOCALAPPDATA $ProductName
$InstallExe = Join-Path $InstallDir "$ProductName.exe"
$StartMenuDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
$StartMenuLnk = Join-Path $StartMenuDir "$ProductName.lnk"

Write-Host "Publishing WinUI host..."
& (Join-Path $PSScriptRoot "build-release.ps1")

$builtDir = Join-Path $Root "dist\winui"
$builtExe = Join-Path $builtDir "$ProductName.exe"
if (-not (Test-Path $builtExe)) { throw "Could not find published $ProductName.exe" }

Write-Host "Installing to $InstallDir"
Get-Process -Name "Mawaqit Widget","mawaqit-widget","widget" -ErrorAction SilentlyContinue |
    Stop-Process -Force
Start-Sleep -Milliseconds 400

if (Test-Path $InstallDir) { Remove-Item -Recurse -Force $InstallDir }
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item -Recurse -Force (Join-Path $builtDir "*") $InstallDir

$shell = New-Object -ComObject WScript.Shell
$sc = $shell.CreateShortcut($StartMenuLnk)
$sc.TargetPath = $InstallExe
$sc.WorkingDirectory = $InstallDir
$sc.Description = "$ProductName - taskbar prayer times"
$sc.IconLocation = "$InstallExe,0"
$sc.Save()

$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
Set-ItemProperty -Path $runKey -Name $ProductName -Value "`"$InstallExe`"" -Force

Write-Host ""
Write-Host "Installed: $InstallExe"
Write-Host "Start Menu: $StartMenuLnk"
Write-Host "Startup: enabled (HKCU Run → $ProductName)"
Write-Host "Launching..."
Start-Process -FilePath $InstallExe -WorkingDirectory $InstallDir
Write-Host "Done. Look for the chip on the taskbar (clock-adjacent). Right-click -> Change mosque..."
