# Build (if needed) and create dist\MawaqitWidget-Portable-<ver>.zip
# Usage:
#   .\scripts\package-portable.ps1

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root

$Version = "0.2.0"
$csproj = Get-Content (Join-Path $Root "host-winui\Mawaqit.WinUI.csproj") -Raw
if ($csproj -match '<Version>([^<]+)</Version>') { $Version = $Matches[1] }

$Winui = Join-Path $Root "dist\winui"
$Exe = Join-Path $Winui "Mawaqit Widget.exe"
if (-not (Test-Path $Exe)) {
    Write-Host "dist\winui missing - publishing first..."
    & (Join-Path $PSScriptRoot "build-release.ps1")
}
if (-not (Test-Path $Exe)) { throw "dist\winui\Mawaqit Widget.exe missing" }

$Dist = Join-Path $Root "dist"
$Stage = Join-Path $Dist "portable-stage"
if (Test-Path $Stage) { Remove-Item -Recurse -Force $Stage }
New-Item -ItemType Directory -Force -Path $Stage | Out-Null

Copy-Item -Recurse -Force (Join-Path $Winui "*") $Stage

$portableDir = Join-Path $Root "installer\portable"
Copy-Item -Force (Join-Path $portableDir "Install.bat") $Stage
Copy-Item -Force (Join-Path $portableDir "Uninstall.bat") $Stage
Copy-Item -Force (Join-Path $portableDir "README.txt") $Stage

$icon = Join-Path $Root "assets\icon.ico"
if (Test-Path $icon) { Copy-Item -Force $icon (Join-Path $Stage "icon.ico") }

$ZipName = "MawaqitWidget-Portable-$Version.zip"
$ZipPath = Join-Path $Dist $ZipName
if (Test-Path $ZipPath) { Remove-Item -Force $ZipPath }
Compress-Archive -Path (Join-Path $Stage "*") -DestinationPath $ZipPath -Force

Write-Host ""
Write-Host "Portable package: $ZipPath"
Write-Host "Users extract the zip, then double-click Install.bat"
