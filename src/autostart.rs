//! HKCU Run-key autostart for "Mawaqit Widget".

use crate::Error;

#[cfg(windows)]
pub fn set_enabled(enabled: bool) -> Result<(), Error> {
    use windows::core::w;
    use windows::Win32::Foundation::{ERROR_FILE_NOT_FOUND, ERROR_SUCCESS};
    use windows::Win32::System::Registry::{
        RegCloseKey, RegDeleteValueW, RegOpenKeyExW, RegSetValueExW, HKEY_CURRENT_USER, KEY_WRITE,
        REG_SZ,
    };

    let exe = std::env::current_exe()
        .map_err(|e| Error::msg(format!("current_exe: {e}")))?;
    let exe = exe
        .canonicalize()
        .unwrap_or(exe)
        .to_string_lossy()
        .trim_start_matches(r"\\?\")
        .to_string();
    // Quote path for Run key (handles spaces in "Mawaqit Widget.exe").
    let command = format!("\"{exe}\"");

    unsafe {
        let mut hkey = Default::default();
        let status = RegOpenKeyExW(
            HKEY_CURRENT_USER,
            w!("Software\\Microsoft\\Windows\\CurrentVersion\\Run"),
            0,
            KEY_WRITE,
            &mut hkey,
        );
        if status != ERROR_SUCCESS {
            return Err(Error::msg(format!("RegOpenKeyExW failed: {status:?}")));
        }

        let name = w!("Mawaqit Widget");
        let result = if enabled {
            let wide: Vec<u16> = command.encode_utf16().chain(std::iter::once(0)).collect();
            let bytes = std::slice::from_raw_parts(wide.as_ptr() as *const u8, wide.len() * 2);
            let st = RegSetValueExW(hkey, name, 0, REG_SZ, Some(bytes));
            if st == ERROR_SUCCESS {
                Ok(())
            } else {
                Err(Error::msg(format!("RegSetValueExW failed: {st:?}")))
            }
        } else {
            let st = RegDeleteValueW(hkey, name);
            if st == ERROR_SUCCESS || st == ERROR_FILE_NOT_FOUND {
                Ok(())
            } else {
                Err(Error::msg(format!("RegDeleteValueW failed: {st:?}")))
            }
        };
        let _ = RegCloseKey(hkey);
        result
    }
}

#[cfg(not(windows))]
pub fn set_enabled(_enabled: bool) -> Result<(), Error> {
    Ok(())
}

#[cfg(windows)]
pub fn is_enabled() -> bool {
    use windows::core::w;
    use windows::Win32::Foundation::ERROR_SUCCESS;
    use windows::Win32::System::Registry::{
        RegCloseKey, RegOpenKeyExW, RegQueryValueExW, HKEY_CURRENT_USER, KEY_READ, REG_SZ,
    };

    unsafe {
        let mut hkey = Default::default();
        if RegOpenKeyExW(
            HKEY_CURRENT_USER,
            w!("Software\\Microsoft\\Windows\\CurrentVersion\\Run"),
            0,
            KEY_READ,
            &mut hkey,
        ) != ERROR_SUCCESS
        {
            return false;
        }
        let mut typ = REG_SZ;
        let mut size = 0u32;
        let q = RegQueryValueExW(
            hkey,
            w!("Mawaqit Widget"),
            None,
            Some(&mut typ),
            None,
            Some(&mut size),
        );
        let _ = RegCloseKey(hkey);
        q == ERROR_SUCCESS
    }
}

#[cfg(not(windows))]
pub fn is_enabled() -> bool {
    false
}
