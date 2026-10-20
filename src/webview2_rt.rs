//! Detect / install the Evergreen Microsoft Edge WebView2 Runtime.
//!
//! HRESULT 0x80070002 (“Package dependency criteria could not be resolved”)
//! often happens on newer Windows 11 even when Evergreen *is* installed:
//! the loader looks up an inbox MSIX package and gives up. Pointing
//! `WEBVIEW2_BROWSER_EXECUTABLE_FOLDER` at the on-disk runtime avoids that.

use std::fs;
use std::path::{Path, PathBuf};
use std::process::Command;
use std::time::Duration;

use crate::Error;

pub const BOOTSTRAPPER_URL: &str = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";
const CLIENT_GUID: &str = "{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";

/// True when a usable `msedgewebview2.exe` is on disk (not just a registry `pv`).
pub fn is_usable() -> bool {
    runtime_folder().is_some()
}

pub fn is_installed() -> bool {
    #[cfg(windows)]
    {
        is_usable() || webview2_pv().is_some()
    }
    #[cfg(not(windows))]
    {
        false
    }
}

/// Folder that contains `msedgewebview2.exe` (the versioned EdgeWebView dir).
pub fn runtime_folder() -> Option<PathBuf> {
    if let Ok(existing) = std::env::var("WEBVIEW2_BROWSER_EXECUTABLE_FOLDER") {
        let p = PathBuf::from(existing.trim().trim_matches('"'));
        if folder_is_runtime(&p) {
            return Some(p);
        }
    }

    let mut found: Vec<(Version, PathBuf)> = Vec::new();

    #[cfg(windows)]
    if let Some(pv) = webview2_pv() {
        for root in application_roots() {
            let p = root.join(&pv);
            if folder_is_runtime(&p) {
                found.push((parse_version(&pv).unwrap_or_default(), p));
            }
        }
    }

    for root in application_roots() {
        if let Ok(entries) = fs::read_dir(&root) {
            for entry in entries.flatten() {
                let p = entry.path();
                if folder_is_runtime(&p) {
                    let name = entry.file_name().to_string_lossy().into_owned();
                    found.push((parse_version(&name).unwrap_or_default(), p));
                }
            }
        }
    }

    found.sort_by(|a, b| a.0.cmp(&b.0));
    found.pop().map(|(_, p)| p)
}

/// Create the user-data dir and aim WebView2 at the Evergreen files on disk.
pub fn prepare() {
    if let Some(dir) = crate::config::webview_data_dir() {
        let _ = fs::create_dir_all(&dir);
    }

    if let Ok(existing) = std::env::var("WEBVIEW2_BROWSER_EXECUTABLE_FOLDER") {
        let p = PathBuf::from(existing.trim().trim_matches('"'));
        if folder_is_runtime(&p) {
            return;
        }
        // Stale/wrong env (common cause of “runtime installed but apps don't see it”).
        std::env::remove_var("WEBVIEW2_BROWSER_EXECUTABLE_FOLDER");
    }

    if let Some(folder) = runtime_folder() {
        std::env::set_var("WEBVIEW2_BROWSER_EXECUTABLE_FOLDER", folder);
    }
}

pub fn wipe_user_data() {
    if let Some(dir) = crate::config::webview_data_dir() {
        let _ = fs::remove_dir_all(&dir);
        let _ = fs::create_dir_all(&dir);
    }
}

/// Download the Evergreen bootstrapper and run a silent per-user install.
pub fn install_silent() -> Result<(), Error> {
    let dir = std::env::temp_dir().join("mawaqit-widget-webview2");
    fs::create_dir_all(&dir)?;
    let setup: PathBuf = dir.join("MicrosoftEdgeWebview2Setup.exe");
    let client = reqwest::blocking::Client::builder()
        .timeout(Duration::from_secs(180))
        .user_agent(format!("MawaqitWidget/{}", env!("CARGO_PKG_VERSION")))
        .build()?;
    let bytes = client
        .get(BOOTSTRAPPER_URL)
        .send()?
        .error_for_status()?
        .bytes()?;
    fs::write(&setup, &bytes)?;
    let status = Command::new(&setup)
        .args(["/silent", "/install"])
        .status()
        .map_err(|e| Error::msg(format!("could not run WebView2 installer: {e}")))?;
    if !status.success() && !is_usable() {
        return Err(Error::msg(format!(
            "WebView2 installer exited with {status}. Try {BOOTSTRAPPER_URL}"
        )));
    }
    Ok(())
}

fn folder_is_runtime(dir: &Path) -> bool {
    dir.join("msedgewebview2.exe").is_file()
}

fn application_roots() -> Vec<PathBuf> {
    let mut roots = Vec::new();
    if let Ok(pf86) = std::env::var("ProgramFiles(x86)") {
        roots.push(PathBuf::from(pf86).join(r"Microsoft\EdgeWebView\Application"));
    }
    if let Ok(pf) = std::env::var("ProgramFiles") {
        roots.push(PathBuf::from(pf).join(r"Microsoft\EdgeWebView\Application"));
    }
    if let Ok(local) = std::env::var("LOCALAPPDATA") {
        roots.push(PathBuf::from(local).join(r"Microsoft\EdgeWebView\Application"));
    }
    roots
}

#[derive(Clone, Copy, Default, PartialEq, Eq, PartialOrd, Ord)]
struct Version(u64, u64, u64, u64);

fn parse_version(s: &str) -> Option<Version> {
    let mut nums = s.split('.').filter_map(|p| p.parse::<u64>().ok());
    Some(Version(
        nums.next().unwrap_or(0),
        nums.next().unwrap_or(0),
        nums.next().unwrap_or(0),
        nums.next().unwrap_or(0),
    ))
}

#[cfg(windows)]
fn webview2_pv() -> Option<String> {
    use windows::core::PCWSTR;
    use windows::Win32::System::Registry::{
        RegCloseKey, RegOpenKeyExW, RegQueryValueExW, HKEY_CURRENT_USER, HKEY_LOCAL_MACHINE,
        KEY_READ, KEY_WOW64_32KEY, KEY_WOW64_64KEY, REG_VALUE_TYPE,
    };

    fn wide(s: &str) -> Vec<u16> {
        s.encode_utf16().chain(std::iter::once(0)).collect()
    }
    fn read_pv(
        root: windows::Win32::System::Registry::HKEY,
        subkey: &str,
        sam: windows::Win32::System::Registry::REG_SAM_FLAGS,
    ) -> Option<String> {
        unsafe {
            let mut hkey = Default::default();
            let path = wide(subkey);
            if RegOpenKeyExW(root, PCWSTR(path.as_ptr()), 0, sam, &mut hkey).is_err() {
                return None;
            }
            let mut kind = REG_VALUE_TYPE::default();
            let mut buf = vec![0u16; 64];
            let mut size = (buf.len() * 2) as u32;
            let name = wide("pv");
            let ok = RegQueryValueExW(
                hkey,
                PCWSTR(name.as_ptr()),
                None,
                Some(&mut kind),
                Some(buf.as_mut_ptr().cast()),
                Some(&mut size),
            );
            let _ = RegCloseKey(hkey);
            if ok.is_err() {
                return None;
            }
            let n = (size as usize / 2).saturating_sub(1).min(buf.len());
            let s = String::from_utf16_lossy(&buf[..n]);
            let s = s.trim_matches('\0').trim().to_string();
            if s.is_empty() || s == "0.0.0.0" {
                None
            } else {
                Some(s)
            }
        }
    }

    let sub = format!(r"SOFTWARE\Microsoft\EdgeUpdate\Clients\{CLIENT_GUID}");
    let wow = format!(r"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{CLIENT_GUID}");
    read_pv(HKEY_LOCAL_MACHINE, &sub, KEY_READ | KEY_WOW64_64KEY)
        .or_else(|| read_pv(HKEY_LOCAL_MACHINE, &wow, KEY_READ | KEY_WOW64_32KEY))
        .or_else(|| read_pv(HKEY_CURRENT_USER, &sub, KEY_READ))
}
