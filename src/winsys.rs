//! Win32 plumbing: AppBar, window styles, theme registry, input, time zones.
#![allow(clippy::missing_safety_doc)]
use std::ffi::c_void;
use windows::core::{BOOL, PCWSTR};
use windows::Win32::Foundation::*;
use windows::Win32::Graphics::Dwm::*;
use windows::Win32::Graphics::Gdi::*;
use windows::Win32::System::Registry::*;
use windows::Win32::System::SystemInformation::*;
use windows::Win32::System::Threading::*;
use windows::Win32::System::Time::*;
use windows::Win32::UI::Input::KeyboardAndMouse::*;
use windows::Win32::UI::Shell::*;
use windows::Win32::UI::WindowsAndMessaging::*;

pub fn wide(s: &str) -> Vec<u16> {
    s.encode_utf16().chain(std::iter::once(0)).collect()
}

pub fn from_wide(w: &[u16]) -> String {
    let n = w.iter().position(|&c| c == 0).unwrap_or(w.len());
    String::from_utf16_lossy(&w[..n])
}

#[derive(Clone, Copy, PartialEq, Eq, Debug, Default)]
pub struct Hwnd(pub isize);
impl Hwnd {
    pub fn h(self) -> HWND {
        HWND(self.0 as *mut c_void)
    }
    pub fn is_null(self) -> bool {
        self.0 == 0
    }
}

// ───────────────────────── time ─────────────────────────

pub fn local_time_string() -> String {
    let t = unsafe { GetLocalTime() };
    format!(
        "{:04}-{:02}-{:02} {:02}:{:02}:{:02}.{:03}",
        t.wYear, t.wMonth, t.wDay, t.wHour, t.wMinute, t.wSecond, t.wMilliseconds
    )
}

/// .NET `DateTime.Now.Ticks` (local wall clock, 100ns since 0001-01-01).
pub fn now_ticks() -> i64 {
    unsafe {
        let st = GetLocalTime();
        let mut ft = FILETIME::default();
        let _ = SystemTimeToFileTime(&st, &mut ft);
        let v = ((ft.dwHighDateTime as i64) << 32) | ft.dwLowDateTime as i64;
        v + 504_911_232_000_000_000
    }
}

pub const TICKS_PER_SEC: i64 = 10_000_000;

// ───────────────────────── windows ─────────────────────────

struct FindCtx {
    pid: u32,
    title: Vec<u16>,
    found: isize,
}

unsafe extern "system" fn enum_proc(hwnd: HWND, lp: LPARAM) -> BOOL {
    let ctx = &mut *(lp.0 as *mut FindCtx);
    let mut pid = 0u32;
    GetWindowThreadProcessId(hwnd, Some(&mut pid));
    if pid == ctx.pid {
        let mut buf = [0u16; 256];
        let n = GetWindowTextW(hwnd, &mut buf) as usize;
        if buf[..n] == ctx.title[..ctx.title.len() - 1] {
            ctx.found = hwnd.0 as isize;
            return BOOL(0);
        }
    }
    BOOL(1)
}

/// Locate one of *our* top-level windows by exact title.
pub fn find_own_window(title: &str) -> Hwnd {
    let mut ctx = FindCtx { pid: unsafe { GetCurrentProcessId() }, title: wide(title), found: 0 };
    unsafe {
        let _ = EnumWindows(Some(enum_proc), LPARAM(&mut ctx as *mut _ as isize));
    }
    Hwnd(ctx.found)
}

pub fn own_pid() -> u32 {
    unsafe { GetCurrentProcessId() }
}

pub fn set_ex_style(h: Hwnd, add: u32, remove: u32) {
    unsafe {
        let s = GetWindowLongPtrW(h.h(), GWL_EXSTYLE) as u32;
        let n = (s | add) & !remove;
        if n != s {
            SetWindowLongPtrW(h.h(), GWL_EXSTYLE, n as isize);
        }
    }
}

pub fn make_noactivate_tool(h: Hwnd) {
    set_ex_style(h, (WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST).0, WS_EX_APPWINDOW.0);
}

pub fn set_noactivate(h: Hwnd, on: bool) {
    if on {
        set_ex_style(h, WS_EX_NOACTIVATE.0, 0)
    } else {
        set_ex_style(h, 0, WS_EX_NOACTIVATE.0)
    }
}

pub fn set_topmost(h: Hwnd, top: bool) {
    unsafe {
        let _ = SetWindowPos(
            h.h(),
            Some(if top { HWND_TOPMOST } else { HWND_NOTOPMOST }),
            0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE,
        );
    }
}

pub fn force_foreground(h: Hwnd) {
    unsafe {
        let _ = SetForegroundWindow(h.h());
    }
}

pub fn foreground() -> Hwnd {
    Hwnd(unsafe { GetForegroundWindow() }.0 as isize)
}

pub fn show_window(h: Hwnd, show: bool) {
    unsafe {
        let _ = ShowWindow(h.h(), if show { SW_SHOWNOACTIVATE } else { SW_HIDE });
    }
}

pub fn move_window(h: Hwnd, x: i32, y: i32, w: i32, hh: i32, topmost: bool) {
    unsafe {
        let _ = SetWindowPos(
            h.h(),
            Some(if topmost { HWND_TOPMOST } else { HWND_TOP }),
            x, y, w, hh,
            SWP_NOACTIVATE,
        );
    }
}

/// Win11 rounded corners + dark/light titlebar for popup windows.
pub fn set_corner_round(h: Hwnd, round: bool) {
    unsafe {
        let v: i32 = if round { 2 } else { 1 };
        let _ = DwmSetWindowAttribute(h.h(), DWMWINDOWATTRIBUTE(33), &v as *const _ as *const c_void, 4);
    }
}

pub fn apply_window_theme(h: Hwnd, light: bool) {
    unsafe {
        let v: i32 = if light { 0 } else { 1 };
        let _ = DwmSetWindowAttribute(h.h(), DWMWINDOWATTRIBUTE(20), &v as *const _ as *const c_void, 4);
        let _ = DwmSetWindowAttribute(h.h(), DWMWINDOWATTRIBUTE(19), &v as *const _ as *const c_void, 4);
    }
}

pub fn clear_region(h: Hwnd) {
    unsafe {
        SetWindowRgn(h.h(), None, true);
    }
}

pub fn cursor_pos() -> (i32, i32) {
    let mut p = POINT::default();
    unsafe {
        let _ = GetCursorPos(&mut p);
    }
    (p.x, p.y)
}

pub fn key_down(vk: u16) -> bool {
    unsafe { (GetAsyncKeyState(vk as i32) as u16 & 0x8000) != 0 }
}
pub fn shift_down() -> bool {
    key_down(VK_SHIFT.0)
}
pub fn ctrl_down() -> bool {
    key_down(VK_CONTROL.0)
}

/// Ctrl+V via SendInput (what WinForms SendKeys.SendWait("^v") emits).
pub fn send_ctrl_v() {
    unsafe {
        let k = |vk: VIRTUAL_KEY, up: bool| INPUT {
            r#type: INPUT_KEYBOARD,
            Anonymous: INPUT_0 {
                ki: KEYBDINPUT {
                    wVk: vk,
                    wScan: 0,
                    dwFlags: if up { KEYEVENTF_KEYUP } else { KEYBD_EVENT_FLAGS(0) },
                    time: 0,
                    dwExtraInfo: 0,
                },
            },
        };
        let inputs = [k(VK_CONTROL, false), k(VK_V, false), k(VK_V, true), k(VK_CONTROL, true)];
        SendInput(&inputs, std::mem::size_of::<INPUT>() as i32);
    }
}

pub fn shell_open(target: &str) {
    let w = wide(target);
    let op = wide("open");
    unsafe {
        ShellExecuteW(None, PCWSTR(op.as_ptr()), PCWSTR(w.as_ptr()), PCWSTR::null(), PCWSTR::null(), SW_SHOWNORMAL);
    }
}

// ───────────────────────── monitors ─────────────────────────

#[derive(Clone, Copy, Debug, Default)]
pub struct IRect {
    pub l: i32,
    pub t: i32,
    pub r: i32,
    pub b: i32,
}
impl IRect {
    pub fn contains(&self, x: i32, y: i32) -> bool {
        x >= self.l && x < self.r && y >= self.t && y < self.b
    }
    fn from(r: RECT) -> Self {
        Self { l: r.left, t: r.top, r: r.right, b: r.bottom }
    }
}

pub fn monitor_of_window(h: Hwnd) -> (IRect, IRect) {
    unsafe {
        let m = MonitorFromWindow(h.h(), MONITOR_DEFAULTTONEAREST);
        monitor_info(m)
    }
}

pub fn monitor_of_point(x: i32, y: i32) -> (IRect, IRect) {
    unsafe {
        let m = MonitorFromPoint(POINT { x, y }, MONITOR_DEFAULTTONEAREST);
        monitor_info(m)
    }
}

unsafe fn monitor_info(m: HMONITOR) -> (IRect, IRect) {
    let mut mi = MONITORINFO { cbSize: std::mem::size_of::<MONITORINFO>() as u32, ..Default::default() };
    if GetMonitorInfoW(m, &mut mi).as_bool() {
        (IRect::from(mi.rcMonitor), IRect::from(mi.rcWork))
    } else {
        let w = GetSystemMetrics(SM_CXSCREEN);
        let h = GetSystemMetrics(SM_CYSCREEN);
        let r = IRect { l: 0, t: 0, r: w, b: h };
        (r, r)
    }
}

pub fn window_rect(h: Hwnd) -> IRect {
    let mut r = RECT::default();
    unsafe {
        let _ = GetWindowRect(h.h(), &mut r);
    }
    IRect::from(r)
}

pub fn dpi_of_window(h: Hwnd) -> f32 {
    let d = unsafe { windows::Win32::UI::HiDpi::GetDpiForWindow(h.h()) };
    if d == 0 { 1.0 } else { d as f32 / 96.0 }
}

pub fn dpi_of_point(x: i32, y: i32) -> f32 {
    unsafe {
        let m = MonitorFromPoint(POINT { x, y }, MONITOR_DEFAULTTONEAREST);
        let (mut dx, mut dy) = (96u32, 96u32);
        let _ = windows::Win32::UI::HiDpi::GetDpiForMonitor(m, windows::Win32::UI::HiDpi::MDT_EFFECTIVE_DPI, &mut dx, &mut dy);
        dx as f32 / 96.0
    }
}

// ───────────────────────── AppBar ─────────────────────────

pub const APPBAR_CALLBACK: u32 = WM_APP + 0x55;

fn abd(h: Hwnd) -> APPBARDATA {
    APPBARDATA {
        cbSize: std::mem::size_of::<APPBARDATA>() as u32,
        hWnd: h.h(),
        uCallbackMessage: APPBAR_CALLBACK,
        ..Default::default()
    }
}

pub fn appbar_register(h: Hwnd) {
    unsafe {
        let mut a = abd(h);
        SHAppBarMessage(ABM_NEW, &mut a);
    }
}

pub fn appbar_unregister(h: Hwnd) {
    unsafe {
        let mut a = abd(h);
        SHAppBarMessage(ABM_REMOVE, &mut a);
    }
}

/// SetAppBarPos(): reserve `height_px` at top, or directly above the taskbar
/// at the bottom. Returns the final physical rect.
pub fn appbar_set_pos(h: Hwnd, top: bool, height_px: i32, visible: bool) -> IRect {
    unsafe {
        let (mon, _) = monitor_of_window(h);
        let mut taskbar_top = -1;
        if !top {
            let cls = wide("Shell_TrayWnd");
            if let Ok(tb) = FindWindowW(PCWSTR(cls.as_ptr()), PCWSTR::null()) {
                let mut r = RECT::default();
                if GetWindowRect(tb, &mut r).is_ok() {
                    taskbar_top = r.top;
                }
            }
        }
        let mut a = abd(h);
        a.uEdge = if top { ABE_TOP } else { ABE_BOTTOM };
        a.rc.left = mon.l;
        a.rc.right = mon.r;
        if top {
            a.rc.top = mon.t;
            a.rc.bottom = mon.t + height_px;
        } else if taskbar_top > 0 && taskbar_top > mon.t && taskbar_top <= mon.b {
            a.rc.bottom = taskbar_top;
            a.rc.top = taskbar_top - height_px;
        } else {
            a.rc.bottom = mon.b;
            a.rc.top = mon.b - height_px;
        }
        SHAppBarMessage(ABM_QUERYPOS, &mut a);
        if a.rc.bottom - a.rc.top < height_px {
            if top {
                a.rc.bottom = a.rc.top + height_px;
            } else {
                a.rc.top = a.rc.bottom - height_px;
            }
        }
        SHAppBarMessage(ABM_SETPOS, &mut a);
        let r = IRect::from(a.rc);
        let mut flags = SWP_NOACTIVATE;
        if visible {
            flags |= SWP_SHOWWINDOW;
        }
        let _ = SetWindowPos(h.h(), Some(HWND_TOPMOST), r.l, r.t, r.r - r.l, r.b - r.t, flags);
        SetWindowRgn(h.h(), None, true);
        r
    }
}

// ───────── root-window subclass: AppBar notifications ─────────

static mut OLD_PROC: isize = 0;
pub static APPBAR_POS_CHANGED: std::sync::atomic::AtomicBool = std::sync::atomic::AtomicBool::new(false);

/// Previous window procs of popup windows we de-chromed (hwnd → proc).
static POPUP_PROCS: std::sync::Mutex<Vec<(isize, isize)>> = std::sync::Mutex::new(Vec::new());

/// winit's "undecorated shadow" hack insets the client area by 1px
/// (`rgrc.top += 1; rgrc.bottom += 1` in WM_NCCALCSIZE) — its own source
/// admits "a small black 1px border on the top". That was the seam on top
/// of the shelf and pushed every glyph 1px low. Claiming the whole window
/// as client area restores pixel-exact layout.
fn nccalc_full(msg: u32, wp: WPARAM) -> bool {
    msg == WM_NCCALCSIZE && wp.0 != 0
}

unsafe extern "system" fn popup_proc(h: HWND, msg: u32, wp: WPARAM, lp: LPARAM) -> LRESULT {
    if nccalc_full(msg, wp) {
        return LRESULT(0);
    }
    let old = POPUP_PROCS.lock().ok().and_then(|v| v.iter().find(|e| e.0 == h.0 as isize).map(|e| e.1)).unwrap_or(0);
    if old == 0 {
        return DefWindowProcW(h, msg, wp, lp);
    }
    let old: WNDPROC = std::mem::transmute(old);
    CallWindowProcW(old, h, msg, wp, lp)
}

/// No DWM non-client rendering (no frame shadow halo around rounded,
/// transparent windows) and a full-size client area.
fn dechrome_now(h: Hwnd) {
    unsafe {
        let disabled: u32 = 1; // DWMNCRP_DISABLED
        let _ = DwmSetWindowAttribute(h.h(), DWMWINDOWATTRIBUTE(2), &disabled as *const _ as *const c_void, 4);
        let _ = SetWindowPos(h.h(), None, 0, 0, 0, 0, SWP_FRAMECHANGED | SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }
}

/// Apply the de-chrome fix to a popup / dialog window (menus, previews,
/// toast, settings).
pub fn dechrome_popup(h: Hwnd) {
    unsafe {
        let prev = SetWindowLongPtrW(h.h(), GWLP_WNDPROC, popup_proc as usize as isize);
        if let Ok(mut v) = POPUP_PROCS.lock() {
            v.retain(|e| IsWindow(Some(HWND(e.0 as *mut c_void))).as_bool());
            v.push((h.0, prev));
        }
    }
    dechrome_now(h);
}

unsafe extern "system" fn root_proc(h: HWND, msg: u32, wp: WPARAM, lp: LPARAM) -> LRESULT {
    if nccalc_full(msg, wp) {
        return LRESULT(0);
    }
    if msg == APPBAR_CALLBACK {
        // ABN_POSCHANGED = 1. ABN_FULLSCREENAPP (2) deliberately ignored
        // (registration itself fires it — see WPF HwndHandler note).
        if wp.0 == 1 {
            APPBAR_POS_CHANGED.store(true, std::sync::atomic::Ordering::SeqCst);
            crate::events::wake();
        }
        return LRESULT(0);
    }
    if msg == WM_MOUSEACTIVATE {
        // Never take focus on click (WS_EX_NOACTIVATE belt-and-braces),
        // unless we explicitly allowed activation for search typing.
        if crate::events::ALLOW_ACTIVATE.load(std::sync::atomic::Ordering::SeqCst) {
            return LRESULT(MA_ACTIVATE as isize);
        }
        return LRESULT(MA_NOACTIVATE as isize);
    }
    #[allow(static_mut_refs)]
    let old: WNDPROC = std::mem::transmute(OLD_PROC);
    CallWindowProcW(old, h, msg, wp, lp)
}

pub fn subclass_root(h: Hwnd) {
    unsafe {
        let prev = SetWindowLongPtrW(h.h(), GWLP_WNDPROC, root_proc as usize as isize);
        OLD_PROC = prev;
    }
    dechrome_now(h);
}

// ───────────────────────── fullscreen detection ─────────────────────────

pub struct FullscreenDetector {
    pub hidden: bool,
    clear_since: Option<std::time::Instant>,
    /// The window that went fullscreen + its process.
    fs_hwnd: isize,
    fs_pid: u32,
}

impl Default for FullscreenDetector {
    fn default() -> Self {
        Self { hidden: false, clear_since: None, fs_hwnd: 0, fs_pid: 0 }
    }
}

/// Does `h` still exist, visible, not minimized, covering its monitor?
unsafe fn still_fullscreen(h: HWND) -> bool {
    if !IsWindow(Some(h)).as_bool() || !IsWindowVisible(h).as_bool() || IsIconic(h).as_bool() {
        return false;
    }
    let mut r = RECT::default();
    if GetWindowRect(h, &mut r).is_err() {
        return false;
    }
    let (mon, _) = monitor_info(MonitorFromWindow(h, MONITOR_DEFAULTTONEAREST));
    let (mw, mh) = ((mon.r - mon.l) as f32, (mon.b - mon.t) as f32);
    (r.right - r.left) as f32 >= mw * 0.95 && (r.bottom - r.top) as f32 >= mh * 0.95
}

/// Is the foreground window a fullscreen app on the shelf's monitor?
/// `None` = can't tell this tick (our own popup / no foreground) → keep state.
/// Returns (fullscreen?, foreground hwnd, its pid).
unsafe fn foreground_is_fullscreen(shelf: Hwnd) -> Option<(bool, isize, u32)> {
    let r = foreground_check(shelf)?;
    let fg = GetForegroundWindow();
    let mut pid = 0u32;
    GetWindowThreadProcessId(fg, Some(&mut pid));
    Some((r, fg.0 as isize, pid))
}

unsafe fn foreground_check(shelf: Hwnd) -> Option<bool> {
    let fg = GetForegroundWindow();
    if fg.0.is_null() {
        return None;
    }
    let mut pid = 0u32;
    GetWindowThreadProcessId(fg, Some(&mut pid));
    if fg.0 as isize == shelf.0 || pid == own_pid() {
        return None;
    }
    let mut cls = [0u16; 256];
    let n = GetClassNameW(fg, &mut cls) as usize;
    let c = String::from_utf16_lossy(&cls[..n]);
    if c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd" || c == "WorkerW" || c == "Progman" {
        return Some(false);
    }
    // Only a fullscreen app on OUR monitor hides the shelf.
    let fm = MonitorFromWindow(fg, MONITOR_DEFAULTTONEAREST);
    if fm != MonitorFromWindow(shelf.h(), MONITOR_DEFAULTTONEAREST) {
        return Some(false);
    }
    let mut r = RECT::default();
    if GetWindowRect(fg, &mut r).is_err() {
        return None;
    }
    let (mon, _) = monitor_info(fm);
    let (mw, mh) = ((mon.r - mon.l) as f32, (mon.b - mon.t) as f32);
    let (ww, wh) = ((r.right - r.left) as f32, (r.bottom - r.top) as f32);
    let style = GetWindowLongW(fg, GWL_STYLE) as u32;
    let caption = style & 0x00C0_0000 == 0x00C0_0000; // WS_CAPTION
    let thick = style & 0x0004_0000 != 0; // WS_THICKFRAME
    let covers = r.left <= mon.l + 1 && r.top <= mon.t + 1 && r.right >= mon.r - 1 && r.bottom >= mon.b - 1;
    let big = ww >= mw * 0.95 && wh >= mh * 0.95;
    // A normal captioned + resizable window (maximized with an auto-hide
    // taskbar can cover the monitor too) is never "fullscreen".
    let normal_window = caption && thick;
    // 1) Covers the whole monitor and isn't a normal window (F11 browsers,
    //    fullscreen video players, borderless games).
    if covers && !normal_window {
        return Some(true);
    }
    // 2) Qt rule: borderless and ≥95% of the monitor.
    if !caption && !thick && big {
        return Some(true);
    }
    // 3) Shell's own verdict (what the taskbar uses): busy fullscreen app,
    //    exclusive D3D, or presentation mode — with a monitor-sized window.
    if big {
        if let Ok(q) = SHQueryUserNotificationState() {
            if q == QUNS_BUSY || q == QUNS_RUNNING_D3D_FULL_SCREEN || q == QUNS_PRESENTATION_MODE {
                return Some(true);
            }
        }
    }
    Some(false)
}

impl FullscreenDetector {
    /// One timer tick. Returns Some(hide) when the state flips. Hides
    /// instantly; shows again only after 400ms of continuous non-fullscreen,
    /// so seeking / OSD overlays in a fullscreen video never flash the shelf.
    pub fn tick(&mut self, shelf: Hwnd, menu_open: bool) -> Option<bool> {
        if menu_open {
            return None;
        }
        let (fs, fg, pid) = unsafe { foreground_is_fullscreen(shelf) }?;
        if fs {
            self.clear_since = None;
            self.fs_hwnd = fg;
            self.fs_pid = pid;
            if !self.hidden {
                self.hidden = true;
                return Some(true);
            }
            return None;
        }
        if !self.hidden {
            return None;
        }
        // Seek bars / OSD / player controls from the SAME app take focus for a
        // moment while the video window stays fullscreen behind them: stay hidden.
        if pid == self.fs_pid && unsafe { still_fullscreen(HWND(self.fs_hwnd as *mut c_void)) } {
            self.clear_since = None;
            return None;
        }
        let since = *self.clear_since.get_or_insert_with(std::time::Instant::now);
        if since.elapsed() >= std::time::Duration::from_millis(400) {
            self.hidden = false;
            self.clear_since = None;
            return Some(false);
        }
        None
    }
}

// ───────────────────────── registry ─────────────────────────

pub fn reg_dword(root: HKEY, path: &str, name: &str) -> Option<u32> {
    unsafe {
        let p = wide(path);
        let n = wide(name);
        let mut data = 0u32;
        let mut size = 4u32;
        let r = RegGetValueW(root, PCWSTR(p.as_ptr()), PCWSTR(n.as_ptr()), RRF_RT_REG_DWORD, None, Some(&mut data as *mut _ as *mut c_void), Some(&mut size));
        (r == ERROR_SUCCESS).then_some(data)
    }
}

pub fn reg_string(root: HKEY, path: &str, name: &str) -> Option<String> {
    unsafe {
        let p = wide(path);
        let n = wide(name);
        let mut size = 0u32;
        let r = RegGetValueW(root, PCWSTR(p.as_ptr()), PCWSTR(n.as_ptr()), RRF_RT_REG_SZ, None, None, Some(&mut size));
        if r != ERROR_SUCCESS || size == 0 {
            return None;
        }
        let mut buf = vec![0u16; size as usize / 2 + 1];
        let r = RegGetValueW(root, PCWSTR(p.as_ptr()), PCWSTR(n.as_ptr()), RRF_RT_REG_SZ, None, Some(buf.as_mut_ptr() as *mut c_void), Some(&mut size));
        (r == ERROR_SUCCESS).then(|| from_wide(&buf))
    }
}

const PERSONALIZE: &str = r"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

/// OsThemeHelper.IsAppsLightTheme — read fresh every call.
pub fn is_apps_light_theme() -> bool {
    match reg_dword(HKEY_CURRENT_USER, PERSONALIZE, "AppsUseLightTheme") {
        Some(v) => v == 1,
        None => reg_dword(HKEY_CURRENT_USER, PERSONALIZE, "SystemUsesLightTheme") == Some(1),
    }
}

/// `EnableTransparency` (missing counts as on).
pub fn system_transparency_enabled() -> bool {
    reg_dword(HKEY_CURRENT_USER, PERSONALIZE, "EnableTransparency").unwrap_or(1) != 0
}

pub fn wallpaper_key() -> String {
    if let Some(p) = reg_string(HKEY_CURRENT_USER, r"Control Panel\Desktop", "Wallpaper") {
        if let Ok(m) = std::fs::metadata(&p) {
            let t = m.modified().ok().and_then(|t| t.duration_since(std::time::UNIX_EPOCH).ok()).map(|d| d.as_nanos()).unwrap_or(0);
            return format!("{p}|{t}");
        }
    }
    format!("color:{}", reg_string(HKEY_CURRENT_USER, r"Control Panel\Colors", "Background").unwrap_or_default())
}

/// Bottom-seventh, centered 3/5-width band of the wallpaper, averaged.
pub fn wallpaper_bottom_average() -> [u8; 3] {
    if let Some(p) = reg_string(HKEY_CURRENT_USER, r"Control Panel\Desktop", "Wallpaper") {
        if let Ok(img) = image::open(&p) {
            let img = img.to_rgb8();
            let (w, h) = img.dimensions();
            let sx = w / 5;
            let sw = (w * 3 / 5).max(1);
            let sh = (h / 7).max(1);
            let sy = h.saturating_sub(sh);
            let (mut r, mut g, mut b, mut n) = (0u64, 0u64, 0u64, 0u64);
            // Sample on a grid — a 1x1 bicubic downscale is an area average.
            let step = ((sw * sh) as f64 / 40_000.0).sqrt().max(1.0) as u32;
            let mut y = sy;
            while y < (sy + sh).min(h) {
                let mut x = sx;
                while x < (sx + sw).min(w) {
                    let px = img.get_pixel(x, y);
                    r += px[0] as u64;
                    g += px[1] as u64;
                    b += px[2] as u64;
                    n += 1;
                    x += step;
                }
                y += step;
            }
            if n > 0 {
                return [(r / n) as u8, (g / n) as u8, (b / n) as u8];
            }
        }
    }
    if let Some(s) = reg_string(HKEY_CURRENT_USER, r"Control Panel\Colors", "Background") {
        let v: Vec<u8> = s.split_whitespace().filter_map(|x| x.parse().ok()).collect();
        if v.len() == 3 {
            return [v[0], v[1], v[2]];
        }
    }
    [0x2E, 0x3A, 0x55]
}

// ───────────────────────── startup (Run key) ─────────────────────────

const RUN_KEY: &str = r"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

pub fn set_startup(enable: bool) {
    unsafe {
        let p = wide(RUN_KEY);
        let mut key = HKEY::default();
        if RegOpenKeyExW(HKEY_CURRENT_USER, PCWSTR(p.as_ptr()), Some(0), KEY_SET_VALUE, &mut key) != ERROR_SUCCESS {
            return;
        }
        let name = wide("Totthodhara");
        if enable {
            let exe = std::env::current_exe().map(|p| p.to_string_lossy().into_owned()).unwrap_or_default();
            let val = wide(&format!("\"{exe}\""));
            let bytes = std::slice::from_raw_parts(val.as_ptr() as *const u8, val.len() * 2);
            let _ = RegSetValueExW(key, PCWSTR(name.as_ptr()), Some(0), REG_SZ, Some(bytes));
        } else {
            let _ = RegDeleteValueW(key, PCWSTR(name.as_ptr()));
        }
        let _ = RegCloseKey(key);
    }
}

pub fn is_startup_enabled() -> bool {
    let exe = std::env::current_exe().map(|p| p.to_string_lossy().to_lowercase()).unwrap_or_default();
    reg_string(HKEY_CURRENT_USER, RUN_KEY, "Totthodhara")
        .map(|v| v.trim_matches('"').to_lowercase() == exe)
        .unwrap_or(false)
}

// ───────────────────────── time zones ─────────────────────────

#[derive(Clone)]
pub struct Zone {
    pub id: String,
    pub display: String,
    tz: DYNAMIC_TIME_ZONE_INFORMATION,
}

impl Zone {
    /// Minutes, UTC = local + bias (negative east of Greenwich).
    pub fn bias(&self) -> i32 {
        self.tz.Bias
    }
}

pub fn system_zones() -> Vec<Zone> {
    let mut v = Vec::new();
    let mut i = 0u32;
    loop {
        let mut d = DYNAMIC_TIME_ZONE_INFORMATION::default();
        if unsafe { EnumDynamicTimeZoneInformation(i, &mut d) } != 0 {
            break;
        }
        let id = from_wide(&d.TimeZoneKeyName);
        let display = reg_string(
            HKEY_LOCAL_MACHINE,
            &format!(r"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Time Zones\{id}"),
            "Display",
        )
        .unwrap_or_else(|| id.clone());
        v.push(Zone { id, display, tz: d });
        i += 1;
    }
    v
}

pub fn local_zone() -> Zone {
    let mut d = DYNAMIC_TIME_ZONE_INFORMATION::default();
    unsafe { GetDynamicTimeZoneInformation(&mut d) };
    let id = from_wide(&d.TimeZoneKeyName);
    let display = reg_string(
        HKEY_LOCAL_MACHINE,
        &format!(r"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Time Zones\{id}"),
        "Display",
    )
    .unwrap_or_else(|| id.clone());
    Zone { id, display, tz: d }
}

pub struct ZoneTime {
    pub hour: u16,
    pub minute: u16,
    pub second: u16,
    pub dow: u16,
    pub day: u16,
    pub month: u16,
    pub year: u16,
}

pub fn now_in_zone(z: &Zone) -> ZoneTime {
    unsafe {
        let utc = GetSystemTime();
        let mut l = SYSTEMTIME::default();
        if SystemTimeToTzSpecificLocalTimeEx(Some(&z.tz), &utc, &mut l).is_err() {
            l = GetLocalTime();
        }
        ZoneTime { hour: l.wHour, minute: l.wMinute, second: l.wSecond, dow: l.wDayOfWeek, day: l.wDay, month: l.wMonth, year: l.wYear }
    }
}

// ───────────────────────── single instance ─────────────────────────

pub fn acquire_single_instance() -> bool {
    unsafe {
        let name = wide("Totthodhara-ClipDropPro-Unique-Mutex");
        let h = CreateMutexW(None, true, PCWSTR(name.as_ptr()));
        match h {
            Ok(h) => {
                if GetLastError() == ERROR_ALREADY_EXISTS {
                    let _ = CloseHandle(h);
                    false
                } else {
                    std::mem::forget(h);
                    true
                }
            }
            Err(_) => true,
        }
    }
}

/// Registered "please quit" message every Totthodhara build ≥2.0.2 obeys.
pub fn quit_message() -> u32 {
    static M: std::sync::OnceLock<u32> = std::sync::OnceLock::new();
    *M.get_or_init(|| {
        let n = wide("Totthodhara.Quit.v1");
        unsafe { RegisterWindowMessageW(PCWSTR(n.as_ptr())) }
    })
}

unsafe extern "system" fn collect_proc(hwnd: HWND, lp: LPARAM) -> BOOL {
    let v = &mut *(lp.0 as *mut (u32, Vec<isize>));
    let mut pid = 0u32;
    GetWindowThreadProcessId(hwnd, Some(&mut pid));
    if pid == v.0 {
        v.1.push(hwnd.0 as isize);
    }
    BOOL(1)
}

fn windows_of(pid: u32) -> Vec<isize> {
    let mut v: (u32, Vec<isize>) = (pid, Vec::new());
    unsafe {
        let _ = EnumWindows(Some(collect_proc), LPARAM(&mut v as *mut _ as isize));
    }
    v.1
}

/// One shelf only. Ask older instances (any edition) to quit CLEANLY so they
/// release their AppBar reservation; force-kill only as a fallback, then
/// remove the reservation a killed shelf leaked (otherwise the work area keeps
/// an empty strip and the new shelf stacks above it — "two shelves").
pub fn replace_old_instances() {
    use windows::Win32::System::Diagnostics::ToolHelp::*;
    unsafe {
        let me = own_pid();
        let Ok(snap) = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0) else { return };
        let mut e = PROCESSENTRY32W { dwSize: std::mem::size_of::<PROCESSENTRY32W>() as u32, ..Default::default() };
        let mut pids = Vec::new();
        if Process32FirstW(snap, &mut e).is_ok() {
            loop {
                let name = from_wide(&e.szExeFile).to_lowercase();
                if e.th32ProcessID != me && (name == "totthodhara.exe" || name == "clipdroppro.exe") {
                    pids.push(e.th32ProcessID);
                }
                if Process32NextW(snap, &mut e).is_err() {
                    break;
                }
            }
        }
        let _ = CloseHandle(snap);
        // Only replace instances started BEFORE us: two copies launched at
        // the same moment must not kill each other (newest wins, one shelf).
        let created = |h: HANDLE| -> u64 {
            let (mut c, mut e, mut k, mut u) = (FILETIME::default(), FILETIME::default(), FILETIME::default(), FILETIME::default());
            let _ = GetProcessTimes(h, &mut c, &mut e, &mut k, &mut u);
            ((c.dwHighDateTime as u64) << 32) | c.dwLowDateTime as u64
        };
        let my_start = created(GetCurrentProcess());
        let qm = quit_message();
        for pid in pids {
            if let Ok(h) = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid) {
                let older = created(h) <= my_start;
                let _ = CloseHandle(h);
                if !older {
                    continue;
                }
            }
            let wins = windows_of(pid);
            for &h in &wins {
                let _ = PostMessageW(Some(HWND(h as *mut c_void)), qm, WPARAM(0), LPARAM(0));
            }
            if let Ok(p) = OpenProcess(PROCESS_TERMINATE | PROCESS_SYNCHRONIZE, false, pid) {
                if WaitForSingleObject(p, 2500) != WAIT_OBJECT_0 {
                    let _ = TerminateProcess(p, 0);
                    WaitForSingleObject(p, 3000);
                }
                let _ = CloseHandle(p);
            }
            // Clean any AppBar the old process left registered.
            for h in wins {
                let mut a = APPBARDATA { cbSize: std::mem::size_of::<APPBARDATA>() as u32, hWnd: HWND(h as *mut c_void), ..Default::default() };
                SHAppBarMessage(ABM_REMOVE, &mut a);
            }
        }
    }
}

/// Kill older instances by process name (Totthodhara / ClipDropPro).
#[allow(dead_code)]
pub fn kill_old_instances() {
    use windows::Win32::System::Diagnostics::ToolHelp::*;
    unsafe {
        let me = own_pid();
        let Ok(snap) = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0) else { return };
        let mut e = PROCESSENTRY32W { dwSize: std::mem::size_of::<PROCESSENTRY32W>() as u32, ..Default::default() };
        if Process32FirstW(snap, &mut e).is_ok() {
            loop {
                let name = from_wide(&e.szExeFile).to_lowercase();
                if e.th32ProcessID != me && (name == "totthodhara.exe" || name == "clipdroppro.exe") {
                    if let Ok(p) = OpenProcess(PROCESS_TERMINATE | PROCESS_SYNCHRONIZE, false, e.th32ProcessID) {
                        let _ = TerminateProcess(p, 0);
                        WaitForSingleObject(p, 3000);
                        let _ = CloseHandle(p);
                    }
                }
                if Process32NextW(snap, &mut e).is_err() {
                    break;
                }
            }
        }
        let _ = CloseHandle(snap);
    }
}

pub fn message_box(title: &str, text: &str) {
    let t = wide(title);
    let m = wide(text);
    unsafe {
        MessageBoxW(None, PCWSTR(m.as_ptr()), PCWSTR(t.as_ptr()), MB_OK | MB_ICONINFORMATION);
    }
}

pub fn memory_load() -> (u32, u64, u64) {
    unsafe {
        let mut m = MEMORYSTATUSEX { dwLength: std::mem::size_of::<MEMORYSTATUSEX>() as u32, ..Default::default() };
        let _ = GlobalMemoryStatusEx(&mut m);
        (m.dwMemoryLoad, m.ullTotalPhys, m.ullAvailPhys)
    }
}

pub fn system_times() -> (u64, u64, u64) {
    unsafe {
        let (mut i, mut k, mut u) = (FILETIME::default(), FILETIME::default(), FILETIME::default());
        let _ = GetSystemTimes(Some(&mut i), Some(&mut k), Some(&mut u));
        let f = |t: FILETIME| ((t.dwHighDateTime as u64) << 32) | t.dwLowDateTime as u64;
        (f(i), f(k), f(u))
    }
}

// ───────────────────────── Qt-edition theme helpers ─────────────────────────

/// ThemeService: SystemUsesLightTheme first (taskbar follows it), Apps fallback.
pub fn system_uses_light_theme() -> bool {
    match reg_dword(HKEY_CURRENT_USER, PERSONALIZE, "SystemUsesLightTheme") {
        Some(v) => v == 1,
        None => reg_dword(HKEY_CURRENT_USER, PERSONALIZE, "AppsUseLightTheme") == Some(1),
    }
}

/// "Show accent color on Start and taskbar".
pub fn color_prevalence() -> bool {
    reg_dword(HKEY_CURRENT_USER, PERSONALIZE, "ColorPrevalence") == Some(1)
}

/// DWM ColorizationColor masked to RGB.
pub fn accent_color() -> Option<(u8, u8, u8)> {
    let v = reg_dword(HKEY_CURRENT_USER, r"Software\Microsoft\Windows\DWM", "ColorizationColor")?;
    Some(((v >> 16) as u8, (v >> 8) as u8, v as u8))
}

/// Remove the Win11 1px system border (it reads as a seam against the taskbar).
pub fn set_border_none(h: Hwnd) {
    unsafe {
        let none: u32 = 0xFFFF_FFFE; // DWMWA_COLOR_NONE
        let _ = DwmSetWindowAttribute(h.h(), DWMWINDOWATTRIBUTE(34), &none as *const _ as *const c_void, 4);
    }
}

/// DWM blur-behind clipped to a rounded pill (ThemeService::applyGlass).
/// `enable=false` keeps per-pixel alpha but no blur (empty region).
pub fn set_blur_region(h: Hwnd, w: i32, hh: i32, radius: i32, enable: bool) {
    unsafe {
        let rgn = if enable && w > 0 && hh > 0 {
            if radius > 0 {
                CreateRoundRectRgn(0, 0, w + 1, hh + 1, radius * 2, radius * 2)
            } else {
                CreateRectRgn(0, 0, w, hh)
            }
        } else {
            CreateRectRgn(0, 0, -1, -1)
        };
        let bb = DWM_BLURBEHIND {
            dwFlags: DWM_BB_ENABLE | DWM_BB_BLURREGION,
            fEnable: true.into(),
            hRgnBlur: rgn,
            fTransitionOnMaximized: false.into(),
        };
        let _ = DwmEnableBlurBehindWindow(h.h(), &bb);
        let _ = DeleteObject(rgn.into());
        set_corner_round(h, false);
    }
}
