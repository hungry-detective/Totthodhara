//! Totthodhara — floating clipboard shelf. Rust + egui port of the WPF app.
#![windows_subsystem = "windows"]

mod app;
mod clipboard;
mod clock;
mod db;
mod tools;
mod events;
mod fade;
mod icons;
mod msgwin;
mod paths;
mod popup;
mod settings;
mod settings_ui;
mod storage;
mod sysmon;
mod theme;
mod update;
mod winsys;
mod worker;

fn main() {
    // Single instance: kill older Totthodhara/ClipDropPro processes, then mutex.
    // TOTTHODHARA_DEV=1: side-by-side testing (no kill, no mutex).
    let dev = std::env::var("TOTTHODHARA_DEV").is_ok();
    if !dev {
        winsys::kill_old_instances();
    }
    if !dev && !winsys::acquire_single_instance() {
        winsys::message_box("Totthodhara", "Totthodhara is already running.");
        return;
    }
    paths::log("──────── Totthodhara (Rust) starting ────────");

    let db = match db::Db::open() {
        Ok(d) => d,
        Err(e) => {
            winsys::message_box("Totthodhara", &format!("Could not open the database:\n{e}"));
            return;
        }
    };
    db.delete_welcome();
    let settings = settings::Settings::load();
    if settings.start_with_windows != winsys::is_startup_enabled() {
        winsys::set_startup(settings.start_with_windows);
    }
    let core = worker::Core::new(db, settings.clone());

    msgwin::start(&settings.hotkey_string, settings.hotkey_modifiers);
    sysmon::start();
    worker::startup_cleanup(&core);

    let icon = icons::app_icon_rgba();
    let viewport = egui::ViewportBuilder::default()
        .with_title("Totthodhara")
        .with_inner_size([800.0, 26.0])
        .with_decorations(false)
        .with_transparent(true)
        .with_taskbar(false)
        .with_resizable(false)
        .with_active(false)
        .with_always_on_top()
        .with_drag_and_drop(true)
        .with_icon(egui::IconData { rgba: icon.as_raw().clone(), width: icon.width(), height: icon.height() });
    let options = eframe::NativeOptions {
        viewport,
        centered: false,
        persist_window: false,
        ..Default::default()
    };
    let r = eframe::run_native(
        "Totthodhara",
        options,
        Box::new(move |cc| Ok(Box::new(app::App::new(cc, core)))),
    );
    if let Err(e) = r {
        paths::log(format!("eframe error: {e}"));
        winsys::message_box("Totthodhara", &format!("Failed to start the UI:\n{e}"));
    }
}
