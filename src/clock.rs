//! World clocks (Qt SysMon.zoneTime / zoneTag / timeZoneList), on Windows zones.
use crate::winsys::{self, Zone};

pub struct Clocks {
    zones: Vec<Zone>,
    pub a_tag: String,
    pub a_text: String,
    pub b_tag: String,
    pub b_text: String,
}

/// Windows zone id → Qt tag (same table as SystemMonitorService::zoneTag).
fn tag_for(id: &str, display: &str) -> String {
    let t = match id {
        "" | "Local" => "LOC",
        "UTC" | "Coordinated Universal Time" => "UTC",
        "Pacific Standard Time" => "LA",
        "US Mountain Standard Time" => "PHX",
        "Mountain Standard Time" => "DEN",
        "Central Standard Time" => "CHI",
        "Eastern Standard Time" => "NYC",
        "E. South America Standard Time" => "SAO",
        "Alaskan Standard Time" => "ANC",
        "Central Standard Time (Mexico)" => "MEX",
        "Hawaiian Standard Time" => "HNL",
        "GMT Standard Time" => "LON",
        "Romance Standard Time" => "PAR",
        "Russian Standard Time" => "MSK",
        "Egypt Standard Time" => "CAI",
        "Arabian Standard Time" => "DXB",
        "Pakistan Standard Time" => "KHI",
        "Bangladesh Standard Time" => "DHK",
        "SE Asia Standard Time" => "BKK",
        "Singapore Standard Time" => "SIN",
        "Tokyo Standard Time" => "TYO",
        "AUS Eastern Standard Time" => "SYD",
        "New Zealand Standard Time" => "AKL",
        _ => {
            // "(UTC+06:00) Dhaka" → city's first 3 letters.
            let city = display.split(')').nth(1).unwrap_or(id).trim();
            let city = city.split(',').next().unwrap_or(city);
            return city.chars().filter(|c| c.is_alphabetic()).take(3).collect::<String>().to_uppercase();
        }
    };
    t.to_string()
}

fn h_mm_ap(h: u16, m: u16) -> String {
    let ap = if h < 12 { "AM" } else { "PM" };
    let h12 = match h % 12 {
        0 => 12,
        x => x,
    };
    format!("{h12}:{m:02} {ap}")
}

impl Clocks {
    pub fn new() -> Self {
        let mut zones = winsys::system_zones();
        zones.sort_by(|a, b| b.bias().cmp(&a.bias()).then(a.display.cmp(&b.display)));
        Self { zones, a_tag: String::new(), a_text: String::new(), b_tag: String::new(), b_text: String::new() }
    }

    fn find(&self, id: &str) -> Zone {
        if id.is_empty() || id == "Local" {
            return winsys::local_zone();
        }
        self.zones.iter().find(|z| z.id == id).cloned().unwrap_or_else(|| {
            self.zones.iter().find(|z| z.id == "UTC").cloned().unwrap_or_else(winsys::local_zone)
        })
    }

    fn face(&self, id: &str) -> (String, String) {
        let z = self.find(id);
        let t = winsys::now_in_zone(&z);
        (tag_for(id, &z.display), h_mm_ap(t.hour, t.minute))
    }

    pub fn update(&mut self, a: &str, b: &str) {
        (self.a_tag, self.a_text) = self.face(a);
        (self.b_tag, self.b_text) = self.face(b);
    }

    /// Settings dropdown: Local, UTC, then every zone by UTC offset.
    pub fn options(&self) -> Vec<(String, String)> {
        let mut v = vec![("Local".to_string(), String::new()), ("(UTC) UTC".to_string(), "UTC".to_string())];
        for z in &self.zones {
            if z.id != "UTC" {
                v.push((z.display.clone(), z.id.clone()));
            }
        }
        v
    }
}
