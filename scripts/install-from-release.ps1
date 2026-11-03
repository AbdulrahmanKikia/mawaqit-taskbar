# Install the latest GitHub Release for non-developers.
# One-liner:
#   irm https://raw.githubusercontent.com/AbdulrahmanKikia/mawaqit-taskbar/master/scripts/install-from-release.ps1 | iex
#
# Or from a clone:
#   .\scripts\install-from-release.ps1
#
# The script downloads Setup (or Portable), verifies SHA-256 against the release
# sidecar / GitHub digest, and only then launches. No checksum → refuse to run.

$ErrorActionPreference = "Stop"
$Repo = "AbdulrahmanKikia/mawaqit-taskbar"
$Api = "https://api.github.com/repos/$Repo/releases/latest"
$DownloadPrefix = "https://github.com/$Repo/releases/download/"

function Test-TrustedReleaseUrl([string]$Url) {
    if ([string]::IsNullOrWhiteSpace($Url)) { return $false }
    return $Url.StartsWith($DownloadPrefix, [StringComparison]::OrdinalIgnoreCase)
}

function Get-ExpectedSha256($Release, $Asset) {
    $fromDigest = $null
    if ($Asset.PSObject.Properties.Name -contains "digest" -and $Asset.digest) {
        $text = [string]$Asset.digest
        if ($text -match '(?i)^(?:sha256:)?([0-9a-f]{64})$') {
            $fromDigest = $Matches[1].ToLowerInvariant()
        }
    }

    $sumName = "$($Asset.name).sha256"
    $sumAsset = $Release.assets | Where-Object { $_.name -eq $sumName } | Select-Object -First 1
    if (-not $sumAsset) {
        if ($fromDigest) { return $fromDigest }
        return $null
    }
    if (-not (Test-TrustedReleaseUrl $sumAsset.browser_download_url)) {
        throw "Checksum URL is not a trusted GitHub Releases download for $Repo."
    }

    $sumBody = (Invoke-WebRequest -Uri $sumAsset.browser_download_url -UseBasicParsing).Content
    $first = (($sumBody -split '\r?\n|\s+', 2) | Where-Object { $_ })[0]
    if ($first -match '(?i)^(?:sha256:)?([0-9a-f]{64})$') {
        $fromFile = $Matches[1].ToLowerInvariant()
        if ($fromDigest -and $fromDigest -ne $fromFile) {
            throw "Release digest and $($sumName) disagree; refusing to install."
        }
        return $fromFile
    }
    if ($fromDigest) { return $fromDigest }
    return $null
}

function Assert-FileSha256([string]$Path, [string]$ExpectedHex) {
    $actual = (Get-FileHash -Path $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $ExpectedHex.ToLowerInvariant()) {
        Remove-Item -Force -ErrorAction SilentlyContinue $Path
        throw "Downloaded file did not match the release checksum and was deleted."
    }
}

Write-Host "Mawaqit Widget - downloading latest release..."
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
        if (-not (Test-TrustedReleaseUrl $setup.browser_download_url)) {
            throw "Setup download URL is not a trusted GitHub Releases download for $Repo."
        }
        $expected = Get-ExpectedSha256 $release $setup
        if (-not $expected) {
            throw "The release has no checksum for $($setup.name), so the installer was not run."
        }

        $setupPath = Join-Path $Temp $setup.name
        Write-Host "Downloading $($setup.name)..."
        Invoke-WebRequest -Uri $setup.browser_download_url -OutFile $setupPath -UseBasicParsing
        Write-Host "Verifying SHA-256..."
        Assert-FileSha256 $setupPath $expected
        Write-Host "Checksum OK. Starting installer..."
        Start-Process -FilePath $setupPath -Wait
        Write-Host "Done."
        return
    }

    if (-not $portable) {
        throw "No Setup.exe or Portable zip found on the latest release. Assets: $(($release.assets | ForEach-Object name) -join ', ')"
    }

    if (-not (Test-TrustedReleaseUrl $portable.browser_download_url)) {
        throw "Portable download URL is not a trusted GitHub Releases download for $Repo."
    }
    $expected = Get-ExpectedSha256 $release $portable
    if (-not $expected) {
        throw "The release has no checksum for $($portable.name), so the archive was not opened."
    }

    $zipPath = Join-Path $Temp $portable.name
    Write-Host "Downloading $($portable.name)..."
    Invoke-WebRequest -Uri $portable.browser_download_url -OutFile $zipPath -UseBasicParsing
    Write-Host "Verifying SHA-256..."
    Assert-FileSha256 $zipPath $expected

    $extract = Join-Path $Temp "extract"
    Expand-Archive -Path $zipPath -DestinationPath $extract -Force

    $installBat = Get-ChildItem -Path $extract -Filter Install.bat -Recurse | Select-Object -First 1
    if (-not $installBat) { throw "Install.bat missing from portable zip" }

    Write-Host "Checksum OK. Running Install.bat..."
    $p = Start-Process -FilePath $installBat.FullName -WorkingDirectory $installBat.DirectoryName -Wait -PassThru
    if ($p.ExitCode -ne 0 -and $null -ne $p.ExitCode) {
        Write-Warning "Install.bat exited with code $($p.ExitCode)"
    }
    Write-Host "Done."
}
finally {
    # Keep temp on failure for debugging; clean on success path is optional
}
