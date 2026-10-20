use directories::ProjectDirs;
use serde::{Deserialize, Serialize};
use std::fs;
use std::path::PathBuf;

use crate::mawaqit::{IqamaTimes, PrayerTimes};
use crate::Error;

pub const DEFAULT_API_BASE: &str = "http://127.0.0.1:8000";
pub const APP_DISPLAY_NAME: &str = "Mawaqit Widget";

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct AppConfig {
    pub api_base_url: String,
    #[serde(default)]
    pub mosque: Option<MosqueSelection>,
    /// Register/unregister HKCU Run key on launch.
    #[serde(default = "default_true")]
    pub start_with_windows: bool,
    #[serde(default)]
    pub notifications: NotificationPrefs,
    /// What the taskbar countdown counts down to. Default: adhan.
    #[serde(default)]
    pub countdown: CountdownTarget,
}

/// What the bar countdown aims at.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum CountdownTarget {
    Adhan,
    Iqama,
}

impl Default for CountdownTarget {
    fn default() -> Self {
        Self::Adhan
    }
}

impl CountdownTarget {
    pub fn is_iqama(self) -> bool {
        matches!(self, Self::Iqama)
    }
}

fn default_true() -> bool {
    true
}

fn default_before_mins() -> u32 {
    10
}

/// User-controlled prayer alerts (tray balloons).
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct NotificationPrefs {
    #[serde(default = "default_true")]
    pub enabled: bool,
    #[serde(default = "default_true")]
    pub at_adhan: bool,
    #[serde(default)]
    pub at_iqama: bool,
    /// Minutes before adhan. `0` turns the reminder off.
    #[serde(default = "default_before_mins")]
    pub minutes_before: u32,
    #[serde(default = "default_true")]
    pub fajr: bool,
    #[serde(default = "default_true")]
    pub dhuhr: bool,
    #[serde(default = "default_true")]
    pub asr: bool,
    #[serde(default = "default_true")]
    pub maghrib: bool,
    #[serde(default = "default_true")]
    pub isha: bool,
    #[serde(default)]
    pub shuruq: bool,
    /// True after the first-run prefs screen (or a later save).
    #[serde(default)]
    pub setup_completed: bool,
}

impl Default for NotificationPrefs {
    fn default() -> Self {
        Self {
            enabled: true,
            at_adhan: true,
            at_iqama: false,
            minutes_before: 10,
            fajr: true,
            dhuhr: true,
            asr: true,
            maghrib: true,
            isha: true,
            shuruq: false,
            setup_completed: false,
        }
    }
}

impl NotificationPrefs {
    pub fn prayer_enabled(&self, name: &str) -> bool {
        match name {
            "Fajr" => self.fajr,
            "Shuruq" => self.shuruq,
            "Dhuhr" => self.dhuhr,
            "Asr" => self.asr,
            "Maghrib" => self.maghrib,
            "Isha" => self.isha,
            _ => false,
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct MosqueSelection {
    pub masjid_id: String,
    pub display_name: String,
}

impl Default for AppConfig {
    fn default() -> Self {
        Self {
            api_base_url: DEFAULT_API_BASE.to_string(),
            mosque: None,
            start_with_windows: true,
            notifications: NotificationPrefs::default(),
            countdown: CountdownTarget::default(),
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct CacheData {
    pub fetched_at: String,
    pub day: String,
    pub prayer_times: PrayerTimes,
    pub iqama: IqamaTimes,
}

fn project_dirs() -> Result<ProjectDirs, Error> {
    ProjectDirs::from("net", "Mawaqit", "Mawaqit Widget")
        .ok_or_else(|| Error::Config("could not resolve AppData directory".into()))
}

/// Older installs used mawaqit-taskbar; keep reading that as a fallback once.
fn legacy_project_dirs() -> Option<ProjectDirs> {
    ProjectDirs::from("net", "mawaqit", "mawaqit-taskbar")
}

pub fn config_path() -> Result<PathBuf, Error> {
    Ok(project_dirs()?.config_dir().join("config.toml"))
}

pub fn cache_path() -> Result<PathBuf, Error> {
    Ok(project_dirs()?.cache_dir().join("cache.json"))
}

/// WebView2 user-data folder (writable AppData, not next to the exe).
pub fn webview_data_dir() -> Option<PathBuf> {
    project_dirs()
        .ok()
        .map(|p| p.data_local_dir().join("webview"))
}

pub fn load_config() -> Result<AppConfig, Error> {
    let path = config_path()?;
    if path.exists() {
        let text = fs::read_to_string(&path)?;
        return Ok(toml::from_str(&text)?);
    }
    // Migrate from previous folder name if present.
    if let Some(legacy) = legacy_project_dirs() {
        let old = legacy.config_dir().join("config.toml");
        if old.exists() {
            let text = fs::read_to_string(&old)?;
            let cfg: AppConfig = toml::from_str(&text)?;
            let _ = save_config(&cfg);
            return Ok(cfg);
        }
    }
    Ok(AppConfig::default())
}

pub fn save_config(cfg: &AppConfig) -> Result<(), Error> {
    let path = config_path()?;
    if let Some(parent) = path.parent() {
        fs::create_dir_all(parent)?;
    }
    let text = toml::to_string_pretty(cfg)?;
    fs::write(path, text)?;
    Ok(())
}

pub fn load_cache() -> Result<Option<CacheData>, Error> {
    let path = cache_path()?;
    if !path.exists() {
        if let Some(legacy) = legacy_project_dirs() {
            let old = legacy.cache_dir().join("cache.json");
            if old.exists() {
                let text = fs::read_to_string(&old)?;
                return Ok(Some(serde_json::from_str(&text)?));
            }
        }
        return Ok(None);
    }
    let text = fs::read_to_string(&path)?;
    let cache: CacheData = serde_json::from_str(&text)?;
    Ok(Some(cache))
}

pub fn save_cache(cache: &CacheData) -> Result<(), Error> {
    let path = cache_path()?;
    if let Some(parent) = path.parent() {
        fs::create_dir_all(parent)?;
    }
    let text = serde_json::to_string_pretty(cache)?;
    fs::write(path, text)?;
    Ok(())
}
