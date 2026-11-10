//! In-app updater: GitHub Releases check + staged portable install.
//!
//! Contract (mirrors the Qt UpdateService):
//! - Asset match is the EXACT portable zip name — never fuzzy, so foreign
//!   release assets can never cross-install over this build.
//! - The zip holds `Totthodhara/` (exe + docs). Install = replace every file
//!   in the app folder EXCEPT `data/` (history, settings, clips survive).
//! - SHA-256 verified when a `<zip>.sha256` asset exists.
//! - The swap runs from a temp script after this process exits; every
//!   failure path restores the backup and relaunches the old build.
use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use std::io::Read;
use std::path::{Path, PathBuf};

pub const OWNER: &str = "hungry-detective";
pub const REPO: &str = "Totthodhara";
pub const ASSET: &str = "Totthodhara-windows-portable.zip";
const UA: &str = "Totthodhara-Updater";

#[derive(Clone, Debug, Default, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase", default)]
pub struct UpdateInfo {
    pub is_update_available: bool,
    pub current_version: String,
    pub latest_version: String,
    pub download_url: String,
    pub sha256_url: String,
    pub size: u64,
    pub release_notes: String,
    pub published_at: String,
    pub error_message: String,
}

#[derive(Debug, Clone)]
pub enum UpdateMsg {
    Checked { info: UpdateInfo, manual: bool },
    Progress { got: u64, total: u64 },
    Installing,
    Failed(String),
}

pub fn current_version() -> String {
    env!("CARGO_PKG_VERSION").to_string()
}

fn parts(v: &str) -> [u64; 4] {
    let mut out = [0u64; 4];
    for (i, p) in v.trim_start_matches(['v', 'V']).split(['-', '+']).next().unwrap_or("").split('.').take(4).enumerate() {
        out[i] = p.trim().parse().unwrap_or(0);
    }
    out
}

pub fn is_newer(latest: &str, cur: &str) -> bool {
    parts(latest) > parts(cur)
}

fn agent(secs: u64) -> ureq::Agent {
    ureq::Agent::config_builder()
        .timeout_global(Some(std::time::Duration::from_secs(secs)))
        .user_agent(UA)
        .build()
        .into()
}

fn get_text(url: &str) -> Result<String, String> {
    agent(30)
        .get(url)
        .header("Accept", "application/vnd.github+json")
        .call()
        .map_err(|e| e.to_string())?
        .body_mut()
        .read_to_string()
        .map_err(|e| e.to_string())
}

fn cache_file() -> PathBuf {
    let d = crate::paths::data_dir().join("update_cache");
    let _ = std::fs::create_dir_all(&d);
    d.join("last_check.json")
}

pub fn check(use_cache: bool) -> UpdateInfo {
    let cur = current_version();
    let cache = cache_file();
    if use_cache {
        let fresh = std::fs::metadata(&cache).ok().and_then(|m| m.modified().ok()).and_then(|m| m.elapsed().ok()).map(|e| e.as_secs() < 3600).unwrap_or(false);
        if fresh {
            if let Some(i) = std::fs::read_to_string(&cache).ok().and_then(|t| serde_json::from_str::<UpdateInfo>(&t).ok()) {
                let mut i = i;
                i.current_version = cur.clone();
                i.is_update_available = !i.download_url.is_empty() && is_newer(&i.latest_version, &cur);
                return i;
            }
        }
    }
    let res = (|| -> Result<UpdateInfo, String> {
        let t = get_text(&format!("https://api.github.com/repos/{OWNER}/{REPO}/releases/latest"))?;
        let v: serde_json::Value = serde_json::from_str(&t).map_err(|e| e.to_string())?;
        let tag = v["tag_name"].as_str().unwrap_or("").to_string();
        let ver = tag.trim_start_matches(['v', 'V']).split(['-', '+']).next().unwrap_or("").to_string();
        let assets = v["assets"].as_array().cloned().unwrap_or_default();
        let find = |name: &str| assets.iter().find(|a| a["name"].as_str() == Some(name)).cloned();
        let zip = find(ASSET);
        let sha = find(&format!("{ASSET}.sha256"));
        let url = zip.as_ref().and_then(|a| a["browser_download_url"].as_str()).unwrap_or("").to_string();
        Ok(UpdateInfo {
            is_update_available: !url.is_empty() && is_newer(&ver, &cur),
            current_version: cur.clone(),
            latest_version: ver,
            download_url: url,
            sha256_url: sha.as_ref().and_then(|a| a["browser_download_url"].as_str()).unwrap_or("").to_string(),
            size: zip.as_ref().and_then(|a| a["size"].as_u64()).unwrap_or(0),
            release_notes: v["body"].as_str().unwrap_or("").to_string(),
            published_at: v["published_at"].as_str().unwrap_or("").to_string(),
            error_message: String::new(),
        })
    })();
    match res {
        Ok(i) => {
            let _ = std::fs::write(&cache, serde_json::to_string(&i).unwrap_or_default());
            i
        }
        Err(e) => UpdateInfo { current_version: cur, error_message: e, ..Default::default() },
    }
}

pub fn check_async(manual: bool) {
    std::thread::spawn(move || {
        let info = check(!manual);
        crate::events::send(crate::events::Event::Update(UpdateMsg::Checked { info, manual }));
    });
}

fn fail(msg: impl Into<String>) {
    let m = msg.into();
    crate::paths::log(format!("[Update] {m}"));
    crate::events::send(crate::events::Event::Update(UpdateMsg::Failed(m)));
}

/// Download → verify → unpack → hand the swap to a script that runs after
/// we exit. Emits Installing once the script is armed (caller then quits).
pub fn download_and_install(info: UpdateInfo) {
    std::thread::spawn(move || {
        let stage = std::env::temp_dir().join("TotthodharaUpdate").join(uuid::Uuid::new_v4().simple().to_string());
        if std::fs::create_dir_all(&stage).is_err() {
            return fail("Could not create the update folder");
        }
        let zip = stage.join(ASSET);
        let mut last = String::new();
        let mut ok = false;
        for attempt in 0..3u32 {
            if attempt > 0 {
                std::thread::sleep(std::time::Duration::from_secs(2u64 << attempt));
            }
            match download(&info.download_url, &zip, info.size) {
                Ok(()) => {
                    ok = true;
                    break;
                }
                Err(e) => last = e,
            }
        }
        if !ok {
            return fail(format!("Download failed: {last}"));
        }
        if !info.sha256_url.is_empty() {
            let want = match agent(30).get(&info.sha256_url).call().map_err(|e| e.to_string()).and_then(|mut r| r.body_mut().read_to_string().map_err(|e| e.to_string())) {
                Ok(t) => t.split_whitespace().next().unwrap_or("").to_lowercase(),
                Err(e) => return fail(format!("Could not fetch checksum: {e}")),
            };
            let got = match std::fs::read(&zip) {
                Ok(b) => hex(&Sha256::digest(&b)),
                Err(e) => return fail(e.to_string()),
            };
            if want.len() == 64 && want != got {
                return fail("Checksum mismatch — download discarded");
            }
        }
        let new_dir = stage.join("new");
        if let Err(e) = unzip(&zip, &new_dir) {
            return fail(format!("Could not unpack: {e}"));
        }
        // Source = folder that contains Totthodhara.exe (root or one level down).
        let src = if new_dir.join("Totthodhara.exe").exists() {
            new_dir.clone()
        } else {
            match std::fs::read_dir(&new_dir).ok().into_iter().flatten().flatten().map(|e| e.path()).find(|p| p.join("Totthodhara.exe").exists()) {
                Some(p) => p,
                None => return fail("Update package is missing Totthodhara.exe"),
            }
        };
        let exe = std::env::current_exe().unwrap_or_default();
        let root = exe.parent().map(Path::to_path_buf).unwrap_or_default();
        let script = stage.join("apply.ps1");
        let body = apply_script(std::process::id(), &src, &root, &exe);
        if std::fs::write(&script, body).is_err() {
            return fail("Could not write the update script");
        }
        use std::os::windows::process::CommandExt;
        let spawned = std::process::Command::new("powershell.exe")
            .args(["-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File"])
            .arg(&script)
            .creation_flags(0x0800_0000 | 0x0000_0008) // CREATE_NO_WINDOW | DETACHED_PROCESS
            .spawn();
        if let Err(e) = spawned {
            return fail(format!("Could not start the installer: {e}"));
        }
        crate::paths::log(format!("[Update] staged v{} from {}", info.latest_version, src.display()));
        crate::events::send(crate::events::Event::Update(UpdateMsg::Installing));
    });
}

fn hex(b: &[u8]) -> String {
    b.iter().map(|x| format!("{x:02x}")).collect()
}

fn ps_quote(p: &Path) -> String {
    format!("'{}'", p.display().to_string().replace('\'', "''"))
}

/// Wait for exit → back up everything except data/ → copy new files →
/// relaunch. Any failure restores the backup and relaunches the old build.
fn apply_script(pid: u32, src: &Path, root: &Path, exe: &Path) -> String {
    format!(
        r#"$ErrorActionPreference = 'Stop'
$src = {src}
$root = {root}
$exe = {exe}
$log = Join-Path $env:TEMP 'Totthodhara_update.log'
function Log($m) {{ Add-Content -Path $log -Value ("[{{0}}] {{1}}" -f (Get-Date -Format s), $m) }}
Log "update start: $src -> $root"
for ($i = 0; $i -lt 60; $i++) {{ if (-not (Get-Process -Id {pid} -ErrorAction SilentlyContinue)) {{ break }}; Start-Sleep -Milliseconds 500 }}
Start-Sleep -Milliseconds 800
$bak = Join-Path $root '.update_backup'
try {{
  if (Test-Path $bak) {{ Remove-Item $bak -Recurse -Force }}
  New-Item -ItemType Directory -Path $bak | Out-Null
  Get-ChildItem -LiteralPath $root -Force | Where-Object {{ $_.Name -ne 'data' -and $_.Name -ne '.update_backup' }} | ForEach-Object {{
    Move-Item -LiteralPath $_.FullName -Destination (Join-Path $bak $_.Name) -Force
  }}
  Get-ChildItem -LiteralPath $src -Force | Where-Object {{ $_.Name -ne 'data' }} | ForEach-Object {{
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $root $_.Name) -Recurse -Force
  }}
  if (-not (Test-Path $exe)) {{ throw 'new Totthodhara.exe missing after copy' }}
  Remove-Item $bak -Recurse -Force -ErrorAction SilentlyContinue
  Log 'update ok'
}} catch {{
  Log ("update failed: " + $_)
  Get-ChildItem -LiteralPath $bak -Force -ErrorAction SilentlyContinue | ForEach-Object {{
    $dst = Join-Path $root $_.Name
    if (Test-Path $dst) {{ Remove-Item $dst -Recurse -Force -ErrorAction SilentlyContinue }}
    Move-Item -LiteralPath $_.FullName -Destination $dst -Force
  }}
  Remove-Item $bak -Recurse -Force -ErrorAction SilentlyContinue
}}
Start-Process -FilePath $exe -WorkingDirectory $root
Start-Sleep -Seconds 2
Remove-Item -LiteralPath (Split-Path $src -Parent) -Recurse -Force -ErrorAction SilentlyContinue
"#,
        src = ps_quote(src),
        root = ps_quote(root),
        exe = ps_quote(exe),
        pid = pid
    )
}

fn download(url: &str, to: &Path, size_hint: u64) -> Result<(), String> {
    let mut resp = agent(600).get(url).call().map_err(|e| e.to_string())?;
    let total = resp.headers().get("content-length").and_then(|v| v.to_str().ok()).and_then(|s| s.parse().ok()).unwrap_or(size_hint);
    let mut reader = resp.body_mut().with_config().limit(1024 * 1024 * 1024).reader();
    let mut f = std::fs::File::create(to).map_err(|e| e.to_string())?;
    let mut buf = vec![0u8; 64 * 1024];
    let mut got = 0u64;
    let mut last = std::time::Instant::now();
    loop {
        let n = reader.read(&mut buf).map_err(|e| e.to_string())?;
        if n == 0 {
            break;
        }
        std::io::Write::write_all(&mut f, &buf[..n]).map_err(|e| e.to_string())?;
        got += n as u64;
        if last.elapsed().as_millis() > 100 {
            last = std::time::Instant::now();
            crate::events::send(crate::events::Event::Update(UpdateMsg::Progress { got, total }));
        }
    }
    if total > 0 && got < total {
        return Err(format!("incomplete download ({got} of {total} bytes)"));
    }
    crate::events::send(crate::events::Event::Update(UpdateMsg::Progress { got, total: total.max(got) }));
    Ok(())
}

fn unzip(zip: &Path, out: &Path) -> Result<(), String> {
    let f = std::fs::File::open(zip).map_err(|e| e.to_string())?;
    let mut a = zip::ZipArchive::new(f).map_err(|e| e.to_string())?;
    a.extract(out).map_err(|e| e.to_string())
}

#[cfg(test)]
mod tests {
    use super::*;

    /// End-to-end swap: old files replaced, data/ kept, stale files gone,
    /// "relaunch" fires (stand-in exe writes a marker).
    #[test]
    fn apply_script_swaps_folder_and_keeps_data() {
        let base = std::env::temp_dir().join(format!("tott_upd_test_{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&base);
        let root = base.join("install");
        let src = base.join("stage").join("new").join("Totthodhara");
        std::fs::create_dir_all(root.join("data")).unwrap();
        std::fs::create_dir_all(&src).unwrap();
        std::fs::write(root.join("data").join("metadata.db"), b"USER DATA").unwrap();
        std::fs::write(root.join("old_only.txt"), b"stale").unwrap();
        std::fs::write(root.join("README.txt"), b"old readme").unwrap();
        // Stand-in exe: a .cmd renamed would not run, so use where.exe (quiet).
        let stand_in = std::path::PathBuf::from(std::env::var("WINDIR").unwrap()).join("System32").join("where.exe");
        std::fs::copy(&stand_in, root.join("Totthodhara.exe")).unwrap();
        std::fs::copy(&stand_in, src.join("Totthodhara.exe")).unwrap();
        std::fs::write(src.join("README.txt"), b"new readme").unwrap();
        let script = base.join("stage").join("apply.ps1");
        std::fs::write(&script, apply_script(999_999, &src, &root, &root.join("Totthodhara.exe"))).unwrap();
        let st = std::process::Command::new("powershell.exe").args(["-NoProfile", "-ExecutionPolicy", "Bypass", "-File"]).arg(&script).status().unwrap();
        assert!(st.success());
        assert_eq!(std::fs::read(root.join("data").join("metadata.db")).unwrap(), b"USER DATA");
        assert_eq!(std::fs::read(root.join("README.txt")).unwrap(), b"new readme");
        assert!(!root.join("old_only.txt").exists(), "stale file should be gone");
        assert!(root.join("Totthodhara.exe").exists());
        assert!(!root.join(".update_backup").exists());
        let _ = std::fs::remove_dir_all(&base);
    }

    #[test]
    fn version_compare() {
        assert!(is_newer("2.0.1", "2.0.0"));
        assert!(is_newer("v2.1", "2.0.9"));
        assert!(!is_newer("1.4.11", "2.0.0"));
        assert!(!is_newer("2.0.0", "2.0.0"));
    }
}
