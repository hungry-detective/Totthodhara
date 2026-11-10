//! Native popup windows (menus, tooltips) as egui immediate viewports.
//! The shelf is only ~26px tall, so anything that extends past it must be
//! its own transparent, topmost, non-activating window — exactly like WPF
//! Popups/ContextMenus, which are separate HWNDs too.
use crate::winsys::{self, Hwnd, IRect};
use egui::{ViewportBuilder, ViewportId};

pub struct PopupWin {
    pub id: ViewportId,
    title: String,
    hwnd: Hwnd,
    applied: Option<IRect>,
    pub opened_at: f64,
    activate: bool,
}

impl PopupWin {
    pub fn new(key: &str, now: f64, activate: bool) -> Self {
        let title = format!("Totthodhara {key} {}", uuid::Uuid::new_v4().simple());
        Self { id: ViewportId::from_hash_of(&title), title, hwnd: Hwnd(0), applied: None, opened_at: now, activate }
    }

    pub fn hwnd(&self) -> Hwnd {
        self.hwnd
    }

    /// Show this frame at `rect` (physical px). `ppp` = points per pixel ratio of the target monitor.
    pub fn show<R>(
        &mut self,
        ctx: &egui::Context,
        rect: IRect,
        ppp: f32,
        transparent: bool,
        mut content: impl FnMut(&mut egui::Ui) -> R,
    ) -> Option<R> {
        let size = egui::vec2((rect.r - rect.l) as f32 / ppp, (rect.b - rect.t) as f32 / ppp);
        let builder = ViewportBuilder::default()
            .with_title(self.title.clone())
            .with_decorations(false)
            .with_transparent(transparent)
            .with_taskbar(false)
            .with_resizable(false)
            .with_active(self.activate)
            .with_visible(false)
            .with_always_on_top()
            .with_drag_and_drop(false)
            .with_inner_size(size)
            .with_position(egui::pos2(rect.l as f32 / ppp, rect.t as f32 / ppp));
        let mut out = None;
        ctx.show_viewport_immediate(self.id, builder, |ui, _class| {
            out = Some(content(ui));
        });
        if self.hwnd.is_null() {
            self.hwnd = winsys::find_own_window(&self.title);
            if !self.hwnd.is_null() {
                if !self.activate {
                    winsys::make_noactivate_tool(self.hwnd);
                } else {
                    winsys::set_ex_style(self.hwnd, windows::Win32::UI::WindowsAndMessaging::WS_EX_TOOLWINDOW.0, 0);
                }
                winsys::set_corner_round(self.hwnd, false);
                winsys::set_border_none(self.hwnd);
                winsys::dechrome_popup(self.hwnd);
            }
        }
        if !self.hwnd.is_null() && self.applied.map(|a| a.l != rect.l || a.t != rect.t || a.r != rect.r || a.b != rect.b).unwrap_or(true) {
            winsys::move_window(self.hwnd, rect.l, rect.t, rect.r - rect.l, rect.b - rect.t, true);
            if self.applied.is_none() {
                winsys::show_window(self.hwnd, true);
                if self.activate {
                    winsys::force_foreground(self.hwnd);
                }
            }
            self.applied = Some(rect);
        }
        out
    }

    pub fn rect(&self) -> Option<IRect> {
        self.applied
    }
}

/// Fit a w×h popup (px) near an anchor, flipping like WPF Popup placement.
pub fn place_vertical(anchor: IRect, w: i32, h: i32, gap: i32, prefer_below: bool, center_x: bool) -> IRect {
    let (mon, _) = winsys::monitor_of_point((anchor.l + anchor.r) / 2, (anchor.t + anchor.b) / 2);
    let mut x = if center_x { (anchor.l + anchor.r) / 2 - w / 2 } else { anchor.l };
    x = x.clamp(mon.l, (mon.r - w).max(mon.l));
    let below = anchor.b + gap;
    let above = anchor.t - gap - h;
    let y = if prefer_below {
        if below + h <= mon.b { below } else { above.max(mon.t) }
    } else if above >= mon.t {
        above
    } else {
        below.min(mon.b - h)
    };
    IRect { l: x, t: y, r: x + w, b: y + h }
}

/// WPF ToolTip "Mouse" placement: below the cursor, flipped above if needed.
pub fn place_at_mouse(w: i32, h: i32) -> IRect {
    let (cx, cy) = winsys::cursor_pos();
    let (mon, _) = winsys::monitor_of_point(cx, cy);
    let scale = winsys::dpi_of_point(cx, cy);
    let off = (20.0 * scale) as i32;
    let x = cx.clamp(mon.l, (mon.r - w).max(mon.l));
    let y = if cy + off + h <= mon.b { cy + off } else { cy - h - (2.0 * scale) as i32 };
    IRect { l: x, t: y.max(mon.t), r: x + w, b: y.max(mon.t) + h }
}
