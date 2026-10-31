# Publish the WinUI 3 taskbar host (self-contained WASDK) into dist\winui.
# Usage (from repo root):
#   .\scripts\build-release.ps1
#
# Requires: .NET 8 SDK. Windows 11 to run the result.

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root

$Project = Join-Path $Root "host-winui\Mawaqit.WinUI.csproj"
$Out = Join-Path $Root "dist\winui"

Get-Process -Name "Mawaqit Widget","mawaqit-widget","widget" -ErrorAction SilentlyContinue |
    Stop-Process -Force
Start-Sleep -Milliseconds 400

if (Test-Path $Out) { Remove-Item -Recurse -Force $Out }
New-Item -ItemType Directory -Force -Path $Out | Out-Null

Write-Host "Publishing WinUI host (Release, win-x64, self-contained Windows App SDK 1.5)..."
dotnet publish $Project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:Platform=x64 `
    -p:WindowsAppSDKSelfContained=true `
    -o $Out
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$exe = Join-Path $Out "Mawaqit Widget.exe"
if (-not (Test-Path $exe)) { throw "Publish did not produce Mawaqit Widget.exe" }

$icon = Join-Path $Root "assets\icon.ico"
if (Test-Path $icon) {
    Copy-Item -Force $icon (Join-Path $Root "dist\icon.ico")
}

Write-Host ""
Write-Host "Built: $exe"
Write-Host "Quick test:  .\scripts\run-widget.ps1"
Write-Host "Install:     .\scripts\install.ps1"
