use std::path::PathBuf;
use std::process::Command;

use crate::Error;

/// Locate scripts/run-api.ps1 relative to the binary or the crate root.
pub fn api_script_path() -> Option<PathBuf> {
    if let Ok(exe) = std::env::current_exe() {
        if let Some(dir) = exe.parent() {
            let candidates = [
                dir.join("scripts").join("run-api.ps1"),
                dir.join("..").join("..").join("scripts").join("run-api.ps1"),
                dir.join("..").join("..").join("..").join("scripts").join("run-api.ps1"),
            ];
            for c in candidates {
                if let Ok(p) = c.canonicalize() {
                    if p.exists() {
                        return Some(p);
                    }
                }
            }
        }
    }
    let from_manifest = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .join("scripts")
        .join("run-api.ps1");
    if from_manifest.exists() {
        Some(from_manifest)
    } else {
        None
    }
}

/// Open a new PowerShell window that starts the local mrsofiane API.
pub fn start_local_api() -> Result<(), Error> {
    let script = api_script_path().ok_or_else(|| {
        Error::msg("Could not find scripts/run-api.ps1. Run it from the project folder, or use Docker: docker compose up -d")
    })?;

    Command::new("powershell")
        .args([
            "-NoExit",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            script.to_str().unwrap_or_default(),
        ])
        .spawn()
        .map_err(|e| Error::msg(format!("Failed to start API script: {e}")))?;

    Ok(())
}

pub fn open_url(url: &str) -> Result<(), Error> {
    Command::new("cmd")
        .args(["/C", "start", "", url])
        .spawn()
        .map_err(|e| Error::msg(format!("Failed to open browser: {e}")))?;
    Ok(())
}
