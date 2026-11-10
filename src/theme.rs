//! Port of the Qt edition's AppState palette (theme, glass fill, tones).
use crate::settings::Settings;
use egui::Color32;

pub fn rgb(r: u8, g: u8, b: u8) -> Color32 {
    Color32::from_rgb(r, g, b)
}
pub fn rgbaf(r: f32, g: f32, b: f32, a: f32) -> Color32 {
    let c = |v: f32| (v.clamp(0.0, 1.0) * 255.0).round() as u8;
    Color32::from_rgba_unmultiplied(c(r), c(g), c(b), c(a))
}

pub fn hex_color(s: &str) -> Option<Color32> {
    let h = s.trim().trim_start_matches('#');
    let v = u32::from_str_radix(h, 16).ok()?;
    Some(match h.len() {
        6 => rgb((v >> 16) as u8, (v >> 8) as u8, v as u8),
        8 => Color32::from_rgba_unmultiplied((v >> 16) as u8, (v >> 8) as u8, v as u8, (v >> 24) as u8),
        3 => {
            let e = |x: u32| ((x & 0xF) * 17) as u8;
            rgb(e(v >> 8), e(v >> 4), e(v))
        }
        _ => return None,
    })
}

/// Live Windows state (ThemeWatcher).
#[derive(Clone, Copy, PartialEq, Debug)]
pub struct OsState {
    pub system_dark: bool,
    pub transparency_on: bool,
    pub color_prevalence: bool,
    pub accent: Color32,
}

impl OsState {
    pub fn read() -> Self {
        Self {
            system_dark: !crate::winsys::system_uses_light_theme(),
            transparency_on: crate::winsys::system_transparency_enabled() && crate::winsys::is_win11(),
            color_prevalence: crate::winsys::color_prevalence(),
            accent: crate::winsys::accent_color().map(|(r, g, b)| rgb(r, g, b)).unwrap_or(rgb(0x00, 0x78, 0xD4)),
        }
    }
}

#[derive(Clone, PartialEq)]
pub struct Pal {
    pub dark: bool,
    /// DWM blur under the pill (System + transparent bar + OS effects on).
    pub glass: bool,
    pub bar_bg: Color32,
    pub bar_fill: Color32,
    pub card_bg: Color32,
    pub card_border: Color32,
    pub text: Color32,
    pub muted: Color32,
    pub accent: Color32,
    pub menu_bg: Color32,
}

pub const DANGER: Color32 = Color32::from_rgb(0xff, 0x5c, 0x5c);

pub fn build(s: &Settings, os: &OsState) -> Pal {
    let system = s.theme == "System";
    let dark = match s.theme.as_str() {
        "Dark" => true,
        "Light" => false,
        _ => os.system_dark,
    };
    let bar_bg = if dark { rgb(0x20, 0x20, 0x20) } else { rgb(0xf2, 0xf2, 0xf2) };
    let card_bg = if system {
        if dark { rgbaf(0.16, 0.16, 0.19, 0.88) } else { rgbaf(1.0, 1.0, 1.0, 0.72) }
    } else if dark {
        rgb(0x2d, 0x2d, 0x2d)
    } else {
        Color32::WHITE
    };
    let card_border = if system {
        if dark { rgbaf(1.0, 1.0, 1.0, 0.28) } else { rgbaf(0.0, 0.0, 0.0, 0.28) }
    } else if dark {
        rgb(0x3d, 0x3d, 0x3d)
    } else {
        rgb(0xd4, 0xd4, 0xd4)
    };
    let accent = if system && os.color_prevalence { os.accent } else { rgb(0x4c, 0xc2, 0xff) };
    let glass_a = s.glass_alpha.clamp(0.05, 1.0);
    let auto_fill = if !system || !s.transparent_bar || !os.transparency_on {
        bar_bg
    } else {
        let [ar, ag, ab, _] = os.accent.to_array().map(|v| v as f32 / 255.0);
        let t = if os.color_prevalence { 0.10 } else { 0.0 };
        if dark {
            rgbaf(0.22 * (1.0 - t) + ar * t, 0.22 * (1.0 - t) + ag * t, 0.24 * (1.0 - t) + ab * t, glass_a)
        } else {
            rgbaf(0.953 * (1.0 - t) + ar * t, 0.953 * (1.0 - t) + ag * t, 0.953 * (1.0 - t) + ab * t, glass_a)
        }
    };
    let bar_fill = if s.shelf_tone != "auto" && system {
        let tone = match s.shelf_tone.as_str() {
            "custom" => hex_color(&s.custom_tone).unwrap_or(rgb(0x9d, 0x4f, 0xd0)),
            "windows" => os.accent,
            "dark" => rgb(0x20, 0x20, 0x20),
            "grey" => rgb(0x3a, 0x3a, 0x3d),
            "light" => rgb(0xf2, 0xf2, 0xf2),
            "blue" => rgb(0x27, 0x4e, 0x7d),
            _ => Color32::BLACK,
        };
        let k = s.tone_intensity.clamp(0.0, 1.0);
        let a = auto_fill.to_srgba_unmultiplied().map(|v| v as f32 / 255.0);
        let b = tone.to_srgba_unmultiplied().map(|v| v as f32 / 255.0);
        let m = |i: usize| a[i] * (1.0 - k) + b[i] * k;
        rgbaf(m(0), m(1), m(2), if s.transparent_bar && os.transparency_on { glass_a } else { 1.0 })
    } else {
        auto_fill
    };
    Pal {
        dark,
        glass: system && s.transparent_bar && os.transparency_on,
        bar_bg,
        bar_fill,
        card_bg,
        card_border,
        text: if dark { Color32::WHITE } else { rgb(0x1b, 0x1b, 0x1b) },
        muted: if dark { rgb(0xa0, 0xa0, 0xa0) } else { rgb(0x61, 0x61, 0x61) },
        accent,
        menu_bg: if dark { rgb(0x2b, 0x2b, 0x2b) } else { Color32::WHITE },
    }
}

/// `Qt.rgba(accent, a)`.
pub fn alpha(c: Color32, a: f32) -> Color32 {
    let [r, g, b, _] = c.to_srgba_unmultiplied();
    Color32::from_rgba_unmultiplied(r, g, b, (a.clamp(0.0, 1.0) * 255.0).round() as u8)
}

/// Multiply existing alpha.
pub fn fade(c: Color32, k: f32) -> Color32 {
    let [r, g, b, a] = c.to_srgba_unmultiplied();
    Color32::from_rgba_unmultiplied(r, g, b, (a as f32 * k.clamp(0.0, 1.0)).round() as u8)
}

/// `Qt.lighter(c, f)` (HSV value scale).
pub fn lighter(c: Color32, f: f32) -> Color32 {
    let [r, g, b, a] = c.to_srgba_unmultiplied();
    let s = |v: u8| ((v as f32 * f).min(255.0)) as u8;
    Color32::from_rgba_unmultiplied(s(r), s(g), s(b), a)
}

/// Source-over blend of `top` onto `base` (both unmultiplied).
pub fn over(base: Color32, top: Color32) -> Color32 {
    let b = base.to_srgba_unmultiplied().map(|v| v as f32 / 255.0);
    let t = top.to_srgba_unmultiplied().map(|v| v as f32 / 255.0);
    let a = t[3] + b[3] * (1.0 - t[3]);
    if a <= 0.0 {
        return Color32::TRANSPARENT;
    }
    let ch = |i: usize| (t[i] * t[3] + b[i] * b[3] * (1.0 - t[3])) / a;
    rgbaf(ch(0), ch(1), ch(2), a)
}
