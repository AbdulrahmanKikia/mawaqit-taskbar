# Remove Mawaqit Widget install + Windows startup entry.
# Usage:
#   .\scripts\uninstall.ps1

$ErrorActionPreference = "Stop"
$ProductName = "Mawaqit Widget"
$InstallDir = Join-Path $env:LOCALAPPDATA $ProductName
$StartMenuLnk = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\$ProductName.lnk"
$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"

Get-Process -Name "Mawaqit Widget","mawaqit-widget","widget" -ErrorAction SilentlyContinue |
    Stop-Process -Force
Remove-ItemProperty -Path $runKey -Name $ProductName -ErrorAction SilentlyContinue
if (Test-Path $StartMenuLnk) { Remove-Item -Force $StartMenuLnk }
if (Test-Path $InstallDir) { Remove-Item -Recurse -Force $InstallDir }

Write-Host "Uninstalled $ProductName."
Write-Host "Config (kept): $env:APPDATA\Mawaqit\Mawaqit Widget\"
Write-Host "Delete that folder manually if you also want settings removed."
