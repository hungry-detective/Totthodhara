//! Drag a card OUT to any app (chat upload boxes, Explorer, editors).
//!
//! Files use the shell's own data object (BHID_DataObject), i.e. exactly
//! what Explorer hands out — targets that only accept real file drags
//! (WhatsApp, browsers' upload areas) treat it like a file from Explorer.
//! Images additionally carry CF_DIB (preview-aware targets). Text drags a
//! plain CF_UNICODETEXT object. SHDoDragDrop with no drop source uses the
//! shell's default source (standard cursors, Esc cancels). Runs a modal OLE
//! loop on the UI thread (OleInitialize'd by winit), like Qt's DoDragDrop.
use crate::winsys::{wide, Hwnd};
use windows::core::PCWSTR;
use windows::Win32::Foundation::*;
use windows::Win32::System::Com::*;
use windows::Win32::System::Memory::*;
use windows::Win32::System::Ole::*;
use windows::Win32::UI::Shell::*;

unsafe fn hglobal(bytes: &[u8]) -> Option<HGLOBAL> {
    let g = GlobalAlloc(GMEM_MOVEABLE, bytes.len()).ok()?;
    let p = GlobalLock(g) as *mut u8;
    if p.is_null() {
        return None;
    }
    std::ptr::copy_nonoverlapping(bytes.as_ptr(), p, bytes.len());
    let _ = GlobalUnlock(g);
    Some(g)
}

unsafe fn set_hglobal(obj: &IDataObject, cf: u16, bytes: &[u8]) {
    let Some(g) = hglobal(bytes) else { return };
    let fmt = FORMATETC { cfFormat: cf, ptd: std::ptr::null_mut(), dwAspect: DVASPECT_CONTENT.0, lindex: -1, tymed: TYMED_HGLOBAL.0 as u32 };
    let med = STGMEDIUM { tymed: TYMED_HGLOBAL.0 as u32, u: STGMEDIUM_0 { hGlobal: g }, pUnkForRelease: std::mem::ManuallyDrop::new(None) };
    // fRelease = TRUE: the data object owns the HGLOBAL now.
    let _ = obj.SetData(&fmt, &med, true);
}

/// Returns Some(true) when dropped, Some(false) when cancelled, None on error.
pub fn drag_file(hwnd: Hwnd, path: &str, dib: Option<Vec<u8>>) -> Option<bool> {
    unsafe {
        let w = wide(path);
        let item: IShellItem = SHCreateItemFromParsingName(PCWSTR(w.as_ptr()), None).ok()?;
        let obj: IDataObject = item.BindToHandler(None, &BHID_DataObject).ok()?;
        if let Some(d) = dib {
            set_hglobal(&obj, CF_DIB.0, &d);
        }
        run(hwnd, &obj, DROPEFFECT_COPY | DROPEFFECT_LINK)
    }
}

pub fn drag_text(hwnd: Hwnd, text: &str) -> Option<bool> {
    unsafe {
        let obj: IDataObject = SHCreateDataObject(None, None, None::<&IDataObject>).ok()?;
        let w = wide(text);
        let bytes: Vec<u8> = w.iter().flat_map(|c| c.to_le_bytes()).collect();
        set_hglobal(&obj, CF_UNICODETEXT.0, &bytes);
        run(hwnd, &obj, DROPEFFECT_COPY)
    }
}

unsafe fn run(hwnd: Hwnd, obj: &IDataObject, effects: DROPEFFECT) -> Option<bool> {
    match SHDoDragDrop(Some(hwnd.h()), obj, None::<&IDropSource>, effects) {
        Ok(e) => Some(e != DROPEFFECT_NONE),
        Err(_) => Some(false),
    }
}

/// The OLE loop swallows the mouse-up; replay it so egui releases the card.
pub fn release_mouse(hwnd: Hwnd) {
    unsafe {
        let (x, y) = crate::winsys::cursor_pos();
        let mut p = POINT { x, y };
        let _ = windows::Win32::Graphics::Gdi::ScreenToClient(hwnd.h(), &mut p);
        let lp = ((p.y as u32 & 0xFFFF) << 16) | (p.x as u32 & 0xFFFF);
        let _ = windows::Win32::UI::WindowsAndMessaging::PostMessageW(Some(hwnd.h()), windows::Win32::UI::WindowsAndMessaging::WM_LBUTTONUP, WPARAM(0), LPARAM(lp as isize));
    }
}
