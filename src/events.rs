//! Cross-thread events into the UI thread (+ wakeup of egui).
use std::sync::atomic::{AtomicBool, AtomicIsize, AtomicU32};
use std::sync::mpsc::{Receiver, Sender};
use std::sync::{Mutex, OnceLock};

#[derive(Debug)]
pub enum Event {
    ClipboardChanged,
    ItemsChanged,
    Hotkey,
    TrayClick,
    TrayDoubleClick,
    TrayMenu(i32, i32),
    /// Global left/right mouse-down (screen px) while a popup is open.
    GlobalMouseDown(i32, i32),
    Fullscreen(bool),
    ThemeMaybeChanged,
    SysMon(crate::sysmon::Sample),
    Thumb(i64, egui::ColorImage, String),
    Favicon(String, egui::ColorImage),
    Status(String),
    Title(i64, Option<String>),
    Update(crate::update::UpdateMsg),
    /// Another (newer) instance asked us to exit cleanly.
    Quit,
}

struct Sink {
    tx: Sender<Event>,
    ctx: egui::Context,
}

static SINK: OnceLock<Mutex<Option<Sink>>> = OnceLock::new();

pub fn init(ctx: egui::Context) -> Receiver<Event> {
    let (tx, rx) = std::sync::mpsc::channel();
    *SINK.get_or_init(|| Mutex::new(None)).lock().unwrap() = Some(Sink { tx, ctx });
    rx
}

pub fn send(e: Event) {
    if let Some(m) = SINK.get() {
        if let Ok(g) = m.lock() {
            if let Some(s) = g.as_ref() {
                let _ = s.tx.send(e);
                s.ctx.request_repaint();
            }
        }
    }
}

pub fn wake() {
    if let Some(m) = SINK.get() {
        if let Ok(g) = m.lock() {
            if let Some(s) = g.as_ref() {
                s.ctx.request_repaint();
            }
        }
    }
}

/// Shelf HWND (for fullscreen detection on the message thread).
pub static SHELF_HWND: AtomicIsize = AtomicIsize::new(0);
/// A shelf popup/menu is open → report global clicks, pause fullscreen hide.
pub static POPUP_OPEN: AtomicBool = AtomicBool::new(false);
/// Allow the shelf to take activation (search box typing).
pub static ALLOW_ACTIVATE: AtomicBool = AtomicBool::new(false);
/// Ignore WM_CLIPBOARDUPDATE until this tick (ms, GetTickCount) — our own sets.
pub static INTERNAL_UNTIL_MS: AtomicU32 = AtomicU32::new(0);
/// Fullscreen check enabled (false while shelf hidden by the user).
pub static FS_CHECK: AtomicBool = AtomicBool::new(true);
