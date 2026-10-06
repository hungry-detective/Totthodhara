# Totthodhara — QML edition (GUI-first)

QML rebuild of the Totthodhara clipboard shelf
(https://github.com/hungry-detective/Totthodhara).
**Stage 1 (this folder): the complete GUI with mock data.**
Stage 2 (later): C++ backend — clipboard hook, SQLite history, global
hotkeys, AppBar docking — behind the same `ClipStore` contract, QML stays.

## Layout

```
Totthodhara/
├── CMakeLists.txt        Qt 6.8 / MinGW / Ninja build
├── main.cpp              Minimal entry point (no backend yet)
├── Main.qml              Floating shelf bar + tray icon
├── qml/
│   ├── AppState.qml      Settings + theme singleton (ViewModel)
│   ├── ClipStore.qml     Clip list, search, pin/snippet/delete (mock data)
│   ├── ClipCard.qml      One shelf card (+ right-click menu)
│   ├── SettingRow.qml    Settings card row
│   └── SettingsWindow.qml Appearance/Behavior/Storage/About
├── resources/app.png     Tray + About icon (from the WPF repo)
├── reference/            UI contract copied from the WPF repo
│   ├── MainWindow.xaml / Views/SettingsWindow.xaml
│   ├── Models/ClipboardItem.cs / ViewModels/*.cs
│   └── README.md, AGENTS.md, HOW_TO_BUILD.md
├── build*/               Local Debug/Release builds (ignored later)
└── deploy/               Self-contained Release folder (double-click exe)
```

## Build (needs the global Qt env + a restarted terminal for PATH)

```powershell
cd Totthodhara
cmake -S . -B build -G Ninja -DCMAKE_BUILD_TYPE=Debug "-DCMAKE_PREFIX_PATH=C:/Qt/6.8.3/mingw_64" "-DCMAKE_CXX_COMPILER=C:/Qt/Tools/mingw1310_64/bin/g++.exe"
cmake --build build
```

## Run

* Dev: `build\Totthodhara.exe` (needs Qt on PATH — restart terminal once
  after the environment setup so `C:\Qt\6.8.3\mingw_64\bin` resolves).
* Portable: `deploy\Totthodhara.exe` — runs anywhere, no Qt needed.

## In-app updates (portable only)

* About > Check for updates asks GitHub Releases for the newest tag and
  compares it numerically with the app version (`setApplicationVersion`
  in `main.cpp` — bump this before every release, e.g. `0.2.0`; About
  shows it live via `Qt.application.version`). Repeat checks within an
  hour are answered from `data/update_cache/last_check.json` (no GitHub
  rate-limit burn per launch).
* Repo target lives in `backend/UpdateService.h` (`kUpdateOwner` /
  `kUpdateRepo` — point them at the repo that publishes the releases).
* Publishing a release: run `package.cmd`, create tag `vX.Y.Z`, upload
  `Totthodhara-windows-portable.zip` (made by the script: stub exe +
  `library/`, never `data/`) as a release asset under that EXACT name —
  anything else in the release is ignored, so foreign assets can never
  cross-install into the app. Also upload the
  `Totthodhara-windows-portable.zip.sha256` the script writes beside it:
  the client SHA-256-verifies the zip before installing and refuses
  tampered downloads. Delete superseded old-edition releases so
  `releases/latest` always points at a Qt release.
* Install flow: download to `%TEMP%/Totthodhara-update`, stage an
  `apply.cmd` that waits out file locks + mutex, `Expand-Archive`s the
  zip, replaces everything except `data/` (history.db, clips/, favicons/,
  settings INI all survive), restarts the stub, deletes the stage.
* Guards: refuses to run outside the portable layout (dev trees are never
  wiped); needs HTTPS. No signature verification yet — integrity rests on
  HTTPS + GitHub + the SHA-256 check, releases come from your own repo.

## Defaults (first run looks like a real setup)

Fresh installs open with: System theme, Medium Bottom-Center bar, rounded
corners, near-solid transparent frost, tint pills + borderless thumbs,
always-on-top, auto-paste, hardware left / speed right / clock left in
clock-first order, world clock ON (Phoenix, second clock off), 50 history
items, 25 MB file cap, 2 h file cleaning. Anyone with an existing `data/`
keeps their own settings — stored values always win per-key, defaults only
fill gaps.

## Deploy (refresh after code changes — portable folder, no install needed)

```powershell
.\package.cmd
```

* What it does: kills the app, configures + builds Release, wipes
  `deploy/` **except `data/`**, then lays out the clean portable tree:
  `deploy/Totthodhara.exe` (launcher stub with the app icon — this is what
  friends double-click), `deploy/data/` (untouched: history + settings),
  `deploy/library/` (real exe + Qt DLLs + `Totthodhara/` QML module dir +
  everything `windeployqt --qmldir .` pulls in, incl. QtCore Settings).
* Why the stub: Windows only loads DLLs from beside the exe, so a bare
  exe + subfolder layout cannot start directly. The stub (pure Win32, no
  console, `launcher/stub.cpp`) sets `TOTTHODHARA_DATA_DIR=<root>\data`
  and launches `library\Totthodhara.exe`, forwarding args (`--settings`
  works). `AppPaths::dataDir()` honors that env var, else `<exe>/data`.
* The QML module dir MUST ship next to the real exe (`library/` here) —
  the engine cannot resolve `Main` from resources alone in this setup.
* `deploy/data/` auto-creates on first run (history.db, clips/, favicons/,
  `Totthodhara/Totthodhara.ini`) — never ship it, never delete it here;
  dev `build/` and portable `deploy/` do NOT share data.
* Reinstalling / re-extracting over the old folder keeps the old profile by
  design (history + settings survive updates). Factory reset: quit the app,
  delete `data/`, relaunch. Note: a legacy Roaming profile, if present, is
  imported ONCE into a fresh `data/` — clear that too for a truly empty
  start.
* Icon: `resources/app.ico` is linked into BOTH exes via standalone windres
  calls in CMakeLists (NOT the builtin rule — its injected `-I` flags break
  on paths with spaces). Two targets must never share one `.o` output.

## Shelf features wired (mock)

Click = copy/paste toast · Shift+click = multi-select · Paste All ·
right-click = Pin/Snippet/Delete · Ctrl+F search with live filter ·
gear menu = Clear history / Settings / Exit · tray icon + double-click ·
draggable frameless bar · Light/Dark + bar size + toggles in Settings.

Not yet real: clipboard capture, SQLite, hotkeys, taskbar AppBar
docking, live CPU/net stats. These are C++ Stage 2 work.
