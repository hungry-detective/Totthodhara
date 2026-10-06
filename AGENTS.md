# Totthodhara (QML edition) — AI Context Preservation File

**DO NOT remove, modify, or "improve" working code unless the user explicitly asks.**
**DO NOT refactor. DO NOT add features unless asked. Preserve exact drag,
click, animation, and glass behavior described below.**
**When the user judges visuals from screenshots: verify with a real
screenshot yourself (PowerShell `CopyFromScreen` + read the PNG) instead of
guessing pixels. Evidence before synthesis, every time.**

The `reference/` folder is the OLD WPF app — a UI contract reference only.
Never port WPF behavior blindly; the QML app below is the source of truth.

---

## 1. Project overview

- Qt 6.8 QML clipboard shelf (`Totthodhara/`). Floating bar + tray icon.
- `main.cpp` — single instance (`TotthodharaShelfV1`, exits if taken),
  registers `Totthodhara.Backend` types + `SysMon` / `ThemeWatcher` singletons,
  loads module `Totthodhara`, `Main`.
- `Main.qml` — shelf window (frameless Tool, topmost, exact-fit bar).
- `qml/` — `AppState` (settings/theme singleton), `ClipStore` (view-model),
  `ClipCard`, `ClipMenu`, `PreviewPopup`, `SettingsWindow`, `SettingRow`,
  `ShelfButton`, `CaptionButton`, `MeterIcon`, `PopupItem`, `ThemedSegment`,
  `ThemedSwitch`, `ThemedSpin`, `ThemedCombo` (clock zone dropdowns).
  Settings pages: Appearance, Behavior, Items on Bar, Clipboard Style,
  About (meters/clipboard/clocks live on Items on Bar — Storage merged
  into Behavior, so each control exists exactly once).
- `backend/` — `AppBarService` (dock/reserve space), `FullscreenService`
  (auto-hide), `ClipboardService` (monitor, copy-back, favicons, drag-out),
  `StorageService` (SQLite history), `SystemMonitorService` (CPU/RAM/net,
  world-clock zone texts), `ThemeService` (Windows theme watch + glass),
  `UpdateService` (GitHub Releases check + staged portable install).
- Data (portable): `<exe>/data/` — `history.db`, `clips/`, `favicons/`,
  `Totthodhara/Totthodhara.ini` (all prefs; INI forced in main.cpp via
  setDefaultFormat+setPath — never the registry, so the folder stays portable).
- Logs (only if `C:/Temp` exists): `tott_startup.log`, `clip.log`.
- Factory defaults mirror a real user setup (System/Medium/Bottom/Center,
  rounded, transparent bar 0.85, tint pills, borderless thumbs, always-on-top,
  auto-paste, hw left + net right + clock left in "clock,hw,net" order,
  world clock ON with Phoenix + Local Clock-two off, 50 items / 25 MB /
  2 h cleaning). Defaults live TWICE and must match: `AppState.qml` (first
  paint) + the `prefs` block in `Main.qml` (what restore copies). Stored
  INI values ALWAYS win per-key — a returning user's `data/` is never
  overridden by new defaults. When adding a setting: default it in both
  places, add prefs/restore/save-back lines, keep the INI key name stable
  forever (renames orphan user data).
- `alwaysOnTop` + `copyToDestination` are CORE: forced true in restorePrefs,
  no Settings rows (users broke the app with them - never re-add toggles).
  Behavior page holds update-check-on-start (silent toast only when an
  update exists, via `autoUpdChecking`), Run-at-startup (Run key, stub-aware
  path), and the storage rows.
- GUI principles (do not drift): one shared card language everywhere
  (`cardBg`/`cardBorder`/`cardRadius` = cardHeight/2, 1px edges, accent
  reserved for selection/focus/tint); clips primary (pure-white titles),
  meters ambient; every control exists exactly once across Settings pages;
  theme System follows Windows live (mode, transparency switch, taskbar
  accent switch, accent color); explicit Dark/Light are solid signature
  looks; popups are opaque cards (no unblurred translucency); no shadow
  under text that carries meaning; verify taste changes with a real
  screenshot at 1:1 before/after.

## 2. Build & run (exact, verified)

```powershell
cd Totthodhara
$env:PATH = "C:\Program Files\CMake\bin;<winget-Ninja-dir>;C:\Qt\Tools\mingw1310_64\bin;C:\Qt\6.8.3\mingw_64\bin;" + $env:PATH
# Ninja lives at: $env:LOCALAPPDATA\Microsoft\WinGet\Packages\Ninja-build.Ninja_Microsoft.Winget.Source_8wekyb3d8bbwe
cmake -S . -B build -G Ninja -DCMAKE_BUILD_TYPE=Debug "-DCMAKE_PREFIX_PATH=C:/Qt/6.8.3/mingw_64" "-DCMAKE_CXX_COMPILER=C:/Qt/Tools/mingw1310_64/bin/g++.exe"  # re-run after ANY CMakeLists.txt change
cmake --build build
```

- Run from source (NOT `deploy/`): kill the old process FIRST (exe is file-
  locked while running, and the mutex blocks a second instance), then
  `Start-Process .\build\Totthodhara.exe` with working dir `Totthodhara/`.
- `Totthodhara.exe --settings` opens Settings on start (also used to
  screenshot-verify settings: foreground the `Totthodhara Settings` HWND,
  `CopyFromScreen`, read the PNG).
- QML-only edits rebuild in seconds (rcc + link). New/removed QML files MUST
  be added/removed in `CMakeLists.txt` `qt_add_qml_module(QML_FILES ...)`.
- No `Qt5Compat.GraphicalEffects` on this machine — never import it. No
  `DropShadowEffect` anywhere (breaks padding/render bounds).
- `main.cpp` pins `QQuickStyle::setStyle("Basic")` BEFORE the engine loads:
  the native Windows style silently ignores custom
  background/contentItem/indicator (verified: portable ran native, all
  themed controls fell back to white). Never remove this line.
- App icon: `resources/app.ico` (multi-size from `app.png` via Pillow) +
  `resources/app.rc`, linked through a STANDALONE windres custom command in
  CMakeLists — never put the `.rc` in `qt_add_executable` sources (the
  builtin rule's `-I` flags break windres on space-containing paths).
- Portable (`deploy/`, ~100MB, Release): run `package.cmd` (does
  everything: kill-gate, fresh Release configure+build, wipe `deploy/`
  except `data/`, lay out stub+library, `windeployqt --qmldir .`). Layout
  is `deploy/Totthodhara.exe` (Win32 launcher stub, `launcher/stub.cpp`,
  sets `TOTTHODHARA_DATA_DIR` + starts `library/`) + `deploy/data/` (never
  ship, never delete) + `deploy/library/` (real exe + Qt + `Totthodhara`
  module dir next to it — engine can't resolve `Main` from resources alone,
  verified failure). Windows loads DLLs only from beside the exe, so the
  stub split is structural, not cosmetic - never flatten Qt DLLs to root.
  In-app updater (`UpdateService::stageAndLaunch`): copies
  `library/TotthodharaUpdater.exe` to the temp stage and launches it with
  `--root/--zip/--version` — the updater window (updater/updater.cpp, Qt
  Widgets, runs from stage/ never from the tree it wipes) narrates
  wait/unpack/verify/wipe/copy with a real progress bar, then relaunches
  the stub and cleans itself up. 0.2.0 installs predate the updater exe,
  so they fall back to the generated `apply.cmd` script path (same guards).
  Either way: assert staged `new\library\Totthodhara.exe` + root stub exist
  BEFORE wiping, check `xcopy`, relaunch the install on ANY failure path
  (the app already quit). Asset match is the exact portable zip name -
  never fuzzy, or foreign release assets cross-install and brick portable
  installs. `package.cmd` ships the updater exe into `deploy/library/`.
- `--settings` startup flag: evaluated in C++ into the `openSettingsOnStart`
  context property (the QML-side `Qt.application.arguments` check proved
  unreliable), and the actual open is DEFERRED via a 600ms `settingsOpener`
  Timer — `show()` issued inside `onCompleted` silently no-ops (verified:
  no HWND, no error). Never open windows from `onCompleted` directly.
- Killing: `Stop-Process` is racy (lock/mutex survivors observed repeatedly).
  Kill-gate EVERY rebuild/relaunch: loop kill until `Get-Process` is empty
  AND the exe file copies cleanly (copy success = no lock = truly dead).
  A stale survivor silently eats launches (mutex) and fakes test results.

## 3. Theme system (AppState + ThemeService)

- `AppState.theme`: `System | Dark | Light` (default `System`).
  `effectiveTheme` = System ? (ThemeWatcher.systemIsDark ? Dark : Light).
- `ThemeService` watches `SystemUsesLightTheme` first (what the taskbar
  follows; `AppsUseLightTheme` fallback), `EnableTransparency`, and DWM
  colorization. Exposes the Windows accent as `systemAccent`
  (`systemAccentChanged`): `AppState.accent` wears it live in System mode
  ONLY while the taskbar accent switch is on, signature blue otherwise.
  Windows key map (all under HKCU, verified live; 1.5s poll backs the
  window messages): mode = Personalize\SystemUsesLightTheme (taskbar
  follows it, AppsUseLightTheme fallback), transparency effects =
  Personalize\EnableTransparency, accent-on-taskbar =
  Personalize\ColorPrevalence (default off), accent RGB = DWM
  ColorizationColor masked to 0xRRGGBB (construct opaque explicitly —
  fromRgb(uint) reads the zeroed high byte as alpha 0). Signals:
  `systemThemeChanged`,
  `systemTintChanged`, `transparencyChanged`, `colorPrevalenceChanged`
  → QML `refreshGlass()`.
- Glass (`applyGlass(window, enable, dark, radius)`): DWM blur-behind
  clipped to a rounded-rect region matching the QML pill (device pixels,
  refreshed on show/size/theme changes) + QML tint on top. Full-window
  effects are banned here on purpose: DWMWA acrylic reports success yet
  stays NONE on layered windows (verified live 3x), and the legacy
  composition tint paints past the rounded ends. Region blur keeps
  everything outside the silhouette truly empty. Honors the OS
  transparency-effects switch (`EnableTransparency`): solid `barBg` when
  off, on both layers.
  Taskbar tints live in `AppState.barFill` (dark `#202020` @ 0.78, light
  `#f3f3f3` @ 0.80 for System).
  - `barRadius`/`cardRadius` are perfect pills (half height: bar 13/14/17,
    cards 11/12/15), flat when the rounded toggle is off. QML radius must
    match the system-rounded HWND or halos/gaps appear. Do NOT go back to
    fixed radii without asking.
  - Re-apply triggers: `transparentBar`, `effectiveTheme`, `barRadius`,
    `systemTintChanged`, `transparencyChanged`. Miss one and corners/tint
    go stale.
- Palette deltas: System bar is glassy (`barFill` alpha = `glassAlpha`,
  driven by the Bar transparency slider, default 0.82) while System
  cards stay near-solid (dark 0.88) so text stays pure white. System borders
  are brightened (white/black 28%). Explicit Dark/Light keep solid frosts.
- Derived sizes — DO NOT rescale without asking:
  bar `Small 26 / Medium 28 / Large 34`, `cardHeight = barHeight - 4`,
  pill radii `barRadius = height/2`, `cardRadius = height/2` (bound to the
  bar/cards; the Settings toggle visibly rounds/flattens through them — the
  radius was once hardcoded `12`, which made the toggle a no-op).

## 4. Shelf bar rules

- The bar is NOT draggable (removed by request). `hwOnLeft` / `netOnLeft` /
  `clockOnLeft` place the meter groups (hardware, network, world-clock);
  all can share a side. `orderKeys` ("clock,hw,net") is the full display
  priority — drop any group anywhere to splice it into that rank on its
  side (persisted).
- Meter dragging MUST be owned by the bar-level `meterDrag` overlay, NOT
  by handlers inside the meter `Loader`s: side/order flips rebuild the
  slot Loader instances mid-press (and any in-flight gesture). The overlay
  hit-tests the active group (hw/net/clock), rejects all other presses
  (cards/buttons keep working), and the independent `meterGhost` overlay
  follows the cursor (original dims in place). Side flips LIVE at the shelf
  midpoint; any group splices into the drop rank among its side siblings
  (±10px hysteresis, unresolved siblings hold order) — the rearrange itself
  is the feedback (no badge/pill; a floating pill was tried and rejected
  as ugly).
- `hideClipboard` hides ONLY cards + scroll chevrons + search/PasteAll
  (all `visible: false`, fully collapsed — including the strip container
  itself, so no dead middle splits the meters). The edge spacers obey
  `AppState.alignment`: Left docks both meter groups left, Right both
  right, Center shares the space. Meters stay visible — they were once
  tied to the same flag and users objected.
- Shelf edge: `placeShelf()` in Main.qml (called on start, on
  `shelfPosition` change, on height change). AppBar docks BOTTOM
  (`dock()`) AND top (`dockTop()`, `ABE_TOP`) so maximized windows respect
  both edges. `onVisibleChanged` respects the edge too — never dock
  unconditionally. All popups (gear/meter/clip menus, previews) open BELOW
  the bar in Top mode via `popupY(h)` — above would leave the screen.
- Strip alignment (`AppState.alignment`, default Center, Settings segmented
  row): Left stacks from the chevron, Center pads the header so a short row
  sits centered, Right pads it flush right — model order never changes (no
  RTL mirroring). Deliberately NO highlight range: it fights scrolling.
  Pads are set imperatively with hysteresis + callLater coalescing, never
  bound to contentWidth (binding loops). Overflow strips scroll freely.
- Meter `Loader`s need `visible: active`, else inactive loaders contribute
  phantom `spacing` gaps. Meter containers use
  `Layout.preferredHeight: AppState.cardHeight` so icons match pill height.
  Hardware (CPU/RAM, `showCpuRam`) and network (`showSpeed`) toggle
  separately — one combined "Corners & meters" settings row holds all three
  toggles (round + hardware + net). Each side container and loader gates on
  its own flag, so hiding one group collapses it cleanly.
- Meter text: CPU/RAM 13px (CPU box w32 so `100%` clears the RAM icon),
  net up/down **14px regular, NOT bold** (w62). `MeterIcon`: CPU 13px box
  (1.6px), RAM 16px module with contact pins (1px outline, NO inner text —
  the shelf label already says RAM). `ShelfButton` hover wash uses
  `AppState.text`, never hardcoded white (invisible in light mode).
- Network counts PHYSICAL NICs only: NDIS filter drivers (QoS/WFP/Virtual
  WiFi) register separate GetIfTable rows mirroring the NIC 1:1 — measured
  6 copies turning 5 MB/s into 37 MB/s. Blocklist (type tunnel/PPP/loopback/
  propVirtual-53 + vpn/wintun/wireguard/lynx/tap/tun/filter/qos/wfp/
  lightweight/virtual-wifi/hosted/pseudo/loopback/teredo/isatap) + bounded
  `bDescr` reads (`dwDescrLen`, never assume NUL). Verified under load:
  37.6 → 6.1 MB/s against 4.8 true (residual = burst variance on the raw
  1s window, not bias). Never exclude by "virtual"/"hyper-v" names alone —
  inside a VM the guest NIC is the real link.
- Never nest a `MouseArea` with `width: parent.width` inside a `RowLayout`
  (binding loop collapses it to a zero hit-area — this silently killed meter
  dragging once). Wrap: `Item` root (`implicitWidth: inner.implicitWidth`,
  `implicitHeight: cardHeight`) + inner `RowLayout` + `anchors.fill` mouse area.
- Edge fades are CONDITIONAL: left shows only when `contentX > 4`, right only
  when more content sits right (`contentX < originX + contentWidth - w - 4`),
  150ms crossfade. Permanent fades gray out item 1 at rest. Chevron steppers
  live in fixed 30px gutters BESIDE the strip (reserved only while the strip
  can scroll, so widths never jump mid-scroll): click glides `contentX` 60%
  of the strip in 220ms. Never overlay buttons on the cards — sliding under
  an opaque button reads as a glitch. They are RowLayout siblings of the
  strip container, not ListView children.
- Toast 1400ms. Preview delay 350ms. Fullscreen auto-hide remembers manual
  hides (`userHidden`).

## 5. Clip cards (ClipCard.qml + ClipStore.qml)

- Card: radius 10, `width = max(52, min(200, implicitWidth + 23))` — the +23
  covers row margins (9+10) plus slack. Shrinking it clips short titles
  (a "1920x1020" → "1920x10…" regression happened exactly this way).
  Title `maximumWidth: 130` + elide: short text never clips, paragraphs may.
- Row geometry: `contentRow` margins L9/R10, spacing 4. Number (12/13 Black,
  w14), title (11/13), visit glyph (15px) all use `fillHeight +
  verticalAlignment: AlignVCenter` — one shared centerline. NO `topPadding`
  offsets on kind glyphs (a 2px one once hung video icons low).
- Hover: the full-card `hoverArea` sits ABOVE the visit glyph and steals Qt
  hover, so link highlight is tracked manually (`openHovered` from mapped
  coords): glyph brightens 1.45x + underline + 0.34 halo + hand cursor.
  Whole-card hover wash is selected-only — never re-add it.
- Cards lock the strip while pressed (`listRef.interactive = false`,
  restored on release/cancel/drag-end): otherwise press jitter side-scrolls
  the shelf before the OS drag takes over. Flick-from-card was already
  claimed by the 16px drag threshold, so nothing is lost.
- QML Font has NO `families` fallback property (verified fatal) — glyphs
  rely on automatic system fallback. Prefer codepoints present in both
  Segoe Fluent Icons and Segoe MDL2 Assets.
- Click feedback: `pulse()` accent bloom (0.65 peak, 480ms) fired from the
  delegate + press scale 0.93. Neighbors MUST NOT move on click: plain clicks
  never call `refresh()` (it rebuilds the model and replays the arrival
  animation on every card). Selection changes use `view.setProperty` on the
  row; same for favicon arrival/failure. `deleteItem` uses `view.remove(i,1)`
  + in-place `pos` renumber (exactly the deleted row animates out).
  Structural changes only (add, pin/snippet reorder, search, clear) may
  refresh. `replaceItem` reassigns the whole `items` array (element writes
  never notify `var` bindings — without this the Paste All button bound to
  `selectedCount()` stays dead after Shift+click). `searchText` has a direct
  `onSearchTextChanged: refresh()` handler (typing otherwise never filters).
- `findItem()` matches the unique `added` id first (`added` role is in the
  view model for this). Title+detail matching deleted the WRONG twin on
  Ctrl+click with duplicate contents — never revert. (The old Shift+click
  toast was also inverted; fixed.)
- NEVER locate `items` rows by object identity (`indexOf(o)`/`splice` by
  ref): it returns -1 on C++-sourced rows (verified: replace k=-1 on loaded
  items), which silently skipped selection/pin/snippet writes AND made
  `deleteItem`/`trimHistory` splice(-1,1) the WRONG (last) item while the
  view row went away — the ghost-row desync. Always `indexOfItem()` (stable
  `added` id, title+detail fallback), splice a sliced copy, reassign.
  `items` is normalized to a real JS array at load for the same reason.
- Storage settings are LIVE, not restart-only: `maxHistoryItems` changes
  trim+refresh+persist immediately (SpinBox min is 1, so "3" works);
  `maxFileSizeMB` is bound into `ClipboardService` and gates capture
  (oversize files/images are removed + `rejected` toast); `autoCleanHours`
  prunes file-backed unpinned items older than N hours on start and on
  change (`StorageService.fileAgeHours/removeFile`). Text/links carry no
  files and are exempt from cleaning.
- Drag-out (`beginSystemDrag` → `ClipboardService::startSystemDrag`):
  - Files/images go through RAW OLE `DoDragDrop` with a custom `IDataObject`
    carrying **only** `CF_HDROP` (+`CF_DIB` bitmap for images). PROVEN by
    clipboard probing: `QDrag`/`QMimeData` always advertises `text/uri-list`
    alongside, and targets insert `@///C:/...` link text instead of
    uploading. Do NOT "fix" this with explicit `FileNameW` bytes (Qt ignores
    them), `text/html`, or custom `DropEffect` (an ASCII `"1"` once broke
    effect negotiation) — all regressed delivery. Explorer-equivalence =
    HDROP-only. Text/links keep plain `QDrag` (text IS the payload there).
  - Mirror every drag onto the clipboard first (`copyFiles`/`copyText`,
    no auto-paste): Ctrl+V fallback if a target only pastes. Echo is
    suppressed, so no duplicate cards.
  - After the OS call returns: `dragging=false` + cycle `hoverArea.enabled`
    (grab released BEFORE the call, `try/finally` reset, 1.5s watchdog).
    The OS eats the mouse release; without this the card sticks faded/
    pressed until the next click. Keep `suppressClick=true` (eats the
    phantom post-drag click; do NOT clear it early or drops double-fire).
  - Threshold 8px. Toast `Dropped!` / `Drag cancelled`.
- Images: clipboard is read ONCE per notification (title/file/hash from one
  `QImage`; null reads return early — no `0x0` cards). Single-read also
  covers title/file mismatch races.
- Text is sanitized at capture AND on load: embedded NUL bytes (some apps
  include the terminator) render as tofu squares and survive SQLite —
  proven from a DB row `'Flickable \x00'`. `QString::remove(QChar(0))` both
  places; display heals on restart even before the DB row rewrites.
- Favicons: start as globe `\uE774`; empty/failed icons fall back to the
  globe immediately (`faviconFailed`, including empty-string icons);
  `resolveIcon` cache hit upgrades synchronously. `PreviewPopup.showFor`
  MUST reset `fileMode=false` (stale video card showed over images).
  All preview/file/title texts are `Text.PlainText` (`&` in URLs like
  `...&s=10` breaks AutoText rich-text parsing); long URLs use `WrapAnywhere`.
  Text previews size width from `TextMetrics.advanceWidth` (unwrapped) and
  cap at 480×420 / 30 lines — paragraphs show whole, novels elide. Never
  size from `implicitWidth`: it goes circular once text wraps (clamps to the
  current width, collapsing paragraphs to one elided line).
- Image thumbs: borderless rounded tile (`cardHeight - 6`, r6) + offset soft
  shadow (black 0.45, +1px) for shape — no border ink. Letterbox fit inside.
- PDF = the generic extension badge. Red badges, drawn pages, and glyphs were
  each tried and rejected — do NOT re-special-case without asking.

## 6. Settings window contract

- 720×560 (min 600×440), frameless, custom caption. Minimize button =
  `close()` (same as ✕): minimizing a frameless Tool window misfires onto
  other windows. App lives on in the tray either way.
- Dashboard look: section headers (`APPEARANCE/BEHAVIOR/STORAGE/ABOUT`,
  11px bold, letterSpacing 1.2), `SettingRow` cards (radius 10, `barBg` fill,
  NO border, 3px accent tick, 58/46 heights). NO divider lines anywhere
  (caption + sidebar dividers are transparent placeholders — keep the items,
  keep them transparent).
- `SettingRow` interior is ANCHOR-based (text Column left-bound, control slot
  right-anchored, grows leftward): every control shares one right edge at
  any width. Setting rows have NO accent ticks (removed by request) and NO
  borders. The Bar transparency row is `visible` only while Transparent bar
  is on. Column width = `settings.colWidth` (`width - 223`: 158 sidebar
  + 1 divider + 28 card margins + 36 stack margins). NEVER size settings
  geometry off `ScrollView`/`StackLayout` widths, and NEVER reference the
  `settings` id from inside `SettingRow.qml` (separate component scope —
  ids don't cross files; it silently breaks).
- Controls: `ThemedSegment` pill rows for Theme/Bar/Position (78×30, accent
  selected), `ThemedSwitch` (40×22 sliding knob, `compact` 32×18 variant for
  tight rows), `ThemedSpin` (dark field, typed values commit+clamp on
  Enter), `ThemedSlider` for Bar transparency (drives `AppState.glassAlpha`,
  live). Mini settings cards (3-in-a-row) use stacked title + compact
  switch at 12px titles so names never elide — side-packed label+switch
  overflowed narrow windows. Transparency rows show ONLY in
  System mode (`visible` bindings) — explicit Dark/Light are always solid,
  so hidden settings can't ghost-control them. Storage tab also has
  Auto-clean files (hours, 0 = off). `ThemedCombo.qml` exists again by
  explicit user request (clock time-zone dropdowns on the Items on Bar page;
  the old file was deleted to keep Settings dropdown-free). It is fully
  self-styled (dark card + dark popup, no native frame) and registered in
  `CMakeLists.txt` `QML_FILES`. Scrollbars: `AlwaysOff` everywhere (wheel
  still scrolls); a custom scrollbar once rendered as a stray white stub.

## 7. Session workflow for the next agent

1. Kill `Totthodhara.exe` before rebuilding (locked exe + mutex).
2. `cmake --build build`; run `build\Totthodhara.exe` from `Totthodhara/`.
3. Verify VISUALLY: foreground the window (`FindWindowW` + screenshot via
   `CopyFromScreen` to `C:/Temp/*.png`) and READ the image. Ship nothing
   unshot for UI work.
4. Keep independence of payloads in mind: clipboard ≠ drag. Probe first
   (`C:/Users/moham/AppData/Local/Temp/opencode/dropprobe/` pattern) when
   changing OLE/clipboard formats; compare against Windows-native output,
   not assumptions.
5. When the user iterates on taste (icons, animation feel, densities): offer
   direction, change ONE thing, screenshot, repeat. Never bundle taste
   changes with behavior changes in one unverified leap.
