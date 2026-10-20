//! Taskbar geometry + Win32 window helpers for the widget shell.

#[cfg(windows)]
use windows::Win32::Foundation::HWND;

#[derive(Debug, Clone, Copy, PartialEq)]
pub struct TaskbarGeom {
    /// Logical pixels (egui / winit space).
    pub x: f32,
    pub y: f32,
    #[allow(dead_code)]
    pub width: f32,
    pub height: f32,
    pub left_inset: f32,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum TaskbarAlign {
    Left,
    Center,
}

#[cfg(windows)]
pub fn primary_taskbar(pixels_per_point: f32) -> Option<TaskbarGeom> {
    use windows::core::w;
    use windows::Win32::Foundation::RECT;
    use windows::Win32::UI::WindowsAndMessaging::{FindWindowW, GetWindowRect};

    let ppp = pixels_per_point.max(0.5);
    unsafe {
        let hwnd = FindWindowW(w!("Shell_TrayWnd"), None).ok()?;
        let mut rect = RECT::default();
        GetWindowRect(hwnd, &mut rect).ok()?;
        let width = (rect.right - rect.left) as f32 / ppp;
        let height = (rect.bottom - rect.top) as f32 / ppp;
        if width < 80.0 || height < 20.0 {
            return None;
        }

        let left_inset = match taskbar_alignment() {
            TaskbarAlign::Left => 200.0,
            TaskbarAlign::Center => 64.0,
        };

        Some(TaskbarGeom {
            x: rect.left as f32 / ppp,
            y: rect.top as f32 / ppp,
            width,
            height,
            left_inset,
        })
    }
}

#[cfg(not(windows))]
pub fn primary_taskbar(_pixels_per_point: f32) -> Option<TaskbarGeom> {
    None
}

#[cfg(windows)]
pub fn taskbar_alignment() -> TaskbarAlign {
    use windows::core::w;
    use windows::Win32::Foundation::ERROR_SUCCESS;
    use windows::Win32::System::Registry::{
        RegOpenKeyExW, RegQueryValueExW, HKEY_CURRENT_USER, KEY_READ, REG_VALUE_TYPE,
    };

    unsafe {
        let mut hkey = Default::default();
        let path = w!("Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced");
        if RegOpenKeyExW(HKEY_CURRENT_USER, path, 0, KEY_READ, &mut hkey).is_err() {
            return TaskbarAlign::Center;
        }
        let mut kind = REG_VALUE_TYPE::default();
        let mut data: u32 = 1;
        let mut size = std::mem::size_of::<u32>() as u32;
        let name = w!("TaskbarAl");
        let ok = RegQueryValueExW(
            hkey,
            name,
            None,
            Some(&mut kind),
            Some((&mut data as *mut u32).cast()),
            Some(&mut size),
        );
        let _ = windows::Win32::System::Registry::RegCloseKey(hkey);
        if ok == ERROR_SUCCESS && data == 0 {
            TaskbarAlign::Left
        } else {
            TaskbarAlign::Center
        }
    }
}

#[cfg(not(windows))]
pub fn taskbar_alignment() -> TaskbarAlign {
    TaskbarAlign::Center
}

#[cfg(windows)]
pub fn find_widget_hwnd() -> Option<HWND> {
    use windows::core::w;
    use windows::Win32::UI::WindowsAndMessaging::FindWindowW;
    unsafe { FindWindowW(None, w!("Mawaqit Widget")).ok() }
}

#[cfg(not(windows))]
pub fn find_widget_hwnd() -> Option<()> {
    None
}

#[cfg(windows)]
pub fn show_widget(show: bool) {
    use windows::Win32::UI::WindowsAndMessaging::{ShowWindow, SW_HIDE, SW_SHOWNOACTIVATE};
    if let Some(hwnd) = find_widget_hwnd() {
        unsafe {
            let _ = ShowWindow(hwnd, if show { SW_SHOWNOACTIVATE } else { SW_HIDE });
        }
    }
}

#[cfg(not(windows))]
pub fn show_widget(_show: bool) {}

#[cfg(windows)]
pub fn activate_widget() {
    use windows::Win32::UI::WindowsAndMessaging::{
        SetForegroundWindow, ShowWindow, SW_SHOW,
    };
    if let Some(hwnd) = find_widget_hwnd() {
        unsafe {
            let _ = ShowWindow(hwnd, SW_SHOW);
            let _ = SetForegroundWindow(hwnd);
        }
    }
}

#[cfg(not(windows))]
pub fn activate_widget() {}

/// Keep the widget above the taskbar without fighting Explorer every frame.
#[cfg(windows)]
pub fn place_widget(x: i32, y: i32, w: i32, h: i32) {
    use windows::Win32::UI::WindowsAndMessaging::{
        SetWindowPos, HWND_TOPMOST, SWP_NOACTIVATE,
    };
    if let Some(hwnd) = find_widget_hwnd() {
        unsafe {
            // Do not force-show here — tray hide/show owns visibility.
            let _ = SetWindowPos(hwnd, HWND_TOPMOST, x, y, w, h, SWP_NOACTIVATE);
        }
    }
}

#[cfg(not(windows))]
pub fn place_widget(_x: i32, _y: i32, _w: i32, _h: i32) {}
