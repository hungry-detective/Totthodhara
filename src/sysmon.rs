//! CPU / RAM / network sampler (2s), gated by which pills are visible.
use std::sync::atomic::{AtomicBool, Ordering::SeqCst};
use std::time::{Duration, Instant};
use windows::Win32::NetworkManagement::IpHelper::*;

pub static CPU_ENABLED: AtomicBool = AtomicBool::new(true);
pub static NET_ENABLED: AtomicBool = AtomicBool::new(true);

#[derive(Debug, Clone, Copy, Default)]
pub struct Sample {
    pub cpu: i32,
    pub mem: i32,
    pub down_kbs: f64,
    pub up_kbs: f64,
}

const EXCLUDE: &[&str] = &[
    "filter driver", "miniport", "lightweight filter", "wfp", "qos packet scheduler",
    "virtual wifi", "wi-fi direct", "tunnel",
];

/// Physical NICs only (Ethernet=6, Wi-Fi=71, Up, not a filter/tunnel) —
/// summing a VPN tunnel *and* its NIC double-counts (see AGENTS.md).
fn net_totals() -> Option<(u64, u64)> {
    unsafe {
        let mut table: *mut MIB_IF_TABLE2 = std::ptr::null_mut();
        if GetIfTable2(&mut table).is_err() || table.is_null() {
            return None;
        }
        let t = &*table;
        let rows = std::slice::from_raw_parts(t.Table.as_ptr(), t.NumEntries as usize);
        let (mut rx, mut tx) = (0u64, 0u64);
        for r in rows {
            if r.Type != 6 && r.Type != 71 {
                continue;
            }
            if r.OperStatus.0 != 1 {
                continue;
            }
            // InterfaceAndOperStatusFlags bit 1 = FilterInterface
            if r.InterfaceAndOperStatusFlags._bitfield & 0b10 != 0 {
                continue;
            }
            let name = format!(
                "{} {}",
                crate::winsys::from_wide(&r.Description),
                crate::winsys::from_wide(&r.Alias)
            )
            .to_lowercase();
            if EXCLUDE.iter().any(|k| name.contains(k)) {
                continue;
            }
            rx += r.InOctets;
            tx += r.OutOctets;
        }
        FreeMibTable(table as *const _);
        Some((rx, tx))
    }
}

pub fn start() {
    std::thread::Builder::new()
        .name("sysmon".into())
        .spawn(|| {
            let mut last_cpu = crate::winsys::system_times();
            let mut last_net = net_totals().unwrap_or((0, 0));
            let mut last_t = Instant::now();
            let mut s = Sample::default();
            loop {
                std::thread::sleep(Duration::from_secs(1));
                let cpu_on = CPU_ENABLED.load(SeqCst);
                let net_on = NET_ENABLED.load(SeqCst);
                if !cpu_on && !net_on {
                    continue;
                }
                if cpu_on {
                    let now = crate::winsys::system_times();
                    let idle = now.0.saturating_sub(last_cpu.0) as f64;
                    let total = (now.1.saturating_sub(last_cpu.1) + now.2.saturating_sub(last_cpu.2)) as f64;
                    s.cpu = if total > 0.0 { ((1.0 - idle / total) * 100.0).clamp(0.0, 100.0) as i32 } else { 0 };
                    last_cpu = now;
                    s.mem = crate::winsys::memory_load().0 as i32;
                }
                if net_on {
                    let el = last_t.elapsed().as_secs_f64();
                    if el >= 0.5 {
                        last_t = Instant::now();
                        if let Some((rx, tx)) = net_totals() {
                            let r = |a: u64, b: u64| ((a.saturating_sub(b)) as f64 / 1024.0 / el * 10.0).round() / 10.0;
                            s.down_kbs = r(rx, last_net.0);
                            s.up_kbs = r(tx, last_net.1);
                            last_net = (rx, tx);
                        } else {
                            s.down_kbs = 0.0;
                            s.up_kbs = 0.0;
                        }
                    }
                }
                crate::events::send(crate::events::Event::SysMon(s));
            }
        })
        .ok();
}

/// Qt formatPair: one shared unit for up+down (neighbors never shift).
pub fn format_pair(up_kb: f64, down_kb: f64) -> (String, String) {
    if up_kb.max(down_kb) >= 1024.0 {
        (format!("{:.1} MB/s", up_kb / 1024.0), format!("{:.1} MB/s", down_kb / 1024.0))
    } else {
        (format!("{:.1} KB/s", up_kb), format!("{:.1} KB/s", down_kb))
    }
}
