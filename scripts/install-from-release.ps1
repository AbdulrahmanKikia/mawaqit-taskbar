# Install the latest GitHub Release for non-developers.
# One-liner (recommended):
#   irm https://raw.githubusercontent.com/AbdulrahmanKikia/mawaqit-taskbar/master/scripts/install-from-release.ps1 | iex
#
# Or from a clone:
#   .\scripts\install-from-release.ps1

$ErrorActionPreference = "Stop"
$Repo = "AbdulrahmanKikia/mawaqit-taskbar"
$Api = "https://api.github.com/repos/$Repo/releases/latest"

Write-Host "Mawaqit Widget — downloading latest release..."
Write-Host ""

try {
    $release = Invoke-RestMethod -Uri $Api -Headers @{ "User-Agent" = "MawaqitWidget-Installer" }
} catch {
    throw "Could not reach GitHub Releases for $Repo. Is a release published yet? $_"
}

$setup = $release.assets | Where-Object { $_.name -match '^MawaqitWidget-Setup-.*\.exe$' } |
    Select-Object -First 1
$portable = $release.assets | Where-Object { $_.name -match '^MawaqitWidget-Portable-.*\.zip$' } |
    Select-Object -First 1

$Temp = Join-Path $env:TEMP ("mawaqit-widget-install-" + [guid]::NewGuid().ToString("n"))
New-Item -ItemType Directory -Force -Path $Temp | Out-Null

try {
    if ($setup) {
        $setupPath = Join-Path $Temp $setup.name
        Write-Host "Downloading $($setup.name)..."
        Invoke-WebRequest -Uri $setup.browser_download_url -OutFile $setupPath -UseBasicParsing
        Write-Host "Starting installer..."
        Start-Process -FilePath $setupPath -Wait
        Write-Host "Done."
        return
    }

    if (-not $portable) {
        throw "No Setup.exe or Portable zip found on the latest release. Assets: $(($release.assets | ForEach-Object name) -join ', ')"
    }

    $zipPath = Join-Path $Temp $portable.name
    Write-Host "Downloading $($portable.name)..."
    Invoke-WebRequest -Uri $portable.browser_download_url -OutFile $zipPath -UseBasicParsing

    $extract = Join-Path $Temp "extract"
    Expand-Archive -Path $zipPath -DestinationPath $extract -Force

    $installBat = Get-ChildItem -Path $extract -Filter Install.bat -Recurse | Select-Object -First 1
    if (-not $installBat) { throw "Install.bat missing from portable zip" }

    Write-Host "Running Install.bat..."
    $p = Start-Process -FilePath $installBat.FullName -WorkingDirectory $installBat.DirectoryName -Wait -PassThru
    if ($p.ExitCode -ne 0 -and $null -ne $p.ExitCode) {
        Write-Warning "Install.bat exited with code $($p.ExitCode)"
    }
    Write-Host "Done."
}
finally {
    # Keep temp on failure for debugging; clean on success path is optional
}
