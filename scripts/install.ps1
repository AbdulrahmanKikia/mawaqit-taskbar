# Build and install the AppBar "Mawaqit Widget" for daily use + Windows startup.
# Usage (from repo root, PowerShell):
#   .\scripts\install.ps1
#
# Requirements: Rust (rustup), Windows 10/11, WebView2 Runtime.

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root

$ProductName = "Mawaqit Widget"
$InstallDir = Join-Path $env:LOCALAPPDATA $ProductName
$InstallExe = Join-Path $InstallDir "$ProductName.exe"
$StartMenuDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
$StartMenuLnk = Join-Path $StartMenuDir "$ProductName.lnk"

Write-Host "Building release (widget)..."
cargo build --release --bin widget
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

$built = Find-ReleaseExe "widget.exe"
if (-not $built) { throw "Could not find built widget.exe" }

Write-Host "Installing to $InstallDir"
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null

Get-Process -Name "Mawaqit Widget","mawaqit-widget","widget" -ErrorAction SilentlyContinue |
    Stop-Process -Force
Start-Sleep -Milliseconds 400

Copy-Item -Force $built $InstallExe
$iconSrc = Join-Path $Root "assets\icon.ico"
if (Test-Path $iconSrc) {
    Copy-Item -Force $iconSrc (Join-Path $InstallDir "icon.ico")
}

$shell = New-Object -ComObject WScript.Shell
$sc = $shell.CreateShortcut($StartMenuLnk)
$sc.TargetPath = $InstallExe
$sc.WorkingDirectory = $InstallDir
$sc.Description = "$ProductName — taskbar prayer times"
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
Write-Host "Done. Right-click the strip → Change mosque… to pick your mosque."
