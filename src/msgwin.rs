//! Hidden window on its own thread: clipboard listener, global hotkey,
//! tray icon, low-level mouse hook (click-outside), fullscreen timer and
//! OS theme notifications. Nothing here touches egui directly.
use crate::events::{self, Event};
use crate::winsys::{wide, Hwnd};
use std::sync::atomic::{AtomicIsize, AtomicU32, Ordering::SeqCst};
use windows::core::PCWSTR;
use windows::Win32::Foundation::*;
use windows::Win32::System::DataExchange::AddClipboardFormatListener;
use windows::Win32::System::LibraryLoader::GetModuleHandleW;
use windows::Win32::System::SystemInformation::GetTickCount;
use windows::Win32::UI::Input::KeyboardAndMouse::*;
use windows::Win32::UI::Shell::*;
use windows::Win32::UI::WindowsAndMessaging::*;

const WM_TRAY: u32 = WM_APP + 1;
const WM_REHOOK: u32 = WM_APP + 2;
const HOTKEY_ID: i32 = 9000;
const TIMER_FS: usize = 1;

static MSG_HWND: AtomicIsize = AtomicIsize::new(0);
static HOTKEY_VK: AtomicU32 = AtomicU32::new(0xC0);
static HOTKEY_MODS: AtomicU32 = AtomicU32::new(2);
static TASKBAR_CREATED: AtomicU32 = AtomicU32::new(0);
static mut HICON_TRAY: isize = 0;
static mut MOUSE_HOOK: isize = 0;
static mut FS: Option<crate::winsys::FullscreenDetector> = None;

/// WPF `Key` enum name → virtual key.
pub fn vk_from_wpf_key(name: &str) -> u32 {
    let n = name.trim();
    match n {
        "OemTilde" | "Oem3" => 0xC0,
        "Space" => 0x20,
        "Tab" => 0x09,
        "OemMinus" => 0xBD,
        "OemPlus" => 0xBB,
        "OemComma" => 0xBC,
        "OemPeriod" => 0xBE,
        "OemQuestion" | "Oem2" => 0xBF,
        "OemSemicolon" | "Oem1" => 0xBA,
        "OemQuotes" | "Oem7" => 0xDE,
        "OemOpenBrackets" | "Oem4" => 0xDB,
        "OemCloseBrackets" | "Oem6" => 0xDD,
        "OemPipe" | "Oem5" => 0xDC,
        "Insert" => 0x2D,
        "Home" => 0x24,
        "End" => 0x23,
        "PageUp" | "Prior" => 0x21,
        "PageDown" | "Next" => 0x22,
        _ => {
            let b = n.as_bytes();
            if b.len() == 1 && b[0].is_ascii_alphabetic() {
                b[0].to_ascii_uppercase() as u32
            } else if b.len() == 2 && b[0] == b'D' && b[1].is_ascii_digit() {
                b[1] as u32
            } else if let Some(f) = n.strip_prefix('F').and_then(|x| x.parse::<u32>().ok()) {
                if (1..=24).contains(&f) { 0x70 + f - 1 } else { 0xC0 }
            } else {
                0xC0
            }
        }
    }
}

pub fn set_hotkey(key: &str, mods: i32) {
    HOTKEY_VK.store(vk_from_wpf_key(key), SeqCst);
    HOTKEY_MODS.store(mods as u32 & 0xF, SeqCst);
    let h = MSG_HWND.load(SeqCst);
    if h != 0 {
        unsafe {
            let _ = PostMessageW(Some(HWND(h as *mut _)), WM_REHOOK, WPARAM(0), LPARAM(0));
        }
    }
}

/// Re-add the clipboard listener (after sleep/resume or heavy I/O).
pub fn ensure_clipboard_listener() {
    let h = MSG_HWND.load(SeqCst);
    if h != 0 {
        unsafe {
            let _ = AddClipboardFormatListener(HWND(h as *mut _));
        }
    }
}

unsafe fn add_tray(h: HWND, modify: bool) {
    let mut nid = NOTIFYICONDATAW {
        cbSize: std::mem::size_of::<NOTIFYICONDATAW>() as u32,
        hWnd: h,
        uID: 1,
        uFlags: NIF_ICON | NIF_MESSAGE | NIF_TIP,
        uCallbackMessage: WM_TRAY,
        hIcon: HICON(HICON_TRAY as *mut _),
        ..Default::default()
    };
    let tip = wide("Totthodhara is running");
    nid.szTip[..tip.len()].copy_from_slice(&tip);
    let _ = Shell_NotifyIconW(if modify { NIM_MODIFY } else { NIM_ADD }, &nid);
}

pub fn remove_tray() {
    let h = MSG_HWND.load(SeqCst);
    if h == 0 {
        return;
    }
    unsafe {
        let nid = NOTIFYICONDATAW {
            cbSize: std::mem::size_of::<NOTIFYICONDATAW>() as u32,
            hWnd: HWND(h as *mut _),
            uID: 1,
            ..Default::default()
        };
        let _ = Shell_NotifyIconW(NIM_DELETE, &nid);
    }
}

unsafe fn register_hotkey(h: HWND) {
    let _ = UnregisterHotKey(Some(h), HOTKEY_ID);
    let mods = HOT_KEY_MODIFIERS(HOTKEY_MODS.load(SeqCst)) | MOD_NOREPEAT;
    if RegisterHotKey(Some(h), HOTKEY_ID, mods, HOTKEY_VK.load(SeqCst)).is_err() {
        crate::paths::log("[Hotkey] RegisterHotKey failed (in use by another app?)");
    }
}

unsafe extern "system" fn mouse_hook(code: i32, wp: WPARAM, lp: LPARAM) -> LRESULT {
    if code >= 0 && (wp.0 as u32 == WM_LBUTTONDOWN || wp.0 as u32 == WM_RBUTTONDOWN) && events::POPUP_OPEN.load(SeqCst) {
        let info = &*(lp.0 as *const MSLLHOOKSTRUCT);
        events::send(Event::GlobalMouseDown(info.pt.x, info.pt.y));
    }
    CallNextHookEx(Some(HHOOK(MOUSE_HOOK as *mut _)), code, wp, lp)
}

unsafe extern "system" fn wndproc(h: HWND, msg: u32, wp: WPARAM, lp: LPARAM) -> LRESULT {
    match msg {
        WM_CLIPBOARDUPDATE => {
            let now = GetTickCount();
            let until = events::INTERNAL_UNTIL_MS.load(SeqCst);
            // IsInternalChange: skip our own clipboard sets.
            if (until.wrapping_sub(now) as i32) <= 0 {
                events::send(Event::ClipboardChanged);
            }
            LRESULT(0)
        }
        WM_HOTKEY => {
            if wp.0 as i32 == HOTKEY_ID {
                events::send(Event::Hotkey);
            }
            LRESULT(0)
        }
        WM_TRAY => {
            let ev = (lp.0 & 0xFFFF) as u32;
            if ev == WM_LBUTTONUP {
                events::send(Event::TrayClick);
            } else if ev == WM_LBUTTONDBLCLK {
                events::send(Event::TrayDoubleClick);
            } else if ev == WM_RBUTTONUP || ev == WM_CONTEXTMENU {
                let (x, y) = crate::winsys::cursor_pos();
                events::send(Event::TrayMenu(x, y));
            }
            LRESULT(0)
        }
        WM_REHOOK => {
            register_hotkey(h);
            LRESULT(0)
        }
        WM_TIMER if wp.0 == TIMER_FS => {
            let shelf = Hwnd(events::SHELF_HWND.load(SeqCst));
            #[allow(static_mut_refs)]
            if let (false, Some(fs)) = (shelf.is_null(), FS.as_mut()) {
                if events::FS_CHECK.load(SeqCst) {
                    if let Some(hide) = fs.tick(shelf, events::POPUP_OPEN.load(SeqCst)) {
                        events::send(Event::Fullscreen(hide));
                    }
                }
            }
            LRESULT(0)
        }
        WM_SETTINGCHANGE | WM_THEMECHANGED | 0x0320 /* WM_DWMCOLORIZATIONCOLORCHANGED */ => {
            events::send(Event::ThemeMaybeChanged);
            DefWindowProcW(h, msg, wp, lp)
        }
        WM_POWERBROADCAST => {
            if wp.0 as u32 == PBT_APMRESUMEAUTOMATIC || wp.0 as u32 == PBT_APMRESUMESUSPEND {
                let _ = AddClipboardFormatListener(h);
            }
            LRESULT(1)
        }
        _ => {
            if msg != 0 && msg == TASKBAR_CREATED.load(SeqCst) {
                add_tray(h, false);
                return LRESULT(0);
            }
            DefWindowProcW(h, msg, wp, lp)
        }
    }
}

/// Tray icon from app.png: crop transparency, pad square, 32x32 (WPF SetupTrayIcon).
fn tray_icon_png() -> Vec<u8> {
    let img = image::load_from_memory(crate::icons::APP_PNG).map(|i| i.to_rgba8()).unwrap_or_default();
    let sq = crate::icons::crop_pad_square(&img);
    let small = image::imageops::resize(&sq, 32, 32, image::imageops::FilterType::CatmullRom);
    let mut out = Vec::new();
    let _ = image::DynamicImage::ImageRgba8(small).write_to(&mut std::io::Cursor::new(&mut out), image::ImageFormat::Png);
    out
}

pub fn start(hotkey: &str, mods: i32) {
    HOTKEY_VK.store(vk_from_wpf_key(hotkey), SeqCst);
    HOTKEY_MODS.store(mods as u32 & 0xF, SeqCst);
    std::thread::Builder::new()
        .name("msgwin".into())
        .spawn(|| unsafe {
            let hinst = GetModuleHandleW(None).unwrap_or_default();
            let cls = wide("TotthodharaMsgWindow");
            let wc = WNDCLASSW {
                lpfnWndProc: Some(wndproc),
                hInstance: hinst.into(),
                lpszClassName: PCWSTR(cls.as_ptr()),
                ..Default::default()
            };
            RegisterClassW(&wc);
            let title = wide("Totthodhara Message Window");
            let Ok(h) = CreateWindowExW(
                WS_EX_TOOLWINDOW,
                PCWSTR(cls.as_ptr()),
                PCWSTR(title.as_ptr()),
                WS_POPUP,
                0, 0, 0, 0,
                None, None, Some(hinst.into()), None,
            ) else {
                crate::paths::log("[MsgWin] CreateWindowEx failed");
                return;
            };
            MSG_HWND.store(h.0 as isize, SeqCst);
            let tc = wide("TaskbarCreated");
            TASKBAR_CREATED.store(RegisterWindowMessageW(PCWSTR(tc.as_ptr())), SeqCst);

            let png = tray_icon_png();
            if let Ok(ic) = CreateIconFromResourceEx(&png, true, 0x0003_0000, 32, 32, LR_DEFAULTCOLOR) {
                HICON_TRAY = ic.0 as isize;
            }
            add_tray(h, false);
            let _ = AddClipboardFormatListener(h);
            register_hotkey(h);
            if let Ok(hk) = SetWindowsHookExW(WH_MOUSE_LL, Some(mouse_hook), Some(hinst.into()), 0) {
                MOUSE_HOOK = hk.0 as isize;
            }
            FS = Some(Default::default());
            SetTimer(Some(h), TIMER_FS, 50, None);
            crate::paths::log("[MsgWin] ready (clipboard, hotkey, tray, hook)");

            let mut msg = MSG::default();
            while GetMessageW(&mut msg, None, 0, 0).as_bool() {
                let _ = TranslateMessage(&msg);
                DispatchMessageW(&msg);
            }
        })
        .ok();
}
