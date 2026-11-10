//! The items area's OpacityMask (transparent → black at 3% / 97%), done on the
//! CPU: tessellate the item shapes, slice triangles at vertical cut lines
//! inside the fade bands, then scale each vertex's alpha by the mask.
use egui::epaint::{Mesh, Vertex};
use egui::{Color32, Pos2, Rect, Shape};

fn lerp_v(a: &Vertex, b: &Vertex, t: f32) -> Vertex {
    let c = |x: u8, y: u8| (x as f32 + (y as f32 - x as f32) * t).round() as u8;
    let ca = a.color.to_array();
    let cb = b.color.to_array();
    Vertex {
        pos: Pos2::new(a.pos.x + (b.pos.x - a.pos.x) * t, a.pos.y + (b.pos.y - a.pos.y) * t),
        uv: Pos2::new(a.uv.x + (b.uv.x - a.uv.x) * t, a.uv.y + (b.uv.y - a.uv.y) * t),
        color: Color32::from_rgba_premultiplied(c(ca[0], cb[0]), c(ca[1], cb[1]), c(ca[2], cb[2]), c(ca[3], cb[3])),
    }
}

/// Clip polygon to x <= cut (keep_left) or x >= cut.
fn clip(poly: &[Vertex], cut: f32, keep_left: bool) -> Vec<Vertex> {
    let inside = |v: &Vertex| if keep_left { v.pos.x <= cut } else { v.pos.x >= cut };
    let mut out = Vec::with_capacity(poly.len() + 2);
    for i in 0..poly.len() {
        let a = &poly[i];
        let b = &poly[(i + 1) % poly.len()];
        let (ia, ib) = (inside(a), inside(b));
        if ia {
            out.push(*a);
        }
        if ia != ib {
            let t = (cut - a.pos.x) / (b.pos.x - a.pos.x);
            out.push(lerp_v(a, b, t));
        }
    }
    out
}

fn mask(x: f32, area: Rect, band: f32, kl: f32, kr: f32) -> f32 {
    if band <= 0.0 {
        return 1.0;
    }
    let l = ((x - area.left()) / band).clamp(0.0, 1.0);
    let r = ((area.right() - x) / band).clamp(0.0, 1.0);
    (1.0 - kl * (1.0 - l)) * (1.0 - kr * (1.0 - r))
}

/// Returns a single mesh shape with edge fades (`kl`/`kr` = strength 0..1).
pub fn faded(ctx: &egui::Context, shapes: Vec<Shape>, area: Rect, ppp: f32, band: f32, kl: f32, kr: f32) -> Shape {
    if kl <= 0.0 && kr <= 0.0 {
        return Shape::Vec(shapes);
    }
    let mut cuts: Vec<f32> = Vec::new();
    let steps = (band / 3.0).ceil().max(1.0) as i32;
    for i in 0..=steps {
        let d = band * i as f32 / steps as f32;
        cuts.push(area.left() + d);
        cuts.push(area.right() - d);
    }
    cuts.sort_by(|a, b| a.partial_cmp(b).unwrap());

    let tex_size = ctx.fonts(|f| f.font_image_size());
    let opts = ctx.tessellation_options(|o| *o);
    let mut tess = egui::epaint::Tessellator::new(ppp, opts, tex_size, vec![]);
    let mut meshes: Vec<Mesh> = Vec::new();
    for s in shapes {
        let mut m = Mesh::default();
        // Text shapes keep their own texture (font atlas) id.
        tess.tessellate_shape(s, &mut m);
        if !m.is_empty() {
            meshes.push(m);
        }
    }
    let mut out_shapes = Vec::with_capacity(meshes.len());
    for m in meshes {
        let mut out = Mesh::with_texture(m.texture_id);
        for tri in m.indices.chunks(3) {
            let t = [m.vertices[tri[0] as usize], m.vertices[tri[1] as usize], m.vertices[tri[2] as usize]];
            let minx = t.iter().map(|v| v.pos.x).fold(f32::MAX, f32::min);
            let maxx = t.iter().map(|v| v.pos.x).fold(f32::MIN, f32::max);
            let in_band = minx < area.left() + band || maxx > area.right() - band;
            let mut pieces: Vec<Vec<Vertex>> = vec![t.to_vec()];
            if in_band {
                for &c in cuts.iter().filter(|&&c| c > minx && c < maxx) {
                    let mut next = Vec::new();
                    for p in pieces {
                        let a = clip(&p, c, true);
                        let b = clip(&p, c, false);
                        if a.len() >= 3 {
                            next.push(a);
                        }
                        if b.len() >= 3 {
                            next.push(b);
                        }
                    }
                    pieces = next;
                }
            }
            for p in pieces {
                let base = out.vertices.len() as u32;
                for v in &p {
                    let mut v = *v;
                    if in_band {
                        let k = mask(v.pos.x, area, band, kl, kr);
                        v.color = v.color.gamma_multiply(k);
                    }
                    out.vertices.push(v);
                }
                for i in 1..p.len() as u32 - 1 {
                    out.indices.extend_from_slice(&[base, base + i, base + i + 1]);
                }
            }
        }
        out_shapes.push(Shape::mesh(out));
    }
    Shape::Vec(out_shapes)
}
