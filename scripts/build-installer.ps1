# Build release binary + portable zip + (optional) Inno Setup installer.
# Usage:
#   .\scripts\build-installer.ps1
#
# Inno Setup 6 is optional. If ISCC.exe is missing, only the portable zip is produced.

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root

Write-Host "==> Building release binary"
& (Join-Path $PSScriptRoot "build-release.ps1")

Write-Host ""
Write-Host "==> Packaging portable zip"
& (Join-Path $PSScriptRoot "package-portable.ps1")

function Find-ISCC {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )
    $cmd = Get-Command iscc -ErrorAction SilentlyContinue
    if ($cmd) { $candidates += $cmd.Source }
    foreach ($c in $candidates) {
        if ($c -and (Test-Path $c)) { return $c }
    }
    return $null
}

$iscc = Find-ISCC
if (-not $iscc) {
    Write-Host ""
    Write-Host "Inno Setup 6 not found - skipping Setup.exe."
    Write-Host "Install from https://jrsoftware.org/isinfo.php then re-run this script,"
    Write-Host "or rely on CI (.github/workflows/release.yml) to build the installer."
    Write-Host ""
    Write-Host "Portable zip is ready in dist\ for testers (Install.bat)."
    exit 0
}

$iss = Join-Path $Root "installer\mawaqit-widget.iss"
Write-Host ""
Write-Host "==> Compiling Inno Setup installer with $iscc"
& $iscc $iss
if ($LASTEXITCODE -ne 0) { throw "ISCC failed" }

Write-Host ""
Write-Host "Installer(s) in dist\:"
Get-ChildItem (Join-Path $Root "dist") -File |
    Where-Object { $_.Name -match "Setup|Portable" } |
    ForEach-Object { Write-Host ("  " + $_.FullName) }
