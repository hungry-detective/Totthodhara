//! ClipStore text/code/color tools (Transform…, Code tools…, Copy as …).
use egui::Color32;

pub fn find_mail(t: &str) -> Option<String> {
    let re = regex::Regex::new(r"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}").ok()?;
    re.find(t).map(|m| m.as_str().to_string())
}

pub fn find_phone(t: &str) -> Option<String> {
    let re = regex::Regex::new(r"\+?\d[\d\s().\-]{6,}\d").ok()?;
    let found = re.find_iter(t).map(|m| m.as_str().trim().to_string()).find(|m| m.chars().filter(|c| c.is_ascii_digit()).count() >= 7);
    found
}

fn title_case(s: &str) -> String {
    let re = regex::Regex::new(r"\w\S*").unwrap();
    re.replace_all(s, |c: &regex::Captures| {
        let w = &c[0];
        let mut ch = w.chars();
        match ch.next() {
            Some(f) => f.to_uppercase().collect::<String>() + &ch.as_str().to_lowercase(),
            None => String::new(),
        }
    })
    .into_owned()
}

const B64: &[u8; 64] = b"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

fn b64_enc(b: &[u8]) -> String {
    let mut o = String::new();
    for c in b.chunks(3) {
        let n = (c[0] as u32) << 16 | (*c.get(1).unwrap_or(&0) as u32) << 8 | *c.get(2).unwrap_or(&0) as u32;
        for i in 0..4 {
            if i <= c.len() {
                o.push(B64[(n >> (18 - 6 * i) & 63) as usize] as char);
            } else {
                o.push('=');
            }
        }
    }
    o
}

fn b64_dec(s: &str) -> Option<Vec<u8>> {
    let s: Vec<u8> = s.bytes().filter(|c| !c.is_ascii_whitespace() && *c != b'=').collect();
    let mut out = Vec::new();
    let (mut acc, mut bits) = (0u32, 0);
    for c in s {
        let v = B64.iter().position(|&b| b == c).or_else(|| match c {
            b'-' => Some(62),
            b'_' => Some(63),
            _ => None,
        })? as u32;
        acc = acc << 6 | v;
        bits += 6;
        if bits >= 8 {
            bits -= 8;
            out.push((acc >> bits) as u8);
        }
    }
    Some(out)
}

fn url_enc(s: &str) -> String {
    s.bytes()
        .map(|b| match b {
            b'A'..=b'Z' | b'a'..=b'z' | b'0'..=b'9' | b'-' | b'_' | b'.' | b'!' | b'~' | b'*' | b'\'' | b'(' | b')' => (b as char).to_string(),
            _ => format!("%{b:02X}"),
        })
        .collect()
}

fn url_dec(s: &str) -> Option<String> {
    let b = s.as_bytes();
    let mut out = Vec::new();
    let mut i = 0;
    while i < b.len() {
        if b[i] == b'%' && i + 2 < b.len() {
            let h = std::str::from_utf8(&b[i + 1..i + 3]).ok()?;
            out.push(u8::from_str_radix(h, 16).ok()?);
            i += 3;
        } else {
            out.push(b[i]);
            i += 1;
        }
    }
    String::from_utf8(out).ok()
}

pub fn apply(tool: &str, t: &str) -> Result<String, String> {
    let lines: Vec<&str> = t.split('\n').collect();
    Ok(match tool {
        "t-upper" => t.to_uppercase(),
        "t-lower" => t.to_lowercase(),
        "t-title" => title_case(t),
        "t-trim" => lines.iter().map(|l| l.trim()).filter(|l| !l.is_empty()).collect::<Vec<_>>().join("\n"),
        "t-sort" => {
            let mut v = lines.clone();
            v.sort_by_key(|a| a.to_lowercase());
            v.join("\n")
        }
        "t-unique" => {
            let mut out: Vec<&str> = vec![];
            for l in lines {
                if !out.contains(&l) {
                    out.push(l);
                }
            }
            out.join("\n")
        }
        "t-join" => lines.iter().map(|l| l.trim()).filter(|l| !l.is_empty()).collect::<Vec<_>>().join(" "),
        "c-json" => {
            let v: serde_json::Value = serde_json::from_str(t).map_err(|_| "Not JSON".to_string())?;
            serde_json::to_string_pretty(&v).map_err(|_| "Not JSON".to_string())?
        }
        "c-b64enc" => b64_enc(t.as_bytes()),
        "c-b64dec" => b64_dec(t.trim()).and_then(|b| String::from_utf8(b).ok()).ok_or("Can't decode")?,
        "c-urlenc" => url_enc(t),
        "c-urldec" => url_dec(t).ok_or("Can't decode")?,
        _ => t.to_string(),
    })
}

pub fn flash_name(tool: &str) -> String {
    let n = match tool {
        "t-upper" => "Uppercased",
        "t-lower" => "Lowercased",
        "t-title" => "Title Cased",
        "t-trim" => "Trimmed",
        "t-sort" => "Sorted",
        "t-unique" => "Deduped",
        "t-join" => "Joined",
        "c-json" => "Formatted",
        "c-b64enc" | "c-urlenc" => "Encoded",
        "c-b64dec" | "c-urldec" => "Decoded",
        _ => "Done",
    };
    format!("{n}!")
}

pub fn color_format(id: &crate::app::MenuId, c: Color32) -> (&'static str, String) {
    use crate::app::MenuId;
    let [r, g, b, _] = c.to_srgba_unmultiplied();
    match id {
        MenuId::ColorRgb => ("RGB", format!("rgb({r}, {g}, {b})")),
        MenuId::ColorHsl => {
            let (rf, gf, bf) = (r as f32 / 255.0, g as f32 / 255.0, b as f32 / 255.0);
            let mx = rf.max(gf).max(bf);
            let mn = rf.min(gf).min(bf);
            let l = (mx + mn) / 2.0;
            let d = mx - mn;
            let (h, s) = if d == 0.0 {
                (0.0, 0.0)
            } else {
                let s = d / (1.0 - (2.0 * l - 1.0).abs());
                let h = if mx == rf {
                    60.0 * (((gf - bf) / d) % 6.0)
                } else if mx == gf {
                    60.0 * ((bf - rf) / d + 2.0)
                } else {
                    60.0 * ((rf - gf) / d + 4.0)
                };
                ((h + 360.0) % 360.0, s)
            };
            ("HSL", format!("hsl({}, {}%, {}%)", h.round(), (s * 100.0).round(), (l * 100.0).round()))
        }
        _ => ("HEX", format!("#{r:02X}{g:02X}{b:02X}")),
    }
}
