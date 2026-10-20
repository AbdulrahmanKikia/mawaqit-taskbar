# Build (if needed) and create dist\MawaqitWidget-Portable-<ver>.zip
# Usage:
#   .\scripts\package-portable.ps1

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root

$Version = "0.1.0"
$toml = Get-Content (Join-Path $Root "Cargo.toml") -Raw
if ($toml -match 'version\s*=\s*"([^"]+)"') { $Version = $Matches[1] }

$Dist = Join-Path $Root "dist"
$Widget = Join-Path $Dist "widget.exe"
if (-not (Test-Path $Widget)) {
    Write-Host "Building release first..."
    & (Join-Path $PSScriptRoot "build-release.ps1")
}
if (-not (Test-Path $Widget)) { throw "dist\widget.exe missing" }

$Stage = Join-Path $Dist "portable-stage"
if (Test-Path $Stage) { Remove-Item -Recurse -Force $Stage }
New-Item -ItemType Directory -Force -Path $Stage | Out-Null

Copy-Item -Force $Widget (Join-Path $Stage "widget.exe")
$icon = Join-Path $Root "assets\icon.ico"
if (Test-Path $icon) { Copy-Item -Force $icon (Join-Path $Stage "icon.ico") }

$portableDir = Join-Path $Root "installer\portable"
Copy-Item -Force (Join-Path $portableDir "Install.bat") $Stage
Copy-Item -Force (Join-Path $portableDir "Uninstall.bat") $Stage
Copy-Item -Force (Join-Path $portableDir "README.txt") $Stage

$ZipName = "MawaqitWidget-Portable-$Version.zip"
$ZipPath = Join-Path $Dist $ZipName
if (Test-Path $ZipPath) { Remove-Item -Force $ZipPath }
Compress-Archive -Path (Join-Path $Stage "*") -DestinationPath $ZipPath -Force

Write-Host ""
Write-Host "Portable package: $ZipPath"
Write-Host "Users extract the zip, then double-click Install.bat"
