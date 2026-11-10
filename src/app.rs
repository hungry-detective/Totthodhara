//! The shelf (Qt-edition design): state, events, layout, painting, popups.
use crate::db::Item;
use crate::events::{self, Event};
use crate::icons::{self, fam, Meter};
use crate::popup::PopupWin;
use crate::settings::Settings;
use crate::theme::{self, OsState, Pal};
use crate::update::UpdateInfo;
use crate::winsys::{self, Hwnd, IRect};
use crate::worker::{self, CoreRef};
use egui::{pos2, vec2, Align2, Color32, FontFamily, FontId, Id, Rect, Sense, Shape, Stroke, StrokeKind, TextureHandle, Ui};
use std::collections::HashMap;
use std::sync::atomic::Ordering::SeqCst;
use std::sync::mpsc::Receiver;

pub fn fid(size: f32, family: &str) -> FontId {
    match family {
        "" | "regular" => FontId::new(size, FontFamily::Proportional),
        f => FontId::new(size, fam(f)),
    }
}

pub fn ease_out(t: f32) -> f32 {
    1.0 - (1.0 - t.clamp(0.0, 1.0)).powi(3)
}

/// AppState.barHeight (cardHeight = bar − 5).
pub fn bar_h(size: &str) -> f32 {
    match size {
        "Small" => 26.0,
        "Large" => 34.0,
        _ => 28.0,
    }
}

/// Top-left that puts a galley's CAPITALS (not its line box) on `c`.
/// Font line boxes include descender space, so plain centering sits caps high.
pub fn caps_center(g: &egui::Galley, c: egui::Pos2, size: f32) -> egui::Pos2 {
    let base = g.rows.first().and_then(|r| r.row.glyphs.first().map(|gl| r.pos.y + gl.pos.y)).unwrap_or(g.size().y * 0.75);
    pos2(c.x - g.size().x / 2.0, (c.y - (base - 0.35 * size)).round())
}

/// Baseline offset of a single-row galley from its top.
pub fn baseline(g: &egui::Galley) -> f32 {
    g.rows.first().and_then(|r| r.row.glyphs.first().map(|gl| r.pos.y + gl.pos.y)).unwrap_or(g.size().y * 0.75)
}

/// Paint `t` left-aligned at `x` with its caps/digits centered on `cy`
/// (the shelf's one shared centerline — icons are centered on it too).
pub fn text_vc(p: &egui::Painter, x: f32, cy: f32, t: &str, size: f32, family: &str, color: Color32) -> f32 {
    let g = p.layout_no_wrap(t.to_string(), fid(size, family), color);
    let at = caps_center(&g, pos2(0.0, cy), size);
    let w = g.size().x;
    p.galley(pos2(x, at.y), g, color);
    w
}

pub fn text_w(ctx: &egui::Context, text: &str, font: FontId) -> f32 {
    ctx.fonts_mut(|f| f.layout_no_wrap(text.to_string(), font, Color32::WHITE).size().x)
}

pub fn elide(ctx: &egui::Context, text: &str, font: FontId, color: Color32, max_w: f32) -> std::sync::Arc<egui::Galley> {
    let mut job = egui::text::LayoutJob::single_section(text.to_string(), egui::TextFormat::simple(font, color));
    job.wrap = egui::text::TextWrapping { max_width: max_w.max(1.0), max_rows: 1, break_anywhere: true, overflow_character: Some('…') };
    ctx.fonts_mut(|f| f.layout_job(job))
}

// ───────────────────────── cards ─────────────────────────

#[derive(Clone, Copy, PartialEq, Debug)]
pub enum Kind {
    Text,
    Url,
    Color,
    Image,
    File,
}

pub struct Card {
    pub it: Item,
    pub pos: usize,
    pub kind: Kind,
    pub title: String,
    pub host: String,
    pub ext: String,
    pub selected: bool,
    pub flash: Option<(String, f64)>,
    pub locked_w: Option<f32>,
    pub pulse: Option<f64>,
    pub appear: Option<f64>,
    pub removing: Option<f64>,
}

/// ClipboardService::shortTitle — first 4 words + " …", else left(60).
pub fn short_title(t: &str) -> String {
    let flat = t.replace(['\r', '\n', '\t'], " ").trim().to_string();
    let words: Vec<&str> = flat.split(' ').filter(|w| !w.is_empty()).collect();
    if words.len() <= 4 {
        flat.chars().take(60).collect()
    } else {
        format!("{} …", words[..4].join(" "))
    }
}

fn is_color_text(t: &str) -> bool {
    let t = t.trim();
    t.starts_with('#') && matches!(t.len(), 4 | 7 | 9) && t[1..].chars().all(|c| c.is_ascii_hexdigit())
}

impl Card {
    fn new(it: Item, pos: usize, dims: Option<(u32, u32)>) -> Self {
        let text = it.text_content.trim();
        let (kind, title, host) = if it.is_file {
            if it.is_image() {
                (Kind::Image, dims.map(|(w, h)| format!("{w}x{h}")).unwrap_or_else(|| "Image".into()), String::new())
            } else {
                let name = if it.file_name.is_empty() {
                    std::path::Path::new(&it.file_path).file_name().map(|n| n.to_string_lossy().into_owned()).unwrap_or_default()
                } else {
                    it.file_name.clone()
                };
                (Kind::File, name, String::new())
            }
        } else if let Some(h) = worker::url_host(text) {
            (Kind::Url, h.clone(), h)
        } else if is_color_text(text) {
            (Kind::Color, text.to_string(), String::new())
        } else {
            (Kind::Text, short_title(&it.text_content.replace('\0', "")), String::new())
        };
        let ext = if kind == Kind::File {
            std::path::Path::new(&it.file_path).extension().map(|e| e.to_string_lossy().to_uppercase()).unwrap_or_else(|| "FILE".into())
        } else {
            String::new()
        };
        Self { it, pos, kind, title, host, ext, selected: false, flash: None, locked_w: None, pulse: None, appear: None, removing: None }
    }
    fn matches(&self, q: &str) -> bool {
        let q = q.trim().to_lowercase();
        q.is_empty() || self.title.to_lowercase().contains(&q) || self.it.text_content.to_lowercase().contains(&q) || self.it.file_name.to_lowercase().contains(&q)
    }
    fn is_video(&self) -> bool {
        matches!(self.ext.as_str(), "MP4" | "AVI" | "MKV" | "MOV" | "WMV")
    }
    fn is_audio(&self) -> bool {
        matches!(self.ext.as_str(), "MP3" | "WAV" | "WMA" | "M4A" | "OGG" | "FLAC" | "AAC")
    }
    fn is_code(&self) -> bool {
        matches!(
            self.ext.as_str(),
            "JS" | "TS" | "PY" | "CS" | "CPP" | "H" | "C" | "JAVA" | "GO" | "RS" | "RB" | "PHP" | "SWIFT" | "KT" | "SQL" | "SH" | "PS1" | "BAT" | "CMD" | "JSON" | "XML" | "HTML" | "CSS" | "SCSS" | "YML" | "YAML"
        )
    }
}

// ───────────────────────── menus ─────────────────────────

struct MeterDrag {
    group: Group,
    grab: f32,
    w: f32,
    y: f32,
}

#[derive(Clone, Copy, PartialEq, Debug)]
pub enum Group {
    Clock,
    Hw,
    Net,
}

#[derive(Clone, PartialEq, Debug)]
pub enum MenuId {
    Pin,
    Snippet,
    Tool(&'static str),
    ColorHex,
    ColorRgb,
    ColorHsl,
    CopyTitle,
    CopyMail,
    ComposeMail,
    CopyPhone,
    Delete,
    Search,
    ClearHistory,
    Settings,
    Exit,
    ToggleShelf,
    Move(Group),
    ChangeTime,
    Sub,
}

#[derive(Clone)]
pub struct MenuRow {
    pub id: MenuId,
    pub glyph: char,
    pub label: String,
    pub sub: Option<Vec<MenuRow>>,
}

fn row(id: MenuId, glyph: char, label: &str) -> MenuRow {
    MenuRow { id, glyph, label: label.into(), sub: None }
}

#[derive(Clone, Copy, PartialEq)]
enum MenuKind {
    Clip(i64),
    Gear,
    Tray,
    Meter,
}

struct Menu {
    kind: MenuKind,
    rows: Vec<MenuRow>,
    sub: Option<Vec<MenuRow>>,
    win: PopupWin,
    width: f32,
    anchor_x: i32,
    was_focused: bool,
}

enum PreviewKind {
    Image(i64),
    Text(String),
    File { glyph: char, title: String, sub: String },
}

struct Preview {
    card: i64,
    kind: PreviewKind,
    anchor_x: i32,
    win: PopupWin,
}

struct Toast {
    text: String,
    until: f64,
    win: PopupWin,
}

#[derive(Clone, Debug, PartialEq)]
pub enum UpdState {
    Idle,
    Checking,
    Available,
    Downloading,
    Installing,
    UpToDate,
}

pub struct Upd {
    pub state: UpdState,
    pub version: String,
    pub note: String,
    pub notes_full: String,
    pub details_open: bool,
    pub info: Option<UpdateInfo>,
    pub uptodate_at: f64,
    pub auto: bool,
    pub pct: f32,
    pub exit_at: Option<f64>,
}

// ───────────────────────── app ─────────────────────────

pub struct App {
    core: CoreRef,
    rx: Receiver<Event>,
    pub s: Settings,
    saved: Settings,
    pub os: OsState,
    pub pal: Pal,
    hwnd: Hwnd,
    shelf_px: IRect,
    shelf_visible: bool,
    fs_hidden: bool,
    first_load: bool,
    cards: Vec<Card>,
    dims: HashMap<i64, (u32, u32)>,
    thumbs: HashMap<i64, TextureHandle>,
    favicons: HashMap<String, Option<TextureHandle>>,
    scroll: f32,
    scroll_anim: Option<(f32, f32, f64)>,
    max_scroll: f32,
    menu: Option<Menu>,
    preview: Option<Preview>,
    hover_card: Option<(i64, f64, i32)>,
    toast: Option<Toast>,
    search_open: bool,
    search_text: String,
    search_focus_req: bool,
    sys: crate::sysmon::Sample,
    pub clocks: crate::clock::Clocks,
    last_clock: f64,
    last_os_poll: f64,
    pub settings_win: Option<crate::settings_ui::SettingsWin>,
    pub upd: Upd,
    pub app_tex: Option<TextureHandle>,
    pending_reload: bool,
    painted_once: bool,
    glass_key: String,
    tray_click_at: Option<f64>,
    title_w: HashMap<(String, u32), f32>,
    meter_drag: Option<MeterDrag>,
    group_rects: HashMap<u8, Rect>,
    drag_out: Option<i64>,
    ignore_drops_until: f64,
}

impl App {
    pub fn new(cc: &eframe::CreationContext, core: CoreRef) -> Self {
        icons::install_fonts(&cc.egui_ctx);
        cc.egui_ctx.options_mut(|o| o.zoom_with_keyboard = false);
        let rx = events::init(cc.egui_ctx.clone());
        let s = core.settings();
        let os = OsState::read();
        let pal = theme::build(&s, &os);
        let mut clocks = crate::clock::Clocks::new();
        clocks.update(&s.zone_a, &s.zone_b);
        let auto = s.auto_check_updates;
        let app = Self {
            core,
            rx,
            s: s.clone(),
            saved: s,
            os,
            pal,
            hwnd: Hwnd(0),
            shelf_px: IRect::default(),
            shelf_visible: true,
            fs_hidden: false,
            first_load: true,
            cards: vec![],
            dims: HashMap::new(),
            thumbs: HashMap::new(),
            favicons: HashMap::new(),
            scroll: 0.0,
            scroll_anim: None,
            max_scroll: 0.0,
            menu: None,
            preview: None,
            hover_card: None,
            toast: None,
            search_open: false,
            search_text: String::new(),
            search_focus_req: false,
            sys: Default::default(),
            clocks,
            last_clock: 0.0,
            last_os_poll: 0.0,
            settings_win: None,
            upd: Upd { state: UpdState::Idle, version: String::new(), note: String::new(), notes_full: String::new(), details_open: false, info: None, uptodate_at: 0.0, auto, pct: 0.0, exit_at: None },
            app_tex: None,
            pending_reload: true,
            painted_once: false,
            glass_key: String::new(),
            tray_click_at: None,
            title_w: HashMap::new(),
            meter_drag: None,
            group_rects: HashMap::new(),
            drag_out: None,
            ignore_drops_until: 0.0,
        };
        app.apply_monitor_flags();
        // `Totthodhara.exe --settings` opens Settings on start (Qt parity;
        // also used for screen-free verification).
        let mut app = app;
        if std::env::args().any(|a| a == "--settings") {
            app.settings_win = Some(crate::settings_ui::SettingsWin::new(0, &app.clocks));
        }
        if auto {
            crate::update::check_async(false);
        }
        app
    }

    fn apply_monitor_flags(&self) {
        crate::sysmon::CPU_ENABLED.store(self.s.show_cpu_ram_monitor, SeqCst);
        crate::sysmon::NET_ENABLED.store(self.s.show_network_monitor, SeqCst);
    }

    pub fn bar(&self) -> f32 {
        bar_h(&self.s.bar_size)
    }
    /// Content scale: everything on the shelf (fonts, icons, gaps, widths)
    /// follows the bar height so Small/Medium/Large stay proportional.
    pub fn k(&self) -> f32 {
        self.bar() / 28.0
    }
    pub fn card_h(&self) -> f32 {
        self.bar() - 5.0
    }
    fn bar_radius(&self) -> f32 {
        if self.s.rounded_corners { (self.bar() / 2.0).round() } else { 0.0 }
    }

    pub fn toast(&mut self, text: &str, now: f64) {
        self.toast = Some(Toast { text: text.into(), until: now + 1.4, win: PopupWin::new("toast", now, false) });
    }

    // ───────── data ─────────

    fn reload(&mut self, now: f64) {
        let lim = self.s.display_limit();
        let mut items = self.core.db.lock().unwrap().items(lim.max(500));
        items.sort_by(|a, b| b.is_snippet.cmp(&a.is_snippet).then(b.is_pinned.cmp(&a.is_pinned)).then(b.date_added.cmp(&a.date_added)));
        items.truncate(lim);
        let mut old: HashMap<i64, Card> = self.cards.drain(..).filter(|c| c.removing.is_none()).map(|c| (c.it.id, c)).collect();
        let mut out = Vec::with_capacity(items.len());
        for (i, it) in items.into_iter().enumerate() {
            let id = it.id;
            if it.is_image() && !self.dims.contains_key(&id) {
                if let Ok(d) = image::image_dimensions(&it.file_path) {
                    self.dims.insert(id, d);
                }
            }
            let mut c = Card::new(it, i + 1, self.dims.get(&id).copied());
            if let Some(o) = old.remove(&id) {
                c.selected = o.selected;
                c.flash = o.flash;
                c.locked_w = o.locked_w;
                c.pulse = o.pulse;
                c.appear = o.appear;
            } else if !self.first_load {
                c.appear = Some(now);
            }
            if c.kind == Kind::Image && !self.thumbs.contains_key(&id) {
                worker::load_image_thumb(id, c.it.file_path.clone());
            }
            if c.kind == Kind::Url && !self.favicons.contains_key(&c.host) {
                self.favicons.insert(c.host.clone(), None);
                worker::load_favicon(c.host.clone());
            }
            out.push(c);
        }
        self.first_load = false;
        let live: std::collections::HashSet<i64> = out.iter().map(|c| c.it.id).collect();
        self.thumbs.retain(|k, _| live.contains(k));
        self.cards = out;
    }

    fn renumber(&mut self) {
        let mut n = 0;
        for c in &mut self.cards {
            if c.removing.is_none() {
                n += 1;
                c.pos = n;
            }
        }
    }

    fn set_flash(&mut self, id: i64, text: &str, now: f64) {
        let w = self.cards.iter().find(|c| c.it.id == id).map(|c| c.locked_w.unwrap_or(0.0));
        let cur_w = self.cards.iter().position(|c| c.it.id == id).map(|i| self.card_w_raw(i));
        if let Some(c) = self.cards.iter_mut().find(|c| c.it.id == id) {
            // Width locks while flashing so neighbors never move.
            if w.unwrap_or(0.0) <= 0.0 {
                c.locked_w = cur_w;
            }
            c.flash = Some((text.into(), now + 1.2));
            c.pulse = Some(now);
        }
    }

    fn card(&self, id: i64) -> Option<&Card> {
        self.cards.iter().find(|c| c.it.id == id)
    }

    fn delete_card(&mut self, id: i64, now: f64) {
        if let Some(c) = self.cards.iter_mut().find(|c| c.it.id == id) {
            if c.removing.is_none() {
                c.removing = Some(now);
                c.selected = false;
                worker::delete_item(&self.core, c.it.clone());
            }
        }
    }

    // ───────── window ─────────

    fn ensure_hwnd(&mut self, ctx: &egui::Context) {
        if !self.hwnd.is_null() {
            return;
        }
        let h = winsys::find_own_window("Totthodhara");
        if h.is_null() {
            return;
        }
        self.hwnd = h;
        events::SHELF_HWND.store(h.0, SeqCst);
        winsys::subclass_root(h);
        winsys::make_noactivate_tool(h);
        winsys::set_border_none(h);
        if let Some((kind, _)) = showcase() {
            // README showcase: render off-screen, never reserve screen space.
            let dpi = winsys::dpi_of_window(h);
            let (w, hh) = if kind == "bar" { (1366.0, self.bar()) } else { (720.0, 560.0) };
            winsys::move_window(h, -6000, 0, (w * dpi) as i32, (hh * dpi + 0.5) as i32, false);
            ctx.send_viewport_cmd(egui::ViewportCommand::InnerSize(egui::vec2(w, hh)));
            ctx.send_viewport_cmd(egui::ViewportCommand::OuterPosition(egui::pos2(-6000.0, 0.0)));
            ctx.request_repaint();
            return;
        }
        winsys::appbar_register(h);
        self.place_shelf();
        ctx.request_repaint();
    }

    pub fn place_shelf(&mut self) {
        if self.hwnd.is_null() {
            return;
        }
        let dpi = winsys::dpi_of_window(self.hwnd);
        let px = (self.bar() * dpi + 0.5) as i32;
        let vis = self.shelf_visible && !self.fs_hidden;
        self.shelf_px = winsys::appbar_set_pos(self.hwnd, self.s.shelf_position == "Top", px, vis);
        self.glass_key.clear();
        self.apply_glass();
    }

    fn apply_glass(&mut self) {
        if self.hwnd.is_null() {
            return;
        }
        let dpi = winsys::dpi_of_window(self.hwnd);
        let (w, h) = (self.shelf_px.r - self.shelf_px.l, self.shelf_px.b - self.shelf_px.t);
        let r = (self.bar_radius() * dpi + 0.5) as i32;
        let key = format!("{}|{w}|{h}|{r}", self.pal.glass);
        if key != self.glass_key {
            self.glass_key = key;
            winsys::set_blur_region(self.hwnd, w, h, r, self.pal.glass);
            winsys::set_border_none(self.hwnd);
        }
    }

    fn set_shelf_visible(&mut self, v: bool) {
        self.shelf_visible = v;
        if self.hwnd.is_null() {
            return;
        }
        if v {
            if !self.fs_hidden {
                winsys::appbar_register(self.hwnd);
                self.place_shelf();
                winsys::show_window(self.hwnd, true);
            }
        } else {
            self.fs_hidden = false;
            winsys::appbar_unregister(self.hwnd);
            winsys::show_window(self.hwnd, false);
            self.menu = None;
            self.preview = None;
        }
        events::FS_CHECK.store(v, SeqCst);
    }

    pub fn open_settings(&mut self, section: usize) {
        match &mut self.settings_win {
            Some(w) => {
                w.section = section;
                winsys::force_foreground(w.hwnd());
            }
            None => self.settings_win = Some(crate::settings_ui::SettingsWin::new(section, &self.clocks)),
        }
        // About always has an answer: silent cache-backed check while empty.
        if self.upd.note.is_empty() && self.upd.state == UpdState::Idle {
            self.upd.state = UpdState::Checking;
            crate::update::check_async(false);
        }
    }

    pub fn check_updates(&mut self) {
        self.upd.state = UpdState::Checking;
        crate::update::check_async(true);
    }

    pub fn exit(&mut self) {
        if !self.hwnd.is_null() {
            winsys::appbar_unregister(self.hwnd);
        }
        crate::msgwin::remove_tray();
        self.s.save();
        std::process::exit(0);
    }

    fn popup_y(&self, h: i32) -> i32 {
        let gap = (8.0 * winsys::dpi_of_window(self.hwnd)) as i32;
        if self.s.shelf_position == "Top" { self.shelf_px.b + gap } else { self.shelf_px.t - h - gap }
    }

    fn screen_x(&self, x: f32) -> i32 {
        self.shelf_px.l + (x * winsys::dpi_of_window(self.hwnd)).round() as i32
    }

    // ───────── events ─────────

    fn handle_events(&mut self, ctx: &egui::Context) {
        let now = ctx.input(|i| i.time);
        while let Ok(e) = self.rx.try_recv() {
            match e {
                Event::ClipboardChanged => worker::on_clipboard_changed(&self.core),
                Event::ItemsChanged => self.pending_reload = true,
                Event::Hotkey => {
                    let v = !self.shelf_visible;
                    self.set_shelf_visible(v);
                }
                Event::TrayClick => self.tray_click_at = Some(now),
                Event::TrayDoubleClick => {
                    self.tray_click_at = None;
                    let v = !self.shelf_visible;
                    self.set_shelf_visible(v);
                }
                Event::TrayMenu(x, _) => {
                    let label = if self.shelf_visible { "Hide Shelf" } else { "Show Shelf" };
                    let rows = vec![row(MenuId::ToggleShelf, icons::SHOW, label), row(MenuId::Settings, icons::SETTINGS, "Settings"), row(MenuId::Exit, icons::CLOSE, "Exit")];
                    self.open_menu(MenuKind::Tray, rows, 170.0, x, now);
                }
                Event::GlobalMouseDown(x, y) => {
                    if let Some(m) = &self.menu {
                        let in_menu = m.win.rect().map(|r| r.contains(x, y)).unwrap_or(false);
                        if !in_menu && !self.shelf_px.contains(x, y) {
                            self.menu = None;
                        }
                    }
                    if self.search_open && !self.shelf_px.contains(x, y) && self.search_text.is_empty() {
                        self.close_search();
                    }
                }
                Event::Fullscreen(hide) => {
                    self.fs_hidden = hide;
                    if self.shelf_visible && !self.hwnd.is_null() {
                        if hide {
                            winsys::show_window(self.hwnd, false);
                            winsys::appbar_unregister(self.hwnd);
                        } else {
                            winsys::appbar_register(self.hwnd);
                            self.place_shelf();
                            winsys::show_window(self.hwnd, true);
                        }
                    }
                }
                Event::ThemeMaybeChanged => self.refresh_os(),
                Event::SysMon(s) => self.sys = s,
                Event::Thumb(id, img, _) => {
                    let tex = ctx.load_texture(format!("thumb{id}"), img, egui::TextureOptions::LINEAR);
                    self.thumbs.insert(id, tex);
                }
                Event::Favicon(host, img) => {
                    let tex = ctx.load_texture(format!("fav{host}"), img, egui::TextureOptions::LINEAR);
                    self.favicons.insert(host, Some(tex));
                }
                Event::Status(t) => self.toast(&t, now),
                Event::Title(id, t) => match t {
                    Some(t) => {
                        worker::paste_text(t);
                        self.set_flash(id, "Title copied!", now);
                    }
                    None => self.set_flash(id, "No title", now),
                },
                Event::Update(m) => self.on_update(m, now),
                Event::Quit => self.exit(),
            }
        }
        if winsys::APPBAR_POS_CHANGED.swap(false, SeqCst) {
            self.place_shelf();
        }
        if let Some(t) = self.tray_click_at {
            if now - t > 0.26 {
                self.tray_click_at = None;
                let v = !self.shelf_visible;
                self.set_shelf_visible(v);
            }
        }
        if self.pending_reload {
            self.pending_reload = false;
            self.reload(now);
        }
        let before = self.cards.len();
        self.cards.retain(|c| c.removing.map(|t| now - t < 0.16).unwrap_or(true));
        if self.cards.len() != before {
            self.renumber();
        }
        for c in &mut self.cards {
            if c.flash.as_ref().map(|f| now > f.1).unwrap_or(false) {
                c.flash = None;
                c.locked_w = None;
            }
        }
        if now - self.last_clock >= 1.0 {
            self.last_clock = now;
            self.clocks.update(&self.s.zone_a, &self.s.zone_b);
        }
        // ThemeService backs the window messages with a 1.5s poll.
        if now - self.last_os_poll >= 1.5 {
            self.last_os_poll = now;
            self.refresh_os();
        }
        if self.upd.exit_at.map(|t| now >= t).unwrap_or(false) {
            self.exit();
        }
        if self.upd.state == UpdState::UpToDate && now - self.upd.uptodate_at > 2.5 {
            self.upd.state = UpdState::Idle;
        }
        if self.toast.as_ref().map(|t| now > t.until).unwrap_or(false) {
            self.toast = None;
        }
    }

    fn refresh_os(&mut self) {
        let os = OsState::read();
        if os != self.os {
            self.os = os;
            self.pal = theme::build(&self.s, &self.os);
            self.apply_glass();
        }
    }

    pub fn start_install(&mut self) {
        if let Some(info) = self.upd.info.clone() {
            self.upd.state = UpdState::Downloading;
            self.upd.pct = 0.0;
            self.upd.note = "Downloading…".into();
            crate::update::download_and_install(info);
        }
    }

    fn on_update(&mut self, m: crate::update::UpdateMsg, now: f64) {
        use crate::update::UpdateMsg;
        match m {
            UpdateMsg::Checked { info, manual } => {
                let auto = self.upd.auto && !manual;
                self.upd.auto = false;
                if matches!(self.upd.state, UpdState::Downloading | UpdState::Installing) {
                    return;
                }
                if !info.error_message.is_empty() {
                    self.upd.state = UpdState::Idle;
                    if manual {
                        self.upd.note = format!("Could not check for updates: {}", info.error_message);
                    }
                } else if info.is_update_available {
                    self.upd.state = UpdState::Available;
                    self.upd.version = info.latest_version.clone();
                    let head = info.release_notes.lines().map(str::trim).find(|l| !l.is_empty()).unwrap_or("").trim_start_matches('#').trim().to_string();
                    let mut cut = head;
                    if cut.chars().count() > 140 {
                        cut = cut.chars().take(140).collect();
                        if let Some(sp) = cut.rfind(' ').filter(|&sp| sp > 60) {
                            cut.truncate(sp);
                        }
                        cut.push('…');
                    }
                    self.upd.note = format!("New in {}{}", info.latest_version, if cut.is_empty() { String::new() } else { format!(": {cut}") });
                    self.upd.notes_full = info.release_notes.trim().to_string();
                    self.upd.details_open = false;
                    self.upd.info = Some(info);
                    if auto {
                        self.open_settings(4);
                    }
                } else {
                    self.upd.state = if manual { UpdState::UpToDate } else { UpdState::Idle };
                    self.upd.uptodate_at = now;
                    self.upd.note.clear();
                }
            }
            UpdateMsg::Progress { got, total } => {
                self.upd.state = UpdState::Downloading;
                let mb = |b: u64| b as f64 / 1_048_576.0;
                self.upd.pct = if total > 0 { (got as f32 / total as f32 * 100.0).min(100.0) } else { 0.0 };
                self.upd.note = if total > 0 {
                    format!("Downloading… {:.0}%  ({:.1} MB / {:.1} MB)", self.upd.pct, mb(got), mb(total))
                } else {
                    format!("Downloading… {:.1} MB", mb(got))
                };
            }
            UpdateMsg::Installing => {
                self.upd.state = UpdState::Installing;
                self.upd.pct = 100.0;
                self.upd.note = "Verified — installing, the app will restart…".into();
                self.upd.exit_at = Some(now + 1.2);
            }
            UpdateMsg::Failed(e) => {
                self.upd.state = UpdState::Idle;
                self.upd.note = e;
            }
        }
    }

    fn settings_changed(&mut self, ctx: &egui::Context) {
        if self.s == self.saved {
            return;
        }
        let old = self.saved.clone();
        let s = self.s.clone();
        *self.core.settings.write().unwrap() = s.clone();
        s.save();
        self.pal = theme::build(&self.s, &self.os);
        if old.bar_size != s.bar_size || old.shelf_position != s.shelf_position || old.rounded_corners != s.rounded_corners {
            self.place_shelf();
        }
        self.apply_glass();
        if old.start_with_windows != s.start_with_windows {
            winsys::set_startup(s.start_with_windows);
        }
        if old.max_history_items != s.max_history_items {
            let c = self.core.clone();
            std::thread::spawn(move || {
                worker::trim_history(&c);
                events::send(Event::ItemsChanged);
            });
        }
        if old.auto_clean_hours != s.auto_clean_hours {
            worker::startup_cleanup(&self.core);
        }
        if old.zone_a != s.zone_a || old.zone_b != s.zone_b {
            self.clocks.update(&s.zone_a, &s.zone_b);
        }
        self.apply_monitor_flags();
        self.saved = self.s.clone();
        ctx.request_repaint();
    }

    fn close_search(&mut self) {
        self.search_open = false;
        self.search_text.clear();
        events::ALLOW_ACTIVATE.store(false, SeqCst);
        winsys::set_noactivate(self.hwnd, true);
    }

    fn open_search(&mut self) {
        if self.s.hide_clipboard {
            return;
        }
        self.search_open = true;
        self.search_focus_req = true;
        events::ALLOW_ACTIVATE.store(true, SeqCst);
        winsys::set_noactivate(self.hwnd, false);
        winsys::force_foreground(self.hwnd);
    }

    // ───────── menus ─────────

    fn open_menu(&mut self, kind: MenuKind, rows: Vec<MenuRow>, width: f32, anchor_x: i32, now: f64) {
        self.preview = None;
        self.hover_card = None;
        self.menu = Some(Menu { kind, rows, sub: None, win: PopupWin::new("menu", now, true), width, anchor_x, was_focused: false });
    }

    fn clip_menu_rows(&self, c: &Card) -> Vec<MenuRow> {
        let mut r = vec![
            row(MenuId::Pin, icons::PIN, if c.it.is_pinned { "Unpin" } else { "Pin" }),
            row(MenuId::Snippet, icons::STAR_OUTLINE, if c.it.is_snippet { "Remove snippet" } else { "Save as snippet" }),
        ];
        if c.kind == Kind::Text {
            let g = icons::EDIT;
            let t = [("t-upper", "UPPERCASE"), ("t-lower", "lowercase"), ("t-title", "Title Case"), ("t-trim", "Trim lines"), ("t-sort", "Sort lines A–Z"), ("t-unique", "Unique lines"), ("t-join", "Join lines")];
            r.push(MenuRow { id: MenuId::Sub, glyph: g, label: "Transform…".into(), sub: Some(t.iter().map(|(id, l)| row(MenuId::Tool(id), g, l)).collect()) });
            if self.s.show_dev_tools {
                let g = icons::CODE;
                let t = [("c-json", "Format JSON"), ("c-b64enc", "Base64 encode"), ("c-b64dec", "Base64 decode"), ("c-urlenc", "URL encode"), ("c-urldec", "URL decode")];
                r.push(MenuRow { id: MenuId::Sub, glyph: g, label: "Code tools…".into(), sub: Some(t.iter().map(|(id, l)| row(MenuId::Tool(id), g, l)).collect()) });
            }
        }
        if c.kind == Kind::Color {
            r.push(row(MenuId::ColorHex, icons::PALETTE, "Copy as HEX"));
            r.push(row(MenuId::ColorRgb, icons::PALETTE, "Copy as RGB"));
            r.push(row(MenuId::ColorHsl, icons::PALETTE, "Copy as HSL"));
        }
        if c.kind == Kind::Url {
            r.push(row(MenuId::CopyTitle, icons::LINK, "Copy page title"));
        }
        if matches!(c.kind, Kind::Text | Kind::Url) {
            if crate::tools::find_mail(&c.it.text_content).is_some() {
                r.push(row(MenuId::CopyMail, icons::MAIL, "Copy email"));
                r.push(row(MenuId::ComposeMail, icons::MAIL, "Compose email"));
            }
            if crate::tools::find_phone(&c.it.text_content).is_some() {
                r.push(row(MenuId::CopyPhone, icons::PHONE, "Copy number"));
            }
        }
        r.push(row(MenuId::Delete, icons::DELETE, "Delete"));
        r
    }

    fn run_menu(&mut self, kind: MenuKind, id: MenuId, now: f64) {
        match (kind, id) {
            (_, MenuId::Search) => self.open_search(),
            (_, MenuId::ClearHistory) => {
                for c in &mut self.cards {
                    if !c.it.is_pinned && !c.it.is_snippet && c.removing.is_none() {
                        c.removing = Some(now);
                    }
                }
                worker::clear_all(&self.core);
                self.search_text.clear();
            }
            (_, MenuId::Settings) => self.open_settings(0),
            (_, MenuId::Exit) => self.exit(),
            (_, MenuId::ToggleShelf) => {
                let v = !self.shelf_visible;
                self.set_shelf_visible(v);
            }
            (_, MenuId::Move(g)) => match g {
                Group::Clock => self.s.clock_on_left = !self.s.clock_on_left,
                Group::Hw => self.s.hardware_on_left = !self.s.hardware_on_left,
                Group::Net => self.s.monitors_on_left = !self.s.monitors_on_left,
            },
            (_, MenuId::ChangeTime) => self.open_settings(2),
            (MenuKind::Clip(cid), id) => self.run_clip_action(cid, id, now),
            _ => {}
        }
    }

    fn run_clip_action(&mut self, cid: i64, id: MenuId, now: f64) {
        let Some(c) = self.card(cid) else {
            self.toast("Item is gone", now);
            return;
        };
        let text = c.it.text_content.clone();
        match id {
            MenuId::Pin | MenuId::Snippet => {
                let (mut p, mut s) = (c.it.is_pinned, c.it.is_snippet);
                let msg = if id == MenuId::Pin {
                    p = !p;
                    if p { "Pinned" } else { "Unpinned" }
                } else {
                    s = !s;
                    if s { "Saved as snippet" } else { "Snippet removed" }
                };
                self.core.db.lock().unwrap().set_flags(cid, p, s);
                if let Some(c) = self.cards.iter_mut().find(|c| c.it.id == cid) {
                    c.it.is_pinned = p;
                    c.it.is_snippet = s;
                }
                self.pending_reload = true;
                self.set_flash(cid, msg, now);
            }
            MenuId::Tool(tool) => match crate::tools::apply(tool, &text) {
                Err(e) => self.set_flash(cid, &e, now),
                Ok(out) if out == text => self.set_flash(cid, "No change", now),
                Ok(out) => {
                    self.core.db.lock().unwrap().update_text(cid, &out);
                    if let Some(c) = self.cards.iter_mut().find(|c| c.it.id == cid) {
                        c.it.text_content = out.clone();
                        c.title = short_title(&out);
                    }
                    worker::paste_text(out);
                    self.set_flash(cid, &crate::tools::flash_name(tool), now);
                }
            },
            MenuId::ColorHex | MenuId::ColorRgb | MenuId::ColorHsl => match theme::hex_color(&text) {
                None => self.set_flash(cid, "Not a color", now),
                Some(col) => {
                    let (name, out) = crate::tools::color_format(&id, col);
                    worker::paste_text(out);
                    self.set_flash(cid, &format!("{name} copied!"), now);
                }
            },
            MenuId::CopyTitle => {
                self.set_flash(cid, "Fetching title…", now);
                worker::fetch_title(cid, text.trim().to_string());
            }
            MenuId::CopyMail => match crate::tools::find_mail(&text) {
                Some(m) => {
                    worker::paste_text(m);
                    self.set_flash(cid, "Email copied!", now);
                }
                None => self.set_flash(cid, "No email", now),
            },
            MenuId::ComposeMail => match crate::tools::find_mail(&text) {
                Some(m) => {
                    winsys::shell_open(&format!("mailto:{m}"));
                    self.set_flash(cid, "Composing…", now);
                }
                None => self.set_flash(cid, "No email", now),
            },
            MenuId::CopyPhone => match crate::tools::find_phone(&text) {
                Some(p) => {
                    worker::paste_text(p);
                    self.set_flash(cid, "Number copied!", now);
                }
                None => self.set_flash(cid, "No number", now),
            },
            MenuId::Delete => self.delete_card(cid, now),
            _ => {}
        }
    }

    /// Blocking OLE drag of a card; mirrors it to the clipboard first so a
    /// target that only pastes still works (Qt behavior, no auto-paste).
    fn run_drag_out(&mut self, ctx: &egui::Context, id: i64) {
        let Some(c) = self.card(id) else { return };
        let it = c.it.clone();
        let kind = c.kind;
        // The OLE loop blocks repainting, so hide the preview window natively
        // NOW — otherwise it would stay frozen on screen for the whole drag.
        if let Some(p) = self.preview.take() {
            winsys::show_window(p.win.hwnd(), false);
        }
        self.hover_card = None;
        // The OLE loop blocks this frame; egui's frame clock still reads the
        // time the drag STARTED, so time the status from when it ENDED.
        let t0 = std::time::Instant::now();
        let res = if it.is_file {
            if !std::path::Path::new(&it.file_path).exists() {
                let now = ctx.input(|i| i.time);
                self.set_flash(id, "Not a file", now);
                return;
            }
            let dib = if kind == Kind::Image { image::open(&it.file_path).ok().map(|i| crate::clipboard::dib_bytes(&i.to_rgba8())) } else { None };
            // Drag a copy carrying the ORIGINAL file name (storage names are
            // GUIDs — an upload would otherwise show "05876e08-….pdf").
            let path = crate::storage::temp_copy(&it.file_path, &it.file_name).unwrap_or_else(|| it.file_path.clone());
            crate::clipboard::set_files(&[path.clone()]);
            crate::dragdrop::drag_file(self.hwnd, &path, dib)
        } else {
            crate::clipboard::set_text(&it.text_content);
            crate::dragdrop::drag_text(self.hwnd, &it.text_content)
        };
        crate::dragdrop::release_mouse(self.hwnd);
        let now = ctx.input(|i| i.time) + t0.elapsed().as_secs_f64();
        self.ignore_drops_until = now + 1.5;
        match res {
            Some(true) => self.set_flash(id, "Dropped!", now),
            Some(false) => self.set_flash(id, "Drag cancelled", now),
            None => self.set_flash(id, "Can't drag", now),
        }
        ctx.request_repaint();
    }

    fn click_card(&mut self, cid: i64, shift: bool, ctrl: bool, now: f64) {
        if ctrl {
            self.delete_card(cid, now);
            return;
        }
        let Some(c) = self.cards.iter_mut().find(|c| c.it.id == cid) else { return };
        if shift {
            c.selected = !c.selected;
            let msg = if c.selected { "Selected" } else { "Deselected" };
            self.set_flash(cid, msg, now);
            return;
        }
        if c.it.is_file && !std::path::Path::new(&c.it.file_path).exists() {
            self.set_flash(cid, "Not a file", now);
            return;
        }
        worker::paste_item(c.it.clone());
        self.set_flash(cid, "Pasted!", now);
    }

    fn animating(&self, now: f64) -> bool {
        self.scroll_anim.is_some()
            || self.cards.iter().any(|c| c.appear.map(|t| now - t < 0.2).unwrap_or(false) || c.removing.is_some() || c.pulse.map(|t| now - t < 0.48).unwrap_or(false))
            || self.menu.as_ref().map(|m| now - m.win.opened_at < 0.15).unwrap_or(false)
    }
}

// ───────────────────────── eframe::App ─────────────────────────

/// `TOTTHODHARA_SHOWCASE=bar|settings0..4:<out.png>` renders one frame for the README.
fn showcase() -> Option<(String, String)> {
    let v = std::env::var("TOTTHODHARA_SHOWCASE").ok()?;
    let (k, o) = v.split_once(':')?;
    Some((k.to_string(), o.to_string()))
}

impl App {
    fn run_showcase(&mut self, ui: &mut Ui, now: f64, kind: &str, out: String) {
        static FRAMES: std::sync::atomic::AtomicU32 = std::sync::atomic::AtomicU32::new(0);
        if kind == "bar" {
            self.draw_bar(ui, now);
        } else {
            let sec = kind.trim_start_matches("settings").parse().unwrap_or(0);
            let mut w = self.settings_win.take().unwrap_or_else(|| crate::settings_ui::SettingsWin::new(sec, &self.clocks));
            w.section = sec;
            crate::settings_ui::style(ui, &self.pal);
            w.body(ui, self, now);
            self.settings_win = Some(w);
        }
        let ctx = ui.ctx().clone();
        ctx.request_repaint();
        if FRAMES.fetch_add(1, SeqCst) == 90 {
            ctx.send_viewport_cmd(egui::ViewportCommand::Screenshot(egui::ScreenshotCallback::new(move |img| {
                let [w, h] = img.size;
                let raw: Vec<u8> = img.pixels.iter().flat_map(|c| c.to_array()).collect();
                if let Some(buf) = image::RgbaImage::from_raw(w as u32, h as u32, raw) {
                    let _ = buf.save(&out);
                }
                std::process::exit(0);
            })));
        }
    }
}

impl eframe::App for App {
    fn clear_color(&self, _v: &egui::Visuals) -> [f32; 4] {
        [0.0, 0.0, 0.0, 0.0]
    }

    fn logic(&mut self, ctx: &egui::Context, _frame: &mut eframe::Frame) {
        if self.painted_once {
            self.ensure_hwnd(ctx);
        }
        self.handle_events(ctx);
    }

    fn ui(&mut self, ui: &mut Ui, _frame: &mut eframe::Frame) {
        let ctx = ui.ctx().clone();
        let now = ctx.input(|i| i.time);
        if self.app_tex.is_none() {
            let img = icons::app_icon_rgba();
            let ci = egui::ColorImage::from_rgba_unmultiplied([img.width() as usize, img.height() as usize], img.as_raw());
            self.app_tex = Some(ctx.load_texture("appicon", ci, egui::TextureOptions::LINEAR));
        }
        let dropped: Vec<String> = ctx.input(|i| i.raw.dropped_files.iter().map(|f| f.path().to_string_lossy().into_owned()).filter(|p| !p.is_empty()).collect());
        // A card dropped back onto the shelf must not duplicate itself.
        if !dropped.is_empty() && now > self.ignore_drops_until {
            worker::handle_dropped_files(&self.core, dropped);
        }
        if ctx.input(|i| i.modifiers.ctrl && i.key_pressed(egui::Key::F)) {
            self.open_search();
        }

        if let Some((kind, out)) = showcase() {
            self.run_showcase(ui, now, &kind, out);
            return;
        }
        self.draw_bar(ui, now);
        if let Some(id) = self.drag_out.take() {
            self.run_drag_out(&ctx, id);
        }
        self.show_menu(&ctx, now);
        self.show_preview(&ctx);
        self.show_toast(&ctx);

        if let Some(mut w) = self.settings_win.take() {
            if w.show(&ctx, self, now) {
                self.settings_win = Some(w);
            }
        }
        self.settings_changed(&ctx);
        if !self.painted_once {
            self.painted_once = true;
            ctx.request_repaint();
        }
        events::POPUP_OPEN.store(self.menu.is_some() || self.search_open, SeqCst);

        if self.animating(now) {
            ctx.request_repaint();
        } else {
            let mut next: f64 = 1.0 - (now - self.last_clock).min(1.0);
            for c in &self.cards {
                if let Some((_, t)) = &c.flash {
                    next = next.min(t - now);
                }
            }
            if let Some((_, t, _)) = self.hover_card {
                if self.preview.is_none() {
                    next = next.min(t + 0.35 - now);
                }
            }
            if let Some(t) = &self.toast {
                next = next.min(t.until - now);
            }
            if let Some(t) = self.tray_click_at {
                next = next.min(t + 0.27 - now);
            }
            ctx.request_repaint_after(std::time::Duration::from_secs_f64(next.max(0.01)));
        }
    }
}

// ───────────────────────── the bar ─────────────────────────

enum Slot {
    Spacer,
    Meters(Vec<Group>),
    ChevL,
    Strip,
    ChevR,
    Search,
    PasteAll,
    Unselect,
    Gear,
}

impl App {
    fn groups(&self, left: bool) -> Vec<Group> {
        self.s
            .order_keys
            .split(',')
            .filter_map(|k| match k.trim() {
                "clock" if self.s.show_world_clock && (self.s.show_clock1 || self.s.show_clock2) && self.s.clock_on_left == left => Some(Group::Clock),
                "hw" if self.s.show_cpu_ram_monitor && self.s.hardware_on_left == left => Some(Group::Hw),
                "net" if self.s.show_network_monitor && self.s.monitors_on_left == left => Some(Group::Net),
                _ => None,
            })
            .collect()
    }

    fn group_w(&self, g: Group) -> f32 {
        let k = self.k();
        let inner = k * match g {
            Group::Hw => 18.0 + 4.0 + 32.0 + 4.0 + 24.0 + 4.0 + 28.0,
            Group::Net => 18.0 + 4.0 + 62.0 + 4.0 + 18.0 + 4.0 + 62.0,
            Group::Clock => {
                let one = 18.0 + 4.0 + 30.0 + 4.0 + 58.0;
                match (self.s.show_clock1, self.s.show_clock2) {
                    (true, true) => one * 2.0 + 4.0,
                    (false, false) => 0.0,
                    _ => one,
                }
            }
        };
        inner + if self.s.pill_meters { 20.0 * k } else { 0.0 }
    }

    fn selected_count(&self) -> usize {
        self.cards.iter().filter(|c| c.selected && c.removing.is_none()).count()
    }

    fn slot_w(&self, ctx: &egui::Context, s: &Slot) -> Option<f32> {
        let k = self.k();
        Some(match s {
            Slot::Spacer | Slot::Strip => return None,
            Slot::Meters(gs) => gs.iter().map(|g| self.group_w(*g)).sum::<f32>() + 6.0 * k * (gs.len().saturating_sub(1)) as f32,
            Slot::ChevL | Slot::ChevR => 30.0 * k,
            Slot::Search => 130.0 * k,
            Slot::PasteAll => text_w(ctx, "Paste All", fid(12.0 * k, "bold")) + 24.0 * k,
            Slot::Unselect => text_w(ctx, "Unselect all", fid(12.0 * k, "bold")) + 24.0 * k,
            Slot::Gear => 32.0 * k,
        })
    }

    fn draw_bar(&mut self, ui: &mut Ui, now: f64) {
        let ctx = ui.ctx().clone();
        let full = ui.max_rect();
        let pal = self.pal.clone();
        // The bar: pill capsule, no border, no shadow.
        ui.painter().rect_filled(full, self.bar_radius() as u8, pal.bar_fill);

        let hide = self.s.hide_clipboard;
        let left = self.groups(true);
        let right = self.groups(false);
        let mut slots: Vec<Slot> = Vec::new();
        if hide && self.s.alignment != "Left" {
            slots.push(Slot::Spacer);
        }
        if !left.is_empty() {
            slots.push(Slot::Meters(left));
        }
        if !hide {
            slots.push(Slot::ChevL);
            slots.push(Slot::Strip);
            slots.push(Slot::ChevR);
            if self.search_open || !self.search_text.is_empty() {
                slots.push(Slot::Search);
            }
            let sel = self.selected_count();
            if sel > 1 {
                slots.push(Slot::PasteAll);
            }
            if sel > 0 {
                slots.push(Slot::Unselect);
            }
        }
        if !right.is_empty() {
            slots.push(Slot::Meters(right));
        }
        if hide && self.s.alignment != "Right" {
            slots.push(Slot::Spacer);
        }
        slots.push(Slot::Gear);

        let inner_w = full.width() - 14.0;
        let fixed: f32 = slots.iter().filter_map(|s| self.slot_w(&ctx, s)).sum();
        let fills = slots.iter().filter(|s| self.slot_w(&ctx, s).is_none()).count().max(1) as f32;
        let kk = self.k();
        let gaps = 8.0 * kk * (slots.len().saturating_sub(1)) as f32;
        let fill_w = ((inner_w - fixed - gaps) / fills).max(0.0);
        let cy = full.center().y;
        let ch = self.card_h();
        let mut x = full.left() + 7.0;
        let mut strip_rect = Rect::NOTHING;
        let mut chev: Vec<(bool, Rect)> = vec![];
        for s in &slots {
            let w = self.slot_w(&ctx, s).unwrap_or(fill_w);
            let r = Rect::from_min_size(pos2(x, cy - ch / 2.0), vec2(w, ch));
            match s {
                Slot::Meters(gs) => {
                    let mut gx = r.left();
                    for g in gs {
                        let gw = self.group_w(*g);
                        self.draw_group(ui, *g, Rect::from_min_size(pos2(gx, r.top()), vec2(gw, ch)), now);
                        gx += gw + 6.0 * kk;
                    }
                }
                Slot::ChevL => chev.push((true, r)),
                Slot::ChevR => chev.push((false, r)),
                Slot::Strip => strip_rect = r,
                Slot::Search => self.draw_search(ui, r),
                Slot::PasteAll => {
                    let resp = ui.interact(r, Id::new("pasteall"), Sense::click());
                    let col = if resp.hovered() { theme::lighter(pal.accent, 1.1) } else { pal.accent };
                    ui.painter().rect_filled(r, 8, col);
                    let g = ui.painter().layout_no_wrap("Paste All".into(), fid(12.0 * kk, "bold"), Color32::WHITE);
                    ui.painter().galley(caps_center(&g, r.center(), 12.0 * kk), g, Color32::WHITE);
                    if resp.clicked() {
                        let sel: Vec<Item> = self.cards.iter().filter(|c| c.selected).map(|c| c.it.clone()).collect();
                        for c in &mut self.cards {
                            c.selected = false;
                        }
                        worker::paste_all(sel);
                    }
                }
                Slot::Unselect => {
                    let resp = ui.interact(r, Id::new("unselect"), Sense::click());
                    ui.painter().rect(r, 8, pal.card_bg, Stroke::new(1.0, pal.card_border), StrokeKind::Inside);
                    let g = ui.painter().layout_no_wrap("Unselect all".into(), fid(12.0 * kk, "bold"), pal.text);
                    ui.painter().galley(caps_center(&g, r.center(), 12.0 * kk), g, pal.text);
                    if resp.clicked() {
                        for c in &mut self.cards {
                            c.selected = false;
                        }
                        self.toast("Selection cleared", now);
                    }
                }
                Slot::Gear => {
                    let br = Rect::from_center_size(pos2(r.center().x, cy), vec2(32.0, 24.0) * kk);
                    let resp = ui.interact(br, Id::new("gear"), Sense::click());
                    let k = ctx.animate_bool_with_time(Id::new("gear-h"), resp.hovered(), 0.12);
                    ui.painter().rect_filled(Rect::from_center_size(br.center(), vec2(30.0, 24.0) * kk), (8.0 * kk) as u8, theme::alpha(pal.text, 0.12 * k));
                    ui.painter().text(br.center(), Align2::CENTER_CENTER, icons::SETTINGS.to_string(), fid(16.0 * kk, "icons"), pal.text);
                    if resp.clicked() {
                        if matches!(self.menu.as_ref().map(|m| m.kind), Some(MenuKind::Gear)) {
                            self.menu = None;
                        } else {
                            let rows = vec![
                                row(MenuId::Search, icons::SEARCH, "Search  (Ctrl+F)"),
                                row(MenuId::ClearHistory, icons::DELETE, "Clear history"),
                                row(MenuId::Settings, icons::SETTINGS, "Settings"),
                                row(MenuId::Exit, icons::CLOSE, "Exit"),
                            ];
                            let ax = self.shelf_px.r;
                            self.open_menu(MenuKind::Gear, rows, 170.0, ax, now);
                        }
                    }
                }
                Slot::Spacer => {}
            }
            x += w + 8.0 * kk;
        }
        self.update_meter_drag(ui, full);
        if !hide {
            self.draw_strip(ui, strip_rect, now);
            for (is_left, r) in chev {
                self.draw_chevron(ui, r, is_left, strip_rect.width(), now);
            }
        }
        // A press on the shelf (outside the gear) closes an open menu.
        if ctx.input(|i| i.pointer.any_pressed()) && self.menu.is_some() && ui.rect_contains_pointer(full) {
            let gear_hit = ctx.input(|i| i.pointer.interact_pos()).map(|p| p.x > full.right() - 50.0).unwrap_or(false);
            if !gear_hit {
                self.menu = None;
            }
        }
    }

    fn draw_chevron(&mut self, ui: &mut Ui, r: Rect, left: bool, strip_w: f32, now: f64) {
        let ctx = ui.ctx().clone();
        let pal = self.pal.clone();
        let can = if left { self.scroll > 4.0 } else { self.scroll < self.max_scroll - 4.0 };
        let c = r.center();
        let kk = self.k();
        let hit = Rect::from_center_size(c, vec2(22.0, 22.0) * kk);
        let resp = ui.interact(hit, Id::new(("chev", left)), Sense::click());
        let hov = ctx.animate_bool_with_time(Id::new(("chev-h", left)), resp.hovered(), 0.15);
        let sc = 1.0 + 0.08 * ease_out(ctx.animate_bool_with_time(Id::new(("chev-s", left)), resp.hovered(), 0.13));
        let op = 0.35 + 0.65 * ctx.animate_bool_with_time(Id::new(("chev-o", left)), can, 0.15);
        let rad = 11.0 * kk * sc;
        let style = self.s.pill_style.as_str();
        let fill = match style {
            "tint" | "borderless" => theme::alpha(pal.accent, 0.08),
            "contrast" => pal.card_bg,
            _ => Color32::TRANSPARENT,
        };
        let border = if style == "border" { pal.accent } else { pal.card_border };
        let p = ui.painter();
        // Glow beneath, fill, wash (all fade with the dim opacity).
        p.circle_filled(c, rad, theme::fade(theme::alpha(pal.accent, 0.15 * hov), op));
        p.circle_filled(c, rad, theme::fade(fill, op));
        p.circle_filled(c, rad, theme::fade(theme::alpha(pal.accent, 0.12 * hov), op));
        if style != "borderless" {
            p.circle_stroke(c, rad - 0.5, Stroke::new(1.0, theme::fade(border, op)));
        }
        p.text(c, Align2::CENTER_CENTER, if left { icons::CHEVRON_LEFT } else { icons::CHEVRON_RIGHT }.to_string(), fid(12.0 * kk * sc, "icons"), theme::fade(pal.text, op));
        if resp.clicked() && can {
            let step = (strip_w * 0.6).max(120.0);
            let to = if left { (self.scroll - step).max(0.0) } else { (self.scroll + step).min(self.max_scroll) };
            self.scroll_anim = Some((self.scroll, to, now));
        }
    }

    fn draw_search(&mut self, ui: &mut Ui, r: Rect) {
        let pal = self.pal.clone();
        let fk = self.k();
        let id = Id::new("search-box");
        let focused = ui.ctx().memory(|m| m.has_focus(id));
        ui.painter().rect(r, 8, pal.card_bg, Stroke::new(1.0, if focused { pal.accent } else { pal.card_border }), StrokeKind::Inside);
        let resp = ui.put(
            r.shrink2(vec2(8.0, 2.0)),
            egui::TextEdit::singleline(&mut self.search_text)
                .id(id)
                .frame(egui::Frame::NONE)
                .font(fid(12.0 * fk, ""))
                .text_color(pal.text)
                .hint_text(egui::RichText::new("Search…").color(pal.muted))
                .vertical_align(egui::Align::Center),
        );
        if self.search_focus_req {
            resp.request_focus();
            self.search_focus_req = false;
        }
        if resp.changed() {
            self.scroll = 0.0;
        }
        if ui.input(|i| i.key_pressed(egui::Key::Escape)) || (resp.lost_focus() && self.search_text.is_empty()) {
            self.close_search();
        }
    }

    /// Paint one meter group's content (+ optional pill shell) into `r`.
    fn paint_group(&self, p: &egui::Painter, g: Group, r: Rect, pill: bool) {
        let pal = &self.pal;
        let cy = r.center().y;
        if pill {
            let rad = (self.card_h() / 2.0).round() as u8;
            let (fill, border, bw) = self.pill_paint(false);
            if self.s.pill_style == "shadow" {
                p.rect_filled(r, rad, theme::alpha(Color32::BLACK, 0.3));
            }
            p.rect(r, rad, fill, Stroke::new(bw, border), StrokeKind::Inside);
        }
        let k = self.k();
        let mut x = r.left() + if pill { 10.0 * k } else { 0.0 };
        let icon = |x: &mut f32, m: Meter| {
            let sz = m.size() * k;
            icons::draw_meter(p, pos2(*x, cy - sz.y / 2.0), m, pal.text, k);
            *x += sz.x + 4.0 * k;
        };
        let text = |x: &mut f32, t: &str, size: f32, w: f32| {
            text_vc(p, *x, cy, t, size * k, "", pal.text);
            *x += (w + 4.0) * k;
        };
        match g {
            Group::Hw => {
                icon(&mut x, Meter::Cpu);
                text(&mut x, &format!("{}%", self.sys.cpu), 13.0, 32.0);
                icon(&mut x, Meter::Ram);
                text(&mut x, &format!("{}%", self.sys.mem), 13.0, 28.0);
            }
            Group::Net => {
                let (up, down) = crate::sysmon::format_pair(self.sys.up_kbs, self.sys.down_kbs);
                icon(&mut x, Meter::Up);
                text(&mut x, &up, 14.0, 62.0);
                icon(&mut x, Meter::Down);
                text(&mut x, &down, 14.0, 62.0);
            }
            Group::Clock => {
                // Tag (12px) sits on the SAME baseline as its time (13px):
                // centering each size separately rounds them 1px apart.
                let clock = |x: &mut f32, tag: &str, time: &str| {
                    icon(x, Meter::Clock);
                    let tg = p.layout_no_wrap(time.to_string(), fid(13.0 * k, ""), pal.text);
                    let ty = caps_center(&tg, pos2(0.0, cy), 13.0 * k).y;
                    let tb = ty + baseline(&tg);
                    let gg = p.layout_no_wrap(tag.to_string(), fid(12.0 * k, ""), pal.text);
                    let gy = (tb - baseline(&gg)).round();
                    p.galley(pos2(*x, gy), gg, pal.text);
                    *x += (30.0 + 4.0) * k;
                    p.galley(pos2(*x, ty), tg, pal.text);
                    *x += (58.0 + 4.0) * k;
                };
                if self.s.show_clock1 {
                    clock(&mut x, &self.clocks.a_tag, &self.clocks.a_text);
                }
                if self.s.show_clock2 {
                    clock(&mut x, &self.clocks.b_tag, &self.clocks.b_text);
                }
            }
        }
    }

    fn draw_group(&mut self, ui: &mut Ui, g: Group, r: Rect, now: f64) {
        self.group_rects.insert(g as u8, r);
        let dragged = self.meter_drag.as_ref().map(|d| d.group == g).unwrap_or(false);
        // Dimmed in place while dragged: the floating ghost carries the drag.
        let k = ui.ctx().animate_bool_with_time(Id::new(("meter-dim", g as u8)), dragged, 0.12);
        let mut p = ui.painter().clone();
        p.multiply_opacity(1.0 - 0.55 * k);
        self.paint_group(&p, g, r, self.s.pill_meters);
        let resp = ui.interact(r, Id::new(("meter", g as u8)), Sense::click_and_drag());
        if resp.drag_started_by(egui::PointerButton::Primary) {
            let px = resp.interact_pointer_pos().map(|p| p.x).unwrap_or(r.left());
            self.meter_drag = Some(MeterDrag { group: g, grab: px - r.left(), w: r.width(), y: r.top() });
            self.menu = None;
        }
        // Right-click: move menu (clock also offers "Change time").
        if resp.secondary_clicked() {
            let left = self.group_left(g);
            let mut rows = vec![row(MenuId::Move(g), '⇄', if left { "Move to right" } else { "Move to left" })];
            if g == Group::Clock {
                rows.push(row(MenuId::ChangeTime, '⇄', "Change time"));
            }
            let ax = winsys::cursor_pos().0;
            self.open_menu(MenuKind::Meter, rows, 170.0, ax, now);
        }
    }

    fn group_left(&self, g: Group) -> bool {
        match g {
            Group::Clock => self.s.clock_on_left,
            Group::Hw => self.s.hardware_on_left,
            Group::Net => self.s.monitors_on_left,
        }
    }

    fn set_group_left(&mut self, g: Group, left: bool) {
        match g {
            Group::Clock => self.s.clock_on_left = left,
            Group::Hw => self.s.hardware_on_left = left,
            Group::Net => self.s.monitors_on_left = left,
        }
    }

    /// Bar-level meter drag: live side flip at the shelf midpoint, splice into
    /// rank among same-side siblings (+-10px hysteresis), ghost under cursor.
    fn update_meter_drag(&mut self, ui: &mut Ui, full: Rect) {
        let Some((g, grab, gw, gy)) = self.meter_drag.as_ref().map(|d| (d.group, d.grab, d.w, d.y)) else { return };
        let ctx = ui.ctx().clone();
        if !ctx.input(|i| i.pointer.primary_down()) {
            self.meter_drag = None;
            return;
        }
        let Some(p) = ctx.input(|i| i.pointer.latest_pos()) else { return };
        let left = p.x < full.center().x;
        if left != self.group_left(g) {
            self.set_group_left(g, left);
        }
        fn key(g: Group) -> &'static str {
            match g {
                Group::Clock => "clock",
                Group::Hw => "hw",
                Group::Net => "net",
            }
        }
        let mut order: Vec<String> = self.s.order_keys.split(',').map(|k| k.trim().to_string()).filter(|k| !k.is_empty()).collect();
        for k in ["clock", "hw", "net"] {
            if !order.iter().any(|o| o == k) {
                order.push(k.to_string());
            }
        }
        let me = key(g);
        let my_idx = order.iter().position(|o| o == me).unwrap_or(0);
        // Siblings on this side, in display order, with last-frame centers.
        let sibs: Vec<(usize, f32)> = self
            .groups(left)
            .into_iter()
            .filter(|s| *s != g)
            .filter_map(|s| {
                let i = order.iter().position(|o| o == key(s))?;
                let c = self.group_rects.get(&(s as u8))?.center().x;
                Some((i, c))
            })
            .collect();
        let mut target: Option<usize> = None;
        for (i, c) in &sibs {
            let cross = if my_idx < *i { c + 10.0 } else { c - 10.0 };
            if p.x < cross {
                target = Some(*i);
                break;
            }
        }
        let mut new_order = order.clone();
        new_order.remove(my_idx);
        let ins = match target {
            Some(i) => new_order.iter().position(|o| *o == order[i]).unwrap_or(new_order.len()),
            None => match sibs.last() {
                Some((i, _)) => new_order.iter().position(|o| *o == order[*i]).map(|x| x + 1).unwrap_or(new_order.len()),
                None => new_order.len(),
            },
        };
        new_order.insert(ins.min(new_order.len()), me.to_string());
        let joined = new_order.join(",");
        if joined != self.s.order_keys {
            self.s.order_keys = joined;
        }
        // Ghost: card pill under the cursor carrying the group.
        let pill = self.s.pill_meters;
        let w = if pill { gw } else { gw + 20.0 };
        let gx = (p.x - grab - if pill { 0.0 } else { 10.0 }).clamp(full.left(), (full.right() - w).max(full.left()));
        let gr = Rect::from_min_size(pos2(gx, gy), vec2(w, self.card_h()));
        let rad = (self.card_h() / 2.0).round() as u8;
        let painter = ui.painter().clone().with_layer_id(egui::LayerId::new(egui::Order::Foreground, Id::new("meter-ghost")));
        painter.rect_filled(gr.translate(vec2(0.0, 1.0)), rad, theme::alpha(Color32::BLACK, 0.3));
        painter.rect(gr, rad, theme::over(self.pal.bar_bg, self.pal.card_bg), Stroke::new(1.0, self.pal.card_border), StrokeKind::Inside);
        self.paint_group(&painter, g, Rect::from_min_size(pos2(gx + 10.0, gy), vec2(w - 20.0, self.card_h())), false);
        ctx.request_repaint();
    }

    /// (fill, border, border width) for the active pill style.
    fn pill_paint(&self, selected: bool) -> (Color32, Color32, f32) {
        let pal = &self.pal;
        match self.s.pill_style.as_str() {
            "tint" => (theme::alpha(pal.accent, 0.08), if selected { pal.accent } else { pal.card_border }, 1.0),
            "borderless" => (theme::alpha(pal.accent, if selected { 0.16 } else { 0.08 }), Color32::TRANSPARENT, 0.0),
            "contrast" => (pal.card_bg, if selected { pal.accent } else { pal.card_border }, 1.0),
            "border" => (Color32::TRANSPARENT, pal.accent, 1.0),
            _ => (Color32::TRANSPARENT, if selected { pal.accent } else { pal.card_border }, 1.0),
        }
    }

    /// Card title / index size (13px at Medium, scaled with the bar).
    fn title_size(&self) -> f32 {
        (13.0 * self.k()).round()
    }

    /// Natural card width: contentRow (index 14 | kind 28 | title ≤130 | link 16, spacing 4) + 23.
    fn card_w_raw(&self, i: usize) -> f32 {
        let c = &self.cards[i];
        let k = self.k();
        let key = (c.title.clone(), (self.title_size() * 10.0) as u32);
        let tw = self.title_w.get(&key).copied().unwrap_or(c.title.chars().count() as f32 * 6.5 * k);
        let mut parts = vec![14.0 * k];
        if c.kind != Kind::Text {
            parts.push(28.0 * k);
        }
        parts.push(tw.min(130.0 * k));
        if c.kind == Kind::Url {
            parts.push(16.0 * k);
        }
        let row: f32 = parts.iter().sum::<f32>() + 4.0 * k * (parts.len() - 1) as f32;
        (row + 23.0 * k).clamp(52.0 * k, 200.0 * k)
    }

    fn card_w(&mut self, ctx: &egui::Context, i: usize) -> f32 {
        if let Some(w) = self.cards[i].locked_w {
            return w;
        }
        let ts = self.title_size();
        let key = (self.cards[i].title.clone(), (ts * 10.0) as u32);
        if !self.title_w.contains_key(&key) {
            let w = text_w(ctx, &key.0, fid(ts, ""));
            self.title_w.insert(key, w);
        }
        self.card_w_raw(i)
    }

    fn draw_strip(&mut self, ui: &mut Ui, area: Rect, now: f64) {
        let ctx = ui.ctx().clone();
        let ppp = ctx.pixels_per_point();
        let q = self.search_text.clone();
        let vis: Vec<usize> = (0..self.cards.len()).filter(|&i| self.cards[i].matches(&q) || self.cards[i].removing.is_some()).collect();
        let widths: Vec<f32> = vis.iter().map(|&i| self.card_w(&ctx, i)).collect();
        let kk = self.k();
        let gap = 6.0 * kk;
        let items_w: f32 = widths.iter().sum::<f32>() + gap * (widths.len().saturating_sub(1)) as f32;
        let w = area.width();
        let (pad_min, foot) = (12.0 * kk, 20.0 * kk);
        let pad = match self.s.alignment.as_str() {
            "Left" => pad_min,
            "Right" => (w - items_w - foot).max(pad_min),
            _ => ((w - items_w - foot) / 2.0).max(pad_min),
        };
        let content = pad + items_w + foot;
        self.max_scroll = (content - w).max(0.0);

        // Wheel / touchpad scrolls the shelf sideways.
        if ui.rect_contains_pointer(area) {
            let d = ctx.input(|i| {
                i.raw
                    .events
                    .iter()
                    .map(|e| match e {
                        egui::Event::MouseWheel { unit, delta, modifiers, .. } if modifiers.is_none() => {
                            let v = if delta.y != 0.0 { delta.y } else { delta.x };
                            match unit {
                                egui::MouseWheelUnit::Line => v * 40.0,
                                egui::MouseWheelUnit::Page => v * w,
                                egui::MouseWheelUnit::Point => v,
                            }
                        }
                        _ => 0.0,
                    })
                    .sum::<f32>()
            });
            if d != 0.0 {
                self.scroll_anim = None;
                self.scroll = (self.scroll - d).clamp(0.0, self.max_scroll);
            }
        }
        if let Some((from, to, t0)) = self.scroll_anim {
            let t = ((now - t0) / 0.22) as f32;
            self.scroll = from + (to - from) * ease_out(t);
            if t >= 1.0 {
                self.scroll_anim = None;
            }
        }
        self.scroll = self.scroll.clamp(0.0, self.max_scroll);

        let cy = area.center().y;
        let ch = self.card_h();
        let mut shapes: Vec<Shape> = Vec::new();
        let mut actions: Vec<(i64, &'static str, Rect)> = vec![];
        let mut hovered: Option<(i64, Rect)> = None;
        let mut cx = pad;
        for (vi, &i) in vis.iter().enumerate() {
            let cw = widths[vi];
            let id = self.cards[i].it.id;
            // Displaced neighbors glide (200ms); arrivals slide in from −32.
            let ax = ctx.animate_value_with_time(Id::new(("cx", id)), cx, 0.2);
            let mut x = area.left() + ax - self.scroll;
            let c = &self.cards[i];
            let mut opacity = 1.0;
            if let Some(t) = c.appear {
                let k = ((now - t) / 0.2) as f32;
                x -= 32.0 * (1.0 - ease_out(k));
                opacity *= k.clamp(0.0, 1.0);
            }
            if let Some(t) = c.removing {
                opacity *= 1.0 - ((now - t) / 0.16).clamp(0.0, 1.0) as f32;
            }
            cx += cw + gap;
            let rect = Rect::from_min_size(pos2(x, cy - ch / 2.0), vec2(cw, ch));
            if rect.right() < area.left() - 40.0 || rect.left() > area.right() + 40.0 {
                continue;
            }
            let hit = rect.intersect(area);
            let resp = ui.interact(hit, Id::new(("card", id)), Sense::click_and_drag());
            // Drag OUT to another app: 16px threshold, never with Ctrl/Shift
            // (those are delete/select clicks).
            if resp.dragged_by(egui::PointerButton::Primary) && c.removing.is_none() && self.drag_out.is_none() {
                let far = ctx.input(|i| i.pointer.press_origin().zip(i.pointer.latest_pos()).map(|(a, b)| a.distance(b) > 16.0).unwrap_or(false));
                // Only the card the press actually started on may drag.
                let on_me = ctx.input(|i| i.pointer.press_origin()).map(|o| hit.contains(o)).unwrap_or(false);
                // The OLE loop eats the mouse-up, so egui can still think the
                // button is held afterwards — check the physical button or every
                // frame would start (and instantly cancel) a new drag.
                let held = winsys::key_down(0x01); // VK_LBUTTON
                if far && on_me && held && !winsys::shift_down() && !winsys::ctrl_down() {
                    self.drag_out = Some(id);
                }
            }
            let removing = c.removing.is_some();
            let mut open_hover = false;
            if c.kind == Kind::Url && c.flash.is_none() {
                let gr = Rect::from_center_size(pos2(rect.right() - 18.0 * kk, cy), vec2(24.0, 22.0) * kk);
                open_hover = ui.rect_contains_pointer(gr.intersect(area));
                if open_hover {
                    ctx.set_cursor_icon(egui::CursorIcon::PointingHand);
                    if resp.clicked() {
                        actions.push((id, "open", rect));
                    }
                }
            }
            if resp.hovered() && !removing {
                hovered = Some((id, rect));
            }
            if resp.clicked() && !open_hover && !removing {
                actions.push((id, "click", rect));
            }
            if resp.secondary_clicked() && !removing {
                actions.push((id, "menu", rect));
            }
            let hover = resp.hovered() && !removing;
            let pressed = resp.is_pointer_button_down_on() && !removing;
            self.paint_card(&ctx, &mut shapes, rect, i, hover, pressed, open_hover, opacity, now);
        }
        // Conditional edge fades (40px, 150ms crossfade).
        let kl = ctx.animate_bool_with_time(Id::new("fade-l"), self.scroll > 4.0, 0.15);
        let kr = ctx.animate_bool_with_time(Id::new("fade-r"), self.scroll < self.max_scroll - 4.0, 0.15);
        // Clip only horizontally: hover lift (1.04) must never be cut by the card row.
        let clip = Rect::from_x_y_ranges(area.x_range(), ui.max_rect().y_range());
        ui.painter().with_clip_rect(clip).add(crate::fade::faded(&ctx, shapes, area, ppp, 40.0, kl, kr));

        // Hover preview after 350ms (not for color swatches). Never while a
        // button is held (pressing / dragging a card) or a drag is starting.
        let pressing = ctx.input(|i| i.pointer.any_down()) || self.drag_out.is_some();
        match hovered {
            Some((id, rect)) if self.menu.is_none() && !pressing => {
                let ax = self.screen_x(rect.center().x);
                if self.hover_card.map(|h| h.0 != id).unwrap_or(true) {
                    self.hover_card = Some((id, now, ax));
                    self.preview = None;
                }
            }
            _ => {
                self.hover_card = None;
                self.preview = None;
            }
        }
        if let Some((id, t, ax)) = self.hover_card {
            if now - t >= 0.35 && self.preview.is_none() {
                self.open_preview(id, ax, now);
            }
        }

        let shift = winsys::shift_down();
        let ctrl = winsys::ctrl_down();
        for (id, a, rect) in actions {
            match a {
                "click" => self.click_card(id, shift, ctrl, now),
                "open" => {
                    if let Some(c) = self.card(id) {
                        winsys::shell_open(c.it.text_content.trim());
                    }
                }
                "menu" => {
                    if let Some(c) = self.card(id) {
                        let rows = self.clip_menu_rows(c);
                        let ax = self.screen_x(rect.center().x);
                        self.open_menu(MenuKind::Clip(id), rows, 230.0, ax, now);
                    }
                }
                _ => {}
            }
        }
    }

    #[allow(clippy::too_many_arguments)]
    fn paint_card(&self, ctx: &egui::Context, out: &mut Vec<Shape>, rect: Rect, i: usize, hover: bool, pressed: bool, open_hover: bool, opacity: f32, now: f64) {
        let c = &self.cards[i];
        let pal = &self.pal;
        let id = c.it.id;
        let style = self.s.pill_style.as_str();
        let lift = matches!(style, "hover" | "tint");
        let rad = (self.card_h() / 2.0).round() as u8;
        let hov_k = ctx.animate_bool_with_time(Id::new(("ch", id)), hover && lift, 0.15);
        let press_k = ease_out(ctx.animate_bool_with_time(Id::new(("cp", id)), pressed, 0.13));
        let lift_k = ease_out(ctx.animate_bool_with_time(Id::new(("cl", id)), hover && lift && !pressed, 0.13));
        let sel_k = ctx.animate_bool_with_time(Id::new(("cs", id)), c.selected, 0.15);
        // Press shrinks to 0.93, hover lift grows to 1.04.
        let scale = 1.0 - 0.07 * press_k + 0.04 * lift_k;
        let mut shapes: Vec<Shape> = Vec::new();
        let (fill, border, bw) = self.pill_paint(c.selected);
        if style == "shadow" {
            shapes.push(Shape::rect_filled(rect, rad, theme::alpha(Color32::BLACK, 0.3)));
        }
        if hov_k > 0.0 {
            shapes.push(Shape::rect_filled(rect, rad, theme::alpha(pal.accent, 0.15 * hov_k)));
        }
        shapes.push(Shape::rect_filled(rect, rad, fill));
        let wash = (0.28 * sel_k).max(0.12 * hov_k);
        if wash > 0.0 {
            shapes.push(Shape::rect_filled(rect, rad, theme::alpha(pal.accent, wash)));
        }
        if let Some(t) = c.pulse {
            let f = 1.0 - ease_out(((now - t) / 0.48) as f32);
            if f > 0.0 {
                shapes.push(Shape::rect_filled(rect, rad, theme::alpha(pal.accent, 0.65 * f)));
            }
        }
        if bw > 0.0 {
            shapes.push(Shape::rect_stroke(rect, rad, Stroke::new(bw, border), StrokeKind::Inside));
        }

        // Content row: margins L9 / R10, spacing 4, one shared centerline.
        let cy = rect.center().y;
        let k = self.k();
        let isz = self.title_size();
        let mut x = rect.left() + 9.0 * k;
        let flashing = c.flash.as_ref().map(|f| f.0.clone());
        // While a status shows ("Dropped!", "Pasted!"…) it owns the whole card:
        // number and icon step aside so the message sits dead-center.
        if flashing.is_none() {
            let ig = ctx.fonts_mut(|f| f.layout_no_wrap(c.pos.to_string(), fid(isz, "bold"), pal.text));
            shapes.push(Shape::galley(pos2(x, caps_center(&ig, pos2(0.0, cy), isz).y), ig, pal.text));
        }
        x += (14.0 + 4.0) * k;
        if c.kind != Kind::Text {
            if flashing.is_none() {
                self.paint_kind(ctx, &mut shapes, c, Rect::from_min_size(pos2(x, rect.top()), vec2(28.0 * k, rect.height())));
            }
            x += (28.0 + 4.0) * k;
        }
        let right = rect.right() - 10.0 * k - if c.kind == Kind::Url && flashing.is_none() { 20.0 * k } else { 0.0 };
        let tsize = isz;
        match &flashing {
            Some(t) => {
                // Status: bold, centered on the whole card (both axes).
                let g = elide(ctx, t, fid(tsize, "bold"), pal.text, rect.width() - 16.0 * k);
                let at = caps_center(&g, rect.center(), tsize);
                shapes.push(Shape::galley(pos2(at.x.round(), at.y), g, pal.text));
            }
            None => {
                let g = elide(ctx, &c.title, fid(tsize, ""), pal.text, (right - x).min(130.0 * k));
                shapes.push(Shape::galley(pos2(x, caps_center(&g, pos2(0.0, cy), tsize).y), g, pal.text));
            }
        }
        if c.kind == Kind::Url && flashing.is_none() {
            let gc = pos2(rect.right() - 18.0 * k, cy);
            if open_hover {
                shapes.push(Shape::circle_filled(gc, 11.0 * k, theme::alpha(pal.accent, 0.34)));
            }
            let col = if open_hover { theme::lighter(pal.accent, 1.45) } else { pal.accent };
            let g = ctx.fonts_mut(|f| f.layout_no_wrap(icons::OPEN_LINK.to_string(), fid(15.0 * k, "icons"), col));
            let gp = gc - g.size() / 2.0;
            if open_hover {
                let uy = gp.y + g.size().y - 3.0;
                shapes.push(Shape::line_segment([pos2(gp.x + 1.0, uy), pos2(gp.x + g.size().x - 1.0, uy)], Stroke::new(1.0, col)));
            }
            shapes.push(Shape::galley(gp, g, col));
        }
        // Badges: snippet star (left of pin when both), pin top-right.
        if c.it.is_snippet {
            let gold = theme::rgb(0xff, 0xd6, 0x59);
            let g = ctx.fonts_mut(|f| f.layout_no_wrap(icons::STAR.to_string(), fid(9.0 * k, "icons"), gold));
            let rx = rect.right() - if c.it.is_pinned { 14.0 * k } else { 2.0 * k };
            shapes.push(Shape::galley(pos2(rx - g.size().x, rect.top() + 2.0 * k), g, gold));
        }
        if c.it.is_pinned {
            let g = ctx.fonts_mut(|f| f.layout_no_wrap(icons::PIN.to_string(), fid(9.0 * k, "icons"), pal.accent));
            shapes.push(Shape::galley(pos2(rect.right() - 2.0 * k - g.size().x, rect.top() + 2.0 * k), g, pal.accent));
        }

        if scale != 1.0 || opacity < 1.0 {
            let ctr = rect.center().to_vec2();
            let tr = egui::emath::TSTransform::new(ctr - ctr * scale, scale);
            for s in &mut shapes {
                if scale != 1.0 {
                    s.transform(tr);
                }
                if opacity < 1.0 {
                    fade_shape(s, opacity);
                }
            }
        }
        out.extend(shapes);
    }

    /// The 28px kind slot: thumb / swatch / favicon / badge / glyph.
    fn paint_kind(&self, ctx: &egui::Context, out: &mut Vec<Shape>, c: &Card, slot: Rect) {
        let pal = &self.pal;
        let ctr = slot.center();
        let k = self.k();
        let glyph = |out: &mut Vec<Shape>, ch: char, size: f32, col: Color32| {
            let g = ctx.fonts_mut(|f| f.layout_no_wrap(ch.to_string(), fid(size * k, "icons"), col));
            out.push(Shape::galley(ctr - g.size() / 2.0, g, col));
        };
        let badge = |out: &mut Vec<Shape>, text: &str| {
            let r = Rect::from_center_size(ctr, vec2(26.0, 16.0) * k);
            out.push(Shape::rect_stroke(r, (4.0 * k) as u8, Stroke::new(1.0, pal.text), StrokeKind::Inside));
            let g = elide(ctx, text, fid(8.0 * k, "bold"), pal.text, 24.0 * k);
            out.push(Shape::galley(caps_center(&g, ctr, 8.0 * k), g, pal.text));
        };
        match c.kind {
            Kind::Image => {
                let tile = Rect::from_center_size(ctr, vec2(26.0 * k, (self.card_h() - 6.0 * k).max(16.0)));
                match self.thumbs.get(&c.it.id) {
                    Some(tex) => {
                        match self.s.thumb_style.as_str() {
                            "glow" => out.push(Shape::rect_filled(tile.expand(1.0), 7, theme::alpha(pal.accent, 0.35))),
                            "shadow" => out.push(Shape::rect_filled(tile.translate(vec2(1.0, 1.0)), 6, theme::alpha(Color32::BLACK, 0.45))),
                            _ => {}
                        }
                        out.push(Shape::rect_filled(tile, 6, pal.bar_bg));
                        let inner = tile.shrink(2.0);
                        let ts = tex.size_vec2();
                        let k = (inner.width() / ts.x).min(inner.height() / ts.y);
                        let ir = Rect::from_center_size(inner.center(), ts * k);
                        out.push(Shape::image(tex.id(), ir, Rect::from_min_max(pos2(0.0, 0.0), pos2(1.0, 1.0)), Color32::WHITE));
                        let (bw, bc) = match self.s.thumb_style.as_str() {
                            "glow" => (2.0, pal.accent),
                            "accent" => (1.0, pal.accent),
                            "line" => (1.0, pal.card_border),
                            _ => (0.0, Color32::TRANSPARENT),
                        };
                        if bw > 0.0 {
                            out.push(Shape::rect_stroke(tile, 6, Stroke::new(bw, bc), StrokeKind::Inside));
                        }
                    }
                    None if !std::path::Path::new(&c.it.file_path).exists() => badge(out, "IMG"),
                    None => out.push(Shape::rect_filled(tile, 6, pal.bar_bg)),
                }
            }
            Kind::Color => {
                let col = theme::hex_color(&c.it.text_content).unwrap_or(Color32::GRAY);
                out.push(Shape::circle_filled(ctr, 8.0 * k, col));
                out.push(Shape::circle_stroke(ctr, 8.0 * k - 0.5, Stroke::new(1.0, pal.card_border)));
            }
            Kind::Url => match self.favicons.get(&c.host).and_then(|f| f.as_ref()) {
                Some(tex) => out.push(Shape::image(tex.id(), Rect::from_center_size(ctr, vec2(16.0, 16.0) * k), Rect::from_min_max(pos2(0.0, 0.0), pos2(1.0, 1.0)), Color32::WHITE)),
                None => glyph(out, icons::GLOBE, 16.0, pal.accent),
            },
            Kind::File => {
                if c.is_video() {
                    glyph(out, icons::VIDEO, 16.0, pal.text)
                } else if c.is_audio() {
                    glyph(out, icons::AUDIO, 16.0, pal.text)
                } else if c.is_code() {
                    out.extend(icons::meter_shapes(ctr - Meter::Code.size() * k / 2.0, Meter::Code, pal.text, k));
                } else {
                    badge(out, &c.ext)
                }
            }
            Kind::Text => {}
        }
    }

    // ───────── popups ─────────

    fn show_menu(&mut self, ctx: &egui::Context, now: f64) {
        let Some(mut m) = self.menu.take() else { return };
        let rows: Vec<MenuRow> = m.sub.clone().unwrap_or_else(|| m.rows.clone());
        let n = rows.len() + usize::from(m.sub.is_some());
        let dpi = winsys::dpi_of_window(self.hwnd);
        let (w, h) = (m.width, 14.0 + n as f32 * 40.0);
        let (wp, hp) = ((w * dpi).round() as i32, (h * dpi).round() as i32);
        let (mon, work) = winsys::monitor_of_point(m.anchor_x, self.shelf_px.t);
        let m8 = (8.0 * dpi) as i32;
        let x = match m.kind {
            MenuKind::Gear => self.shelf_px.r - wp - m8,
            MenuKind::Meter => m.anchor_x - (40.0 * dpi) as i32,
            _ => m.anchor_x - wp / 2,
        }
        .clamp(mon.l + m8, (mon.r - wp - m8).max(mon.l));
        let y = if m.kind == MenuKind::Tray { (work.b - hp - m8).max(mon.t + m8) } else { self.popup_y(hp) };
        let rect = IRect { l: x, t: y, r: x + wp, b: y + hp };
        let pal = self.pal.clone();
        let in_sub = m.sub.is_some();
        let mut picked: Option<MenuRow> = None;
        let mut back = false;
        let opened = m.win.opened_at;
        m.win.show(ctx, rect, dpi, true, |ui| {
            let t = ((now - opened) / 0.12).clamp(0.0, 1.0) as f32;
            ui.set_opacity(t);
            let full = ui.max_rect();
            ui.painter().rect(full, 10, pal.menu_bg, Stroke::new(1.0, pal.card_border), StrokeKind::Inside);
            let mut y = full.top() + 8.0;
            let mut item = |ui: &mut Ui, glyph: char, label: &str, key: usize| -> bool {
                let r = Rect::from_min_size(pos2(full.left() + 8.0, y), vec2(full.width() - 16.0, 38.0));
                let resp = ui.interact(r, Id::new(("mi", key, opened.to_bits())), Sense::click());
                let k = ui.ctx().animate_bool_with_time(Id::new(("mih", key, opened.to_bits())), resp.hovered(), 0.1);
                ui.painter().rect_filled(r, 8, theme::alpha(pal.accent, 0.12 * k));
                let g = ui.painter().layout_no_wrap(glyph.to_string(), fid(14.0, "icons"), pal.text);
                ui.painter().galley(pos2(r.left() + 12.0, r.center().y - g.size().y / 2.0), g.clone(), pal.text);
                ui.painter().text(pos2(r.left() + 12.0 + g.size().x + 12.0, r.center().y), Align2::LEFT_CENTER, label, fid(13.0, ""), pal.text);
                y += 40.0;
                resp.clicked()
            };
            if in_sub && item(ui, '‹', "Back", 999) {
                back = true;
            }
            for (k, r) in rows.iter().enumerate() {
                if item(ui, r.glyph, &r.label, k) {
                    picked = Some(r.clone());
                }
            }
        });
        let focused = ctx.input_for(m.win.id, |i| i.focused);
        let esc = ctx.input_for(m.win.id, |i| i.key_pressed(egui::Key::Escape));
        if focused {
            m.was_focused = true;
        }
        let lost = m.was_focused && !focused;
        let kind = m.kind;
        if back {
            m.sub = None;
            self.menu = Some(m);
        } else if let Some(r) = picked {
            if let Some(sub) = r.sub {
                m.sub = Some(sub);
                self.menu = Some(m);
            } else {
                self.run_menu(kind, r.id, now);
            }
        } else if !esc && !lost {
            self.menu = Some(m);
        }
    }

    fn open_preview(&mut self, id: i64, anchor_x: i32, now: f64) {
        let Some(c) = self.card(id) else { return };
        let kind = match c.kind {
            Kind::Color => return,
            Kind::Image => PreviewKind::Image(id),
            Kind::File => {
                let name = std::path::Path::new(&c.it.file_path).file_name().map(|n| n.to_string_lossy().into_owned()).unwrap_or_default();
                let (glyph, label) = if c.is_video() {
                    (icons::VIDEO, "Video file".to_string())
                } else if c.is_audio() {
                    (icons::AUDIO, "Audio file".to_string())
                } else if c.ext == "PDF" {
                    (icons::FILE, "PDF document".to_string())
                } else {
                    (icons::FILE, format!("{} file", c.ext))
                };
                PreviewKind::File { glyph, title: c.title.clone(), sub: format!("{name}  •  {label}") }
            }
            _ => PreviewKind::Text(c.it.text_content.replace('\0', "")),
        };
        self.preview = Some(Preview { card: id, kind, anchor_x, win: PopupWin::new("preview", now, false) });
    }

    fn show_preview(&mut self, ctx: &egui::Context) {
        let Some(mut p) = self.preview.take() else { return };
        let dpi = winsys::dpi_of_window(self.hwnd);
        let bg = theme::rgb(0x14, 0x14, 0x14);
        let bd = theme::rgb(0x3d, 0x3d, 0x3d);
        let fg = theme::rgb(0xec, 0xec, 0xec);
        let accent = self.pal.accent;
        let (w, h, galley) = match &p.kind {
            PreviewKind::Image(id) => {
                let Some(tex) = self.thumbs.get(id) else {
                    self.preview = Some(p);
                    return;
                };
                let dims = self.dims.get(id).map(|d| vec2(d.0 as f32, d.1 as f32)).unwrap_or(tex.size_vec2());
                let aspect = dims.x / dims.y.max(1.0);
                let (mut w, mut h) = (420.0, 420.0 / aspect);
                if h > 420.0 {
                    h = 420.0;
                    w = 420.0 * aspect;
                }
                (w.max(80.0), h.max(80.0), None)
            }
            PreviewKind::Text(t) => {
                let shown: String = t.lines().take(30).collect::<Vec<_>>().join("\n");
                let nat = shown.lines().map(|l| text_w(ctx, l, fid(13.0, ""))).fold(0.0, f32::max);
                let w = (nat + 24.0).clamp(120.0, 480.0);
                let mut job = egui::text::LayoutJob::single_section(shown, egui::TextFormat::simple(fid(13.0, ""), fg));
                job.wrap = egui::text::TextWrapping { max_width: w - 24.0, max_rows: 30, break_anywhere: true, overflow_character: Some('…') };
                let g = ctx.fonts_mut(|f| f.layout_job(job));
                let h = (g.size().y + 24.0).clamp(44.0, 420.0);
                (w, h, Some(g))
            }
            PreviewKind::File { title, sub, .. } => {
                let tw = text_w(ctx, title, fid(13.0, "bold"));
                let sw = text_w(ctx, sub, fid(12.0, ""));
                let w = (tw.max(sw) + 36.0).clamp(200.0, 420.0);
                let tl = (tw / (w - 24.0)).ceil().max(1.0);
                let sl = (sw / (w - 24.0)).ceil().max(1.0);
                (w, 30.0 + 8.0 + tl * 18.0 + sl * 16.0 + 24.0, None)
            }
        };
        let (wp, hp) = ((w * dpi).round() as i32, (h * dpi).round() as i32);
        let (mon, _) = winsys::monitor_of_point(p.anchor_x, self.shelf_px.t);
        let m8 = (8.0 * dpi) as i32;
        let x = (p.anchor_x - wp / 2).clamp(mon.l + m8, (mon.r - wp - m8).max(mon.l));
        let y = self.popup_y(hp);
        let thumb = if let PreviewKind::Image(id) = &p.kind { self.thumbs.get(id).cloned() } else { None };
        let kind = &p.kind;
        p.win.show(ctx, IRect { l: x, t: y, r: x + wp, b: y + hp }, dpi, true, |ui| {
            let full = ui.max_rect();
            ui.painter().rect(full, 10, bg, Stroke::new(1.0, bd), StrokeKind::Inside);
            match kind {
                PreviewKind::Image(_) => {
                    if let Some(tex) = &thumb {
                        let inner = full.shrink(6.0);
                        let ts = tex.size_vec2();
                        let k = (inner.width() / ts.x).min(inner.height() / ts.y);
                        ui.painter().image(tex.id(), Rect::from_center_size(inner.center(), ts * k), Rect::from_min_max(pos2(0.0, 0.0), pos2(1.0, 1.0)), Color32::WHITE);
                    }
                }
                PreviewKind::Text(_) => {
                    if let Some(g) = &galley {
                        ui.painter().galley(full.min + vec2(12.0, 12.0), g.clone(), fg);
                    }
                }
                PreviewKind::File { glyph, title, sub } => {
                    let wrap = full.width() - 24.0;
                    let gg = ui.painter().layout_no_wrap(glyph.to_string(), fid(30.0, "icons"), accent);
                    let tg = ui.painter().layout(title.clone(), fid(13.0, "bold"), fg, wrap);
                    let sg = ui.painter().layout(sub.clone(), fid(12.0, ""), theme::rgb(0xa0, 0xa0, 0xa0), wrap);
                    let total = gg.size().y + 4.0 + tg.size().y + 4.0 + sg.size().y;
                    let mut y = full.center().y - total / 2.0;
                    ui.painter().galley(pos2(full.center().x - gg.size().x / 2.0, y), gg.clone(), accent);
                    y += gg.size().y + 4.0;
                    ui.painter().galley(pos2(full.center().x - tg.size().x / 2.0, y), tg.clone(), fg);
                    y += tg.size().y + 4.0;
                    ui.painter().galley(pos2(full.center().x - sg.size().x / 2.0, y), sg, fg);
                }
            }
        });
        self.preview = Some(p);
    }

    fn show_toast(&mut self, ctx: &egui::Context) {
        let Some(mut t) = self.toast.take() else { return };
        let dpi = winsys::dpi_of_window(self.hwnd);
        let w = (text_w(ctx, &t.text, fid(12.0, "")) + 24.0).min(480.0);
        let (wp, hp) = ((w * dpi).round() as i32, (28.0 * dpi).round() as i32);
        let x = (self.shelf_px.l + self.shelf_px.r) / 2 - wp / 2;
        let y = self.popup_y(hp);
        let pal = self.pal.clone();
        let text = t.text.clone();
        t.win.show(ctx, IRect { l: x, t: y, r: x + wp, b: y + hp }, dpi, true, |ui| {
            let full = ui.max_rect();
            ui.painter().rect(full, 14, theme::over(pal.bar_bg, pal.card_bg), Stroke::new(1.0, pal.accent), StrokeKind::Inside);
            let g = elide(ui.ctx(), &text, fid(12.0, ""), pal.text, full.width() - 24.0);
            ui.painter().galley(full.center() - g.size() / 2.0, g, pal.text);
        });
        self.toast = Some(t);
    }
}

fn fade_shape(s: &mut Shape, a: f32) {
    match s {
        Shape::Vec(v) => v.iter_mut().for_each(|s| fade_shape(s, a)),
        Shape::Rect(r) => {
            r.fill = r.fill.gamma_multiply(a);
            r.stroke.color = r.stroke.color.gamma_multiply(a);
        }
        Shape::Circle(c) => {
            c.fill = c.fill.gamma_multiply(a);
            c.stroke.color = c.stroke.color.gamma_multiply(a);
        }
        Shape::LineSegment { stroke, .. } => stroke.color = stroke.color.gamma_multiply(a),
        Shape::Path(p) => {
            if let egui::epaint::ColorMode::Solid(c) = &mut p.stroke.color {
                *c = c.gamma_multiply(a);
            }
        }
        Shape::Text(t) => t.opacity_factor *= a,
        Shape::Mesh(m) => {
            let m = std::sync::Arc::make_mut(m);
            for v in &mut m.vertices {
                v.color = v.color.gamma_multiply(a);
            }
        }
        _ => {}
    }
}
