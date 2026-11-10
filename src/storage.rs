//! `data\Storage\` file copies, captured bitmaps, downloads, auto-clean.
use std::path::{Path, PathBuf};

fn guid() -> String {
    uuid::Uuid::new_v4().to_string()
}

fn ext_with_dot(p: &Path) -> String {
    p.extension().map(|e| format!(".{}", e.to_string_lossy())).unwrap_or_default()
}

/// Copy a dropped/copied file into Storage as `{guid}{ext}`.
pub fn save_file(src: &str) -> Option<String> {
    let src = Path::new(src);
    if src.is_dir() {
        // Folders are referenced in place (WPF copy would fail on a dir too).
        return Some(src.to_string_lossy().into_owned());
    }
    let dst = crate::paths::storage_dir().join(format!("{}{}", guid(), ext_with_dot(src)));
    std::fs::copy(src, &dst).ok()?;
    Some(dst.to_string_lossy().into_owned())
}

/// Encode RGBA pixels as `{guid}.png`. Deletes the partial file on failure.
pub fn save_bitmap(img: &image::RgbaImage) -> Option<String> {
    let dst = crate::paths::storage_dir().join(format!("{}.png", guid()));
    match img.save_with_format(&dst, image::ImageFormat::Png) {
        Ok(_) => Some(dst.to_string_lossy().into_owned()),
        Err(e) => {
            crate::paths::log(format!("[Storage] SaveBitmap failed: {e}"));
            let _ = std::fs::remove_file(&dst);
            None
        }
    }
}

pub fn download_image(url: &str) -> Option<String> {
    let ext = url
        .split(['?', '#'])
        .next()
        .and_then(|u| Path::new(u).extension().map(|e| format!(".{}", e.to_string_lossy())))
        .unwrap_or_else(|| ".png".into());
    let mut resp = ureq::get(url).call().ok()?;
    let bytes = resp.body_mut().with_config().limit(100 * 1024 * 1024).read_to_vec().ok()?;
    let dst = crate::paths::storage_dir().join(format!("{}{}", guid(), ext));
    std::fs::write(&dst, bytes).ok()?;
    Some(dst.to_string_lossy().into_owned())
}

pub fn delete_file(path: &str) {
    if path.is_empty() {
        return;
    }
    let p = PathBuf::from(path);
    // Only delete our own copies, never an original referenced folder.
    if p.starts_with(crate::paths::storage_dir()) && p.is_file() {
        let _ = std::fs::remove_file(p);
    }
}

/// Remove Storage files older than `hours` (pinned paths preserved).
pub fn clean_old_files(hours: i32, keep: &[String]) {
    if hours <= 0 {
        return;
    }
    let cutoff = std::time::SystemTime::now() - std::time::Duration::from_secs(hours as u64 * 3600);
    let keep: Vec<String> = keep.iter().map(|k| k.to_lowercase()).collect();
    let Ok(rd) = std::fs::read_dir(crate::paths::storage_dir()) else { return };
    for e in rd.flatten() {
        let p = e.path();
        let Ok(meta) = e.metadata() else { continue };
        if !meta.is_file() {
            continue;
        }
        let created = meta.created().or_else(|_| meta.modified()).unwrap_or(std::time::SystemTime::now());
        if created < cutoff && !keep.contains(&p.to_string_lossy().to_lowercase()) {
            let _ = std::fs::remove_file(&p);
        }
    }
}

/// Copy an item file to `%TEMP%\TotthodharaDrag\{name}` (fresh dir each time),
/// returning the temp path — mirrors CopyAsFileDrop in the WPF view model.
pub fn temp_copy(file_path: &str, file_name: &str) -> Option<String> {
    let dir = crate::paths::temp_drag_dir();
    let _ = std::fs::remove_dir_all(&dir);
    std::fs::create_dir_all(&dir).ok()?;
    let src = Path::new(file_path);
    let mut name = if file_name.is_empty() || file_name == "Captured Image" {
        src.file_name()?.to_string_lossy().into_owned()
    } else {
        file_name.to_string()
    };
    let ext = ext_with_dot(src);
    if !ext.is_empty() && !name.to_lowercase().ends_with(&ext.to_lowercase()) {
        name.push_str(&ext);
    }
    let dst = dir.join(name);
    std::fs::copy(src, &dst).ok()?;
    Some(dst.to_string_lossy().into_owned())
}
