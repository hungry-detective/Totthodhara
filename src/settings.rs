//! `data\settings.json` — same keys/defaults as the WPF SettingsService.
use serde::{Deserialize, Serialize};

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase", default)]
pub struct Settings {
    pub always_on_top: bool,
    pub auto_clean_hours: i32,
    pub hotkey_string: String,
    pub hotkey_modifiers: i32,
    pub theme: String,
    pub bar_size: String,
    pub alignment: String,
    pub shelf_position: String,
    pub start_with_windows: bool,
    pub copy_items_to_destination: bool,
    pub tray_icon_action: String,
    #[serde(rename = "MaxFileSizeMB")]
    pub max_file_size_mb: i32,
    pub max_history_items: i32,
    pub custom_card_color: String,
    pub custom_control_bg_color: String,
    pub custom_accent_color: String,
    pub custom_window_bg_color: String,
    pub custom_border_color: String,
    pub custom_text_color: String,
    pub custom_icon_color: String,
    pub show_system_monitor: bool,
    pub show_network_monitor: bool,
    pub show_cpu_ram_monitor: bool,
    pub show_world_clock: bool,
    pub world_clock_time_zone: String,
    pub pinned_world_clock_zones: Vec<String>,
    pub monitors_on_left: bool,
    pub hardware_on_left: bool,
    pub compact_clock: bool,
    pub auto_check_updates: bool,
    pub silent_auto_update: bool,
    pub hide_clipboard: bool,
    #[serde(rename = "IncludeDIBInDrag")]
    pub include_dib_in_drag: bool,
    pub transparency_effect: bool,
    pub follow_system_transparency: bool,
    pub rounded_corners: bool,
    // ── Qt-edition settings (AppState.qml) ──
    pub transparent_bar: bool,
    pub glass_alpha: f32,
    pub shelf_tone: String,
    pub tone_intensity: f32,
    pub custom_tone: String,
    pub pill_style: String,
    pub thumb_style: String,
    pub pill_meters: bool,
    pub show_dev_tools: bool,
    pub zone_a: String,
    pub zone_b: String,
    pub show_clock1: bool,
    pub show_clock2: bool,
    pub clock_on_left: bool,
    pub order_keys: String,
}

impl Default for Settings {
    fn default() -> Self {
        Self {
            always_on_top: true,
            auto_clean_hours: 2,
            hotkey_string: "OemTilde".into(),
            hotkey_modifiers: 2,
            theme: "System".into(),
            bar_size: "Medium".into(),
            alignment: "Center".into(),
            shelf_position: "Bottom".into(),
            start_with_windows: false,
            copy_items_to_destination: true,
            tray_icon_action: "Show/Hide Shelf".into(),
            max_file_size_mb: 25,
            max_history_items: 50,
            custom_card_color: "#FFFFFF".into(),
            custom_control_bg_color: "#E5E5E5".into(),
            custom_accent_color: "#0078D4".into(),
            custom_window_bg_color: "#F3F3F3".into(),
            custom_border_color: "#CCCCCC".into(),
            custom_text_color: "#000000".into(),
            custom_icon_color: "#323232".into(),
            show_system_monitor: true,
            show_network_monitor: true,
            show_cpu_ram_monitor: true,
            show_world_clock: true,
            world_clock_time_zone: "US Mountain Standard Time".into(),
            pinned_world_clock_zones: vec![
                "Bangladesh Standard Time".into(),
                "US Mountain Standard Time".into(),
            ],
            monitors_on_left: false,
            hardware_on_left: true,
            compact_clock: false,
            auto_check_updates: true,
            silent_auto_update: false,
            hide_clipboard: false,
            include_dib_in_drag: false,
            transparency_effect: false,
            follow_system_transparency: true,
            rounded_corners: true,
            transparent_bar: true,
            glass_alpha: 0.80,
            shelf_tone: "auto".into(),
            tone_intensity: 0.50,
            custom_tone: "#9d4fd0".into(),
            pill_style: "tint".into(),
            thumb_style: "none".into(),
            pill_meters: false,
            show_dev_tools: false,
            zone_a: "US Mountain Standard Time".into(),
            zone_b: "UTC".into(),
            show_clock1: true,
            show_clock2: false,
            clock_on_left: true,
            order_keys: "clock,hw,net".into(),
        }
    }
}

fn path() -> std::path::PathBuf {
    crate::paths::data_dir().join("settings.json")
}

impl Settings {
    pub fn load() -> Self {
        let mut s: Settings = std::fs::read_to_string(path())
            .ok()
            .and_then(|t| serde_json::from_str(t.trim_start_matches('\u{feff}')).ok())
            .unwrap_or_default();
        if s.theme == "Transparent" {
            s.theme = "Dark".into();
        }
        // Core behaviors are forced on (Qt restorePrefs).
        s.always_on_top = true;
        s.copy_items_to_destination = true;
        if s.bar_size == "Default" {
            s.bar_size = "Small".into();
        }
        if s.pinned_world_clock_zones.is_empty() {
            s.pinned_world_clock_zones = if !s.world_clock_time_zone.is_empty() {
                vec![s.world_clock_time_zone.clone()]
            } else {
                vec!["Bangladesh Standard Time".into(), "US Mountain Standard Time".into()]
            };
        }
        s
    }

    pub fn save(&self) {
        if let Ok(j) = serde_json::to_string(self) {
            let _ = std::fs::write(path(), j);
        }
    }

    /// Item cap used when loading (WPF: max(MaxHistoryItems, 10)).
    pub fn display_limit(&self) -> usize {
        self.max_history_items.max(1) as usize
    }
}
