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
use std::collections::HashSet;
use std::mem::size_of;
use std::os::windows::process::CommandExt;
use std::path::PathBuf;
use std::process::Command;
use std::sync::atomic::{AtomicBool, AtomicIsize, Ordering};
use std::sync::Mutex;
use std::thread;
use std::time::Instant;

use chrono::Datelike;
use mawaqit_widget::config::{self, AppConfig, CacheData, MosqueSelection, NotificationPrefs};
use mawaqit_widget::mawaqit::{IqamaTimes, MawaqitClient, MosqueHit, PrayerTimes};
use mawaqit_widget::notify;
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
    BeginPaint, CreateFontW, CreateSolidBrush, DeleteObject, DrawTextW, EndPaint, FillRect, GetDC,
    GetSysColorBrush, GetTextExtentPoint32W, InvalidateRect, ReleaseDC, SelectObject, SetBkMode,
    SetTextColor,
    UpdateWindow, CLEARTYPE_QUALITY, CLIP_DEFAULT_PRECIS, DEFAULT_CHARSET, DEFAULT_PITCH,
    DT_END_ELLIPSIS, DT_LEFT, DT_NOPREFIX, DT_RIGHT, DT_SINGLELINE, DT_VCENTER, FF_DONTCARE,
    FW_BOLD, HDC, HFONT, OUT_DEFAULT_PRECIS, PAINTSTRUCT, TRANSPARENT,
};
use windows::Win32::System::LibraryLoader::GetModuleHandleW;
use windows::Win32::System::Registry::{
    RegCloseKey, RegOpenKeyExW, RegQueryValueExW, HKEY_CURRENT_USER, KEY_READ, REG_VALUE_TYPE,
};
use windows::Win32::System::Threading::CreateMutexW;
use windows::Win32::Graphics::Gdi::COLOR_3DFACE;
use windows::Win32::UI::Controls::{CheckDlgButton, IsDlgButtonChecked, BST_CHECKED, BST_UNCHECKED};
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
const FLYOUT_HINT: &str = "Click elsewhere to close · tray for menu";
const PREFS_CLASS: PCWSTR = w!("MawaqitPrefs");
const PICKER_W: i32 = 420;
const PICKER_H: i32 = 460;
const WM_APPBAR: u32 = WM_USER + 64;
const WM_TRAY: u32 = WM_USER + 65;
const WM_DATA: u32 = WM_USER + 66;
const WM_SEARCH_DONE: u32 = WM_USER + 67;
const WM_MOSQUE_PICKED: u32 = WM_USER + 68;
const WM_CLICKAWAY: u32 = WM_USER + 69;
const WM_OPEN_SETUP: u32 = WM_USER + 70;
const WM_UPDATE_DONE: u32 = WM_USER + 71;
const TIMER_TICK: usize = 1;
const TIMER_REFRESH: usize = 2;
const TIMER_SETUP: usize = 3;
const ID_TRAY: u32 = 1;
const IDM_SHOW: u16 = 1001;
const IDM_HIDE: u16 = 1002;
const IDM_QUIT: u16 = 1003;
const IDM_REFRESH: u16 = 1004;
const IDM_MOSQUE: u16 = 1005;
const IDM_PREFS: u16 = 1006;
const IDM_UPDATE: u16 = 1007;
const IDC_N_ENABLE: i32 = 3101;
const IDC_N_FAJR: i32 = 3102;
const IDC_N_DHUHR: i32 = 3103;
const IDC_N_ASR: i32 = 3104;
const IDC_N_MAGHRIB: i32 = 3105;
const IDC_N_ISHA: i32 = 3106;
const IDC_N_SHURUQ: i32 = 3107;
const IDC_N_ADHAN: i32 = 3108;
const IDC_N_IQAMA: i32 = 3109;
const IDC_N_BEFORE: i32 = 3110;
const IDC_N_MINUTES: i32 = 3111;
const IDC_N_AUTOSTART: i32 = 3112;
const IDC_N_SAVE: i32 = 3113;
const IDC_N_CANCEL: i32 = 3114;
const IDC_N_UPDATE: i32 = 3115;
const IDC_EDIT: i32 = 2001;
const IDC_LIST: i32 = 2002;
const IDC_SEARCH: i32 = 2003;
const IDC_SELECT: i32 = 2004;
const IDC_STATUS: i32 = 2005;
const IDC_CANCEL: i32 = 2006;
const IDC_MAP: i32 = 2007;
const REFRESH_EVERY_SECS: u64 = 45 * 60;

static BAR_HWND: AtomicIsize = AtomicIsize::new(0);
static FLYOUT_HWND: AtomicIsize = AtomicIsize::new(0);
static PICKER_HWND: AtomicIsize = AtomicIsize::new(0);
static PREFS_HWND: AtomicIsize = AtomicIsize::new(0);
static MOUSE_HOOK: AtomicIsize = AtomicIsize::new(0);
static FETCH_IN_FLIGHT: AtomicBool = AtomicBool::new(false);
static SEARCH_IN_FLIGHT: AtomicBool = AtomicBool::new(false);
static PREFS_FIRST_RUN: AtomicBool = AtomicBool::new(false);
static MAP_IN_FLIGHT: AtomicBool = AtomicBool::new(false);
static UPDATE_IN_FLIGHT: AtomicBool = AtomicBool::new(false);

const DETACHED_PROCESS: u32 = 0x00000008;
const CREATE_NEW_PROCESS_GROUP: u32 = 0x00000200;
const CREATE_NO_WINDOW: u32 = 0x08000000;

enum UpdateOutcome {
    UpToDate(String),
    Available {
        version: String,
        name: String,
        url: String,
    },
    Failed(String),
    Ready(PathBuf),
}

static UPDATE: Mutex<Option<UpdateOutcome>> = Mutex::new(None);

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
    notified: HashSet<String>,
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
            notified: HashSet::new(),
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
            // Delay until the AppBar message loop is alive — opening WebView2
            // immediately at startup often flashes and dies on other PCs.
            let _ = SetTimer(bar, TIMER_SETUP, 700, None);
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

    let prefs = WNDCLASSW {
        style: CS_HREDRAW | CS_VREDRAW,
        lpfnWndProc: Some(prefs_proc),
        hInstance: hinst,
        hCursor: LoadCursorW(None, IDC_ARROW).unwrap_or_default(),
        hbrBackground: GetSysColorBrush(COLOR_3DFACE),
        lpszClassName: PREFS_CLASS,
        ..Default::default()
    };
    let _ = RegisterClassW(&prefs);
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
        WINDOW_EX_STYLE(WS_EX_TOOLWINDOW.0 | WS_EX_TOPMOST.0 | WS_EX_NOACTIVATE.0),
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
        install_clickaway_hook();
    } else {
        uninstall_clickaway_hook();
        let _ = ShowWindow(flyout, SW_HIDE);
    }
    let _ = InvalidateRect(bar, None, false);
}

unsafe fn hide_flyout() {
    STATE.with(|s| s.borrow_mut().expanded = false);
    uninstall_clickaway_hook();
    let flyout = hwnd_from(FLYOUT_HWND.load(Ordering::SeqCst));
    if hwnd_ok(flyout) {
        let _ = ShowWindow(flyout, SW_HIDE);
    }
    let bar = hwnd_from(BAR_HWND.load(Ordering::SeqCst));
    if hwnd_ok(bar) {
        let _ = InvalidateRect(bar, None, false);
    }
}

fn pt_in_hwnd(hwnd: HWND, pt: POINT) -> bool {
    if !hwnd_ok(hwnd) {
        return false;
    }
    unsafe {
        let mut rc = RECT::default();
        if GetWindowRect(hwnd, &mut rc).is_err() {
            return false;
        }
        pt.x >= rc.left && pt.x < rc.right && pt.y >= rc.top && pt.y < rc.bottom
    }
}

unsafe fn install_clickaway_hook() {
    if MOUSE_HOOK.load(Ordering::SeqCst) != 0 {
        return;
    }
    let module = GetModuleHandleW(None).unwrap_or_default();
    match SetWindowsHookExW(WH_MOUSE_LL, Some(clickaway_hook), module, 0) {
        Ok(hook) => MOUSE_HOOK.store(hook.0 as isize, Ordering::SeqCst),
        Err(_) => {}
    }
}

unsafe fn uninstall_clickaway_hook() {
    let raw = MOUSE_HOOK.swap(0, Ordering::SeqCst);
    if raw != 0 {
        let _ = UnhookWindowsHookEx(HHOOK(raw as *mut _));
    }
}

unsafe extern "system" fn clickaway_hook(
    code: i32,
    wparam: WPARAM,
    lparam: LPARAM,
) -> LRESULT {
    if code >= 0 {
        let msg = wparam.0 as u32;
        if matches!(
            msg,
            WM_LBUTTONDOWN | WM_RBUTTONDOWN | WM_MBUTTONDOWN | WM_NCLBUTTONDOWN | WM_NCRBUTTONDOWN
        ) {
            let info = *(lparam.0 as *const MSLLHOOKSTRUCT);
            let flyout = hwnd_from(FLYOUT_HWND.load(Ordering::SeqCst));
            let bar = hwnd_from(BAR_HWND.load(Ordering::SeqCst));
            let prefs = hwnd_from(PREFS_HWND.load(Ordering::SeqCst));
            if !pt_in_hwnd(flyout, info.pt)
                && !pt_in_hwnd(bar, info.pt)
                && !pt_in_hwnd(prefs, info.pt)
            {
                let _ = PostMessageW(bar, WM_CLICKAWAY, WPARAM(0), LPARAM(0));
            }
        }
    }
    CallNextHookEx(
        HHOOK(MOUSE_HOOK.load(Ordering::SeqCst) as *mut _),
        code,
        wparam,
        lparam,
    )
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
    let _ = AppendMenuW(menu, MF_STRING, IDM_PREFS as usize, w!("Preferences…"));
    let _ = AppendMenuW(menu, MF_STRING, IDM_UPDATE as usize, w!("Check for updates…"));
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
        IDM_PREFS => open_prefs(false),
        IDM_UPDATE => start_update_check(hwnd, false),
        IDM_REFRESH => request_refresh(hwnd),
        IDM_QUIT => {
            PostQuitMessage(0);
        }
        _ => {}
    }
}

fn wide_z(s: &str) -> Vec<u16> {
    s.encode_utf16().chain(std::iter::once(0)).collect()
}

unsafe fn add_checkbox(parent: HWND, hinst: HINSTANCE, id: i32, text: &str, x: i32, y: i32, w: i32) {
    let title = wide_z(text);
    let _ = CreateWindowExW(
        WINDOW_EX_STYLE(0),
        w!("BUTTON"),
        PCWSTR(title.as_ptr()),
        WINDOW_STYLE(WS_CHILD.0 | WS_VISIBLE.0 | WS_TABSTOP.0 | BS_AUTOCHECKBOX as u32),
        x,
        y,
        w,
        22,
        parent,
        HMENU(id as isize as *mut _),
        hinst,
        None,
    );
}

unsafe fn set_check(parent: HWND, id: i32, on: bool) {
    let _ = CheckDlgButton(
        parent,
        id,
        if on { BST_CHECKED } else { BST_UNCHECKED },
    );
}

unsafe fn is_checked(parent: HWND, id: i32) -> bool {
    IsDlgButtonChecked(parent, id) == BST_CHECKED.0 as u32
}

unsafe fn open_prefs(first_run: bool) {
    hide_flyout();
    let existing = hwnd_from(PREFS_HWND.load(Ordering::SeqCst));
    if hwnd_ok(existing) {
        let _ = ShowWindow(existing, SW_RESTORE);
        let _ = SetForegroundWindow(existing);
        return;
    }
    PREFS_FIRST_RUN.store(first_run, Ordering::SeqCst);
    let hinst = STATE.with(|s| s.borrow().hinst);
    let hwnd = CreateWindowExW(
        WINDOW_EX_STYLE(0),
        PREFS_CLASS,
        if first_run {
            w!("Mawaqit — notification setup")
        } else {
            w!("Mawaqit — Preferences")
        },
        WINDOW_STYLE(WS_OVERLAPPED.0 | WS_CAPTION.0 | WS_SYSMENU.0 | WS_VISIBLE.0 | WS_MINIMIZEBOX.0),
        200,
        160,
        440,
        520,
        None,
        None,
        hinst,
        None,
    )
    .unwrap_or_default();
    if hwnd_ok(hwnd) {
        PREFS_HWND.store(hwnd_raw(hwnd), Ordering::SeqCst);
        let _ = SetForegroundWindow(hwnd);
    }
}

unsafe fn prefs_build_controls(hwnd: HWND) {
    let hinst = STATE.with(|s| s.borrow().hinst);
    let n = STATE.with(|s| s.borrow().config.notifications.clone());
    let autostart = STATE.with(|s| s.borrow().config.start_with_windows);

    let intro = if PREFS_FIRST_RUN.load(Ordering::SeqCst) {
        "Choose how you want to be notified. You can change this later from the bar menu."
    } else {
        "Prayer notifications appear near the clock (system tray)."
    };
    let intro_w = wide_z(intro);
    let _ = CreateWindowExW(
        WINDOW_EX_STYLE(0),
        w!("STATIC"),
        PCWSTR(intro_w.as_ptr()),
        WINDOW_STYLE(WS_CHILD.0 | WS_VISIBLE.0),
        20,
        16,
        390,
        40,
        hwnd,
        HMENU(0 as *mut _),
        hinst,
        None,
    );

    add_checkbox(hwnd, hinst, IDC_N_ENABLE, "Enable prayer notifications", 20, 62, 380);
    set_check(hwnd, IDC_N_ENABLE, n.enabled);

    let prayers = wide_z("Notify for:");
    let _ = CreateWindowExW(
        WINDOW_EX_STYLE(0),
        w!("STATIC"),
        PCWSTR(prayers.as_ptr()),
        WINDOW_STYLE(WS_CHILD.0 | WS_VISIBLE.0),
        20,
        96,
        200,
        20,
        hwnd,
        HMENU(0 as *mut _),
        hinst,
        None,
    );
    add_checkbox(hwnd, hinst, IDC_N_FAJR, "Fajr", 20, 120, 90);
    add_checkbox(hwnd, hinst, IDC_N_DHUHR, "Dhuhr", 120, 120, 90);
    add_checkbox(hwnd, hinst, IDC_N_ASR, "Asr", 220, 120, 90);
    add_checkbox(hwnd, hinst, IDC_N_MAGHRIB, "Maghrib", 20, 148, 90);
    add_checkbox(hwnd, hinst, IDC_N_ISHA, "Isha", 120, 148, 90);
    add_checkbox(hwnd, hinst, IDC_N_SHURUQ, "Shuruq", 220, 148, 90);
    set_check(hwnd, IDC_N_FAJR, n.fajr);
    set_check(hwnd, IDC_N_DHUHR, n.dhuhr);
    set_check(hwnd, IDC_N_ASR, n.asr);
    set_check(hwnd, IDC_N_MAGHRIB, n.maghrib);
    set_check(hwnd, IDC_N_ISHA, n.isha);
    set_check(hwnd, IDC_N_SHURUQ, n.shuruq);

    let when = wide_z("When:");
    let _ = CreateWindowExW(
        WINDOW_EX_STYLE(0),
        w!("STATIC"),
        PCWSTR(when.as_ptr()),
        WINDOW_STYLE(WS_CHILD.0 | WS_VISIBLE.0),
        20,
        186,
        200,
        20,
        hwnd,
        HMENU(0 as *mut _),
        hinst,
        None,
    );
    add_checkbox(hwnd, hinst, IDC_N_ADHAN, "At adhan (prayer time)", 20, 210, 380);
    add_checkbox(hwnd, hinst, IDC_N_IQAMA, "At iqama", 20, 238, 380);
    add_checkbox(hwnd, hinst, IDC_N_BEFORE, "Reminder before adhan", 20, 266, 240);
    set_check(hwnd, IDC_N_ADHAN, n.at_adhan);
    set_check(hwnd, IDC_N_IQAMA, n.at_iqama);
    set_check(hwnd, IDC_N_BEFORE, n.minutes_before > 0);

    let mins_l = wide_z("Minutes before:");
    let _ = CreateWindowExW(
        WINDOW_EX_STYLE(0),
        w!("STATIC"),
        PCWSTR(mins_l.as_ptr()),
        WINDOW_STYLE(WS_CHILD.0 | WS_VISIBLE.0),
        44,
        298,
        120,
        20,
        hwnd,
        HMENU(0 as *mut _),
        hinst,
        None,
    );
    let mins = wide_z(&if n.minutes_before == 0 {
        "10".into()
    } else {
        n.minutes_before.to_string()
    });
    let _ = CreateWindowExW(
        WINDOW_EX_STYLE(WS_EX_CLIENTEDGE.0),
        w!("EDIT"),
        PCWSTR(mins.as_ptr()),
        WINDOW_STYLE(WS_CHILD.0 | WS_VISIBLE.0 | WS_TABSTOP.0 | ES_NUMBER as u32),
        170,
        294,
        50,
        24,
        hwnd,
        HMENU(IDC_N_MINUTES as isize as *mut _),
        hinst,
        None,
    );

    add_checkbox(hwnd, hinst, IDC_N_AUTOSTART, "Start with Windows", 20, 340, 380);
    set_check(hwnd, IDC_N_AUTOSTART, autostart);

    let upd = wide_z("Check for updates…");
    let _ = CreateWindowExW(
        WINDOW_EX_STYLE(0),
        w!("BUTTON"),
        PCWSTR(upd.as_ptr()),
        WINDOW_STYLE(WS_CHILD.0 | WS_VISIBLE.0 | WS_TABSTOP.0),
        20,
        380,
        180,
        28,
        hwnd,
        HMENU(IDC_N_UPDATE as isize as *mut _),
        hinst,
        None,
    );

    let save = wide_z("Save");
    let _ = CreateWindowExW(
        WINDOW_EX_STYLE(0),
        w!("BUTTON"),
        PCWSTR(save.as_ptr()),
        WINDOW_STYLE(WS_CHILD.0 | WS_VISIBLE.0 | WS_TABSTOP.0 | BS_DEFPUSHBUTTON as u32),
        220,
        420,
        90,
        32,
        hwnd,
        HMENU(IDC_N_SAVE as isize as *mut _),
        hinst,
        None,
    );
    let cancel = wide_z("Cancel");
    let _ = CreateWindowExW(
        WINDOW_EX_STYLE(0),
        w!("BUTTON"),
        PCWSTR(cancel.as_ptr()),
        WINDOW_STYLE(WS_CHILD.0 | WS_VISIBLE.0 | WS_TABSTOP.0),
        320,
        420,
        90,
        32,
        hwnd,
        HMENU(IDC_N_CANCEL as isize as *mut _),
        hinst,
        None,
    );
}

unsafe fn prefs_save(hwnd: HWND) -> bool {
    let mut minutes: u32 = 10;
    if let Ok(edit) = GetDlgItem(hwnd, IDC_N_MINUTES) {
        let mut buf = [0u16; 16];
        let n = GetWindowTextW(edit, &mut buf);
        let text = String::from_utf16_lossy(&buf[..n as usize]);
        if let Ok(v) = text.trim().parse::<u32>() {
            minutes = v.min(120);
        }
    }
    let before_on = is_checked(hwnd, IDC_N_BEFORE);
    let prefs = NotificationPrefs {
        enabled: is_checked(hwnd, IDC_N_ENABLE),
        at_adhan: is_checked(hwnd, IDC_N_ADHAN),
        at_iqama: is_checked(hwnd, IDC_N_IQAMA),
        minutes_before: if before_on { minutes.max(1) } else { 0 },
        fajr: is_checked(hwnd, IDC_N_FAJR),
        dhuhr: is_checked(hwnd, IDC_N_DHUHR),
        asr: is_checked(hwnd, IDC_N_ASR),
        maghrib: is_checked(hwnd, IDC_N_MAGHRIB),
        isha: is_checked(hwnd, IDC_N_ISHA),
        shuruq: is_checked(hwnd, IDC_N_SHURUQ),
        setup_completed: true,
    };
    let autostart = is_checked(hwnd, IDC_N_AUTOSTART);
    let mut cfg = STATE.with(|s| s.borrow().config.clone());
    cfg.notifications = prefs;
    cfg.start_with_windows = autostart;
    if let Err(e) = config::save_config(&cfg) {
        let msg = wide_z(&format!("Could not save: {e}"));
        let _ = MessageBoxW(hwnd, PCWSTR(msg.as_ptr()), w!("Mawaqit"), MB_OK | MB_ICONERROR);
        return false;
    }
    let _ = mawaqit_widget::autostart::set_enabled(autostart);
    STATE.with(|s| s.borrow_mut().config = cfg);
    true
}

unsafe fn prefs_mark_setup_done() {
    let mut cfg = STATE.with(|s| s.borrow().config.clone());
    if cfg.notifications.setup_completed {
        return;
    }
    cfg.notifications.setup_completed = true;
    let _ = config::save_config(&cfg);
    STATE.with(|s| s.borrow_mut().config = cfg);
}

unsafe extern "system" fn prefs_proc(
    hwnd: HWND,
    msg: u32,
    wparam: WPARAM,
    lparam: LPARAM,
) -> LRESULT {
    match msg {
        WM_CREATE => {
            prefs_build_controls(hwnd);
            LRESULT(0)
        }
        WM_COMMAND => {
            let id = wparam.0 as u16 as i32;
            match id {
                IDC_N_SAVE => {
                    if prefs_save(hwnd) {
                        let bar = hwnd_from(BAR_HWND.load(Ordering::SeqCst));
                        if hwnd_ok(bar) {
                            tray_balloon(
                                bar,
                                "Preferences saved",
                                "Prayer notifications will follow your settings.",
                            );
                        }
                        let _ = DestroyWindow(hwnd);
                    }
                    LRESULT(0)
                }
                IDC_N_CANCEL => {
                    if PREFS_FIRST_RUN.load(Ordering::SeqCst) {
                        prefs_mark_setup_done();
                    }
                    let _ = DestroyWindow(hwnd);
                    LRESULT(0)
                }
                IDC_N_UPDATE => {
                    start_update_check(hwnd, true);
                    LRESULT(0)
                }
                _ => DefWindowProcW(hwnd, msg, wparam, lparam),
            }
        }
        WM_CLOSE => {
            if PREFS_FIRST_RUN.load(Ordering::SeqCst) {
                prefs_mark_setup_done();
            }
            let _ = DestroyWindow(hwnd);
            LRESULT(0)
        }
        WM_DESTROY => {
            PREFS_HWND.store(0, Ordering::SeqCst);
            PREFS_FIRST_RUN.store(false, Ordering::SeqCst);
            LRESULT(0)
        }
        _ => DefWindowProcW(hwnd, msg, wparam, lparam),
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
            maybe_notify(hwnd);
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
        WM_TIMER if wparam.0 == TIMER_SETUP => {
            let _ = KillTimer(hwnd, TIMER_SETUP);
            open_picker();
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
                let need_setup = STATE.with(|s| !s.borrow().config.notifications.setup_completed);
                if need_setup {
                    open_prefs(true);
                }
            }
            LRESULT(0)
        }
        WM_CLICKAWAY => {
            hide_flyout();
            LRESULT(0)
        }
        WM_OPEN_SETUP => {
            open_picker();
            LRESULT(0)
        }
        WM_UPDATE_DONE => {
            handle_update_done(hwnd);
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
            uninstall_clickaway_hook();
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

fn maybe_notify(hwnd: HWND) {
    let (prefs, slots, date) = STATE.with(|s| {
        let st = s.borrow();
        let date = st
            .schedule
            .as_ref()
            .map(|sch| sch.date)
            .unwrap_or_else(today_local);
        (
            st.config.notifications.clone(),
            st.slots.clone(),
            date,
        )
    });
    if slots.is_empty() {
        return;
    }
    let today = date.to_string();
    STATE.with(|s| {
        s.borrow_mut()
            .notified
            .retain(|k| k.starts_with(&today));
    });
    let already = STATE.with(|s| s.borrow().notified.clone());
    let alerts = notify::pending_alerts(date, &slots, now_local(), &prefs, &already);
    if alerts.is_empty() {
        return;
    }
    STATE.with(|s| {
        let mut st = s.borrow_mut();
        for a in &alerts {
            st.notified.insert(a.key.clone());
        }
    });
    unsafe {
        for a in alerts {
            tray_balloon(hwnd, &a.title, &a.body);
            // One balloon at a time — later alerts in the same tick wait for next seconds.
            break;
        }
    }
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
    unsafe {
        hide_flyout();
        open_native_picker();
    }
}

unsafe fn open_native_picker() {
    let existing = hwnd_from(PICKER_HWND.load(Ordering::SeqCst));
    if hwnd_ok(existing) {
        let _ = ShowWindow(existing, SW_RESTORE);
        let _ = SetForegroundWindow(existing);
        return;
    }
    let hinst = STATE.with(|s| s.borrow().hinst);
    let hwnd = CreateWindowExW(
        WINDOW_EX_STYLE(WS_EX_APPWINDOW.0),
        PICKER_CLASS,
        w!("Mawaqit — Choose mosque"),
        WINDOW_STYLE(
            WS_OVERLAPPED.0 | WS_CAPTION.0 | WS_SYSMENU.0 | WS_VISIBLE.0 | WS_MINIMIZEBOX.0,
        ),
        180,
        120,
        540,
        560,
        None,
        None,
        hinst,
        None,
    )
    .unwrap_or_default();
    if hwnd_ok(hwnd) {
        PICKER_HWND.store(hwnd_raw(hwnd), Ordering::SeqCst);
        let _ = SetForegroundWindow(hwnd);
    } else {
        PICKER_HWND.store(0, Ordering::SeqCst);
        let bar = hwnd_from(BAR_HWND.load(Ordering::SeqCst));
        tray_balloon(
            bar,
            "Could not open mosque setup",
            "Try again from the tray menu: Change mosque…",
        );
    }
}

unsafe fn picker_build_controls(hwnd: HWND) {
    let hinst = STATE.with(|s| s.borrow().hinst);
    let hint = wide_z(
        "Paste a mawaqit.net mosque link, or type a name/city and Search. The map is optional.",
    );
    let _ = CreateWindowExW(
        WINDOW_EX_STYLE(0),
        w!("STATIC"),
        PCWSTR(hint.as_ptr()),
        WINDOW_STYLE(WS_CHILD.0 | WS_VISIBLE.0),
        16,
        12,
        500,
        36,
        hwnd,
        HMENU(0 as *mut _),
        hinst,
        None,
    );
    let _ = CreateWindowExW(
        WINDOW_EX_STYLE(WS_EX_CLIENTEDGE.0),
        w!("EDIT"),
        w!(""),
        WINDOW_STYLE(WS_CHILD.0 | WS_VISIBLE.0 | WS_TABSTOP.0 | ES_AUTOHSCROLL as u32),
        16,
        54,
        380,
        26,
        hwnd,
        HMENU(IDC_EDIT as isize as *mut _),
        hinst,
        None,
    );
    let search = wide_z("Search");
    let _ = CreateWindowExW(
        WINDOW_EX_STYLE(0),
        w!("BUTTON"),
        PCWSTR(search.as_ptr()),
        WINDOW_STYLE(WS_CHILD.0 | WS_VISIBLE.0 | WS_TABSTOP.0 | BS_DEFPUSHBUTTON as u32),
        406,
        52,
        100,
        28,
        hwnd,
        HMENU(IDC_SEARCH as isize as *mut _),
        hinst,
        None,
    );
    let _ = CreateWindowExW(
        WINDOW_EX_STYLE(WS_EX_CLIENTEDGE.0),
        w!("LISTBOX"),
        w!(""),
        WINDOW_STYLE(WS_CHILD.0 | WS_VISIBLE.0 | WS_VSCROLL.0 | WS_TABSTOP.0 | LBS_NOTIFY as u32),
        16,
        92,
        490,
        300,
        hwnd,
        HMENU(IDC_LIST as isize as *mut _),
        hinst,
        None,
    );
    let status = wide_z("Waiting for a mosque…");
    let _ = CreateWindowExW(
        WINDOW_EX_STYLE(0),
        w!("STATIC"),
        PCWSTR(status.as_ptr()),
        WINDOW_STYLE(WS_CHILD.0 | WS_VISIBLE.0),
        16,
        400,
        490,
        22,
        hwnd,
        HMENU(IDC_STATUS as isize as *mut _),
        hinst,
        None,
    );
    let map = wide_z("Open map…");
    let _ = CreateWindowExW(
        WINDOW_EX_STYLE(0),
        w!("BUTTON"),
        PCWSTR(map.as_ptr()),
        WINDOW_STYLE(WS_CHILD.0 | WS_VISIBLE.0 | WS_TABSTOP.0),
        16,
        432,
        110,
        32,
        hwnd,
        HMENU(IDC_MAP as isize as *mut _),
        hinst,
        None,
    );
    let use_sel = wide_z("Use selected mosque");
    let _ = CreateWindowExW(
        WINDOW_EX_STYLE(0),
        w!("BUTTON"),
        PCWSTR(use_sel.as_ptr()),
        WINDOW_STYLE(WS_CHILD.0 | WS_VISIBLE.0 | WS_TABSTOP.0),
        136,
        432,
        170,
        32,
        hwnd,
        HMENU(IDC_SELECT as isize as *mut _),
        hinst,
        None,
    );
    let cancel = wide_z("Cancel");
    let _ = CreateWindowExW(
        WINDOW_EX_STYLE(0),
        w!("BUTTON"),
        PCWSTR(cancel.as_ptr()),
        WINDOW_STYLE(WS_CHILD.0 | WS_VISIBLE.0 | WS_TABSTOP.0),
        396,
        432,
        110,
        32,
        hwnd,
        HMENU(IDC_CANCEL as isize as *mut _),
        hinst,
        None,
    );
}

unsafe fn offer_webview2_install(err: &str) -> bool {
    let installed = mawaqit_widget::webview2_rt::is_installed();
    let body = if installed {
        format!(
            "{err}\n\nWebView2 looks installed but failed to start.\nInstall/repair it now, then retry the map?"
        )
    } else {
        format!(
            "{err}\n\nThe map needs Microsoft Edge WebView2 (missing on this PC).\nDownload and install it now? (about a minute; mosque search still works without it.)"
        )
    };
    let msg = wide_z(&body);
    let ans = MessageBoxW(
        HWND::default(),
        PCWSTR(msg.as_ptr()),
        w!("Mawaqit map"),
        MB_YESNO | MB_ICONWARNING,
    );
    if ans != IDYES {
        return false;
    }
    let wait = wide_z("Installing WebView2… this can take a minute.");
    let _ = MessageBoxW(
        HWND::default(),
        PCWSTR(wait.as_ptr()),
        w!("Mawaqit"),
        MB_OK | MB_ICONINFORMATION,
    );
    match mawaqit_widget::webview2_rt::install_silent() {
        Ok(()) => true,
        Err(e) => {
            let fail = wide_z(&format!(
                "{e}\n\nOpen Microsoft’s WebView2 page in your browser instead?"
            ));
            let go = MessageBoxW(
                HWND::default(),
                PCWSTR(fail.as_ptr()),
                w!("Mawaqit map"),
                MB_YESNO | MB_ICONWARNING,
            );
            if go == IDYES {
                let _ = Command::new("cmd")
                    .args([
                        "/C",
                        "start",
                        "",
                        mawaqit_widget::webview2_rt::BOOTSTRAPPER_URL,
                    ])
                    .creation_flags(CREATE_NO_WINDOW)
                    .spawn();
            }
            false
        }
    }
}

fn pick_mosque_on_map() -> Option<MosqueHit> {
    mawaqit_widget::webview2_rt::prepare();
    if !mawaqit_widget::webview2_rt::is_usable() {
        let _ = unsafe {
            offer_webview2_install(
                "The in-app map needs Microsoft Edge WebView2, which is missing on this PC.",
            )
        };
        mawaqit_widget::webview2_rt::prepare();
    }

    let (tx, rx) = std::sync::mpsc::sync_channel(1);
    match mawaqit_widget::map_picker::run(move |hit| {
        let _ = tx.send(hit);
    }) {
        Ok(()) => return rx.try_recv().ok().flatten(),
        Err(_) => {}
    }

    // Windows 11 can report WebView2 as installed and still fail with
    // HRESULT 0x80070002 (“Package dependency criteria could not be resolved”).
    // Same map in the default browser still lets people search and pick.
    let (tx2, rx2) = std::sync::mpsc::sync_channel(1);
    match mawaqit_widget::map_picker::run_in_browser(move |hit| {
        let _ = tx2.send(hit);
    }) {
        Ok(()) => rx2.try_recv().ok().flatten(),
        Err(e) => {
            unsafe {
                let msg = wide_z(&format!(
                    "{e}\n\nYou can still search by name or paste a mawaqit.net link in the setup window."
                ));
                let _ = MessageBoxW(
                    HWND::default(),
                    PCWSTR(msg.as_ptr()),
                    w!("Mawaqit map"),
                    MB_OK | MB_ICONWARNING,
                );
            }
            None
        }
    }
}

fn open_map_picker() {
    if MAP_IN_FLIGHT.swap(true, Ordering::SeqCst) {
        return;
    }
    unsafe {
        hide_flyout();
        let native = hwnd_from(PICKER_HWND.load(Ordering::SeqCst));
        if hwnd_ok(native) {
            let _ = ShowWindow(native, SW_HIDE);
        }
    }
    let bar = BAR_HWND.load(Ordering::SeqCst);
    thread::spawn(move || {
        #[cfg(target_os = "windows")]
        {
            use windows::Win32::System::Com::{CoInitializeEx, COINIT_APARTMENTTHREADED};
            unsafe {
                let _ = CoInitializeEx(None, COINIT_APARTMENTTHREADED);
            }
        }
        let result = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
            pick_mosque_on_map()
        }));
        MAP_IN_FLIGHT.store(false, Ordering::SeqCst);
        match result {
            Ok(Some(hit)) => {
                let mut cfg = config::load_config().unwrap_or_default();
                cfg.mosque = Some(MosqueSelection {
                    masjid_id: hit.slug,
                    display_name: hit.label,
                });
                if config::save_config(&cfg).is_ok() {
                    unsafe {
                        let native = hwnd_from(PICKER_HWND.load(Ordering::SeqCst));
                        if hwnd_ok(native) {
                            let _ = DestroyWindow(native);
                        }
                        let _ = PostMessageW(
                            hwnd_from(bar),
                            WM_MOSQUE_PICKED,
                            WPARAM(0),
                            LPARAM(0),
                        );
                    }
                }
            }
            _ => unsafe {
                let native = hwnd_from(PICKER_HWND.load(Ordering::SeqCst));
                if hwnd_ok(native) {
                    let _ = ShowWindow(native, SW_SHOW);
                    let _ = SetForegroundWindow(native);
                } else {
                    let _ = PostMessageW(hwnd_from(bar), WM_OPEN_SETUP, WPARAM(0), LPARAM(0));
                }
            },
        }
    });
}

fn start_update_check(hwnd: HWND, _from_prefs: bool) {
    if UPDATE_IN_FLIGHT.swap(true, Ordering::SeqCst) {
        unsafe {
            let msg = wide_z("Already checking for updates…");
            let _ = MessageBoxW(hwnd, PCWSTR(msg.as_ptr()), w!("Mawaqit"), MB_OK);
        }
        return;
    }
    unsafe {
        tray_balloon(
            hwnd_from(BAR_HWND.load(Ordering::SeqCst)),
            "Checking for updates",
            "Looking up the latest GitHub release…",
        );
    }
    let hwnd_raw = hwnd_raw(hwnd);
    thread::spawn(move || {
        let outcome = match mawaqit_widget::update::fetch_latest() {
            Ok(rel) => {
                if mawaqit_widget::update::is_newer(&rel.version, mawaqit_widget::update::current_version())
                {
                    UpdateOutcome::Available {
                        version: rel.version,
                        name: rel.setup_name,
                        url: rel.setup_url,
                    }
                } else {
                    UpdateOutcome::UpToDate(rel.version)
                }
            }
            Err(e) => UpdateOutcome::Failed(e.to_string()),
        };
        {
            let mut slot = UPDATE.lock().unwrap_or_else(|e| e.into_inner());
            *slot = Some(outcome);
        }
        UPDATE_IN_FLIGHT.store(false, Ordering::SeqCst);
        unsafe {
            let _ = PostMessageW(hwnd_from(hwnd_raw), WM_UPDATE_DONE, WPARAM(0), LPARAM(0));
        }
    });
}

fn start_update_download(hwnd: HWND, url: String, name: String) {
    if UPDATE_IN_FLIGHT.swap(true, Ordering::SeqCst) {
        return;
    }
    unsafe {
        tray_balloon(
            hwnd,
            "Downloading update",
            "The installer will run when the download finishes.",
        );
    }
    let hwnd_raw = hwnd_raw(hwnd);
    thread::spawn(move || {
        let dest = mawaqit_widget::update::staging_setup_path(&name);
        let outcome = match mawaqit_widget::update::download_setup(&url, &dest) {
            Ok(()) => UpdateOutcome::Ready(dest),
            Err(e) => UpdateOutcome::Failed(format!("Download failed: {e}")),
        };
        {
            let mut slot = UPDATE.lock().unwrap_or_else(|e| e.into_inner());
            *slot = Some(outcome);
        }
        UPDATE_IN_FLIGHT.store(false, Ordering::SeqCst);
        unsafe {
            let _ = PostMessageW(hwnd_from(hwnd_raw), WM_UPDATE_DONE, WPARAM(0), LPARAM(0));
        }
    });
}

unsafe fn apply_downloaded_update(hwnd: HWND, setup: PathBuf) {
    match mawaqit_widget::update::write_apply_script(&setup) {
        Ok(script) => {
            let _ = Command::new("cmd")
                .args(["/C", &script.to_string_lossy()])
                .creation_flags(DETACHED_PROCESS | CREATE_NEW_PROCESS_GROUP | CREATE_NO_WINDOW)
                .spawn();
            PostQuitMessage(0);
        }
        Err(e) => {
            let msg = wide_z(&format!("Could not start updater: {e}"));
            let _ = MessageBoxW(hwnd, PCWSTR(msg.as_ptr()), w!("Mawaqit"), MB_OK | MB_ICONERROR);
        }
    }
}

unsafe fn handle_update_done(hwnd: HWND) {
    let taken = {
        let mut slot = UPDATE.lock().unwrap_or_else(|e| e.into_inner());
        slot.take()
    };
    let Some(outcome) = taken else {
        return;
    };
    match outcome {
        UpdateOutcome::UpToDate(v) => {
            let msg = wide_z(&format!(
                "You are on the latest version ({v})."
            ));
            let _ = MessageBoxW(hwnd, PCWSTR(msg.as_ptr()), w!("Mawaqit"), MB_OK | MB_ICONINFORMATION);
        }
        UpdateOutcome::Available { version, name, url } => {
            let msg = wide_z(&format!(
                "Version {version} is available (you have {}).\n\nDownload and install now?",
                mawaqit_widget::update::current_version()
            ));
            let ans = MessageBoxW(
                hwnd,
                PCWSTR(msg.as_ptr()),
                w!("Mawaqit update"),
                MB_YESNO | MB_ICONQUESTION,
            );
            if ans == IDYES {
                start_update_download(hwnd, url, name);
            }
        }
        UpdateOutcome::Failed(e) => {
            let msg = wide_z(&e);
            let _ = MessageBoxW(hwnd, PCWSTR(msg.as_ptr()), w!("Mawaqit"), MB_OK | MB_ICONWARNING);
        }
        UpdateOutcome::Ready(path) => {
            let msg = wide_z(
                "Update downloaded. Mawaqit will close, install the new version, then reopen.",
            );
            let _ = MessageBoxW(
                hwnd,
                PCWSTR(msg.as_ptr()),
                w!("Mawaqit update"),
                MB_OK | MB_ICONINFORMATION,
            );
            apply_downloaded_update(hwnd, path);
        }
    }
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
        WM_CREATE => {
            picker_build_controls(hwnd);
            LRESULT(0)
        }
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
                IDC_MAP => {
                    open_map_picker();
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
    let dpi = GetDpiForWindow(hwnd).max(96);
    let font = create_ui_font(dpi);
    let old = SelectObject(hdc, font);
    let pad = 14 * dpi as i32 / 96;
    let gap = 16 * dpi as i32 / 96;
    let flags = DT_SINGLELINE | DT_NOPREFIX | DT_VCENTER;

    // Far-right mosque name: measure full width and right-align so it isn't clipped.
    let name = muted.trim();
    let name_w = if name.is_empty() {
        0
    } else {
        text_width(hdc, name)
    };
    let name_left = if name_w > 0 {
        (rc.right - pad - name_w).max(pad)
    } else {
        rc.right - pad
    };

    let brand_w = text_width(hdc, &brand);
    let brand_right = pad + brand_w;
    let chip_left = brand_right + gap;
    let chip_right = if name_w > 0 {
        (name_left - gap).max(chip_left + 40)
    } else {
        rc.right - pad
    };

    let mut brand_buf = wide(&brand);
    let mut brand_rc = RECT {
        left: pad,
        top: 0,
        right: brand_right + 2,
        bottom: rc.bottom,
    };
    let _ = SetTextColor(hdc, COLORREF(fg));
    let _ = DrawTextW(hdc, &mut brand_buf, &mut brand_rc, DT_LEFT | flags);

    if chip_right > chip_left {
        let mut chip_buf = wide(&chip);
        let mut chip_rc = RECT {
            left: chip_left,
            top: 0,
            right: chip_right,
            bottom: rc.bottom,
        };
        let _ = SetTextColor(hdc, COLORREF(if expanded { accent } else { accent }));
        let _ = DrawTextW(
            hdc,
            &mut chip_buf,
            &mut chip_rc,
            DT_LEFT | flags | DT_END_ELLIPSIS,
        );
    }

    if name_w > 0 {
        let mut name_buf = wide(name);
        let mut name_rc = RECT {
            left: name_left,
            top: 0,
            right: rc.right - pad,
            bottom: rc.bottom,
        };
        let _ = SetTextColor(hdc, COLORREF(dim));
        let _ = DrawTextW(hdc, &mut name_buf, &mut name_rc, DT_RIGHT | flags);
    }

    let _ = SelectObject(hdc, old);
    let _ = DeleteObject(font);
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

    let dpi_u = GetDpiForWindow(hwnd).max(96);
    let hdc = GetDC(hwnd);
    let font = create_ui_font(dpi_u);
    let old = SelectObject(hdc, font);

    let title = flyout_title(&next_name, &next_hhmm);
    let sub = if mosque.is_empty() {
        status.clone()
    } else {
        mosque
    };

    let mut content_w = text_width(hdc, &title)
        .max(text_width(hdc, FLYOUT_HINT))
        .max(text_width(hdc, &sub).min(480));

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
    let _ = DeleteObject(font);
    let _ = ReleaseDC(hwnd, hdc);

    let dpi = dpi_u as i32;
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
    let dpi_u = GetDpiForWindow(hwnd).max(96);
    let font = create_ui_font(dpi_u);
    let old = SelectObject(hdc, font);

    let dpi = dpi_u as i32;
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
    let _ = DeleteObject(font);
    let _ = EndPaint(hwnd, &ps);
}

fn wide(s: &str) -> Vec<u16> {
    s.encode_utf16().collect()
}

/// Bold Segoe UI sized for the given window DPI.
unsafe fn create_ui_font(dpi: u32) -> HFONT {
    let height = -((13 * dpi as i32) / 96);
    CreateFontW(
        height,
        0,
        0,
        0,
        FW_BOLD.0 as i32,
        0,
        0,
        0,
        DEFAULT_CHARSET.0 as u32,
        OUT_DEFAULT_PRECIS.0 as u32,
        CLIP_DEFAULT_PRECIS.0 as u32,
        CLEARTYPE_QUALITY.0 as u32,
        DEFAULT_PITCH.0 as u32 | FF_DONTCARE.0 as u32,
        w!("Segoe UI"),
    )
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
