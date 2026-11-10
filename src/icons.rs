//! Fonts (Segoe UI weights + Segoe Fluent Icons) and the Qt MeterIcon
//! vector glyphs (CPU / RAM / clock / net arrows / code).
use egui::{Color32, FontData, FontDefinitions, FontFamily, Painter, Pos2, Rect, Shape, Stroke, StrokeKind, Vec2};
use std::sync::Arc;

pub const APP_PNG: &[u8] = include_bytes!("../assets/app.png");

// Segoe Fluent Icons / MDL2 code points used by the Qt edition.
pub const CHEVRON_LEFT: char = '\u{E76B}';
pub const CHEVRON_RIGHT: char = '\u{E76C}';
pub const SETTINGS: char = '\u{E713}';
pub const SEARCH: char = '\u{E721}';
pub const DELETE: char = '\u{E74D}';
pub const CLOSE: char = '\u{E8BB}';
pub const GLOBE: char = '\u{E774}';
pub const OPEN_LINK: char = '\u{E8A7}';
pub const VIDEO: char = '\u{E714}';
pub const AUDIO: char = '\u{E767}';
pub const FILE: char = '\u{E7C3}';
pub const PIN: char = '\u{E718}';
pub const STAR: char = '\u{E734}';
pub const STAR_OUTLINE: char = '\u{E735}';
pub const EDIT: char = '\u{E70F}';
pub const CODE: char = '\u{E943}';
pub const PALETTE: char = '\u{E790}';
pub const LINK: char = '\u{E71B}';
pub const MAIL: char = '\u{E76E}';
pub const PHONE: char = '\u{E717}';
pub const CONTACT: char = '\u{E77B}';
pub const MAXIMIZE: char = '\u{E922}';
pub const RESTORE: char = '\u{E923}';
pub const SHOW: char = '\u{E7B3}';

pub fn fam(name: &str) -> FontFamily {
    FontFamily::Name(name.into())
}

fn sys_font(names: &[&str]) -> Option<Vec<u8>> {
    let dir = std::env::var("WINDIR").unwrap_or_else(|_| r"C:\Windows".into());
    names.iter().find_map(|n| std::fs::read(format!(r"{dir}\Fonts\{n}")).ok())
}

pub fn install_fonts(ctx: &egui::Context) {
    let mut f = FontDefinitions::default();
    let mut have = |key: &str, files: &[&str]| -> bool {
        match sys_font(files) {
            Some(d) => {
                f.font_data.insert(key.into(), Arc::new(FontData::from_owned(d)));
                true
            }
            None => false,
        }
    };
    let ui = have("segoe", &["segoeui.ttf"]);
    let bd = have("segoe-b", &["segoeuib.ttf"]);
    let bl = have("segoe-black", &["seguibl.ttf"]);
    let sb = have("segoe-sb", &["seguisb.ttf"]);
    let fl = have("fluent", &["SegoeIcons.ttf"]);
    let mdl = have("mdl2", &["segmdl2.ttf"]);
    let sym = have("segoe-sym", &["seguisym.ttf"]);
    let nir = have("nirmala", &["Nirmala.ttc", "Nirmala.ttf", "nirmala.ttf"]);
    let mono = have("consolas", &["consola.ttf"]);
    let defaults = f.families.get(&FontFamily::Proportional).cloned().unwrap_or_default();
    let chain = |first: &[(&str, bool)]| -> Vec<String> {
        let mut v: Vec<String> = first.iter().filter(|(_, ok)| *ok).map(|(n, _)| n.to_string()).collect();
        if sym {
            v.push("segoe-sym".into());
        }
        if nir {
            v.push("nirmala".into());
        }
        v.extend(defaults.iter().cloned());
        v
    };
    f.families.insert(FontFamily::Proportional, chain(&[("segoe", ui)]));
    f.families.insert(fam("bold"), chain(&[("segoe-b", bd), ("segoe", ui)]));
    f.families.insert(fam("black"), chain(&[("segoe-black", bl), ("segoe-b", bd), ("segoe", ui)]));
    f.families.insert(fam("semibold"), chain(&[("segoe-sb", sb), ("segoe", ui)]));
    f.families.insert(fam("icons"), chain(&[("fluent", fl), ("mdl2", mdl), ("segoe", ui)]));
    if mono {
        let mut m = vec!["consolas".to_string()];
        m.extend(f.families.get(&FontFamily::Monospace).cloned().unwrap_or_default());
        f.families.insert(FontFamily::Monospace, m);
    }
    ctx.set_fonts(f);
}

pub fn app_icon_rgba() -> image::RgbaImage {
    image::load_from_memory(APP_PNG).map(|i| i.to_rgba8()).unwrap_or_default()
}

/// Trim fully transparent margins, pad square (tray icon source).
pub fn crop_pad_square(src: &image::RgbaImage) -> image::RgbaImage {
    let (w, h) = src.dimensions();
    let (mut x0, mut y0, mut x1, mut y1) = (w, h, 0u32, 0u32);
    let mut any = false;
    for (x, y, p) in src.enumerate_pixels() {
        if p[3] > 0 {
            any = true;
            x0 = x0.min(x);
            y0 = y0.min(y);
            x1 = x1.max(x);
            y1 = y1.max(y);
        }
    }
    if !any {
        return src.clone();
    }
    let (cw, ch) = (x1 - x0 + 1, y1 - y0 + 1);
    let size = cw.max(ch);
    let mut sq = image::RgbaImage::new(size, size);
    let crop = image::imageops::crop_imm(src, x0, y0, cw, ch).to_image();
    image::imageops::overlay(&mut sq, &crop, ((size - cw) / 2) as i64, ((size - ch) / 2) as i64);
    sq
}

#[derive(Clone, Copy, PartialEq)]
pub enum Meter {
    Cpu,
    Ram,
    Clock,
    Up,
    Down,
    Code,
}

impl Meter {
    /// MeterIcon implicit size (ram/code 24×18, others 18×18).
    pub fn size(self) -> Vec2 {
        match self {
            Meter::Ram | Meter::Code => Vec2::new(24.0, 18.0),
            _ => Vec2::new(18.0, 18.0),
        }
    }
}

/// Draw a MeterIcon with its top-left at `o` (canvas units = px).
pub fn draw_meter(p: &Painter, o: Pos2, kind: Meter, ink: Color32, scale: f32) {
    p.extend(meter_shapes(o, kind, ink, scale));
}

/// Shapes are authored on the 18px canvas and scaled by `scale` (bar size),
/// strokes included, so icons stay proportional to the text beside them.
pub fn meter_shapes(o: Pos2, kind: Meter, ink: Color32, scale: f32) -> Vec<Shape> {
    let mut out = meter_shapes_unit(Pos2::ZERO, kind, ink);
    let tr = egui::emath::TSTransform::new(o.to_vec2(), scale);
    for s in &mut out {
        s.transform(tr);
    }
    out
}

fn meter_shapes_unit(o: Pos2, kind: Meter, ink: Color32) -> Vec<Shape> {
    let mut out: Vec<Shape> = Vec::new();
    let at = |x: f32, y: f32| o + Vec2::new(x, y);
    let mut line = |out: &mut Vec<Shape>, pts: &[(f32, f32)], w: f32| {
        let v: Vec<Pos2> = pts.iter().map(|&(x, y)| at(x, y)).collect();
        out.push(Shape::line(v, Stroke::new(w, ink)));
    };
    match kind {
        Meter::Cpu => {
            out.push(Shape::rect_stroke(Rect::from_min_size(at(2.5, 2.5), Vec2::splat(13.0)), 3, Stroke::new(1.6, ink), StrokeKind::Middle));
            line(&mut out, &[(4.8, 9.5), (7.0, 9.5), (8.2, 6.0), (10.2, 12.0), (11.6, 8.2), (13.4, 8.2)], 1.6);
        }
        Meter::Ram => {
            out.push(Shape::rect_stroke(Rect::from_min_size(at(4.0, 3.0), Vec2::new(16.0, 12.0)), 2, Stroke::new(1.0, ink), StrokeKind::Middle));
            let mut x = 8.2;
            while x <= 16.1 {
                line(&mut out, &[(x, 15.0), (x, 17.0)], 1.0);
                x += 3.9;
            }
        }
        Meter::Clock => {
            out.push(Shape::circle_stroke(at(9.0, 9.0), 6.5, Stroke::new(1.6, ink)));
            line(&mut out, &[(9.0, 9.0), (9.0, 4.3)], 1.6);
            line(&mut out, &[(9.0, 9.0), (12.7, 10.7)], 1.6);
        }
        Meter::Up | Meter::Down => {
            let dir = if kind == Meter::Up { -1.0 } else { 1.0 };
            let tip = 9.0 + dir * 6.0;
            let base = 9.0 - dir * 6.0;
            line(&mut out, &[(9.0, tip), (9.0, base)], 1.5);
            line(&mut out, &[(4.5, tip - dir * 4.5), (9.0, tip), (13.5, tip - dir * 4.5)], 1.5);
        }
        Meter::Code => {
            line(&mut out, &[(10.5, 4.5), (6.0, 9.0), (10.5, 13.5)], 1.6);
            line(&mut out, &[(12.5, 15.0), (15.5, 3.0)], 1.6);
            line(&mut out, &[(17.5, 4.5), (22.0, 9.0), (17.5, 13.5)], 1.6);
        }
    }
    out
}
