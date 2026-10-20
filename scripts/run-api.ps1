# Run mrsofiane/mawaqit-api locally without Docker (Python venv).
# Usage (from repo root):
#   .\scripts\run-api.ps1

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Src = Join-Path $Root ".mawaqit-api-src"

if (-not (Test-Path $Src)) {
    Write-Host "Cloning mrsofiane/mawaqit-api..."
    git clone --depth 1 https://github.com/mrsofiane/mawaqit-api.git $Src
}

Push-Location $Src
try {
    if (-not (Test-Path ".venv")) {
        python -m venv .venv
    }
    & .\.venv\Scripts\pip.exe install -r requirements.txt
    if (-not (Test-Path ".env")) {
        $localEnv = Join-Path $Root ".env.example"
        if (Test-Path ".env.example") {
            Copy-Item ".env.example" ".env"
        } elseif (Test-Path $localEnv) {
            Copy-Item $localEnv ".env"
        }
    }
    Write-Host "Starting API on http://127.0.0.1:8000 ..."
    & .\.venv\Scripts\uvicorn.exe main:app --host 127.0.0.1 --port 8000
}
finally {
    Pop-Location
}
