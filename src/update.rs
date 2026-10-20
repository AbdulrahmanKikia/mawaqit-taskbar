//! Fetch the latest GitHub Release and compare it to this build.

use std::fs;
use std::path::{Path, PathBuf};

use serde::Deserialize;

use crate::Error;

pub const REPO: &str = "AbdulrahmanKikia/mawaqit-taskbar";
pub const CURRENT_VERSION: &str = env!("CARGO_PKG_VERSION");

#[derive(Debug, Clone)]
pub struct LatestRelease {
    pub version: String,
    pub setup_name: String,
    pub setup_url: String,
}

#[derive(Debug, Deserialize)]
struct GhRelease {
    tag_name: String,
    assets: Vec<GhAsset>,
}

#[derive(Debug, Deserialize)]
struct GhAsset {
    name: String,
    browser_download_url: String,
}

pub fn current_version() -> &'static str {
    CURRENT_VERSION
}

pub fn fetch_latest() -> Result<LatestRelease, Error> {
    let url = format!("https://api.github.com/repos/{REPO}/releases/latest");
    let client = reqwest::blocking::Client::builder()
        .timeout(std::time::Duration::from_secs(30))
        .user_agent(format!("MawaqitWidget/{CURRENT_VERSION}"))
        .build()?;
    let gh: GhRelease = client.get(&url).send()?.error_for_status()?.json()?;
    let version = gh.tag_name.trim().trim_start_matches('v').to_string();
    let asset = gh
        .assets
        .into_iter()
        .find(|a| {
            a.name.starts_with("MawaqitWidget-Setup-") && a.name.ends_with(".exe")
        })
        .ok_or_else(|| Error::msg("Latest release has no Setup.exe asset"))?;
    Ok(LatestRelease {
        version,
        setup_name: asset.name,
        setup_url: asset.browser_download_url,
    })
}

pub fn is_newer(remote: &str, local: &str) -> bool {
    version_tuple(remote) > version_tuple(local)
}

fn version_tuple(v: &str) -> (u64, u64, u64) {
    let v = v.trim().trim_start_matches('v');
    let mut it = v.split('.');
    let mut next = || {
        it.next()
            .and_then(|s| {
                s.chars()
                    .take_while(|c| c.is_ascii_digit())
                    .collect::<String>()
                    .parse()
                    .ok()
            })
            .unwrap_or(0)
    };
    (next(), next(), next())
}

pub fn download_setup(url: &str, dest: &Path) -> Result<(), Error> {
    let client = reqwest::blocking::Client::builder()
        .timeout(std::time::Duration::from_secs(180))
        .user_agent(format!("MawaqitWidget/{CURRENT_VERSION}"))
        .build()?;
    let bytes = client.get(url).send()?.error_for_status()?.bytes()?;
    if let Some(parent) = dest.parent() {
        fs::create_dir_all(parent)?;
    }
    fs::write(dest, &bytes)?;
    Ok(())
}

pub fn staging_setup_path(filename: &str) -> PathBuf {
    std::env::temp_dir().join("mawaqit-widget-update").join(filename)
}

/// Write a detached helper that waits for this process to exit, runs Setup silently, then relaunches.
pub fn write_apply_script(setup_exe: &Path) -> Result<PathBuf, Error> {
    let dir = std::env::temp_dir().join("mawaqit-widget-update");
    fs::create_dir_all(&dir)?;
    let script = dir.join("apply-update.cmd");
    let setup = setup_exe.display();
    let installed = r#"%LOCALAPPDATA%\Mawaqit Widget\Mawaqit Widget.exe"#;
    let body = format!(
        "@echo off\r\n\
         setlocal\r\n\
         timeout /t 2 /nobreak >nul\r\n\
         \"{setup}\" /SILENT /NORESTART /CLOSEAPPLICATIONS\r\n\
         if exist \"{installed}\" start \"\" \"{installed}\"\r\n"
    );
    fs::write(&script, body)?;
    Ok(script)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn compares_versions() {
        assert!(is_newer("0.1.5", "0.1.4"));
        assert!(is_newer("v0.2.0", "0.1.9"));
        assert!(!is_newer("0.1.4", "0.1.4"));
        assert!(!is_newer("0.1.3", "0.1.4"));
    }
}
