//! Background work: clipboard capture, drops, history trim, thumbnails,
//! favicons and click-to-paste. Everything heavy stays off the UI thread.
use crate::clipboard::{self, Content};
use crate::db::{Db, Item};
use crate::events::{send, Event};
use crate::settings::Settings;
use crate::winsys::{now_ticks, TICKS_PER_SEC};
use std::sync::atomic::{AtomicBool, Ordering::SeqCst};
use std::sync::{Arc, Mutex, RwLock};
use std::time::{Duration, Instant};

const MAX_TEXT: usize = 200_000;
const DEDUPE: Duration = Duration::from_millis(500);

pub struct Core {
    pub db: Mutex<Db>,
    pub settings: RwLock<Settings>,
    last: Mutex<(String, Instant)>,
    busy: AtomicBool,
}

pub type CoreRef = Arc<Core>;

fn log(m: impl AsRef<str>) {
    crate::paths::log(format!("[VM] {}", m.as_ref()));
}

impl Core {
    pub fn new(db: Db, s: Settings) -> CoreRef {
        Arc::new(Self {
            db: Mutex::new(db),
            settings: RwLock::new(s),
            last: Mutex::new((String::new(), Instant::now() - Duration::from_secs(60))),
            busy: AtomicBool::new(false),
        })
    }
    pub fn settings(&self) -> Settings {
        self.settings.read().unwrap().clone()
    }
    fn dup_recent(&self, key: &str) -> bool {
        let l = self.last.lock().unwrap();
        l.0 == key && l.1.elapsed() < DEDUPE
    }
    fn mark(&self, key: &str) {
        *self.last.lock().unwrap() = (key.to_string(), Instant::now());
    }
    pub fn reset_dedupe(&self) {
        *self.last.lock().unwrap() = (String::new(), Instant::now() - Duration::from_secs(60));
        self.busy.store(false, SeqCst);
    }
}

fn cap_text(t: String) -> String {
    if t.chars().count() <= MAX_TEXT {
        return t;
    }
    let mut s: String = t.chars().take(MAX_TEXT).collect();
    s.push_str("\n…[truncated — full text was larger than storage limit]");
    s
}

fn top_is_recent(top: &Option<Item>, pred: impl Fn(&Item) -> bool) -> bool {
    top.as_ref().map(|t| pred(t) && (now_ticks() - t.date_added) < 2 * TICKS_PER_SEC).unwrap_or(false)
}

pub fn trim_history(core: &Core) {
    let max = core.settings().max_history_items.max(0) as usize;
    let db = core.db.lock().unwrap();
    let mut all: Vec<Item> = db.all_items().into_iter().filter(|x| !x.is_pinned && !x.is_snippet).collect();
    all.sort_by(|a, b| b.date_added.cmp(&a.date_added));
    for it in all.into_iter().skip(max) {
        if it.is_file {
            crate::storage::delete_file(&it.file_path);
        }
        db.delete(it.id);
    }
}

pub fn on_clipboard_changed(core: &CoreRef) {
    if core.busy.swap(true, SeqCst) {
        return;
    }
    let core = core.clone();
    std::thread::spawn(move || {
        process_clipboard(&core);
        core.busy.store(false, SeqCst);
    });
}

fn process_clipboard(core: &CoreRef) {
    let s = core.settings();
    if s.hide_clipboard {
        return;
    }
    for _ in 0..5 {
        let Some(c) = clipboard::read() else {
            std::thread::sleep(Duration::from_millis(30));
            continue;
        };
        match c {
            Content::Image(img) => {
                if core.dup_recent("IMAGE_BITMAP") {
                    return;
                }
                let top = core.db.lock().unwrap().top();
                if top_is_recent(&top, |t| t.is_image()) {
                    log("Skipping duplicate image (top item is already a recent image).");
                    return;
                }
                let Some(path) = crate::storage::save_bitmap(&img) else { return };
                core.mark("IMAGE_BITMAP");
                let it = Item {
                    file_name: "Captured Image".into(),
                    file_path: path,
                    is_file: true,
                    date_added: now_ticks(),
                    origin: "Clipboard".into(),
                    ..Default::default()
                };
                core.db.lock().unwrap().add(&it);
                trim_history(core);
                send(Event::ItemsChanged);
                return;
            }
            Content::Files(files) => {
                let Some(first) = files.first().cloned().filter(|f| !f.is_empty()) else {
                    std::thread::sleep(Duration::from_millis(30));
                    continue;
                };
                if core.dup_recent(&first) {
                    return;
                }
                let top = core.db.lock().unwrap().top();
                if top_is_recent(&top, |t| t.is_file && t.file_path.eq_ignore_ascii_case(&first)) {
                    return;
                }
                if s.max_file_size_mb > 0 {
                    if let Ok(m) = std::fs::metadata(&first) {
                        if m.is_file() && m.len() > s.max_file_size_mb as u64 * 1024 * 1024 {
                            send(Event::Status(format!("File too large ({}MB max)", s.max_file_size_mb)));
                            return;
                        }
                    }
                }
                core.mark(&first);
                handle_files(core, &files, "Clipboard");
                return;
            }
            Content::Text(t) => {
                let t = cap_text(t);
                if t.is_empty() {
                    std::thread::sleep(Duration::from_millis(30));
                    continue;
                }
                if core.dup_recent(&t) {
                    return;
                }
                let top = core.db.lock().unwrap().top();
                if top.as_ref().map(|x| !x.is_file && x.text_content == t).unwrap_or(false) {
                    return;
                }
                core.mark(&t);
                let it = Item { text_content: t, date_added: now_ticks(), origin: "Clipboard".into(), ..Default::default() };
                core.db.lock().unwrap().add(&it);
                trim_history(core);
                send(Event::ItemsChanged);
                return;
            }
        }
    }
}

/// HandleDroppedFilesAsync (also used for clipboard file lists).
pub fn handle_files(core: &CoreRef, files: &[String], origin: &str) {
    let s = core.settings();
    for f in files {
        if s.max_file_size_mb > 0 {
            if let Ok(m) = std::fs::metadata(f) {
                if m.is_file() && m.len() > s.max_file_size_mb as u64 * 1024 * 1024 {
                    log(format!("Skipping large file: {f}"));
                    continue;
                }
            }
        }
        let Some(saved) = crate::storage::save_file(f) else { continue };
        let name = std::path::Path::new(f).file_name().map(|n| n.to_string_lossy().into_owned()).unwrap_or_default();
        let it = Item { file_name: name, file_path: saved, is_file: true, date_added: now_ticks(), origin: origin.into(), ..Default::default() };
        core.db.lock().unwrap().add(&it);
    }
    trim_history(core);
    send(Event::ItemsChanged);
}

pub fn handle_dropped_files(core: &CoreRef, files: Vec<String>) {
    let core = core.clone();
    std::thread::spawn(move || handle_files(&core, &files, "DragDrop"));
}

pub fn handle_dropped_text(core: &CoreRef, text: String) {
    let core = core.clone();
    std::thread::spawn(move || {
        let text = cap_text(text);
        if text.is_empty() || core.dup_recent(&text) {
            return;
        }
        core.mark(&text);
        let lower = text.to_lowercase();
        let path_part = lower.split(['?', '#']).next().unwrap_or("");
        let is_img = (lower.starts_with("http://") || lower.starts_with("https://"))
            && [".png", ".jpg", ".jpeg", ".gif", ".bmp"].iter().any(|e| path_part.ends_with(e));
        let mut added = false;
        if is_img {
            if let Some(p) = crate::storage::download_image(&text) {
                let name = std::path::Path::new(&p).file_name().map(|n| n.to_string_lossy().into_owned()).unwrap_or_default();
                core.db.lock().unwrap().add(&Item { file_name: name, file_path: p, is_file: true, date_added: now_ticks(), origin: "WebDragDrop".into(), ..Default::default() });
                added = true;
            }
        }
        if !added {
            core.db.lock().unwrap().add(&Item { text_content: text, date_added: now_ticks(), origin: "DragDrop".into(), ..Default::default() });
        }
        trim_history(&core);
        send(Event::ItemsChanged);
    });
}

/// InitializeAsync background part: auto-clean, orphan rows, trim.
pub fn startup_cleanup(core: &CoreRef) {
    let core = core.clone();
    std::thread::spawn(move || {
        let s = core.settings();
        let pinned: Vec<String> = core.db.lock().unwrap().items(30).into_iter().filter(|x| x.is_pinned).map(|x| x.file_path).collect();
        crate::storage::clean_old_files(s.auto_clean_hours, &pinned);
        {
            let db = core.db.lock().unwrap();
            for it in db.all_items() {
                if it.is_file && !it.file_path.is_empty() && !std::path::Path::new(&it.file_path).exists() {
                    db.delete(it.id);
                }
            }
        }
        trim_history(&core);
        send(Event::ItemsChanged);
    });
}

fn copy_item_to_clipboard(it: &Item) -> &'static str {
    if it.is_file && !it.file_path.is_empty() {
        let tmp = crate::storage::temp_copy(&it.file_path, &it.file_name);
        let path = tmp.unwrap_or_else(|| it.file_path.clone());
        if it.is_image() {
            if let Ok(img) = image::open(&it.file_path) {
                if clipboard::set_image_and_file(&img.to_rgba8(), &path) {
                    return "Image Copied!";
                }
            }
        }
        clipboard::set_files(&[path]);
        "File Copied!"
    } else {
        let t = if it.text_content.is_empty() { it.display_title.clone() } else { it.text_content.clone() };
        clipboard::set_text(&t);
        "Copied!"
    }
}

/// ItemClicked (normal mode): copy, wait 100ms, Ctrl+V.
pub fn paste_item(it: Item) -> &'static str {
    let label = if it.is_file { if it.is_image() { "Image Copied!" } else { "File Copied!" } } else { "Copied!" };
    std::thread::spawn(move || {
        copy_item_to_clipboard(&it);
        std::thread::sleep(Duration::from_millis(100));
        crate::winsys::send_ctrl_v();
    });
    label
}

/// Qt copyText(out, copyToDestination): copy, then auto-paste.
pub fn paste_text(text: String) {
    std::thread::spawn(move || {
        clipboard::set_text(&text);
        std::thread::sleep(Duration::from_millis(100));
        crate::winsys::send_ctrl_v();
    });
}

/// Fetch a page <title> (Copy page title).
pub fn fetch_title(id: i64, url: String) {
    std::thread::spawn(move || {
        let title = ureq::get(&url)
            .call()
            .ok()
            .and_then(|mut r| r.body_mut().with_config().limit(2 * 1024 * 1024).read_to_string().ok())
            .and_then(|html| {
                let lo = html.to_lowercase();
                let a = lo.find("<title")?;
                let a = a + lo[a..].find('>')? + 1;
                let b = a + lo[a..].find("</title")?;
                Some(html[a..b].trim().replace("&amp;", "&").replace("&#39;", "'").replace("&quot;", "\"").replace("&lt;", "<").replace("&gt;", ">"))
            })
            .filter(|t| !t.is_empty());
        send(Event::Title(id, title));
    });
}

/// PasteAll: each → copy, 100ms, Ctrl+V, 150ms.
pub fn paste_all(items: Vec<Item>) {
    std::thread::spawn(move || {
        for it in items {
            copy_item_to_clipboard(&it);
            std::thread::sleep(Duration::from_millis(100));
            crate::winsys::send_ctrl_v();
            std::thread::sleep(Duration::from_millis(150));
        }
        send(Event::Status("Pasted All!".into()));
    });
}

pub fn delete_item(core: &CoreRef, it: Item) {
    let core = core.clone();
    std::thread::spawn(move || {
        if it.is_file {
            crate::storage::delete_file(&it.file_path);
            let name = if it.file_name.is_empty() {
                std::path::Path::new(&it.file_path).file_name().map(|n| n.to_string_lossy().into_owned()).unwrap_or_default()
            } else {
                it.file_name.clone()
            };
            let _ = std::fs::remove_file(crate::paths::temp_drag_dir().join(name));
        }
        core.db.lock().unwrap().delete(it.id);
    });
}

pub fn clear_all(core: &CoreRef) {
    let core = core.clone();
    std::thread::spawn(move || {
        {
            let db = core.db.lock().unwrap();
            for it in db.all_items() {
                if !it.is_pinned && !it.is_snippet && it.is_file {
                    crate::storage::delete_file(&it.file_path);
                }
            }
            db.delete_all_except_pinned();
        }
        let _ = std::fs::remove_dir_all(crate::paths::temp_drag_dir());
        core.reset_dedupe();
        crate::paths::storage_dir();
        crate::msgwin::ensure_clipboard_listener();
        send(Event::ItemsChanged);
        send(Event::Status("History Cleared!".into()));
    });
}

// ───────────────────────── thumbnails ─────────────────────────

fn to_color_image(img: &image::RgbaImage) -> egui::ColorImage {
    egui::ColorImage::from_rgba_unmultiplied([img.width() as usize, img.height() as usize], img.as_raw())
}

/// Image: decode, downscale to 200px tall (DecodePixelHeight=200), "W x H".
pub fn load_image_thumb(id: i64, path: String) {
    std::thread::spawn(move || {
        let Ok(img) = image::open(&path) else { return };
        let (w, h) = (img.width(), img.height());
        let th = img.thumbnail(u32::MAX, 200.min(h.max(1))).to_rgba8();
        send(Event::Thumb(id, to_color_image(&th), format!("{w} x {h}")));
    });
}

/// Video: Shell thumbnail (IShellItemImageFactory, 256px).
pub fn load_video_thumb(id: i64, path: String) {
    std::thread::spawn(move || {
        if let Some(img) = shell_thumbnail(&path, 256) {
            send(Event::Thumb(id, to_color_image(&img), "Video".into()));
        }
    });
}

fn shell_thumbnail(path: &str, size: i32) -> Option<image::RgbaImage> {
    use windows::core::PCWSTR;
    use windows::Win32::Foundation::SIZE;
    use windows::Win32::Graphics::Gdi::*;
    use windows::Win32::System::Com::*;
    use windows::Win32::UI::Shell::*;
    unsafe {
        let _ = CoInitializeEx(None, COINIT_APARTMENTTHREADED);
        let w = crate::winsys::wide(path);
        let f: IShellItemImageFactory = SHCreateItemFromParsingName(PCWSTR(w.as_ptr()), None).ok()?;
        let hb = f.GetImage(SIZE { cx: size, cy: size }, SIIGBF_RESIZETOFIT).ok()?;
        let mut bm = BITMAP::default();
        GetObjectW(hb.into(), std::mem::size_of::<BITMAP>() as i32, Some(&mut bm as *mut _ as *mut _));
        let (bw, bh) = (bm.bmWidth, bm.bmHeight);
        let mut bi = BITMAPINFO {
            bmiHeader: BITMAPINFOHEADER {
                biSize: std::mem::size_of::<BITMAPINFOHEADER>() as u32,
                biWidth: bw,
                biHeight: -bh,
                biPlanes: 1,
                biBitCount: 32,
                ..Default::default()
            },
            ..Default::default()
        };
        let mut buf = vec![0u8; (bw * bh * 4) as usize];
        let dc = GetDC(None);
        GetDIBits(dc, hb, 0, bh as u32, Some(buf.as_mut_ptr() as *mut _), &mut bi, DIB_RGB_COLORS);
        ReleaseDC(None, dc);
        let _ = DeleteObject(hb.into());
        let any_alpha = buf.chunks(4).any(|p| p[3] != 0);
        for p in buf.chunks_mut(4) {
            p.swap(0, 2);
            if !any_alpha {
                p[3] = 255;
            }
        }
        image::RgbaImage::from_raw(bw as u32, bh as u32, buf)
    }
}

pub fn url_host(t: &str) -> Option<String> {
    let t = t.trim();
    let rest = t.strip_prefix("http://").or_else(|| t.strip_prefix("https://")).or_else(|| {
        let l = t.to_lowercase();
        if l.starts_with("http://") { Some(&t[7..]) } else if l.starts_with("https://") { Some(&t[8..]) } else { None }
    })?;
    if rest.contains(char::is_whitespace) {
        return None;
    }
    let host = rest.split(['/', '?', '#']).next()?.rsplit('@').next()?;
    let host = host.split(':').next()?.to_lowercase();
    (!host.is_empty()).then_some(host)
}

/// Favicon via Google s2, cached 7 days in data\favicons\{host}.png.
pub fn load_favicon(host: String) {
    std::thread::spawn(move || {
        let safe: String = host.chars().map(|c| if c.is_ascii_alphanumeric() { c } else { '_' }).collect();
        let file = crate::paths::favicon_dir().join(format!("{safe}.png"));
        let fresh = std::fs::metadata(&file)
            .ok()
            .and_then(|m| m.modified().ok())
            .and_then(|m| m.elapsed().ok())
            .map(|e| e.as_secs() < 7 * 86400)
            .unwrap_or(false);
        if !fresh {
            let url = format!("https://www.google.com/s2/favicons?domain={host}&sz=32");
            if let Ok(mut r) = ureq::get(&url).call() {
                if let Ok(b) = r.body_mut().read_to_vec() {
                    let _ = std::fs::write(&file, b);
                }
            }
        }
        if let Ok(img) = image::open(&file) {
            send(Event::Favicon(host, to_color_image(&img.to_rgba8())));
        }
    });
}
