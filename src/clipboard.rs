//! Raw Win32 clipboard: read image/files/text, write text/files/image+file.
use std::ffi::c_void;
use windows::Win32::Foundation::*;
use windows::Win32::System::DataExchange::*;
use windows::Win32::System::Memory::*;
use windows::Win32::System::Ole::{CF_DIB, CF_DIBV5, CF_HDROP, CF_UNICODETEXT, CF_BITMAP};
use windows::Win32::UI::Shell::{DragQueryFileW, DROPFILES, HDROP};

pub enum Content {
    Image(image::RgbaImage),
    Files(Vec<String>),
    Text(String),
}

struct Guard;
impl Drop for Guard {
    fn drop(&mut self) {
        unsafe {
            let _ = CloseClipboard();
        }
    }
}

fn open(retries: u32) -> Option<Guard> {
    for _ in 0..retries {
        if unsafe { OpenClipboard(None) }.is_ok() {
            return Some(Guard);
        }
        std::thread::sleep(std::time::Duration::from_millis(30));
    }
    None
}

fn has(fmt: u16) -> bool {
    unsafe { IsClipboardFormatAvailable(fmt as u32).is_ok() }
}

/// Clipboard.ContainsImage → ContainsFileDropList → ContainsText (WPF order).
pub fn read() -> Option<Content> {
    let _g = open(5)?;
    unsafe {
        if has(CF_DIB.0) || has(CF_DIBV5.0) || has(CF_BITMAP.0) {
            let h = GetClipboardData(CF_DIB.0 as u32).ok()?;
            let p = GlobalLock(HGLOBAL(h.0)) as *const u8;
            if p.is_null() {
                return None;
            }
            let size = GlobalSize(HGLOBAL(h.0));
            let bytes = std::slice::from_raw_parts(p, size).to_vec();
            let _ = GlobalUnlock(HGLOBAL(h.0));
            return dib_to_rgba(&bytes).map(Content::Image);
        }
        if has(CF_HDROP.0) {
            let h = GetClipboardData(CF_HDROP.0 as u32).ok()?;
            let hd = HDROP(h.0);
            let n = DragQueryFileW(hd, 0xFFFF_FFFF, None);
            let mut v = Vec::new();
            for i in 0..n {
                let len = DragQueryFileW(hd, i, None) as usize;
                let mut buf = vec![0u16; len + 1];
                DragQueryFileW(hd, i, Some(&mut buf));
                v.push(String::from_utf16_lossy(&buf[..len]));
            }
            return Some(Content::Files(v));
        }
        if has(CF_UNICODETEXT.0) {
            let h = GetClipboardData(CF_UNICODETEXT.0 as u32).ok()?;
            let p = GlobalLock(HGLOBAL(h.0)) as *const u16;
            if p.is_null() {
                return None;
            }
            let size = GlobalSize(HGLOBAL(h.0)) / 2;
            let s = std::slice::from_raw_parts(p, size);
            let t = crate::winsys::from_wide(s);
            let _ = GlobalUnlock(HGLOBAL(h.0));
            return Some(Content::Text(t));
        }
    }
    None
}

fn rd_u32(b: &[u8], o: usize) -> u32 {
    u32::from_le_bytes([b[o], b[o + 1], b[o + 2], b[o + 3]])
}

/// CF_DIB → RGBA. 32/24bpp handled directly; others via the BMP decoder.
fn dib_to_rgba(b: &[u8]) -> Option<image::RgbaImage> {
    if b.len() < 40 {
        return None;
    }
    let hsize = rd_u32(b, 0) as usize;
    let w = rd_u32(b, 4) as i32;
    let h = rd_u32(b, 8) as i32;
    let bpp = u16::from_le_bytes([b[14], b[15]]);
    let comp = rd_u32(b, 16);
    let clr_used = rd_u32(b, 32) as usize;
    if w <= 0 || h == 0 {
        return None;
    }
    let top_down = h < 0;
    let (w, hh) = (w as u32, h.unsigned_abs());
    let mut off = hsize;
    if comp == 3 && hsize == 40 {
        off += 12; // BI_BITFIELDS masks
    }
    if bpp <= 8 {
        off += 4 * if clr_used == 0 { 1usize << bpp } else { clr_used };
    }
    if (bpp == 32 || bpp == 24) && (comp == 0 || comp == 3) {
        let stride = ((w as usize * bpp as usize + 31) / 32) * 4;
        if b.len() < off + stride * hh as usize {
            return None;
        }
        let mut img = image::RgbaImage::new(w, hh);
        let mut any_alpha = false;
        for y in 0..hh {
            let sy = if top_down { y } else { hh - 1 - y } as usize;
            let row = &b[off + sy * stride..];
            for x in 0..w as usize {
                let (bl, g, r, a) = if bpp == 32 {
                    (row[x * 4], row[x * 4 + 1], row[x * 4 + 2], row[x * 4 + 3])
                } else {
                    (row[x * 3], row[x * 3 + 1], row[x * 3 + 2], 255)
                };
                if a != 0 {
                    any_alpha = true;
                }
                img.put_pixel(x as u32, y, image::Rgba([r, g, bl, a]));
            }
        }
        if bpp == 32 && !any_alpha {
            for p in img.pixels_mut() {
                p[3] = 255;
            }
        }
        return Some(img);
    }
    // Fallback: wrap in a BITMAPFILEHEADER and let `image` decode it.
    let mut f = Vec::with_capacity(b.len() + 14);
    f.extend_from_slice(b"BM");
    f.extend_from_slice(&((b.len() + 14) as u32).to_le_bytes());
    f.extend_from_slice(&[0, 0, 0, 0]);
    f.extend_from_slice(&((off + 14) as u32).to_le_bytes());
    f.extend_from_slice(b);
    image::load_from_memory_with_format(&f, image::ImageFormat::Bmp).ok().map(|i| i.to_rgba8())
}

unsafe fn global_from(bytes: &[u8]) -> Option<HANDLE> {
    let g = GlobalAlloc(GMEM_MOVEABLE, bytes.len()).ok()?;
    let p = GlobalLock(g) as *mut u8;
    if p.is_null() {
        return None;
    }
    std::ptr::copy_nonoverlapping(bytes.as_ptr(), p, bytes.len());
    let _ = GlobalUnlock(g);
    Some(HANDLE(g.0))
}

fn mark_internal() {
    let now = unsafe { windows::Win32::System::SystemInformation::GetTickCount() };
    crate::events::INTERNAL_UNTIL_MS.store(now.wrapping_add(1500), std::sync::atomic::Ordering::SeqCst);
}

fn text_bytes(s: &str) -> Vec<u8> {
    let w = crate::winsys::wide(s);
    w.iter().flat_map(|c| c.to_le_bytes()).collect()
}

fn hdrop_bytes(files: &[String]) -> Vec<u8> {
    let mut v = Vec::new();
    let hdr = DROPFILES { pFiles: std::mem::size_of::<DROPFILES>() as u32, pt: POINT::default(), fNC: false.into(), fWide: true.into() };
    let hp = &hdr as *const _ as *const u8;
    v.extend_from_slice(unsafe { std::slice::from_raw_parts(hp, std::mem::size_of::<DROPFILES>()) });
    for f in files {
        let w = crate::winsys::wide(f);
        v.extend(w.iter().flat_map(|c| c.to_le_bytes()));
    }
    v.extend_from_slice(&[0, 0]);
    v
}

pub fn dib_bytes(img: &image::RgbaImage) -> Vec<u8> {
    let (w, h) = img.dimensions();
    let mut v = Vec::with_capacity(40 + (w * h * 4) as usize);
    v.extend_from_slice(&40u32.to_le_bytes());
    v.extend_from_slice(&(w as i32).to_le_bytes());
    v.extend_from_slice(&(h as i32).to_le_bytes()); // bottom-up
    v.extend_from_slice(&1u16.to_le_bytes());
    v.extend_from_slice(&32u16.to_le_bytes());
    v.extend_from_slice(&0u32.to_le_bytes()); // BI_RGB
    v.extend_from_slice(&(w * h * 4).to_le_bytes());
    v.extend_from_slice(&[0u8; 16]);
    for y in (0..h).rev() {
        for x in 0..w {
            let p = img.get_pixel(x, y);
            v.extend_from_slice(&[p[2], p[1], p[0], p[3]]);
        }
    }
    v
}

fn set_formats(list: &[(u32, Vec<u8>)]) -> bool {
    mark_internal();
    let Some(_g) = open(10) else { return false };
    unsafe {
        let _ = EmptyClipboard();
        for (fmt, bytes) in list {
            if let Some(h) = global_from(bytes) {
                let _ = SetClipboardData(*fmt, Some(h));
            }
        }
    }
    true
}

pub fn set_text(s: &str) -> bool {
    set_formats(&[(CF_UNICODETEXT.0 as u32, text_bytes(s))])
}

pub fn set_files(files: &[String]) -> bool {
    set_formats(&[(CF_HDROP.0 as u32, hdrop_bytes(files))])
}

/// Bitmap + file drop in one DataObject (image editors see DIB, Explorer HDROP).
pub fn set_image_and_file(img: &image::RgbaImage, file: &str) -> bool {
    set_formats(&[(CF_DIB.0 as u32, dib_bytes(img)), (CF_HDROP.0 as u32, hdrop_bytes(&[file.to_string()]))])
}

#[allow(dead_code)]
fn _unused(_: *const c_void) {}
