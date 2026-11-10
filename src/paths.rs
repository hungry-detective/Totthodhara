//! Data layout identical to the WPF build: `{exe dir}\data\...`.
use std::io::Write;
use std::path::PathBuf;
use std::sync::{Mutex, OnceLock};

pub fn base_dir() -> PathBuf {
    std::env::current_exe()
        .ok()
        .and_then(|p| p.parent().map(|p| p.to_path_buf()))
        .unwrap_or_else(|| PathBuf::from("."))
}

pub fn data_dir() -> PathBuf {
    let d = base_dir().join("data");
    let _ = std::fs::create_dir_all(&d);
    d
}

pub fn storage_dir() -> PathBuf {
    let d = data_dir().join("Storage");
    let _ = std::fs::create_dir_all(&d);
    d
}

pub fn favicon_dir() -> PathBuf {
    let d = data_dir().join("favicons");
    let _ = std::fs::create_dir_all(&d);
    d
}

pub fn temp_drag_dir() -> PathBuf {
    std::env::temp_dir().join("TotthodharaDrag")
}

static LOG: OnceLock<Mutex<Option<std::fs::File>>> = OnceLock::new();

/// Append-only debug log at `data\app_debug.txt` (same file as the WPF build).
pub fn log(msg: impl AsRef<str>) {
    let lock = LOG.get_or_init(|| {
        let path = data_dir().join("app_debug.txt");
        // Keep the log from growing unbounded.
        if let Ok(meta) = std::fs::metadata(&path) {
            if meta.len() > 2 * 1024 * 1024 {
                let _ = std::fs::remove_file(&path);
            }
        }
        Mutex::new(
            std::fs::OpenOptions::new()
                .create(true)
                .append(true)
                .open(path)
                .ok(),
        )
    });
    if let Ok(mut g) = lock.lock() {
        if let Some(f) = g.as_mut() {
            let t = crate::winsys::local_time_string();
            let _ = writeln!(f, "[{t}] {}", msg.as_ref());
        }
    }
}
