<p align="center"><img src="assets/app.png" width="96" alt="Totthodhara"></p>

<h1 align="center">Totthodhara</h1>
<p align="center"><b>Never lose a copy again.</b> A floating clipboard shelf for Windows that sits just above the taskbar.</p>

---

Every text, link, color, image and file you copy lands on the shelf as a
pill-shaped card. Click a card to paste it into whatever app you're using.

## Download

Get **`Totthodhara-windows-portable.zip`** from the
[latest release](https://github.com/hungry-detective/Totthodhara/releases/latest),
extract it anywhere, and run `Totthodhara.exe`. No installer is needed.

- **Portable:** history, settings and copied files live in the `data` folder
  next to the exe.
- **Auto-update:** the app checks on start, or go to Settings → About → Check
  for updates. The updater downloads the new zip, verifies its SHA-256, swaps
  the program files and restarts. Your `data` folder is never touched.

## Features

- **Clipboard history:** text, links (with site icons), colors (with
  swatches), images (with thumbnails) and files.
- **Click to paste:** Shift+click selects cards for **Paste All**. Ctrl+click
  deletes a card.
- **Hover previews:** full images, full text, and file details.
- **Right-click a card:** Pin, Save as snippet, Transform (UPPER, lower, Title,
  trim, sort, unique, join), Copy as HEX/RGB/HSL, Copy page title, Copy email
  or number, and Delete.
- **Search:** press Ctrl+F.
- **Meters:** CPU, RAM, network speed and two world clocks. Drag them to
  rearrange, or right-click to move them to the other side.
- **Looks:** System, Dark or Light theme, with glass and Windows accent
  following. Choose from 6 card styles, 5 thumbnail frames and shelf tones.
- **Fits around your work:** docks at the bottom or top of the screen and
  hides automatically when an app goes fullscreen.

## Build from source

Requires Rust (stable) on Windows.

```powershell
cargo build --release          # target\release\Totthodhara.exe
.\package.ps1                  # dist\Totthodhara-windows-portable.zip + .sha256
.\package.ps1 -Publish -Notes "What changed"   # tag v<Cargo version> + GitHub release
```

To ship an update, bump `version` in `Cargo.toml`, then run
`.\package.ps1 -Publish`. Installed copies pick it up automatically. The updater
only accepts the exact asset name `Totthodhara-windows-portable.zip`.

Set `TOTTHODHARA_DEV=1` to run a dev build beside an installed copy. In that
mode it skips the single-instance kill and mutex.

## Tech

The app is Rust with [egui](https://github.com/emilk/egui) and eframe (glow),
plus direct Win32 calls through the `windows` crate. Those calls handle the
AppBar dock, DWM glass, the clipboard listener, the tray icon, the global
hotkey (Ctrl+`) and SendInput paste. History is stored in SQLite (rusqlite,
bundled).
