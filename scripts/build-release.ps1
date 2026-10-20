# Build the AppBar widget (primary) and copy artifacts into dist/.
# Usage (from repo root):
#   .\scripts\build-release.ps1

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root

Write-Host "Building release binaries (widget + mawaqit-widget)..."
cargo build --release --bin widget --bin mawaqit-widget
if ($LASTEXITCODE -ne 0) { throw "cargo build failed" }

function Find-ReleaseExe([string]$Name) {
    $candidates = @(
        (Join-Path $Root "target\release\$Name"),
        (Join-Path $env:CARGO_TARGET_DIR "release\$Name")
    )
    foreach ($c in $candidates) {
        if ($c -and (Test-Path $c)) { return (Resolve-Path $c).Path }
    }
    $searchRoots = @($env:TEMP, "$env:LOCALAPPDATA\Temp")
    foreach ($root in $searchRoots) {
        if (-not (Test-Path $root)) { continue }
        $hit = Get-ChildItem -Path $root -Recurse -Filter $Name -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match "[\\/]release[\\/]$([regex]::Escape($Name))$" } |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 1 -ExpandProperty FullName
        if ($hit) { return $hit }
    }
    return $null
}

$widget = Find-ReleaseExe "widget.exe"
if (-not $widget) { throw "Could not find built widget.exe" }

$Dist = Join-Path $Root "dist"
New-Item -ItemType Directory -Force -Path $Dist | Out-Null
Copy-Item -Force $widget (Join-Path $Dist "widget.exe")

$egui = Find-ReleaseExe "mawaqit-widget.exe"
if ($egui) {
    Copy-Item -Force $egui (Join-Path $Dist "mawaqit-widget.exe")
}

$icon = Join-Path $Root "assets\icon.ico"
if (Test-Path $icon) {
    Copy-Item -Force $icon (Join-Path $Dist "icon.ico")
}

Write-Host ""
Write-Host "Built:"
Write-Host "  dist\widget.exe          <- primary AppBar widget (use this)"
if ($egui) {
    Write-Host "  dist\mawaqit-widget.exe  <- legacy egui UI"
}
Write-Host ""
Write-Host "Quick test:  .\scripts\run-widget.ps1"
Write-Host "Install:     .\scripts\install.ps1"
