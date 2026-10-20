//! Ensure only one Mawaqit Widget process runs.

#[cfg(windows)]
pub fn ensure_single_instance() -> bool {
    use windows::core::w;
    use windows::Win32::Foundation::{CloseHandle, GetLastError, ERROR_ALREADY_EXISTS};
    use windows::Win32::System::Threading::CreateMutexW;

    unsafe {
        let handle = match CreateMutexW(None, true, w!("Local\\MawaqitWidgetSingleInstance")) {
            Ok(h) => h,
            Err(_) => return true, // fail open
        };

        if GetLastError() == ERROR_ALREADY_EXISTS {
            let _ = CloseHandle(handle);
            crate::taskbar::show_widget(true);
            crate::taskbar::activate_widget();
            return false;
        }

        // Intentionally do not CloseHandle: keep the mutex held until process exit.
        let _ = handle;
        true
    }
}

#[cfg(not(windows))]
pub fn ensure_single_instance() -> bool {
    true
}
