//! Windows light/dark + taskbar-like colors.

use eframe::egui::Color32;

#[derive(Debug, Clone, Copy, PartialEq)]
pub struct ShellTheme {
    pub dark: bool,
    /// Solid-ish fill used under acrylic / as opaque fallback.
    pub fill: Color32,
    pub fill_hover: Color32,
    pub stroke: Color32,
    pub text: Color32,
    pub text_muted: Color32,
    pub accent: Color32,
    pub clear_rgba: [f32; 4],
}

impl Default for ShellTheme {
    fn default() -> Self {
        Self::dark()
    }
}

impl ShellTheme {
    pub fn dark() -> Self {
        Self {
            dark: true,
            // Opaque fills — transparent + acrylic was causing the strip to vanish.
            fill: Color32::from_rgb(32, 32, 32),
            fill_hover: Color32::from_rgb(48, 48, 48),
            stroke: Color32::from_rgb(64, 64, 64),
            text: Color32::from_rgb(242, 242, 242),
            text_muted: Color32::from_rgb(170, 170, 170),
            accent: Color32::from_rgb(96, 205, 255),
            clear_rgba: [32.0 / 255.0, 32.0 / 255.0, 32.0 / 255.0, 1.0],
        }
    }

    pub fn light() -> Self {
        Self {
            dark: false,
            fill: Color32::from_rgb(243, 243, 243),
            fill_hover: Color32::from_rgb(230, 230, 230),
            stroke: Color32::from_rgb(200, 200, 200),
            text: Color32::from_rgb(28, 28, 28),
            text_muted: Color32::from_rgb(96, 96, 96),
            accent: Color32::from_rgb(0, 103, 192),
            clear_rgba: [243.0 / 255.0, 243.0 / 255.0, 243.0 / 255.0, 1.0],
        }
    }

    pub fn current() -> Self {
        #[cfg(windows)]
        {
            return if system_uses_light_theme() {
                let mut t = Self::light();
                if let Some(c) = accent_color() {
                    t.accent = c;
                }
                t
            } else {
                let mut t = Self::dark();
                if let Some(c) = accent_color() {
                    t.accent = c;
                }
                t
            };
        }
        #[cfg(not(windows))]
        {
            Self::dark()
        }
    }
}

#[cfg(windows)]
fn system_uses_light_theme() -> bool {
    use windows::core::w;
    use windows::Win32::Foundation::ERROR_SUCCESS;
    use windows::Win32::System::Registry::{
        RegOpenKeyExW, RegQueryValueExW, HKEY_CURRENT_USER, KEY_READ, REG_VALUE_TYPE,
    };

    // Taskbar follows system theme more closely than apps theme.
    unsafe {
        let mut hkey = Default::default();
        let path = w!("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");
        if RegOpenKeyExW(HKEY_CURRENT_USER, path, 0, KEY_READ, &mut hkey).is_err() {
            return false;
        }
        let mut kind = REG_VALUE_TYPE::default();
        let mut data: u32 = 0;
        let mut size = std::mem::size_of::<u32>() as u32;
        let name = w!("SystemUsesLightTheme");
        let ok = RegQueryValueExW(
            hkey,
            name,
            None,
            Some(&mut kind),
            Some((&mut data as *mut u32).cast()),
            Some(&mut size),
        );
        let _ = windows::Win32::System::Registry::RegCloseKey(hkey);
        ok == ERROR_SUCCESS && data == 1
    }
}

#[cfg(windows)]
fn accent_color() -> Option<Color32> {
    use windows::core::w;
    use windows::Win32::Foundation::ERROR_SUCCESS;
    use windows::Win32::System::Registry::{
        RegOpenKeyExW, RegQueryValueExW, HKEY_CURRENT_USER, KEY_READ, REG_VALUE_TYPE,
    };

    unsafe {
        let mut hkey = Default::default();
        let path = w!("Software\\Microsoft\\Windows\\DWM");
        if RegOpenKeyExW(HKEY_CURRENT_USER, path, 0, KEY_READ, &mut hkey).is_err() {
            return None;
        }
        let mut kind = REG_VALUE_TYPE::default();
        let mut data: u32 = 0;
        let mut size = std::mem::size_of::<u32>() as u32;
        let name = w!("AccentColor");
        let ok = RegQueryValueExW(
            hkey,
            name,
            None,
            Some(&mut kind),
            Some((&mut data as *mut u32).cast()),
            Some(&mut size),
        );
        let _ = windows::Win32::System::Registry::RegCloseKey(hkey);
        if ok != ERROR_SUCCESS {
            return None;
        }
        // COLORREF: 0x00BBGGRR
        let r = (data & 0xFF) as u8;
        let g = ((data >> 8) & 0xFF) as u8;
        let b = ((data >> 16) & 0xFF) as u8;
        Some(Color32::from_rgb(r, g, b))
    }
}
