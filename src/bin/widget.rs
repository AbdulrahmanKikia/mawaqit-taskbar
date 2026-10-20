//! Mawaqit Widget — shell-cooperative AppBar (clean foundation).
//!
//! Registers with Explorer via SHAppBarMessage so the OS reserves edge space.
//!
//! W0: empty AppBar band — done
//! W1: live next-prayer chip — done
//! W2: flyout + tray icon — done
//! W3: real mosque config + Mawaqit times — done
//! W4: mosque search/picker — this build
//!
//! Run:  cargo run --release --bin widget
//! Quit: system tray → Quit
//! Show again after Hide: left-click or right-click the tray icon

#![windows_subsystem = "windows"]

use std::cell::RefCell;
use std::mem::size_of;
use std::path::PathBuf;
use std::sync::atomic::{AtomicBool, AtomicIsize, Ordering};
use std::sync::Mutex;
use std::thread;
use std::time::Instant;

use chrono::Datelike;
use mawaqit_widget::config::{self, AppConfig, CacheData, MosqueSelection};
use mawaqit_widget::mawaqit::{IqamaTimes, MawaqitClient, MosqueHit, PrayerTimes};
use mawaqit_widget::schedule::{
    build_day_schedule, format_countdown, format_hhmm, next_prayer_with_tomorrow, now_local,
    today_local, DaySchedule, PrayerKind, PrayerSlot,
};
use windows::core::{w, PCWSTR};
use windows::Win32::Foundation::{
    COLORREF, ERROR_ALREADY_EXISTS, GetLastError, HINSTANCE, HWND, LPARAM, LRESULT, POINT, RECT,
    WPARAM,
};
use windows::Win32::Graphics::Gdi::{
    BeginPaint, CreateSolidBrush, DeleteObject, DrawTextW, EndPaint, FillRect, GetDC,
    GetStockObject, GetTextExtentPoint32W, InvalidateRect, ReleaseDC, SelectObject, SetBkMode,
    SetTextColor, TextOutW, UpdateWindow, DT_END_ELLIPSIS, DT_LEFT, DT_NOPREFIX, DT_RIGHT,
    DT_SINGLELINE, DT_VCENTER, DEFAULT_GUI_FONT, HDC, PAINTSTRUCT, TRANSPARENT,
};
use windows::Win32::System::LibraryLoader::GetModuleHandleW;
use windows::Win32::System::Registry::{
    RegCloseKey, RegOpenKeyExW, RegQueryValueExW, HKEY_CURRENT_USER, KEY_READ, REG_VALUE_TYPE,
};
use windows::Win32::System::Threading::CreateMutexW;
use windows::Win32::UI::HiDpi::{
    GetDpiForWindow, SetProcessDpiAwarenessContext, DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2,
};
use windows::Win32::UI::Shell::{
    Shell_NotifyIconW, SHAppBarMessage, ABE_BOTTOM, ABM_NEW, ABM_QUERYPOS, ABM_REMOVE, ABM_SETPOS,
    APPBARDATA, NIF_ICON, NIF_INFO, NIF_MESSAGE, NIF_TIP, NIIF_INFO, NIM_ADD, NIM_DELETE,
    NIM_MODIFY, NOTIFYICONDATAW,
};
use windows::Win32::UI::WindowsAndMessaging::*;

const BAR_CLASS: PCWSTR = w!("MawaqitShellWidget");
const FLYOUT_CLASS: PCWSTR = w!("MawaqitShellFlyout");
const PICKER_CLASS: PCWSTR = w!("MawaqitMosquePicker");
const BAR_H: i32 = 40;
/// Minimum flyout size; actual size is measured from content + DPI.
const FLYOUT_W_MIN: i32 = 320;
const FLYOUT_H_MIN: i32 = 240;
const FLYOUT_PAD: i32 = 16;
const FLYOUT_ROW_H: i32 = 26;
const FLYOUT_HINT: &str = "Click to close · tray for menu";
const PICKER_W: i32 = 420;
const PICKER_H: i32 = 460;
const WM_APPBAR: u32 = WM_USER + 64;
const WM_TRAY: u32 = WM_USER + 65;
const WM_DATA: u32 = WM_USER + 66;
const WM_SEARCH_DONE: u32 = WM_USER + 67;
const WM_MOSQUE_PICKED: u32 = WM_USER + 68;
const TIMER_TICK: usize = 1;
const TIMER_REFRESH: usize = 2;
const ID_TRAY: u32 = 1;
const IDM_SHOW: u16 = 1001;
const IDM_HIDE: u16 = 1002;
const IDM_QUIT: u16 = 1003;
const IDM_REFRESH: u16 = 1004;
const IDM_MOSQUE: u16 = 1005;
const IDC_EDIT: i32 = 2001;
const IDC_LIST: i32 = 2002;
const IDC_SEARCH: i32 = 2003;
const IDC_SELECT: i32 = 2004;
const IDC_STATUS: i32 = 2005;
const IDC_CANCEL: i32 = 2006;
const REFRESH_EVERY_SECS: u64 = 45 * 60;

static BAR_HWND: AtomicIsize = AtomicIsize::new(0);
static FLYOUT_HWND: AtomicIsize = AtomicIsize::new(0);
static PICKER_HWND: AtomicIsize = AtomicIsize::new(0);
static FETCH_IN_FLIGHT: AtomicBool = AtomicBool::new(false);
static SEARCH_IN_FLIGHT: AtomicBool = AtomicBool::new(false);

thread_local! {
    static STATE: RefCell<UiState> = RefCell::new(UiState::default());
}

struct UiState {
    dark: bool,
    brand: String,
    chip: String,
    muted: String,
    next_name: String,
    next_hhmm: String,
    mosque_label: String,
    slots: Vec<PrayerSlot>,
    status: String,
    expanded: bool,
    bar_visible: bool,
    appbar_registered: bool,
    tray_added: bool,
    tray_icon_owned: bool,
    config: AppConfig,
    schedule: Option<DaySchedule>,
    last_fetch: Option<Instant>,
    picker_hits: Vec<MosqueHit>,
    hinst: HINSTANCE,
}

impl Default for UiState {
    fn default() -> Self {
        Self {
            dark: true,
            brand: String::from("Mawaqit"),
            chip: String::from("…"),
            muted: String::from("loading"),
            next_name: String::new(),
            next_hhmm: String::new(),
            mosque_label: String::new(),
            slots: Vec::new(),
            status: String::new(),
            expanded: false,
            bar_visible: true,
            appbar_registered: false,
            tray_added: false,
            tray_icon_owned: false,
            config: AppConfig::default(),
            schedule: None,
            last_fetch: None,
            picker_hits: Vec::new(),
            hinst: HINSTANCE::default(),
        }
    }
}

/// Cross-thread fetch result mailbox.
struct FetchBox {
    times: Option<(PrayerTimes, IqamaTimes)>,
    error: Option<String>,
}

struct SearchBox {
    hits: Option<Result<Vec<MosqueHit>, String>>,
}

static TRAY_ICON: AtomicIsize = AtomicIsize::new(0);
static FETCH: Mutex<FetchBox> = Mutex::new(FetchBox {
    times: None,
    error: None,
});
static SEARCH: Mutex<SearchBox> = Mutex::new(SearchBox { hits: None });

fn hwnd_raw(h: HWND) -> isize {
    h.0 as isize
}

fn hwnd_from(raw: isize) -> HWND {
    HWND(raw as *mut _)
}

fn hwnd_ok(h: HWND) -> bool {
    !h.0.is_null()
}

fn main() {
    unsafe {
        let _ = SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

        let mutex = CreateMutexW(None, true, w!("Local\\MawaqitShellWidget"));
        if mutex.is_ok() && GetLastError() == ERROR_ALREADY_EXISTS {
            if let Ok(existing) = FindWindowW(BAR_CLASS, None) {
                let _ = ShowWindow(existing, SW_SHOW);
                let _ = SetForegroundWindow(existing);
            }
            return;
        }

        let dark = !system_uses_light_theme();
        let need_picker = bootstrap_data(dark);

        let module = GetModuleHandleW(None).unwrap_or_default();
        let hinst: HINSTANCE = module.into();
        STATE.with(|s| s.borrow_mut().hinst = hinst);
        register_classes(hinst, dark);

        let bar = create_bar(hinst);
        if !hwnd_ok(bar) {
            return;
        }
        BAR_HWND.store(hwnd_raw(bar), Ordering::SeqCst);

        let flyout = create_flyout(hinst);
        FLYOUT_HWND.store(hwnd_raw(flyout), Ordering::SeqCst);

        if !register_appbar(bar) {
            let _ = DestroyWindow(bar);
            return;
        }
        STATE.with(|s| s.borrow_mut().appbar_registered = true);

        position_appbar(bar);
        let _ = ShowWindow(bar, SW_SHOWNOACTIVATE);
        let _ = UpdateWindow(bar);
        let _ = SetTimer(bar, TIMER_TICK, 1000, None);
        let _ = SetTimer(bar, TIMER_REFRESH, 60_000, None);

        if add_tray_icon(bar) {
            STATE.with(|s| s.borrow_mut().tray_added = true);
        }

        request_refresh(bar);
        if need_picker {
            open_picker();
        }

        let mut msg = MSG::default();
        while GetMessageW(&mut msg, None, 0, 0).into() {
            let _ = TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }
    }
}

unsafe fn register_classes(hinst: HINSTANCE, dark: bool) {
    let bg = if dark { 0x0020_2020u32 } else { 0x00F3_F3F3u32 };
    let brush = CreateSolidBrush(COLORREF(bg));

    let bar = WNDCLASSW {
        style: CS_HREDRAW | CS_VREDRAW | CS_DBLCLKS,
        lpfnWndProc: Some(bar_proc),
        hInstance: hinst,
        hCursor: LoadCursorW(None, IDC_ARROW).unwrap_or_default(),
        hbrBackground: brush,
        lpszClassName: BAR_CLASS,
        ..Default::default()
    };
    let _ = RegisterClassW(&bar);

    let fly = WNDCLASSW {
        style: CS_HREDRAW | CS_VREDRAW,
        lpfnWndProc: Some(flyout_proc),
        hInstance: hinst,
        hCursor: LoadCursorW(None, IDC_ARROW).unwrap_or_default(),
        hbrBackground: brush,
        lpszClassName: FLYOUT_CLASS,
        ..Default::default()
    };
    let _ = RegisterClassW(&fly);

    let picker_bg = CreateSolidBrush(COLORREF(if dark { 0x0028_2828 } else { 0x00F0_F0F0 }));
    let picker = WNDCLASSW {
        style: CS_HREDRAW | CS_VREDRAW,
        lpfnWndProc: Some(picker_proc),
        hInstance: hinst,
        hCursor: LoadCursorW(None, IDC_ARROW).unwrap_or_default(),
        hbrBackground: picker_bg,
        lpszClassName: PICKER_CLASS,
        ..Default::default()
    };
    let _ = RegisterClassW(&picker);
}

unsafe fn create_bar(hinst: HINSTANCE) -> HWND {
    CreateWindowExW(
        WINDOW_EX_STYLE(WS_EX_TOOLWINDOW.0 | WS_EX_TOPMOST.0 | WS_EX_NOACTIVATE.0),
        BAR_CLASS,
        w!("Mawaqit"),
        WS_POPUP,
        0,
        0,
        800,
        BAR_H,
        None,
        None,
        hinst,
        None,
    )
    .unwrap_or_default()
}

unsafe fn create_flyout(hinst: HINSTANCE) -> HWND {
    CreateWindowExW(
        WINDOW_EX_STYLE(WS_EX_TOOLWINDOW.0 | WS_EX_TOPMOST.0),
        FLYOUT_CLASS,
        w!("Mawaqit Flyout"),
        WS_POPUP,
        0,
        0,
        FLYOUT_W_MIN,
        FLYOUT_H_MIN,
        None,
        None,
        hinst,
        None,
    )
    .unwrap_or_default()
}

unsafe fn register_appbar(hwnd: HWND) -> bool {
    let mut abd = APPBARDATA {
        cbSize: size_of::<APPBARDATA>() as u32,
        hWnd: hwnd,
        uCallbackMessage: WM_APPBAR,
        uEdge: ABE_BOTTOM,
        rc: RECT::default(),
        lParam: LPARAM(0),
    };
    SHAppBarMessage(ABM_NEW, &mut abd) != 0
}

unsafe fn position_appbar(hwnd: HWND) {
    let (sw, sh) = (
        GetSystemMetrics(SM_CXSCREEN),
        GetSystemMetrics(SM_CYSCREEN),
    );
    let mut abd = APPBARDATA {
        cbSize: size_of::<APPBARDATA>() as u32,
        hWnd: hwnd,
        uCallbackMessage: WM_APPBAR,
        uEdge: ABE_BOTTOM,
        rc: RECT {
            left: 0,
            top: sh - BAR_H,
            right: sw,
            bottom: sh,
        },
        lParam: LPARAM(0),
    };
    let _ = SHAppBarMessage(ABM_QUERYPOS, &mut abd);
    abd.rc.top = abd.rc.bottom - BAR_H;
    let _ = SHAppBarMessage(ABM_SETPOS, &mut abd);
    let _ = MoveWindow(
        hwnd,
        abd.rc.left,
        abd.rc.top,
        abd.rc.right - abd.rc.left,
        abd.rc.bottom - abd.rc.top,
        true,
    );
}

unsafe fn remove_appbar(hwnd: HWND) {
    let mut abd = APPBARDATA {
        cbSize: size_of::<APPBARDATA>() as u32,
        hWnd: hwnd,
        ..Default::default()
    };
    let _ = SHAppBarMessage(ABM_REMOVE, &mut abd);
}

fn icon_candidates() -> Vec<PathBuf> {
    let mut paths = Vec::new();
    if let Ok(exe) = std::env::current_exe() {
        if let Some(dir) = exe.parent() {
            paths.push(dir.join("icon.ico"));
            paths.push(dir.join("assets").join("icon.ico"));
        }
    }
    paths.push(PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("assets").join("icon.ico"));
    paths
}

unsafe fn load_tray_icon() -> (HICON, bool) {
    for path in icon_candidates() {
        if !path.is_file() {
            continue;
        }
        let wide: Vec<u16> = path
            .to_string_lossy()
            .encode_utf16()
            .chain(std::iter::once(0))
            .collect();
        if let Ok(handle) = LoadImageW(
            None,
            PCWSTR(wide.as_ptr()),
            IMAGE_ICON,
            0,
            0,
            LR_LOADFROMFILE | LR_DEFAULTSIZE,
        ) {
            let icon = HICON(handle.0);
            if !icon.is_invalid() {
                return (icon, true);
            }
        }
    }
    (
        LoadIconW(None, IDI_APPLICATION).unwrap_or_default(),
        false,
    )
}

fn copy_wide_tip(dst: &mut [u16], text: &str) {
    let tip: Vec<u16> = text.encode_utf16().chain(std::iter::once(0)).collect();
    let n = tip.len().min(dst.len());
    dst[..n].copy_from_slice(&tip[..n]);
}

unsafe fn tray_tip_text() -> &'static str {
    if STATE.with(|s| s.borrow().bar_visible) {
        "Mawaqit — click for times"
    } else {
        "Mawaqit — click to show the prayer bar"
    }
}

unsafe fn add_tray_icon(hwnd: HWND) -> bool {
    let (icon, owned) = load_tray_icon();
    TRAY_ICON.store(icon.0 as isize, Ordering::SeqCst);
    STATE.with(|s| s.borrow_mut().tray_icon_owned = owned);

    let mut nid = NOTIFYICONDATAW {
        cbSize: size_of::<NOTIFYICONDATAW>() as u32,
        hWnd: hwnd,
        uID: ID_TRAY,
        uFlags: NIF_MESSAGE | NIF_ICON | NIF_TIP,
        uCallbackMessage: WM_TRAY,
        hIcon: icon,
        ..Default::default()
    };
    copy_wide_tip(&mut nid.szTip, tray_tip_text());
    Shell_NotifyIconW(NIM_ADD, &nid).as_bool()
}

unsafe fn update_tray_tip(hwnd: HWND) {
    if !STATE.with(|s| s.borrow().tray_added) {
        return;
    }
    let icon = HICON(TRAY_ICON.load(Ordering::SeqCst) as *mut _);
    let mut nid = NOTIFYICONDATAW {
        cbSize: size_of::<NOTIFYICONDATAW>() as u32,
        hWnd: hwnd,
        uID: ID_TRAY,
        uFlags: NIF_TIP | NIF_ICON,
        hIcon: icon,
        ..Default::default()
    };
    copy_wide_tip(&mut nid.szTip, tray_tip_text());
    let _ = Shell_NotifyIconW(NIM_MODIFY, &nid);
}

unsafe fn tray_balloon(hwnd: HWND, title: &str, body: &str) {
    if !STATE.with(|s| s.borrow().tray_added) {
        return;
    }
    let mut nid = NOTIFYICONDATAW {
        cbSize: size_of::<NOTIFYICONDATAW>() as u32,
        hWnd: hwnd,
        uID: ID_TRAY,
        uFlags: NIF_INFO,
        dwInfoFlags: NIIF_INFO,
        ..Default::default()
    };
    copy_wide_tip(&mut nid.szInfoTitle, title);
    // szInfo is longer than szTip — fill via encode into the field.
    let info: Vec<u16> = body.encode_utf16().chain(std::iter::once(0)).collect();
    let n = info.len().min(nid.szInfo.len());
    nid.szInfo[..n].copy_from_slice(&info[..n]);
    let _ = Shell_NotifyIconW(NIM_MODIFY, &nid);
}

unsafe fn remove_tray_icon(hwnd: HWND) {
    let nid = NOTIFYICONDATAW {
        cbSize: size_of::<NOTIFYICONDATAW>() as u32,
        hWnd: hwnd,
        uID: ID_TRAY,
        ..Default::default()
    };
    let _ = Shell_NotifyIconW(NIM_DELETE, &nid);
    if STATE.with(|s| s.borrow().tray_icon_owned) {
        let icon = HICON(TRAY_ICON.load(Ordering::SeqCst) as *mut _);
        if !icon.is_invalid() {
            let _ = DestroyIcon(icon);
        }
    }
}

unsafe fn show_bar(show: bool) {
    let bar = hwnd_from(BAR_HWND.load(Ordering::SeqCst));
    if !hwnd_ok(bar) {
        return;
    }
    let (registered, visible) = STATE.with(|s| {
        let st = s.borrow();
        (st.appbar_registered, st.bar_visible)
    });
    if show == visible {
        return;
    }
    if show {
        if !registered {
            if register_appbar(bar) {
                STATE.with(|s| s.borrow_mut().appbar_registered = true);
            }
        }
        position_appbar(bar);
        let _ = ShowWindow(bar, SW_SHOWNOACTIVATE);
        STATE.with(|s| s.borrow_mut().bar_visible = true);
        update_tray_tip(bar);
    } else {
        hide_flyout();
        if registered {
            remove_appbar(bar);
            STATE.with(|s| s.borrow_mut().appbar_registered = false);
        }
        let _ = ShowWindow(bar, SW_HIDE);
        STATE.with(|s| s.borrow_mut().bar_visible = false);
        update_tray_tip(bar);
        // Recovery path must be obvious — tray is the only way back.
        tray_balloon(
            bar,
            "Mawaqit hidden",
            "Click the Mawaqit icon in the system tray (near the clock) to show the bar again.",
        );
    }
}

unsafe fn toggle_flyout() {
    let expanded = STATE.with(|s| {
        let mut st = s.borrow_mut();
        st.expanded = !st.expanded;
        st.expanded
    });
    let flyout = hwnd_from(FLYOUT_HWND.load(Ordering::SeqCst));
    let bar = hwnd_from(BAR_HWND.load(Ordering::SeqCst));
    if !hwnd_ok(flyout) || !hwnd_ok(bar) {
        return;
    }
    if expanded {
        let (fw, fh) = measure_flyout_size(flyout);
        let mut br = RECT::default();
        let _ = GetWindowRect(bar, &mut br);
        let screen_w = GetSystemMetrics(SM_CXSCREEN);
        let x = (br.left + 12).clamp(0, (screen_w - fw).max(0));
        let y = br.top - fh - 6;
        let _ = SetWindowPos(
            flyout,
            HWND_TOPMOST,
            x,
            y.max(0),
            fw,
            fh,
            SWP_SHOWWINDOW | SWP_NOACTIVATE,
        );
        let _ = ShowWindow(flyout, SW_SHOWNOACTIVATE);
        let _ = InvalidateRect(flyout, None, true);
    } else {
        let _ = ShowWindow(flyout, SW_HIDE);
    }
    let _ = InvalidateRect(bar, None, false);
}

unsafe fn hide_flyout() {
    STATE.with(|s| s.borrow_mut().expanded = false);
    let flyout = hwnd_from(FLYOUT_HWND.load(Ordering::SeqCst));
    if hwnd_ok(flyout) {
        let _ = ShowWindow(flyout, SW_HIDE);
    }
    let bar = hwnd_from(BAR_HWND.load(Ordering::SeqCst));
    if hwnd_ok(bar) {
        let _ = InvalidateRect(bar, None, false);
    }
}

unsafe fn show_tray_menu(hwnd: HWND) {
    let mut pt = POINT::default();
    let _ = GetCursorPos(&mut pt);
    let menu = CreatePopupMenu().unwrap_or_default();
    if menu.is_invalid() {
        return;
    }
    let visible = STATE.with(|s| s.borrow().bar_visible);
    if visible {
        let _ = AppendMenuW(
            menu,
            MF_STRING,
            IDM_HIDE as usize,
            w!("Hide bar (keeps tray icon)"),
        );
    } else {
        let _ = AppendMenuW(menu, MF_STRING, IDM_SHOW as usize, w!("Show prayer bar"));
    }
    let _ = AppendMenuW(menu, MF_SEPARATOR, 0, PCWSTR::null());
    let _ = AppendMenuW(menu, MF_STRING, IDM_MOSQUE as usize, w!("Change mosque…"));
    let _ = AppendMenuW(menu, MF_STRING, IDM_REFRESH as usize, w!("Refresh times"));
    let _ = AppendMenuW(menu, MF_STRING, IDM_QUIT as usize, w!("Quit"));

    // Required so the menu dismisses correctly for tray icons.
    let _ = SetForegroundWindow(hwnd);
    let cmd = TrackPopupMenu(
        menu,
        TRACK_POPUP_MENU_FLAGS(TPM_RETURNCMD.0 | TPM_NONOTIFY.0 | TPM_RIGHTBUTTON.0),
        pt.x,
        pt.y,
        0,
        hwnd,
        None,
    );
    let _ = DestroyMenu(menu);
    let _ = PostMessageW(hwnd, WM_NULL, WPARAM(0), LPARAM(0));

    match cmd.0 as u16 {
        IDM_SHOW => show_bar(true),
        IDM_HIDE => show_bar(false),
        IDM_MOSQUE => open_picker(),
        IDM_REFRESH => request_refresh(hwnd),
        IDM_QUIT => {
            PostQuitMessage(0);
        }
        _ => {}
    }
}

unsafe extern "system" fn bar_proc(
    hwnd: HWND,
    msg: u32,
    wparam: WPARAM,
    lparam: LPARAM,
) -> LRESULT {
    match msg {
        WM_PAINT => {
            paint_bar(hwnd);
            LRESULT(0)
        }
        WM_LBUTTONUP => {
            toggle_flyout();
            LRESULT(0)
        }
        WM_RBUTTONUP => {
            show_tray_menu(hwnd);
            LRESULT(0)
        }
        WM_TIMER if wparam.0 == TIMER_TICK => {
            let before = STATE.with(|s| s.borrow().chip.clone());
            refresh_chip();
            let after = STATE.with(|s| s.borrow().chip.clone());
            if before != after {
                let _ = InvalidateRect(hwnd, None, false);
                let flyout = hwnd_from(FLYOUT_HWND.load(Ordering::SeqCst));
                if hwnd_ok(flyout) && IsWindowVisible(flyout).as_bool() {
                    let _ = InvalidateRect(flyout, None, false);
                }
            }
            LRESULT(0)
        }
        WM_TIMER if wparam.0 == TIMER_REFRESH => {
            let due = STATE.with(|s| {
                let st = s.borrow();
                match st.last_fetch {
                    None => st.config.mosque.is_some(),
                    Some(t) => t.elapsed().as_secs() >= REFRESH_EVERY_SECS,
                }
            });
            if due {
                request_refresh(hwnd);
            }
            LRESULT(0)
        }
        WM_DATA => {
            apply_fetch_result();
            refresh_chip();
            let _ = InvalidateRect(hwnd, None, false);
            let flyout = hwnd_from(FLYOUT_HWND.load(Ordering::SeqCst));
            if hwnd_ok(flyout) && IsWindowVisible(flyout).as_bool() {
                let _ = InvalidateRect(flyout, None, false);
            }
            LRESULT(0)
        }
        WM_MOSQUE_PICKED => {
            if let Ok(cfg) = config::load_config() {
                STATE.with(|s| {
                    let mut st = s.borrow_mut();
                    st.config = cfg.clone();
                    if let Some(m) = &cfg.mosque {
                        st.mosque_label = m.display_name.clone();
                        st.muted = m.display_name.clone();
                        st.status = String::from("Loading times…");
                        st.schedule = None;
                        st.slots.clear();
                        st.last_fetch = None;
                    }
                });
                refresh_chip();
                request_refresh(hwnd);
                let _ = InvalidateRect(hwnd, None, false);
                tray_balloon(
                    hwnd,
                    "Mosque saved",
                    "Prayer times will refresh for your selected mosque.",
                );
            }
            LRESULT(0)
        }
        WM_TRAY => {
            // Tray is the recovery surface when the AppBar is hidden.
            let mouse = (lparam.0 as u32) & 0xffff;
            match mouse {
                WM_LBUTTONUP | WM_LBUTTONDBLCLK => {
                    let was_visible = STATE.with(|s| s.borrow().bar_visible);
                    show_bar(true);
                    if was_visible {
                        // Already showing — left-click toggles the schedule flyout.
                        toggle_flyout();
                    }
                }
                WM_RBUTTONUP | WM_CONTEXTMENU => show_tray_menu(hwnd),
                _ => {}
            }
            LRESULT(0)
        }
        WM_APPBAR => {
            if STATE.with(|s| s.borrow().appbar_registered) {
                position_appbar(hwnd);
                let _ = InvalidateRect(hwnd, None, true);
            }
            LRESULT(0)
        }
        WM_DISPLAYCHANGE | WM_SETTINGCHANGE => {
            let dark = !system_uses_light_theme();
            STATE.with(|s| s.borrow_mut().dark = dark);
            if STATE.with(|s| s.borrow().appbar_registered) {
                position_appbar(hwnd);
            }
            let _ = InvalidateRect(hwnd, None, true);
            LRESULT(0)
        }
        WM_DESTROY => {
            let _ = KillTimer(hwnd, TIMER_TICK);
            let _ = KillTimer(hwnd, TIMER_REFRESH);
            hide_flyout();
            if STATE.with(|s| s.borrow().tray_added) {
                remove_tray_icon(hwnd);
            }
            if STATE.with(|s| s.borrow().appbar_registered) {
                remove_appbar(hwnd);
            }
            PostQuitMessage(0);
            LRESULT(0)
        }
        _ => DefWindowProcW(hwnd, msg, wparam, lparam),
    }
}

unsafe extern "system" fn flyout_proc(
    hwnd: HWND,
    msg: u32,
    wparam: WPARAM,
    lparam: LPARAM,
) -> LRESULT {
    match msg {
        WM_PAINT => {
            paint_flyout(hwnd);
            LRESULT(0)
        }
        WM_LBUTTONUP => {
            hide_flyout();
            LRESULT(0)
        }
        _ => DefWindowProcW(hwnd, msg, wparam, lparam),
    }
}

/// Returns true when the mosque picker should open (no mosque saved yet).
fn bootstrap_data(dark: bool) -> bool {
    let cfg = config::load_config().unwrap_or_default();
    let _ = mawaqit_widget::autostart::set_enabled(cfg.start_with_windows);
    let need_picker = cfg.mosque.is_none();

    STATE.with(|s| {
        let mut st = s.borrow_mut();
        st.dark = dark;
        st.config = cfg.clone();
        if let Some(m) = &cfg.mosque {
            st.mosque_label = m.display_name.clone();
            st.muted = m.display_name.clone();
            st.status = String::from("Loading times…");
            st.chip = String::from("Next  …");
        } else {
            st.mosque_label.clear();
            st.chip = String::from("No mosque configured");
            st.muted = String::from("tray → Change mosque…");
            st.status = String::from("Search and select your mosque");
        }
    });

    if cfg.mosque.is_some() {
        if let Ok(Some(cache)) = config::load_cache() {
            apply_times(cache.prayer_times, cache.iqama, false);
        }
    }
    refresh_chip();
    need_picker
}

fn apply_times(times: PrayerTimes, iqama: IqamaTimes, from_network: bool) {
    let day = today_local();
    let schedule = build_day_schedule(day, &times, &iqama);
    let slots = schedule.slots.clone();
    STATE.with(|s| {
        let mut st = s.borrow_mut();
        st.schedule = Some(schedule);
        st.slots = slots;
        st.status = if from_network {
            String::from("Updated")
        } else {
            String::from("Cached")
        };
        if from_network {
            st.last_fetch = Some(Instant::now());
        }
    });
}

fn apply_fetch_result() {
    let (times, err) = {
        let mut box_ = FETCH.lock().unwrap_or_else(|e| e.into_inner());
        (box_.times.take(), box_.error.take())
    };
    if let Some((times, iqama)) = times {
        let day = today_local().to_string();
        let _ = config::save_cache(&CacheData {
            fetched_at: chrono::Utc::now().to_rfc3339(),
            day,
            prayer_times: times.clone(),
            iqama: iqama.clone(),
        });
        apply_times(times, iqama, true);
    }
    if let Some(e) = err {
        STATE.with(|s| {
            let mut st = s.borrow_mut();
            if st.schedule.is_none() {
                st.chip = String::from("Times unavailable");
            }
            st.status = e;
            st.muted = String::from("tray → Refresh times");
        });
    }
}

fn request_refresh(hwnd: HWND) {
    let (api, mosque) = STATE.with(|s| {
        let st = s.borrow();
        (st.config.api_base_url.clone(), st.config.mosque.clone())
    });
    let Some(mosque) = mosque else {
        STATE.with(|s| {
            let mut st = s.borrow_mut();
            st.status = String::from("No mosque configured");
            st.muted = String::from("tray → Change mosque…");
        });
        unsafe {
            let _ = InvalidateRect(hwnd, None, false);
        }
        return;
    };
    // Allow a new refresh even if a previous fetch got stuck.
    if FETCH_IN_FLIGHT.swap(true, Ordering::SeqCst) {
        STATE.with(|s| s.borrow_mut().status = String::from("Refresh already running…"));
        unsafe {
            let _ = InvalidateRect(hwnd, None, false);
        }
        return;
    }
    STATE.with(|s| {
        let mut st = s.borrow_mut();
        st.status = String::from("Refreshing…");
        st.schedule = None;
        st.slots.clear();
        st.last_fetch = None;
    });
    unsafe {
        let _ = InvalidateRect(hwnd, None, false);
    }

    let hwnd_raw = hwnd_raw(hwnd);
    thread::spawn(move || {
        let result = (|| {
            let client = MawaqitClient::new(api)?;
            let times = client.prayer_times(&mosque.masjid_id)?;
            let now = today_local();
            let iqama = client
                .iqama_for_day(&mosque.masjid_id, now.month(), now.day())
                .unwrap_or_default();
            Ok::<_, mawaqit_widget::Error>((times, iqama))
        })();

        {
            let mut box_ = FETCH.lock().unwrap_or_else(|e| e.into_inner());
            match result {
                Ok(pair) => {
                    box_.times = Some(pair);
                    box_.error = None;
                }
                Err(e) => {
                    box_.times = None;
                    box_.error = Some(e.to_string());
                }
            }
        }
        FETCH_IN_FLIGHT.store(false, Ordering::SeqCst);
        unsafe {
            let _ = PostMessageW(hwnd_from(hwnd_raw), WM_DATA, WPARAM(0), LPARAM(0));
        }
    });
}

fn refresh_chip() {
    STATE.with(|s| {
        let mut st = s.borrow_mut();
        if st.config.mosque.is_none() {
            st.next_name.clear();
            st.next_hhmm.clear();
            st.chip = String::from("No mosque configured");
            st.muted = String::from("tray → Change mosque…");
            return;
        }
        let Some(schedule) = st.schedule.clone() else {
            st.chip = String::from("Loading times…");
            return;
        };
        let fajr = schedule
            .slots
            .iter()
            .find(|s| s.kind == PrayerKind::Fajr)
            .map(|s| s.adhan);
        let now = now_local();
        if let Some(next) = next_prayer_with_tomorrow(&schedule, fajr, now) {
            let hhmm = format_hhmm(next.at.time());
            let name = next.kind.label();
            st.next_name = name.to_string();
            st.next_hhmm = hhmm.clone();
            st.chip = format!(
                "Next  {name}  {hhmm}   ·   {}",
                format_countdown(next.remaining)
            );
        } else {
            st.chip = String::from("Schedule ready");
        }
        if !st.mosque_label.is_empty() {
            st.muted = st.mosque_label.clone();
        }
    });
}

fn open_picker() {
    // Only one map window at a time.
    if PICKER_HWND.swap(1, Ordering::SeqCst) != 0 {
        return;
    }
    unsafe {
        hide_flyout();
    }
    let bar = BAR_HWND.load(Ordering::SeqCst);
    thread::spawn(move || {
        let result = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
            let (tx, rx) = std::sync::mpsc::sync_channel(1);
            mawaqit_widget::map_picker::run(move |hit| {
                let _ = tx.send(hit);
            });
            // `run` returns after the window closes; the callback has already sent.
            rx.try_recv().ok().flatten()
        }));
        // Always clear — including panic — so Change mosque can be tried again.
        PICKER_HWND.store(0, Ordering::SeqCst);
        match result {
            Ok(Some(hit)) => {
                let mut cfg = config::load_config().unwrap_or_default();
                cfg.mosque = Some(MosqueSelection {
                    masjid_id: hit.slug,
                    display_name: hit.label,
                });
                if config::save_config(&cfg).is_ok() {
                    unsafe {
                        let _ = PostMessageW(
                            hwnd_from(bar),
                            WM_MOSQUE_PICKED,
                            WPARAM(0),
                            LPARAM(0),
                        );
                    }
                }
            }
            Ok(None) => {}
            Err(_) => unsafe {
                let hwnd = hwnd_from(bar);
                if hwnd_ok(hwnd) {
                    tray_balloon(
                        hwnd,
                        "Map picker failed",
                        "Could not open the mosque map. Try again, or paste a mawaqit.net link.",
                    );
                }
            },
        }
    });
}

unsafe fn set_picker_status(hwnd: HWND, text: &str) {
    let status = GetDlgItem(hwnd, IDC_STATUS).unwrap_or_default();
    if hwnd_ok(status) {
        let wide: Vec<u16> = text.encode_utf16().chain(std::iter::once(0)).collect();
        let _ = SetWindowTextW(status, PCWSTR(wide.as_ptr()));
    }
}

unsafe fn picker_run_search(hwnd: HWND) {
    let edit = GetDlgItem(hwnd, IDC_EDIT).unwrap_or_default();
    if !hwnd_ok(edit) {
        return;
    }
    let mut buf = [0u16; 256];
    let n = GetWindowTextW(edit, &mut buf);
    let q = String::from_utf16_lossy(&buf[..n as usize]).trim().to_string();
    if q.is_empty() {
        set_picker_status(hwnd, "Enter a search term first.");
        return;
    }
    if SEARCH_IN_FLIGHT.swap(true, Ordering::SeqCst) {
        set_picker_status(hwnd, "Search already running…");
        return;
    }
    set_picker_status(hwnd, "Searching…");
    let api = STATE.with(|s| s.borrow().config.api_base_url.clone());
    let hwnd_raw = hwnd_raw(hwnd);
    thread::spawn(move || {
        let result = MawaqitClient::new(api)
            .and_then(|c| c.search_mosques(&q))
            .map_err(|e| e.to_string());
        {
            let mut box_ = SEARCH.lock().unwrap_or_else(|e| e.into_inner());
            box_.hits = Some(result);
        }
        SEARCH_IN_FLIGHT.store(false, Ordering::SeqCst);
        let _ = PostMessageW(hwnd_from(hwnd_raw), WM_SEARCH_DONE, WPARAM(0), LPARAM(0));
    });
}

unsafe fn picker_apply_search(hwnd: HWND) {
    let taken = {
        let mut box_ = SEARCH.lock().unwrap_or_else(|e| e.into_inner());
        box_.hits.take()
    };
    let Some(result) = taken else {
        return;
    };
    let list = GetDlgItem(hwnd, IDC_LIST).unwrap_or_default();
    if hwnd_ok(list) {
        let _ = SendMessageW(list, LB_RESETCONTENT, WPARAM(0), LPARAM(0));
    }
    match result {
        Ok(hits) => {
            STATE.with(|s| s.borrow_mut().picker_hits = hits.clone());
            if hits.is_empty() {
                set_picker_status(hwnd, "No mosques found.");
            } else {
                for hit in &hits {
                    if hwnd_ok(list) {
                        let wide: Vec<u16> =
                            hit.label.encode_utf16().chain(std::iter::once(0)).collect();
                        let _ = SendMessageW(
                            list,
                            LB_ADDSTRING,
                            WPARAM(0),
                            LPARAM(wide.as_ptr() as isize),
                        );
                    }
                }
                set_picker_status(hwnd, &format!("{} result(s) — select one, then Use selected.", hits.len()));
            }
        }
        Err(e) => {
            STATE.with(|s| s.borrow_mut().picker_hits.clear());
            set_picker_status(hwnd, &e);
        }
    }
}

unsafe fn picker_select(hwnd: HWND) {
    let list = GetDlgItem(hwnd, IDC_LIST).unwrap_or_default();
    if !hwnd_ok(list) {
        return;
    }
    let sel = SendMessageW(list, LB_GETCURSEL, WPARAM(0), LPARAM(0)).0;
    if sel < 0 {
        set_picker_status(hwnd, "Select a mosque in the list.");
        return;
    }
    let hit = STATE.with(|s| s.borrow().picker_hits.get(sel as usize).cloned());
    let Some(hit) = hit else {
        set_picker_status(hwnd, "Invalid selection.");
        return;
    };

    let mut cfg = STATE.with(|s| s.borrow().config.clone());
    cfg.mosque = Some(MosqueSelection {
        masjid_id: hit.slug.clone(),
        display_name: hit.label.clone(),
    });
    if let Err(e) = config::save_config(&cfg) {
        set_picker_status(hwnd, &format!("Save failed: {e}"));
        return;
    }

    STATE.with(|s| {
        let mut st = s.borrow_mut();
        st.config = cfg;
        st.mosque_label = hit.label.clone();
        st.muted = hit.label.clone();
        st.schedule = None;
        st.slots.clear();
        st.status = String::from("Loading times…");
        st.chip = String::from("Next  …");
        st.last_fetch = None;
    });
    refresh_chip();

    let bar = hwnd_from(BAR_HWND.load(Ordering::SeqCst));
    if hwnd_ok(bar) {
        request_refresh(bar);
        let _ = InvalidateRect(bar, None, false);
    }

    let _ = DestroyWindow(hwnd);
}

unsafe extern "system" fn picker_proc(
    hwnd: HWND,
    msg: u32,
    wparam: WPARAM,
    lparam: LPARAM,
) -> LRESULT {
    match msg {
        WM_COMMAND => {
            let id = (wparam.0 as u16) as i32;
            let code = (wparam.0 >> 16) as u16;
            match id {
                IDC_SEARCH => {
                    picker_run_search(hwnd);
                    LRESULT(0)
                }
                IDC_SELECT => {
                    picker_select(hwnd);
                    LRESULT(0)
                }
                IDC_CANCEL => {
                    let _ = DestroyWindow(hwnd);
                    LRESULT(0)
                }
                IDC_LIST if code == LBN_DBLCLK as u16 => {
                    picker_select(hwnd);
                    LRESULT(0)
                }
                _ => DefWindowProcW(hwnd, msg, wparam, lparam),
            }
        }
        WM_SEARCH_DONE => {
            picker_apply_search(hwnd);
            LRESULT(0)
        }
        WM_CLOSE => {
            let _ = DestroyWindow(hwnd);
            LRESULT(0)
        }
        WM_DESTROY => {
            PICKER_HWND.store(0, Ordering::SeqCst);
            LRESULT(0)
        }
        _ => DefWindowProcW(hwnd, msg, wparam, lparam),
    }
}

fn system_uses_light_theme() -> bool {
    unsafe {
        let mut hkey = Default::default();
        let path = w!("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");
        if RegOpenKeyExW(HKEY_CURRENT_USER, path, 0, KEY_READ, &mut hkey).is_err() {
            return false;
        }
        let mut kind = REG_VALUE_TYPE::default();
        let mut data: u32 = 0;
        let mut size = size_of::<u32>() as u32;
        let ok = RegQueryValueExW(
            hkey,
            w!("SystemUsesLightTheme"),
            None,
            Some(&mut kind),
            Some((&mut data as *mut u32).cast()),
            Some(&mut size),
        );
        let _ = RegCloseKey(hkey);
        ok.is_ok() && data == 1
    }
}

unsafe fn paint_bar(hwnd: HWND) {
    let (dark, brand, chip, muted, expanded) = STATE.with(|s| {
        let st = s.borrow();
        (
            st.dark,
            st.brand.clone(),
            st.chip.clone(),
            st.muted.clone(),
            st.expanded,
        )
    });

    let (bg, fg, dim, accent) = theme(dark);

    let mut ps = PAINTSTRUCT::default();
    let hdc = BeginPaint(hwnd, &mut ps);
    let mut rc = RECT::default();
    let _ = GetClientRect(hwnd, &mut rc);

    let brush = CreateSolidBrush(COLORREF(bg));
    let _ = FillRect(hdc, &rc, brush);
    let _ = DeleteObject(brush);

    let edge = CreateSolidBrush(COLORREF(if dark { 0x0038_3838 } else { 0x00D0_D0D0 }));
    let edge_rc = RECT {
        left: 0,
        top: 0,
        right: rc.right,
        bottom: 1,
    };
    let _ = FillRect(hdc, &edge_rc, edge);
    let _ = DeleteObject(edge);

    let pip = CreateSolidBrush(COLORREF(accent));
    let pip_rc = RECT {
        left: 0,
        top: 10,
        right: 3,
        bottom: (rc.bottom - 10).max(12),
    };
    let _ = FillRect(hdc, &pip_rc, pip);
    let _ = DeleteObject(pip);

    let _ = SetBkMode(hdc, TRANSPARENT);
    let font = GetStockObject(DEFAULT_GUI_FONT);
    let old = SelectObject(hdc, font);
    let y = ((rc.bottom - rc.top).max(20) - 16) / 2;

    let _ = SetTextColor(hdc, COLORREF(fg));
    let brand_w: Vec<u16> = brand.encode_utf16().collect();
    let _ = TextOutW(hdc, 14, y, &brand_w);

    let _ = SetTextColor(hdc, COLORREF(if expanded { accent } else { accent }));
    let chip_w: Vec<u16> = chip.encode_utf16().collect();
    let _ = TextOutW(hdc, 100, y, &chip_w);

    if rc.right > 520 {
        let _ = SetTextColor(hdc, COLORREF(dim));
        let muted_w: Vec<u16> = muted.encode_utf16().collect();
        let _ = TextOutW(hdc, rc.right - 180, y, &muted_w);
    }

    let _ = SelectObject(hdc, old);
    let _ = EndPaint(hwnd, &ps);
}

fn flyout_title(next_name: &str, next_hhmm: &str) -> String {
    if next_name.is_empty() {
        String::from("Mawaqit")
    } else {
        format!("Next: {next_name}  {next_hhmm}")
    }
}

fn flyout_slot_left(slot: &PrayerSlot, next_name: &str) -> String {
    let mark = if slot.kind.label() == next_name {
        ">"
    } else {
        " "
    };
    format!("{mark}  {}", slot.kind.label())
}

fn flyout_slot_right(slot: &PrayerSlot) -> String {
    match slot.iqama_display.as_deref() {
        Some(iq) if !iq.is_empty() => format!("{} · iqama {iq}", slot.adhan_display),
        _ => slot.adhan_display.clone(),
    }
}

unsafe fn text_width(hdc: HDC, text: &str) -> i32 {
    let wide: Vec<u16> = text.encode_utf16().collect();
    let mut size = Default::default();
    let _ = GetTextExtentPoint32W(hdc, &wide, &mut size);
    size.cx
}

unsafe fn measure_flyout_size(hwnd: HWND) -> (i32, i32) {
    let (next_name, next_hhmm, mosque, slots, status) = STATE.with(|s| {
        let st = s.borrow();
        (
            st.next_name.clone(),
            st.next_hhmm.clone(),
            st.mosque_label.clone(),
            st.slots.clone(),
            st.status.clone(),
        )
    });

    let hdc = GetDC(hwnd);
    let font = GetStockObject(DEFAULT_GUI_FONT);
    let old = SelectObject(hdc, font);

    let title = flyout_title(&next_name, &next_hhmm);
    let sub = if mosque.is_empty() {
        status.clone()
    } else {
        mosque
    };

    let mut content_w = text_width(hdc, &title)
        .max(text_width(hdc, FLYOUT_HINT))
        .max(text_width(hdc, &sub).min(420));

    if slots.is_empty() {
        let msg = if status.is_empty() {
            "No times yet"
        } else {
            status.as_str()
        };
        content_w = content_w.max(text_width(hdc, msg));
    } else {
        for slot in &slots {
            let row_w = text_width(hdc, &flyout_slot_left(slot, &next_name))
                + 24
                + text_width(hdc, &flyout_slot_right(slot));
            content_w = content_w.max(row_w);
        }
    }

    let _ = SelectObject(hdc, old);
    let _ = ReleaseDC(hwnd, hdc);

    let dpi = GetDpiForWindow(hwnd).max(96) as i32;
    let pad = FLYOUT_PAD * dpi / 96;
    let row_h = FLYOUT_ROW_H * dpi / 96;
    let header_h = 64 * dpi / 96;
    let footer_h = 36 * dpi / 96;
    let rows = if slots.is_empty() { 1 } else { slots.len() as i32 };

    let width = (content_w + pad * 2)
        .max(FLYOUT_W_MIN * dpi / 96)
        .min(520 * dpi / 96);
    let height = (header_h + rows * row_h + footer_h)
        .max(FLYOUT_H_MIN * dpi / 96);

    (width, height)
}

unsafe fn paint_flyout(hwnd: HWND) {
    let (dark, next_name, next_hhmm, mosque, slots, status) = STATE.with(|s| {
        let st = s.borrow();
        (
            st.dark,
            st.next_name.clone(),
            st.next_hhmm.clone(),
            st.mosque_label.clone(),
            st.slots.clone(),
            st.status.clone(),
        )
    });
    let (bg, fg, dim, accent) = theme(dark);

    let mut ps = PAINTSTRUCT::default();
    let hdc = BeginPaint(hwnd, &mut ps);
    let mut rc = RECT::default();
    let _ = GetClientRect(hwnd, &mut rc);

    let brush = CreateSolidBrush(COLORREF(bg));
    let _ = FillRect(hdc, &rc, brush);
    let _ = DeleteObject(brush);

    let border = CreateSolidBrush(COLORREF(if dark { 0x0040_4040 } else { 0x00C8_C8C8 }));
    let top = RECT {
        left: 0,
        top: 0,
        right: rc.right,
        bottom: 1,
    };
    let _ = FillRect(hdc, &top, border);
    let _ = DeleteObject(border);

    let _ = SetBkMode(hdc, TRANSPARENT);
    let font = GetStockObject(DEFAULT_GUI_FONT);
    let old = SelectObject(hdc, font);

    let dpi = GetDpiForWindow(hwnd).max(96) as i32;
    let pad = FLYOUT_PAD * dpi / 96;
    let row_h = FLYOUT_ROW_H * dpi / 96;
    let flags = DT_SINGLELINE | DT_NOPREFIX | DT_VCENTER | DT_END_ELLIPSIS;

    let _ = SetTextColor(hdc, COLORREF(fg));
    let title = flyout_title(&next_name, &next_hhmm);
    let mut title_w = wide(&title);
    let mut title_rc = RECT {
        left: pad,
        top: 10 * dpi / 96,
        right: rc.right - pad,
        bottom: 34 * dpi / 96,
    };
    let _ = DrawTextW(hdc, &mut title_w, &mut title_rc, DT_LEFT | flags);

    let _ = SetTextColor(hdc, COLORREF(dim));
    let sub = if mosque.is_empty() {
        status.clone()
    } else {
        mosque
    };
    let mut sub_w = wide(&sub);
    let mut sub_rc = RECT {
        left: pad,
        top: 34 * dpi / 96,
        right: rc.right - pad,
        bottom: 56 * dpi / 96,
    };
    let _ = DrawTextW(hdc, &mut sub_w, &mut sub_rc, DT_LEFT | flags);

    let body_top = 64 * dpi / 96;
    if slots.is_empty() {
        let _ = SetTextColor(hdc, COLORREF(dim));
        let msg = if status.is_empty() {
            "No times yet"
        } else {
            status.as_str()
        };
        let mut msg_w = wide(msg);
        let mut msg_rc = RECT {
            left: pad,
            top: body_top,
            right: rc.right - pad,
            bottom: body_top + row_h,
        };
        let _ = DrawTextW(hdc, &mut msg_w, &mut msg_rc, DT_LEFT | flags);
    } else {
        for (i, slot) in slots.iter().enumerate() {
            let is_next = slot.kind.label() == next_name.as_str();
            let _ = SetTextColor(hdc, COLORREF(if is_next { accent } else { fg }));
            let y = body_top + (i as i32) * row_h;
            let left = flyout_slot_left(slot, &next_name);
            let right = flyout_slot_right(slot);
            let mut left_w = wide(&left);
            let mut right_w = wide(&right);
            let mut left_rc = RECT {
                left: pad,
                top: y,
                right: rc.right - pad - text_width(hdc, &right) - 12,
                bottom: y + row_h,
            };
            let mut right_rc = RECT {
                left: left_rc.right + 4,
                top: y,
                right: rc.right - pad,
                bottom: y + row_h,
            };
            if left_rc.right < left_rc.left + 40 {
                left_rc.right = (rc.right - pad) / 2;
                right_rc.left = left_rc.right + 4;
            }
            let _ = DrawTextW(hdc, &mut left_w, &mut left_rc, DT_LEFT | flags);
            let _ = DrawTextW(hdc, &mut right_w, &mut right_rc, DT_RIGHT | flags);
        }
    }

    let _ = SetTextColor(hdc, COLORREF(dim));
    let mut hint_w = wide(FLYOUT_HINT);
    let mut hint_rc = RECT {
        left: pad,
        top: rc.bottom - 30 * dpi / 96,
        right: rc.right - pad,
        bottom: rc.bottom - 8 * dpi / 96,
    };
    let _ = DrawTextW(hdc, &mut hint_w, &mut hint_rc, DT_LEFT | flags);

    let _ = SelectObject(hdc, old);
    let _ = EndPaint(hwnd, &ps);
}

fn wide(s: &str) -> Vec<u16> {
    s.encode_utf16().collect()
}

fn theme(dark: bool) -> (u32, u32, u32, u32) {
    if dark {
        (
            0x0020_2020,
            0x00F3_F3F3,
            0x00A0_A0A0,
            0x00FF_CD60,
        )
    } else {
        (
            0x00F3_F3F3,
            0x001C_1C1C,
            0x0060_6060,
            0x00C0_6700,
        )
    }
}
