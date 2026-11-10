//! SettingsWindow (Qt edition): 720×560 frameless dashboard — caption bar,
//! 158px sidebar, content card with SettingRow tiles.
use crate::app::{ease_out, elide, fid, App, UpdState};
use crate::icons;
use crate::theme::{self, Pal};
use crate::winsys::{self, Hwnd};
use egui::{pos2, vec2, Align2, Color32, Id, Rect, Sense, Stroke, StrokeKind, Ui, ViewportBuilder, ViewportCommand, ViewportId};

pub struct SettingsWin {
    id: ViewportId,
    title: String,
    hwnd: Hwnd,
    pub section: usize,
    maximized: bool,
    zone_opts: Vec<(String, String)>,
    open_combo: Option<&'static str>,
    spin_buf: std::collections::HashMap<&'static str, String>,
    tone_picker: bool,
    pick_color: Color32,
}

const SECTIONS: [(char, &str); 5] = [(icons::CONTACT, "Appearance"), (icons::SETTINGS, "Behavior"), ('☰', "Items on Bar"), ('⬤', "Clipboard Style"), ('ⓘ', "About")];

impl SettingsWin {
    pub fn new(section: usize, clocks: &crate::clock::Clocks) -> Self {
        Self {
            id: ViewportId::from_hash_of("totthodhara-settings"),
            title: "Totthodhara Settings".into(),
            hwnd: Hwnd(0),
            section,
            maximized: false,
            zone_opts: clocks.options(),
            open_combo: None,
            spin_buf: Default::default(),
            tone_picker: false,
            pick_color: theme::rgb(0x9d, 0x4f, 0xd0),
        }
    }

    pub fn hwnd(&self) -> Hwnd {
        self.hwnd
    }

    /// Returns false once closed.
    pub fn show(&mut self, ctx: &egui::Context, app: &mut App, now: f64) -> bool {
        let icon = icons::app_icon_rgba();
        let builder = ViewportBuilder::default()
            .with_title(self.title.clone())
            .with_inner_size(vec2(720.0, 560.0))
            .with_min_inner_size(vec2(700.0, 440.0))
            .with_decorations(false)
            .with_transparent(true)
            .with_resizable(true)
            .with_taskbar(true)
            .with_drag_and_drop(false)
            .with_icon(egui::IconData { rgba: icon.as_raw().clone(), width: icon.width(), height: icon.height() });
        let mut keep = true;
        ctx.show_viewport_immediate(self.id, builder, |ui, _| {
            if ui.input(|i| i.viewport().close_requested()) {
                keep = false;
            }
            style(ui, &app.pal);
            keep &= self.body(ui, app, now);
        });
        if self.hwnd.is_null() {
            self.hwnd = winsys::find_own_window(&self.title);
            if !self.hwnd.is_null() {
                winsys::set_border_none(self.hwnd);
                winsys::dechrome_popup(self.hwnd);
                winsys::set_corner_round(self.hwnd, false);
                let (mon, _) = winsys::monitor_of_point(winsys::cursor_pos().0, winsys::cursor_pos().1);
                let dpi = winsys::dpi_of_window(self.hwnd);
                let (w, h) = ((720.0 * dpi) as i32, (560.0 * dpi) as i32);
                winsys::move_window(self.hwnd, mon.l + ((mon.r - mon.l - w) / 2).max(0), mon.t + ((mon.b - mon.t - h) / 2).max(0), w, h, false);
                winsys::force_foreground(self.hwnd);
            }
        }
        keep
    }

    fn body(&mut self, ui: &mut Ui, app: &mut App, now: f64) -> bool {
        let pal = app.pal.clone();
        let full = ui.max_rect();
        let p = ui.painter().clone();
        p.rect_filled(full, if self.maximized { 0 } else { 12 }, pal.bar_bg);
        let mut keep = true;

        // Caption (48): icon · centered title · maximize / close.
        let cap = Rect::from_min_size(full.min, vec2(full.width(), 48.0));
        let drag = ui.interact(cap, Id::new("cap"), Sense::click_and_drag());
        if drag.drag_started() {
            ui.ctx().send_viewport_cmd(ViewportCommand::StartDrag);
        }
        if drag.double_clicked() {
            self.toggle_max(ui);
        }
        if let Some(tex) = &app.app_tex {
            p.image(tex.id(), Rect::from_min_size(pos2(cap.left() + 14.0, cap.center().y - 13.0), vec2(26.0, 26.0)), Rect::from_min_max(pos2(0.0, 0.0), pos2(1.0, 1.0)), Color32::WHITE);
        }
        p.text(cap.center(), Align2::CENTER_CENTER, "Totthodhara Settings", fid(14.0, "bold"), pal.text);
        let close_r = Rect::from_min_size(pos2(cap.right() - 8.0 - 44.0, cap.center().y - 15.0), vec2(44.0, 30.0));
        let max_r = close_r.translate(vec2(-54.0, 0.0));
        if caption_button(ui, &pal, max_r, if self.maximized { icons::RESTORE } else { icons::MAXIMIZE }, false) {
            self.toggle_max(ui);
        }
        if caption_button(ui, &pal, close_r, icons::CLOSE, true) {
            keep = false;
        }

        // Sidebar (158).
        let mut y = cap.bottom() + 1.0 + 12.0;
        for (i, (g, label)) in SECTIONS.iter().enumerate() {
            let r = Rect::from_min_size(pos2(full.left() + 10.0, y), vec2(138.0, 38.0));
            if popup_item(ui, &pal, r, *g, 17.0, label, self.section == i, ("side", i)) {
                self.section = i;
                self.open_combo = None;
            }
            y += 42.0;
        }

        // Content card.
        let card = Rect::from_min_max(pos2(full.left() + 158.0 + 1.0 + 14.0, cap.bottom() + 1.0 + 14.0), pos2(full.right() - 14.0, full.bottom() - 14.0));
        p.rect_filled(card, 12, pal.card_bg);
        let inner = card.shrink(18.0);
        let mut cui = ui.new_child(egui::UiBuilder::new().max_rect(inner).id_salt(("page", self.section)));
        egui::ScrollArea::vertical()
            .id_salt(("scroll", self.section))
            .scroll_bar_visibility(egui::scroll_area::ScrollBarVisibility::AlwaysHidden)
            .auto_shrink([false, false])
            .show(&mut cui, |ui| {
                ui.spacing_mut().item_spacing = vec2(0.0, 10.0);
                ui.set_width(inner.width());
                match self.section {
                    0 => self.appearance(ui, app),
                    1 => self.behavior(ui, app),
                    2 => self.items_on_bar(ui, app),
                    3 => self.clip_style(ui, app),
                    _ => self.about(ui, app, now),
                }
            });

        // Resize grip (bottom-right).
        if !self.maximized {
            let grip = Rect::from_min_size(full.max - vec2(20.0, 20.0), vec2(20.0, 20.0));
            let g = ui.interact(grip, Id::new("grip"), Sense::drag());
            if g.hovered() {
                ui.ctx().set_cursor_icon(egui::CursorIcon::ResizeNwSe);
            }
            if g.drag_started() {
                ui.ctx().send_viewport_cmd(ViewportCommand::BeginResize(egui::ResizeDirection::SouthEast));
            }
        }
        keep
    }

    fn toggle_max(&mut self, ui: &Ui) {
        self.maximized = !self.maximized;
        ui.ctx().send_viewport_cmd(ViewportCommand::Maximized(self.maximized));
    }

    // ───────── pages ─────────

    fn appearance(&mut self, ui: &mut Ui, app: &mut App) {
        let pal = app.pal.clone();
        section_label(ui, &pal, "APPEARANCE");
        let mut v = app.s.theme.clone();
        setting_row(ui, &pal, "Theme", "System follows Windows", |ui, r| segment(ui, &pal, r, &["System", "Dark", "Light"], &mut v, "theme"), seg_w(3));
        app.s.theme = v;
        let mut v = app.s.bar_size.clone();
        setting_row(ui, &pal, "Bar size", "Shelf height", |ui, r| segment(ui, &pal, r, &["Small", "Medium", "Large"], &mut v, "bar"), seg_w(3));
        app.s.bar_size = v;
        let mut v = app.s.shelf_position.clone();
        setting_row(ui, &pal, "Shelf position", "Top or bottom edge", |ui, r| segment(ui, &pal, r, &["Bottom", "Top"], &mut v, "pos"), seg_w(2));
        app.s.shelf_position = v;
        let mut v = app.s.alignment.clone();
        setting_row(ui, &pal, "Shelf alignment", "New clips land left, centered, or right", |ui, r| segment(ui, &pal, r, &["Left", "Center", "Right"], &mut v, "align"), seg_w(3));
        app.s.alignment = v;
        // Mini card: Rounded shelf.
        let w = ui.available_width();
        let (r, _) = ui.allocate_exact_size(vec2(w, 46.0), Sense::hover());
        mini_switch(ui, &pal, r, "Rounded shelf", &mut app.s.rounded_corners, "round");
        if app.s.theme == "System" {
            // Transparent bar + opacity (System only).
            let (row_r, _) = ui.allocate_exact_size(vec2(w, 46.0), Sense::hover());
            let a = Rect::from_min_size(row_r.min, vec2(190.0, 46.0));
            mini_switch(ui, &pal, a, "Transparent bar", &mut app.s.transparent_bar, "transp");
            if app.s.transparent_bar {
                let b = Rect::from_min_max(pos2(a.right() + 10.0, row_r.top()), row_r.max);
                ui.painter().rect_filled(b, 10, pal.bar_bg);
                ui.painter().text(pos2(b.left() + 12.0, b.center().y), Align2::LEFT_CENTER, "Bar opacity", fid(11.0, "bold"), pal.text);
                let spin_r = Rect::from_min_size(pos2(b.right() - 12.0 - 60.0, b.center().y - 15.0), vec2(60.0, 30.0));
                let slider_r = Rect::from_min_size(pos2(spin_r.left() - 10.0 - 90.0, b.center().y - 10.0), vec2(90.0, 20.0));
                let mut pct = (app.s.glass_alpha * 100.0).round() as i32;
                if slider(ui, &pal, slider_r, &mut pct, 0, 100, "glass") | self.spin(ui, &pal, spin_r, &mut pct, 0, 100, "glass") {
                    app.s.glass_alpha = (pct as f32 / 100.0).max(0.05);
                }
            }
            // Tone dots + intensity (System only). Dots flow-wrap inside
            // the 240px card (Qt Flow), so the last dot never spills out.
            let tones: [(&str, Option<Color32>); 7] = [
                ("auto", None),
                ("windows", Some(app.os.accent)),
                ("dark", Some(theme::rgb(0x20, 0x20, 0x20))),
                ("grey", Some(theme::rgb(0x3a, 0x3a, 0x3d))),
                ("light", Some(theme::rgb(0xf2, 0xf2, 0xf2))),
                ("blue", Some(theme::rgb(0x27, 0x4e, 0x7d))),
                ("custom", theme::hex_color(&app.s.custom_tone)),
            ];
            let card_w = 240.0;
            let flow_w = card_w - 24.0;
            let mut spots: Vec<(f32, f32, f32)> = vec![];
            let (mut fx, mut fy) = (0.0f32, 0.0f32);
            for (key, _) in &tones {
                let dw = if *key == "auto" { 46.0 } else { 26.0 };
                if fx > 0.0 && fx + dw > flow_w {
                    fx = 0.0;
                    fy += 34.0;
                }
                spots.push((fx, fy, dw));
                fx += dw + 8.0;
            }
            let card_h = 48.0 + fy + 26.0 + 10.0;
            let (row_r, _) = ui.allocate_exact_size(vec2(w, card_h), Sense::hover());
            let a = Rect::from_min_size(row_r.min, vec2(card_w, card_h));
            ui.painter().rect_filled(a, 10, pal.bar_bg);
            ui.painter().text(pos2(a.left() + 12.0, a.top() + 8.0), Align2::LEFT_TOP, "Shelf tone", fid(11.0, "bold"), pal.text);
            ui.painter().text(pos2(a.left() + 12.0, a.top() + 26.0), Align2::LEFT_TOP, "2nd follows Windows • + is any color", fid(10.0, ""), pal.muted);
            for ((key, col), (dx, dy, dw)) in tones.iter().zip(spots) {
                let key = *key;
                let dr = Rect::from_min_size(pos2(a.left() + 12.0 + dx, a.top() + 48.0 + dy), vec2(dw, 26.0));
                let sel = app.s.shelf_tone == key;
                let fill = if key == "auto" { if sel { pal.accent } else { pal.card_bg } } else { col.unwrap_or(Color32::GRAY) };
                let resp = ui.interact(dr, Id::new(("tone", key)), Sense::click());
                ui.painter().rect(dr, 13, fill, Stroke::new(if sel { 2.0 } else { 1.0 }, if sel { pal.accent } else { pal.card_border }), StrokeKind::Inside);
                if key == "auto" {
                    ui.painter().text(dr.center(), Align2::CENTER_CENTER, "Auto", fid(11.0, "bold"), if sel { Color32::WHITE } else { pal.text });
                } else if key == "custom" {
                    ui.painter().text(dr.center(), Align2::CENTER_CENTER, "+", fid(15.0, "bold"), Color32::WHITE);
                }
                if resp.clicked() {
                    if key == "custom" {
                        self.tone_picker = !self.tone_picker;
                        self.pick_color = col.unwrap_or(self.pick_color);
                    } else {
                        app.s.shelf_tone = key.into();
                    }
                }
            }
            let b = Rect::from_min_max(pos2(a.right() + 10.0, row_r.top()), row_r.max);
            ui.painter().rect_filled(b, 10, pal.bar_bg);
            ui.painter().text(pos2(b.left() + 12.0, b.center().y), Align2::LEFT_CENTER, "Intensity", fid(11.0, "bold"), pal.text);
            let spin_r = Rect::from_min_size(pos2(b.right() - 12.0 - 60.0, b.center().y - 15.0), vec2(60.0, 30.0));
            let slider_r = Rect::from_min_size(pos2(spin_r.left() - 10.0 - 80.0, b.center().y - 10.0), vec2(80.0, 20.0));
            let mut pct = (app.s.tone_intensity * 100.0).round() as i32;
            if slider(ui, &pal, slider_r, &mut pct, 0, 100, "tone") | self.spin(ui, &pal, spin_r, &mut pct, 0, 100, "tone") {
                app.s.tone_intensity = pct as f32 / 100.0;
            }
            if self.tone_picker {
                let area = egui::Area::new(Id::new("tone-pick")).order(egui::Order::Foreground).fixed_pos(pos2(a.left(), a.bottom() + 6.0));
                area.show(ui.ctx(), |ui| {
                    egui::Frame::NONE.fill(pal.menu_bg).stroke(Stroke::new(1.0, pal.card_border)).corner_radius(10).inner_margin(10).show(ui, |ui| {
                        ui.label(egui::RichText::new("Pick shelf tone").color(pal.text).font(fid(12.0, "bold")));
                        egui::color_picker::color_picker_color32(ui, &mut self.pick_color, egui::color_picker::Alpha::Opaque);
                        ui.horizontal(|ui| {
                            if ui.button("OK").clicked() {
                                let [r, g, b, _] = self.pick_color.to_array();
                                app.s.custom_tone = format!("#{r:02x}{g:02x}{b:02x}");
                                app.s.shelf_tone = "custom".into();
                                self.tone_picker = false;
                            }
                            if ui.button("Cancel").clicked() {
                                self.tone_picker = false;
                            }
                        });
                    });
                });
            }
        }
    }

    fn behavior(&mut self, ui: &mut Ui, app: &mut App) {
        let pal = app.pal.clone();
        section_label(ui, &pal, "BEHAVIOR");
        let mut v = app.s.auto_check_updates;
        setting_row(ui, &pal, "Check updates on start", "Silent unless an update exists", |ui, r| { switch(ui, &pal, r, &mut v, false, "upd"); }, vec2(40.0, 22.0));
        app.s.auto_check_updates = v;
        let mut v = app.s.start_with_windows;
        setting_row(ui, &pal, "Run at Windows startup", "Start with Windows login", |ui, r| { switch(ui, &pal, r, &mut v, false, "run"); }, vec2(40.0, 22.0));
        app.s.start_with_windows = v;
        section_label(ui, &pal, "STORAGE");
        let mut v = app.s.max_history_items;
        setting_row(ui, &pal, "Max history items", "Pinned and snippets are exempt", |ui, r| { self.spin(ui, &pal, r, &mut v, 1, 500, "hist"); }, vec2(100.0, 30.0));
        app.s.max_history_items = v;
        let mut v = app.s.max_file_size_mb;
        setting_row(ui, &pal, "Max file size (MB)", "0 = no limit", |ui, r| { self.spin(ui, &pal, r, &mut v, 0, 500, "size"); }, vec2(100.0, 30.0));
        app.s.max_file_size_mb = v;
        let mut v = app.s.auto_clean_hours;
        setting_row(ui, &pal, "Auto-clean files", "Drop old files after N hours (0 = off)", |ui, r| { self.spin(ui, &pal, r, &mut v, 0, 720, "clean"); }, vec2(100.0, 30.0));
        app.s.auto_clean_hours = v;
    }

    fn items_on_bar(&mut self, ui: &mut Ui, app: &mut App) {
        let pal = app.pal.clone();
        section_label(ui, &pal, "METERS");
        let sw = vec2(40.0, 22.0);
        let mut v = app.s.show_cpu_ram_monitor;
        setting_row(ui, &pal, "Hardware meter", "CPU and RAM group", |ui, r| { switch(ui, &pal, r, &mut v, false, "hw"); }, sw);
        app.s.show_cpu_ram_monitor = v;
        let mut v = app.s.show_network_monitor;
        setting_row(ui, &pal, "Speed meter", "Upload and download group", |ui, r| { switch(ui, &pal, r, &mut v, false, "net"); }, sw);
        app.s.show_network_monitor = v;
        section_label(ui, &pal, "CLIPBOARD");
        let mut v = app.s.hide_clipboard;
        setting_row(ui, &pal, "Hide clipboard items", "Toolbar-only compact mode", |ui, r| { switch(ui, &pal, r, &mut v, false, "hide"); }, sw);
        app.s.hide_clipboard = v;
        section_label(ui, &pal, "CLOCK");
        let mut v = app.s.show_world_clock;
        setting_row(ui, &pal, "World clock", "Master switch for clock pills", |ui, r| { switch(ui, &pal, r, &mut v, false, "wc"); }, sw);
        app.s.show_world_clock = v;
        for (n, key) in [(1, "zone-a"), (2, "zone-b")] {
            let (mut zone, mut on) = if n == 1 { (app.s.zone_a.clone(), app.s.show_clock1) } else { (app.s.zone_b.clone(), app.s.show_clock2) };
            let opts = self.zone_opts.clone();
            let title = if n == 1 { "Clock one" } else { "Clock two" };
            let sub = if n == 1 { "First clock pill" } else { "Second clock pill" };
            let mut open = self.open_combo;
            setting_row(ui, &pal, title, sub, |ui, r| {
                let cr = Rect::from_min_size(r.min, vec2(230.0, 34.0));
                combo(ui, &pal, cr, &opts, &mut zone, key, &mut open);
                let sr = Rect::from_min_size(pos2(cr.right() + 8.0, r.center().y - 9.0), vec2(32.0, 18.0));
                switch(ui, &pal, sr, &mut on, true, key);
            }, vec2(230.0 + 8.0 + 32.0, 34.0));
            self.open_combo = open;
            if n == 1 {
                app.s.zone_a = zone;
                app.s.show_clock1 = on;
            } else {
                app.s.zone_b = zone;
                app.s.show_clock2 = on;
            }
        }
    }

    fn clip_style(&mut self, ui: &mut Ui, app: &mut App) {
        let pal = app.pal.clone();
        section_label(ui, &pal, "CLIPBOARD STYLE");
        let styles = [
            ("border", "Accent border", "1px accent glow border"),
            ("tint", "Accent tint", "Faint accent-tinted back"),
            ("hover", "Hover lift", "Scale up + glow on hover"),
            ("contrast", "Contrast fill", "Lighter/darker than bar"),
            ("borderless", "Borderless", "Tinted fill, no edge"),
            ("shadow", "Soft shadow", "Drop shadow under pill"),
        ];
        radio_grid(ui, &pal, &styles, &mut app.s.pill_style, "pill");
        section_label(ui, &pal, "THUMBNAIL FRAME");
        let frames = [
            ("accent", "Accent border", "1px accent edge"),
            ("none", "Borderless", "No edge, flat tile"),
            ("shadow", "Soft shadow", "Floating offset shade"),
            ("line", "Subtle line", "Theme hairline edge"),
            ("glow", "Accent glow", "Thick edge + halo"),
        ];
        radio_grid(ui, &pal, &frames, &mut app.s.thumb_style, "thumb");
        section_label(ui, &pal, "PILL SHAPE");
        let sw = vec2(40.0, 22.0);
        let mut v = app.s.pill_meters;
        setting_row(ui, &pal, "Pill-shaped meters", "Card pill behind hardware and speed meters", |ui, r| { switch(ui, &pal, r, &mut v, false, "pm"); }, sw);
        app.s.pill_meters = v;
        let mut v = app.s.show_dev_tools;
        setting_row(ui, &pal, "Developer tools", "Code tools in the right-click menu (off by default)", |ui, r| { switch(ui, &pal, r, &mut v, false, "dev"); }, sw);
        app.s.show_dev_tools = v;
    }

    fn about(&mut self, ui: &mut Ui, app: &mut App, now: f64) {
        let pal = app.pal.clone();
        section_label(ui, &pal, "ABOUT");
        let w = ui.available_width();
        let (r, _) = ui.allocate_exact_size(vec2(w, 144.0), Sense::hover());
        if let Some(tex) = &app.app_tex {
            ui.painter().image(tex.id(), Rect::from_center_size(r.center(), vec2(144.0, 144.0)), Rect::from_min_max(pos2(0.0, 0.0), pos2(1.0, 1.0)), Color32::WHITE);
        }
        centered(ui, "Totthodhara", fid(22.0, "bold"), pal.text, w);
        centered(ui, &format!("Version {}", crate::update::current_version()), fid(12.0, "bold"), pal.text, w);
        wrapped_center(ui, "Your second clipboard — every copy lives on the shelf, one click away from any app.", fid(13.0, ""), pal.muted, w, 420.0);
        // Update button (210×34) + checking pulse.
        let (row_r, _) = ui.allocate_exact_size(vec2(w, 34.0), Sense::hover());
        let br = Rect::from_center_size(row_r.center(), vec2(210.0, 34.0));
        let st = app.upd.state.clone();
        let enabled = matches!(st, UpdState::Idle | UpdState::Available | UpdState::UpToDate);
        let resp = ui.interact(br, Id::new("upd-btn"), if enabled { Sense::click() } else { Sense::hover() });
        let col = if st == UpdState::UpToDate {
            theme::rgb(0x3f, 0xb9, 0x50)
        } else if resp.is_pointer_button_down_on() {
            theme::lighter(pal.accent, 1.0 / 1.15)
        } else if resp.hovered() && enabled {
            theme::lighter(pal.accent, 1.1)
        } else {
            pal.accent
        };
        ui.painter().rect_filled(br, 8, theme::fade(col, if enabled { 1.0 } else { 0.45 }));
        let label = match st {
            UpdState::Checking => "Checking…".to_string(),
            UpdState::Available => format!("Download Latest {}", app.upd.version),
            UpdState::Downloading => "Downloading…".to_string(),
            UpdState::Installing => "Installing…".to_string(),
            UpdState::UpToDate => "You are up to date".to_string(),
            UpdState::Idle => "Check for updates".to_string(),
        };
        ui.painter().text(br.center(), Align2::CENTER_CENTER, label, fid(13.0, "bold"), theme::fade(Color32::WHITE, if enabled { 1.0 } else { 0.6 }));
        if st == UpdState::Checking {
            let ph = ((now * 1000.0) % 700.0) as f32 / 350.0;
            let k = if ph < 1.0 { ph } else { 2.0 - ph };
            ui.painter().circle_filled(pos2(br.right() + 16.0, br.center().y), 6.0, theme::fade(pal.accent, 0.3 + 0.7 * k));
            ui.ctx().request_repaint();
        }
        if resp.clicked() && enabled {
            if st == UpdState::Available {
                app.start_install();
            } else {
                app.check_updates();
            }
        }
        if matches!(st, UpdState::Downloading | UpdState::Installing) {
            let (r, _) = ui.allocate_exact_size(vec2(w, 8.0), Sense::hover());
            let bar = Rect::from_center_size(r.center(), vec2(260.0, 8.0));
            ui.painter().rect_filled(bar, 4, pal.bar_bg);
            let k = if st == UpdState::Installing { 1.0 } else { (app.upd.pct / 100.0).clamp(0.0, 1.0) };
            ui.painter().rect_filled(Rect::from_min_size(bar.min, vec2(260.0 * k, 8.0)), 4, pal.accent);
            ui.ctx().request_repaint_after(std::time::Duration::from_millis(100));
        }
        if !app.upd.note.is_empty() {
            wrapped_center(ui, &app.upd.note.clone(), fid(13.0, ""), pal.muted, w, 420.0);
        }
        if st == UpdState::Available && !app.upd.notes_full.is_empty() {
            let t = if app.upd.details_open { "▴ Hide details" } else { "▾ What's new" };
            let g = ui.painter().layout_no_wrap(t.into(), fid(12.0, "bold"), pal.accent);
            let (r, resp) = ui.allocate_exact_size(vec2(w, g.size().y), Sense::click());
            ui.painter().galley(pos2(r.center().x - g.size().x / 2.0, r.top()), g, pal.accent);
            if resp.hovered() {
                ui.ctx().set_cursor_icon(egui::CursorIcon::PointingHand);
            }
            if resp.clicked() {
                app.upd.details_open = !app.upd.details_open;
            }
            if app.upd.details_open {
                wrapped_center(ui, &app.upd.notes_full.clone(), fid(12.0, ""), pal.muted, w, 420.0);
            }
        }
    }

    /// ThemedSpin: field + ▴▾, typed values commit live and clamp.
    #[allow(clippy::too_many_arguments)]
    fn spin(&mut self, ui: &mut Ui, pal: &Pal, r: Rect, v: &mut i32, lo: i32, hi: i32, key: &'static str) -> bool {
        let id = Id::new(("spin", key));
        let focused = ui.ctx().memory(|m| m.has_focus(id));
        ui.painter().rect(r, 8, pal.card_bg, Stroke::new(if focused { 2.0 } else { 1.0 }, if focused { pal.accent } else { pal.card_border }), StrokeKind::Inside);
        let mut buf = self.spin_buf.get(key).cloned().filter(|_| focused).unwrap_or_else(|| v.to_string());
        let resp = ui.put(
            Rect::from_min_max(pos2(r.left() + 6.0, r.top() + 4.0), pos2(r.right() - 26.0, r.bottom() - 4.0)),
            egui::TextEdit::singleline(&mut buf).id(id).frame(egui::Frame::NONE).font(fid(12.0, "")).text_color(pal.text).horizontal_align(egui::Align::Center).vertical_align(egui::Align::Center),
        );
        let mut changed = false;
        if resp.changed() {
            if let Ok(n) = buf.trim().parse::<i32>() {
                let n = n.clamp(lo, hi);
                if n != *v {
                    *v = n;
                    changed = true;
                }
            }
        }
        self.spin_buf.insert(key, buf);
        if resp.lost_focus() {
            self.spin_buf.remove(key);
        }
        let up = Rect::from_min_size(pos2(r.right() - 6.0 - 22.0, r.top() + 3.0), vec2(22.0, (r.height() - 6.0) / 2.0));
        let dn = Rect::from_min_size(pos2(up.left(), r.center().y), up.size());
        for (rr, glyph, d) in [(up, "▴", 1), (dn, "▾", -1)] {
            let b = ui.interact(rr, Id::new(("spinb", key, d)), Sense::click());
            ui.painter().text(rr.center(), Align2::CENTER_CENTER, glyph, fid(10.0, ""), if b.hovered() { pal.accent } else { pal.muted });
            if b.clicked() {
                let n = (*v + d).clamp(lo, hi);
                if n != *v {
                    *v = n;
                    changed = true;
                }
            }
        }
        changed
    }
}

// ───────────────────────── widgets ─────────────────────────

fn style(ui: &mut Ui, pal: &Pal) {
    let s = ui.style_mut();
    s.visuals.override_text_color = Some(pal.text);
    s.visuals.selection.bg_fill = pal.accent;
    s.visuals.selection.stroke = Stroke::new(1.0, Color32::WHITE);
    s.visuals.text_cursor.stroke = Stroke::new(1.0, pal.text);
    s.visuals.widgets.inactive.bg_fill = pal.card_bg;
    s.visuals.widgets.inactive.weak_bg_fill = pal.card_bg;
    s.visuals.widgets.hovered.weak_bg_fill = theme::lighter(pal.card_bg, 1.1);
    s.visuals.window_fill = pal.menu_bg;
    s.visuals.extreme_bg_color = Color32::TRANSPARENT;
}

fn caption_button(ui: &mut Ui, pal: &Pal, r: Rect, glyph: char, close: bool) -> bool {
    let resp = ui.interact(r, Id::new(("capbtn", glyph as u32)), Sense::click());
    if resp.hovered() {
        ui.painter().rect_filled(r, 6, if close { theme::rgb(0xe8, 0x11, 0x23) } else { pal.card_border });
    }
    ui.painter().text(r.center(), Align2::CENTER_CENTER, glyph.to_string(), fid(11.0, "icons"), if resp.hovered() && close { Color32::WHITE } else { pal.text });
    resp.clicked()
}

/// PopupItem: 38 high, radius 8, accent .25 selected / .12 hover.
fn popup_item(ui: &mut Ui, pal: &Pal, r: Rect, glyph: char, glyph_size: f32, label: &str, selected: bool, key: impl std::hash::Hash + std::fmt::Debug) -> bool {
    let resp = ui.interact(r, Id::new(key), Sense::click());
    let k = ui.ctx().animate_bool_with_time(resp.id.with("h"), resp.hovered(), 0.1);
    let a = if selected { 0.25 } else { 0.12 * k };
    ui.painter().rect_filled(r, 8, theme::alpha(pal.accent, a));
    let g = ui.painter().layout_no_wrap(glyph.to_string(), fid(glyph_size, "icons"), pal.text);
    ui.painter().galley(pos2(r.left() + 12.0, r.center().y - g.size().y / 2.0), g.clone(), pal.text);
    ui.painter().text(pos2(r.left() + 12.0 + g.size().x + 12.0, r.center().y), Align2::LEFT_CENTER, label, fid(13.0, ""), pal.text);
    resp.clicked()
}

fn section_label(ui: &mut Ui, pal: &Pal, t: &str) {
    let mut fmt = egui::TextFormat::simple(fid(11.0, "bold"), pal.muted);
    fmt.extra_letter_spacing = 1.2;
    let job = egui::text::LayoutJob::single_section(t.into(), fmt);
    let g = ui.ctx().fonts_mut(|f| f.layout_job(job));
    let (r, _) = ui.allocate_exact_size(vec2(ui.available_width(), g.size().y), Sense::hover());
    ui.painter().galley(r.min, g, pal.muted);
}

/// SettingRow: radius-10 barBg tile, 58/46 high, control right-anchored.
fn setting_row(ui: &mut Ui, pal: &Pal, title: &str, sub: &str, control: impl FnOnce(&mut Ui, Rect), ctl: egui::Vec2) {
    let w = ui.available_width();
    let h = if sub.is_empty() { 46.0 } else { 58.0 };
    let (r, _) = ui.allocate_exact_size(vec2(w, h), Sense::hover());
    let p = ui.painter().clone();
    p.rect_filled(r, 10, pal.bar_bg);
    let cr = Rect::from_min_size(pos2(r.right() - 12.0 - ctl.x, r.center().y - ctl.y / 2.0), ctl);
    let tw = (cr.left() - 12.0 - (r.left() + 14.0)).max(10.0);
    let tg = elide(ui.ctx(), title, fid(13.0, "bold"), pal.text, tw);
    if sub.is_empty() {
        p.galley(pos2(r.left() + 14.0, r.center().y - tg.size().y / 2.0), tg, pal.text);
    } else {
        let sg = elide(ui.ctx(), sub, fid(12.0, ""), pal.muted, tw);
        let total = tg.size().y + 2.0 + sg.size().y;
        let y = r.center().y - total / 2.0;
        p.galley(pos2(r.left() + 14.0, y), tg.clone(), pal.text);
        p.galley(pos2(r.left() + 14.0, y + tg.size().y + 2.0), sg, pal.muted);
    }
    control(ui, cr);
}

fn mini_switch(ui: &mut Ui, pal: &Pal, r: Rect, label: &str, v: &mut bool, key: &'static str) {
    ui.painter().rect_filled(r, 10, pal.bar_bg);
    ui.painter().text(pos2(r.left() + 12.0, r.center().y), Align2::LEFT_CENTER, label, fid(11.0, "bold"), pal.text);
    let sr = Rect::from_min_size(pos2(r.right() - 12.0 - 32.0, r.center().y - 9.0), vec2(32.0, 18.0));
    switch(ui, pal, sr, v, true, key);
}

fn seg_w(n: usize) -> egui::Vec2 {
    vec2(78.0 * n as f32 + 6.0 * (n as f32 - 1.0), 30.0)
}

/// ThemedSegment: 78×30 pills, spacing 6, accent selected.
fn segment(ui: &mut Ui, pal: &Pal, r: Rect, opts: &[&str], v: &mut String, key: &str) {
    for (i, o) in opts.iter().enumerate() {
        let br = Rect::from_min_size(pos2(r.left() + i as f32 * 84.0, r.top()), vec2(78.0, 30.0));
        let resp = ui.interact(br, Id::new(("seg", key, i)), Sense::click());
        let on = v == o;
        let (fill, stroke) = if on {
            (pal.accent, Stroke::NONE)
        } else {
            (if resp.hovered() || resp.is_pointer_button_down_on() { theme::lighter(pal.card_bg, 1.1) } else { pal.card_bg }, Stroke::new(1.0, pal.card_border))
        };
        ui.painter().rect(br, 8, fill, stroke, StrokeKind::Inside);
        ui.painter().text(br.center(), Align2::CENTER_CENTER, *o, fid(12.0, if on { "bold" } else { "" }), if on { Color32::WHITE } else { pal.text });
        if resp.clicked() {
            *v = o.to_string();
        }
    }
}

/// ThemedSwitch: accent track when on, sliding white knob (150ms OutCubic).
fn switch(ui: &mut Ui, pal: &Pal, r: Rect, v: &mut bool, compact: bool, key: &str) -> bool {
    let (w, h, knob) = if compact { (32.0, 18.0, 12.0) } else { (40.0, 22.0, 16.0) };
    let tr = Rect::from_min_size(pos2(r.left(), r.center().y - h / 2.0), vec2(w, h));
    let resp = ui.interact(tr, Id::new(("sw", key)), Sense::click());
    if resp.clicked() {
        *v = !*v;
    }
    let k = ease_out(ui.ctx().animate_bool_with_time(resp.id.with("k"), *v, 0.15));
    let track = theme::over(pal.card_border, theme::alpha(pal.accent, k));
    ui.painter().rect_filled(tr, (h / 2.0) as u8, if k >= 1.0 { pal.accent } else if k <= 0.0 { pal.card_border } else { track });
    let kx = tr.left() + 3.0 + (w - knob - 6.0) * k;
    ui.painter().circle_filled(pos2(kx + knob / 2.0, tr.top() + 3.0 + knob / 2.0), knob / 2.0, Color32::WHITE);
    resp.clicked()
}

/// ThemedSlider: 6px track with accent fill, 16px knob.
fn slider(ui: &mut Ui, pal: &Pal, r: Rect, v: &mut i32, lo: i32, hi: i32, key: &str) -> bool {
    let resp = ui.interact(r, Id::new(("sl", key)), Sense::click_and_drag());
    let mut changed = false;
    let usable = r.width() - 16.0;
    if let Some(p) = resp.interact_pointer_pos().filter(|_| resp.dragged() || resp.clicked() || resp.is_pointer_button_down_on()) {
        let t = ((p.x - r.left() - 8.0) / usable).clamp(0.0, 1.0);
        let n = lo + (t * (hi - lo) as f32).round() as i32;
        if n != *v {
            *v = n;
            changed = true;
        }
    }
    let t = (*v - lo) as f32 / (hi - lo).max(1) as f32;
    let track = Rect::from_min_size(pos2(r.left(), r.center().y - 3.0), vec2(r.width(), 6.0));
    ui.painter().rect_filled(track, 3, pal.card_border);
    ui.painter().rect_filled(Rect::from_min_size(track.min, vec2(8.0 + usable * t, 6.0)), 3, pal.accent);
    let kc = pos2(r.left() + 8.0 + usable * t, r.center().y);
    let pressed = resp.is_pointer_button_down_on();
    ui.painter().circle(kc, 8.0, if pressed { pal.accent } else { Color32::WHITE }, if pressed { Stroke::NONE } else { Stroke::new(1.0, pal.accent) });
    changed
}

/// ThemedCombo: dark card field + themed popup (rows 24, radius 6).
fn combo(ui: &mut Ui, pal: &Pal, r: Rect, opts: &[(String, String)], value: &mut String, key: &'static str, open: &mut Option<&'static str>) {
    let resp = ui.interact(r, Id::new(("combo", key)), Sense::click());
    let is_open = *open == Some(key);
    ui.painter().rect(r, 8, pal.card_bg, Stroke::new(1.0, if is_open { pal.accent } else { pal.card_border }), StrokeKind::Inside);
    let label = opts.iter().find(|o| &o.1 == value).map(|o| o.0.clone()).unwrap_or_else(|| value.clone());
    let g = elide(ui.ctx(), &label, fid(13.0, ""), pal.text, r.width() - 40.0);
    ui.painter().galley(pos2(r.left() + 10.0, r.center().y - g.size().y / 2.0), g, pal.text);
    ui.painter().text(pos2(r.right() - 12.0, r.center().y), Align2::RIGHT_CENTER, "▾", fid(12.0, ""), pal.muted);
    if resp.clicked() {
        *open = if is_open { None } else { Some(key) };
    }
    if *open == Some(key) {
        let h = (opts.len() as f32 * 24.0 + 16.0).min(320.0);
        let area = egui::Area::new(Id::new(("combo-pop", key))).order(egui::Order::Foreground).fixed_pos(pos2(r.left(), r.bottom() + 4.0));
        let mut picked = None;
        let out = area.show(ui.ctx(), |ui| {
            let (pr, _) = ui.allocate_exact_size(vec2(r.width(), h), Sense::hover());
            ui.painter().rect(pr, 10, theme::over(pal.bar_bg, pal.card_bg), Stroke::new(1.0, pal.card_border), StrokeKind::Inside);
            let mut cui = ui.new_child(egui::UiBuilder::new().max_rect(pr.shrink(8.0)));
            egui::ScrollArea::vertical().scroll_bar_visibility(egui::scroll_area::ScrollBarVisibility::AlwaysHidden).show(&mut cui, |ui| {
                ui.spacing_mut().item_spacing = vec2(0.0, 0.0);
                for o in opts {
                    let (rr, rresp) = ui.allocate_exact_size(vec2(r.width() - 16.0, 24.0), Sense::click());
                    let sel = &o.1 == value;
                    if rresp.hovered() {
                        ui.painter().rect_filled(rr, 6, theme::alpha(pal.accent, 0.2));
                    }
                    let gg = elide(ui.ctx(), &o.0, fid(13.0, ""), if sel { pal.accent } else { pal.text }, rr.width() - 16.0);
                    ui.painter().galley(pos2(rr.left() + 8.0, rr.center().y - gg.size().y / 2.0), gg, pal.text);
                    if rresp.clicked() {
                        picked = Some(o.1.clone());
                    }
                }
            });
        });
        if let Some(v) = picked {
            *value = v;
            *open = None;
        } else if ui.input(|i| i.pointer.any_pressed()) && !out.response.contains_pointer() && !resp.contains_pointer() {
            *open = None;
        }
    }
}

/// Single-choice radio grid (3 per row), accent ring + tick when picked.
fn radio_grid(ui: &mut Ui, pal: &Pal, items: &[(&str, &str, &str)], value: &mut String, key: &str) {
    let w = ui.available_width();
    let cw = (w - 20.0) / 3.0;
    for chunk in items.chunks(3) {
        let (row_r, _) = ui.allocate_exact_size(vec2(w, 58.0), Sense::hover());
        for (i, (v, t, sub)) in chunk.iter().enumerate() {
            let r = Rect::from_min_size(pos2(row_r.left() + i as f32 * (cw + 10.0), row_r.top()), vec2(cw, 58.0));
            let sel = value == v;
            let resp = ui.interact(r, Id::new(("radio", key, *v)), Sense::click());
            ui.painter().rect_filled(r, 10, if sel { theme::alpha(pal.accent, 0.18) } else { pal.bar_bg });
            let tick = Rect::from_center_size(pos2(r.right() - 10.0 - 9.0, r.center().y), vec2(18.0, 18.0));
            ui.painter().circle(tick.center(), 8.0, if sel { pal.accent } else { Color32::TRANSPARENT }, Stroke::new(2.0, if sel { pal.accent } else { pal.muted }));
            if sel {
                ui.painter().text(tick.center(), Align2::CENTER_CENTER, "✓", fid(11.0, "bold"), Color32::WHITE);
            }
            let tw = tick.left() - 8.0 - (r.left() + 12.0);
            let tg = elide(ui.ctx(), t, fid(12.0, "bold"), pal.text, tw);
            let sg = elide(ui.ctx(), sub, fid(11.0, ""), pal.muted, tw);
            let total = tg.size().y + 2.0 + sg.size().y;
            let y = r.center().y - total / 2.0;
            ui.painter().galley(pos2(r.left() + 12.0, y), tg.clone(), pal.text);
            ui.painter().galley(pos2(r.left() + 12.0, y + tg.size().y + 2.0), sg, pal.muted);
            if resp.clicked() {
                *value = v.to_string();
            }
        }
    }
}

fn centered(ui: &mut Ui, t: &str, font: egui::FontId, col: Color32, w: f32) {
    let g = ui.painter().layout_no_wrap(t.into(), font, col);
    let (r, _) = ui.allocate_exact_size(vec2(w, g.size().y), Sense::hover());
    ui.painter().galley(pos2(r.center().x - g.size().x / 2.0, r.top()), g, col);
}

fn wrapped_center(ui: &mut Ui, t: &str, font: egui::FontId, col: Color32, w: f32, max: f32) {
    let mut job = egui::text::LayoutJob::single_section(t.into(), egui::TextFormat::simple(font, col));
    job.wrap.max_width = max.min(w);
    job.halign = egui::Align::Center;
    let g = ui.ctx().fonts_mut(|f| f.layout_job(job));
    let (r, _) = ui.allocate_exact_size(vec2(w, g.size().y), Sense::hover());
    ui.painter().galley(pos2(r.center().x, r.top()), g, col);
}
