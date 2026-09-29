# Totthodhara — AI Context Preservation File

**DO NOT remove, modify, or delete any feature described below unless the user explicitly asks.**
**DO NOT change any working code to "improve" it unless asked.**
**DO NOT refactor working code.**
**DO NOT add new features unless asked.**
**Always preserve the exact behavior of drag-and-drop, click-to-paste, animations, and acrylic glass.**
**Clipboard item cards (`CardItemBg`) are visually distinct from widget pills (`WidgetBg`) — never merge them.**
**Never assign a `LinearGradientBrush` to a variable that is later cast to `SolidColorBrush` (causes `InvalidCastException` at startup).**
**Never add `DropShadowEffect` to item cards — it extends render bounds beyond the element and breaks padding consistency across themes.**

---

## Architecture

- **Language:** C# WPF (.NET 10, Windows 10.0.19041.0+)
- **Pattern:** MVVM with CommunityToolkit.Mvvm source generators
- **DI:** Microsoft.Extensions.Hosting (generic host)
- **Database:** SQLite (sqlite-net-pcl)
- **Tray Icon:** H.NotifyIcon.Wpf
- **Global Hotkey:** NHotkey.Wpf
- **UI Library:** WPF-UI (v4.3.0)
- **Thumbnails:** WindowsAPICodePack-Shell (videos)
- **Namespace:** `ClipDropPro` throughout (NOT Totthodhara)

---

## Build System (CRITICAL)

### Project file: Totthodhara.csproj
- **Target:** `net10.0-windows10.0.19041.0`
- **UseWPF:** true, **UseWindowsForms:** true
- **Output:** WinExe, win-x86 (NOT AnyCPU)
- **SelfContained:** true
- **PublishSingleFile:** true
- **EnableCompressionInSingleFile:** true
- **IncludeNativeLibrariesForSelfExtract:** true
- **Optimize:** true, **DebugType:** none
- **SatelliteResourceLanguages:** en

### ⚠️ CRITICAL — NEVER set `<PublishTrimmed>true</PublishTrimmed>`
WPF uses reflection for XAML initialization. Trimming causes a silent startup crash.

### Build command for portable EXE:
```
dotnet publish -c Release -r win-x86 --self-contained true ^
  /p:PublishSingleFile=true ^
  /p:EnableCompressionInSingleFile=true ^
  /p:IncludeNativeLibrariesForSelfExtract=true ^
  /p:Optimize=true ^
  /p:DebugType=none
```
Output goes to `bin\Release\net10.0-windows10.0.19041.0\win-x86\publish\`

### ICO requirement
The app icon (`app.ico`) must be a perfect square. A non-square PNG renamed to .ico causes the splash logo to stretch. Use Pillow to pad with transparency before converting.

---

## Features (Complete List)

### 1. Floating Capsule Dock Shelf
- Window has `AllowsTransparency="True"` and `Background="Transparent"` — the desktop shows through behind the capsule
- Content is wrapped in `CapsuleBorder` (a `<Border>` with `CornerRadius`, `Background="{DynamicResource AppBackground}"`, no DropShadowEffect, no border) creating a floating pill/dock appearance
- Outer wrapper `Border` with `ClipToBounds="True"` surrounds CapsuleBorder as a safety net
- Registers as Windows **AppBar** (reserves screen space so maximized windows don't overlap)
- `WindowStyle="None"`, `ShowInTaskbar="False"`, `Topmost="True"`
- Dynamic corner radius and margin set in `SetAppBarPos()` based on bar size
- **DO NOT** add DropShadowEffect to CapsuleBorder — it extends render bounds below the element into the transparent window, creating a visible gap between the shelf and the taskbar
- **DO NOT** add BorderThickness to CapsuleBorder — the semi-transparent border line is visible as a gap at the screen edge
- **DO NOT** change the AppBar registration logic
- **DO NOT** remove `WS_EX_NOACTIVATE` / `WS_EX_TOOLWINDOW` — prevents focus steal

### 2. Acrylic Glass Effect (MainWindow.xaml.cs `EnableAcrylic()`)
- **Brush-only frost (NOT real DWM blur)** — `EnableAcrylic()` always applies `ACCENT_DISABLED`. Real `ACRYLICBLURBEHIND` was tried and reverted 2026-09: DWM paints blur over the full rectangular HWND, filling the rounded-corner cutouts (verified in screenshots) and `SetWindowRgn` clipping does not prevent it. Frosted fills (`0xE6` shelf) over a sharp desktop look near-identical on a 32px bar with crisp corners. **DO NOT re-enable ACRYLICBLURBEHIND.**
- `RootGrid.Background` is `Transparent` (window bg shows through).
- `SetAppBarPos()` clears any HWND region (`SetWindowRgn(NULL)`) — with DWM blur disabled there is nothing to contain, and a round-rect region is a 1-bit mask that staircases corners ("pixel art"). WPF anti-aliases corners natively. Do NOT re-add region clipping.
- Rounded-corner wash-out fix (2026-09): frosted fills were deepened `0xCC`→`0xE6` so the capsule silhouette reads over blur. Do not thin them back without checking corners on wallpaper.

### 3. System Tray Icon
- H.NotifyIcon.Wpf `TaskbarIcon`
- Right-click: Show Shelf, Exit
- Double-click: Toggles shelf or opens settings (configurable via `TrayIconAction`)
- Icon is loaded from `app.png`, auto-cropped of transparency, resized to 32x32
- **DO NOT** remove the `_keepTrayIconAlive` static field — prevents GC from collecting the tray icon

### 4. Clipboard Monitoring
- `AddClipboardFormatListener(handle)` with `WM_CLIPBOARDUPDATE (0x031D)` message
- Handled in `HwndHandler()` → calls `_viewModel.ProcessClipboardChange()`
- Deduplication: checks `_lastCapturedContent` + 500ms window
- **DO NOT** replace with polling — the listener is the correct approach

### 5. Clipboard History (ProcessClipboardChange)
- Captures: **Images** (saves as PNG via `SaveBitmapAsync`), **Files** (copied to storage), **Text**
- Retry loop: 5 attempts, 30ms delay, catches `COMException` (clipboard busy)
- Internal clipboard guard (`_clipboardGuard` via `Interlocked.Exchange`) prevents re-entry
- `IsInternalChange` property checks `_lastInternalChangeTime < 500ms` to skip our own clipboard sets

### 6. SQLite Persistence
- Database: `{BaseDir}\data\metadata.db` (created automatically)
- Storage: `{BaseDir}\data\Storage\` (copied files)
- Settings: `{BaseDir}\data\settings.json`
- Log: `{BaseDir}\data\app_debug.txt`
- Items survive app restart

### 7. Pin Items
- `IsPinned` flag on ClipboardItem
- Pinned items stay at top of list, never auto-deleted
- Context menu: "Pin to top" / "Unpin"
- Gold pin icon overlay on the index pill

### 8. Snippets
- `IsSnippet` flag on ClipboardItem
- Snippets are permanent saved items, never auto-deleted
- Context menu: "Save as Snippet" / "Remove Snippet"
- Star icon overlay on the index pill

### 9. Item Index Pill (MainWindow.xaml)
- Capsule/pill shape (`MinWidth=ItemCircleSize`, `Height=ItemCircleSize`, `CornerRadius=20`)
- Displays `DisplayIndex` (1-based numbering)
- Pin icon (top-right) and Snippet icon (top-left) overlays
- **DO NOT** revert to a fixed-size circle — the pill was chosen to accommodate 2-digit numbers

### 10. Click-to-Paste (ItemClicked in MainViewModel)
- **Normal mode:** copies item to clipboard, waits 100ms, simulates Ctrl+V via `SendKeys.SendWait("^v")`
- **Multi-paste mode:** toggles selection, no paste
- Image files: copies BOTH bitmap + file drop list in one `WinForms.DataObject`
- Other files: file drop list
- Text: `Clipboard.SetText`
- **DO NOT** remove the 100ms delay before SendKeys — needed for clipboard to be available
- **DO NOT** replace SendKeys with SendInput — SendKeys is more compatible

### 11. Paste All
- Button visible only when `HasSelectedItems` is true
- Iterates selected items, copies each, waits 100ms, simulates Ctrl+V, waits 150ms between items
- Clears selections and disables multi-paste mode after completion

### 12. Shift+Click Select + Paste All (multi-paste toggle button REMOVED 2026-09-30)
- Hold **Shift** and click items to toggle their `IsSelected` state (`IsShiftHeld()` in `ItemClicked`); check marks + blue accent on selected items
- **Paste All** button (visible only when `HasSelectedItems`) pastes all selected sequentially, then clears
- NOTE: global `Ctrl+V` can never be intercepted — it belongs to the foreground app. Sequential paste happens only through Paste All. Do NOT add a global Ctrl+V hook.

### 13. Drag & Drop FROM Shelf (Item_PreviewMouseMove)

**Drag threshold:** 5 pixels (changed from 1px to prevent accidental drags)

**Image drag:**
- WPF DataObject with:
  1. `"ClipDropShelfOrigin"` marker (prevents re-import if dropped back on shelf)
  2. `FileDropList` with original file path (NOT temp copy — temp copy was removed because WhatsApp couldn't read it)
  3. `"DeviceIndependentBitmap"` (DIB format) as MemoryStream for image preview in target apps
- **DO NOT** remove DIB format — it's needed for WhatsApp preview
- **DO NOT** revert to temp copy — original path is more reliable
- **DO NOT** remove FileDropList — needed for apps that need the file path

**File drag (non-image):**
- WPF DataObject with `"ClipDropShelfOrigin"` + `FileDropList` (original path)

**Text drag:**
- WPF DataObject with `"ClipDropShelfOrigin"` + `UnicodeText` + `Text`

**Ctrl+drag (delete):**
- Sets element opacity to 0.3, shows drag popup
- DoDragDrop with `"ClipDropItemDelete"` marker
- If dropped outside shelf bounds → deletes item
- If dropped back on shelf → cancelled

### 14. Drag & Drop TO Shelf (Window_Drop)
- `"ClipDropItemDelete"` → cancels deletion
- `"ClipDropShelfOrigin"` → ignores (prevents duplicates from shelf-to-shelf drag)
- `FileDrop` → `HandleDroppedFilesAsync()`
- `Text` → `HandleDroppedTextAsync()`
- `AllowDrop="True"` on the Window

### 15. Drag Ghost Popup
- Floating semi-transparent window that follows cursor during drag
- Shows item index and text preview
- Updated via `GiveFeedbackHandler`
- Cancellable via Escape key (`QueryContinueDragHandler`)

### 16. Rich Item ToolTips
- **Images:** Thumbnail up to 200px height, high-quality scaling
- **Videos:** Thumbnail + play triangle overlay
- **PDFs:** DocumentPdf24 icon + filename + "PDF Document"
- **Audio:** MusicNote224 icon + filename + "Audio File"
- **Text:** Wrapping text content (hidden when image/video)
- 15s show duration, 100ms initial delay

### 17. Right-Click Context Menu
- Shows on any item via right-click
- Menu items: Pin/Unpin, Snippet/Remove Snippet, Delete
- **Item highlight:** when menu opens, the item border glows with AccentColor
- Context menu is centered horizontally on the item
- ToolTip is removed while menu is open (to prevent overlap), restored on close

### 18. Delete Confirmation Bubble
- Clicking "Delete" shows a small bubble window above the shelf: "✕ Remove this item"
- Clicking the bubble confirms deletion
- Clicking anywhere else closes the bubble
- Low-level mouse hook monitors clicks outside the bubble

### 19. Ctrl+Click Delete
- Hold Ctrl and click an item → immediately deletes (no confirmation)

### 20. Search (Ctrl+F) — NO search button on shelf (REMOVED 2026-09-30)
- The search TextBox is created entirely in code (`OpenSearchBox`) at the old button's grid cell; summoned via `Ctrl+F`
- Filters items by TextContent, FileName, DisplayTitle, DisplayText (case-insensitive); Escape closes
- Search box fill MUST stay `Transparent` (verified pixel-identical 45,55,85 vs bar 45,54,84): any fill — even `AppBackground` — double-filters the frosted wallpaper and renders a darker navy patch. Border-only + themed caret/text.

### 21. Smooth Scroll
- Left/Right scroll buttons animate by ±250px
- Cubic ease-out, 250ms duration, 16ms timer intervals
- Mouse wheel scrolls horizontally
- **DO NOT** change to direct scroll — animation is deliberate

### 22. Animations
- **New item:** fade-in (0→1 opacity) + scale-up (0.85→1), 0.25-0.3s, CubicEaseOut
- **Removing item:** fade-out (1→0) + scale-down (1→0), 0.35s, CubicEaseIn
- **New flash:** `IsNew` trigger → border color `#60CDFF` pulse + shadow opacity 0→0.4, auto-reverse 2x
- **Hover glow:** shadow opacity 0→0.25 on IsMouseOver (0.15s), reverses on exit (0.2s)

### 23. Fullscreen Detection (_fullScreenCheckTimer, 100ms timer)
- Detects when a foreground window covers the entire monitor
- **Two methods:**
  1. Window rect matches monitor bounds (within 1px)
  2. Fallback: window style (no caption `WS_CAPTION`, no thick frame `WS_THICKFRAME`) + >95% screen width
- After 1.5s in fullscreen → hides shelf, unregisters AppBar
- On exit → shows shelf, re-registers AppBar
- Process ID check prevents false triggers from child windows
- Checks `Shell_TrayWnd`, `WorkerW`, `Progman` → force show

### 24. Theme Engine (UpdateTheme / ApplyCustomColors)
- **3 modes:** Light, Dark, System (reads registry), plus Transparent (custom user theme)
- Sets 13 resource brushes: `AppBackground`, `TextColor`, `IconColor`, `CardBg`, `CardItemBg`, `WidgetBg`, `ControlBg`, `BorderColor`, `MenuBg`, `ToolTipBg`, `AccentColor`, `WindowBg`, `ShadowOpacity`, `ShadowColor`
- **`CardItemBg` is SEPARATE from `CardBg`** — see Item Card Visual Rules below. Never collapse them.
- **`WidgetBg` is for hardware pills only** (CPU/RAM/Network/Clock) — separate from `CardItemBg`
- **DO NOT** remove any of the resource keys — each is used in XAML

**Light Mode Colors:**
- Capsule bar (AppBackground): `#FFFFFF` solid (no acrylic, no blur)
- CardItemBg (clipboard items): `#F7F2EC` solid 93% alpha (warm white, flat)
- CardBg (other surfaces): `#10000000` (black 6% overlay)
- WidgetBg (hardware pills): `#08FFFFFF` (white 3% overlay)
- ControlBg (hover): `#33000000` (black 20% overlay)
- TextColor: `#222222`
- IconColor: `#222222`
- BorderColor: `rgba(0,0,0,30)`
- AccentColor: `#0078D4`
- MenuBg: `#FAFAFA`
- ToolTipBg: `rgba(250,250,250,245)`
- WindowBg: `#FFFFFF`
- ShadowOpacity: `0.2`
- ShadowColor: Black

**Dark Mode Colors (Win11 Start-menu tone — desaturated blue-grey):**
- Capsule bar (AppBackground): vertical gradient `#222226` (top) → `#18181C` (bottom)
- CardItemBg (clipboard items): `#2C2C32` solid (flat elevated surface, lighter than shelf)
- CardBg (other surfaces): `#08FFFFFF` (white 3% overlay)
- WidgetBg (hardware pills): `#03FFFFFF` (white 1% overlay — barely visible)
- ControlBg (hover): `#383840` solid (elevated, lighter than CardItemBg)
- TextColor: White
- IconColor: White
- BorderColor: `rgba(255,255,255,40)`
- AccentColor: `#60CDFF`
- MenuBg: `#1E1E1E`
- ToolTipBg: `rgba(30,30,30,245)`
- WindowBg: `#1C1C20`
- ShadowOpacity: `0.35`
- ShadowColor: Black

**Transparent Mode Colors (custom user theme):**
- AppBackground: `Transparent` (desktop shows through)
- CardItemBg: `#18FFFFFF` (white 10% — translucent)
- WidgetBg: `#08FFFFFF` (white 3% — barely visible pills)
- ControlBg (hover): `#38FFFFFF` (white 22%)
- BorderColor: `#55FFFFFF`
- AccentColor: `#60CDFF`
- TextColor: White
- IconColor: White
- ShadowOpacity: `0` (no shadow — bar is invisible)
- All other colors match dark mode

### 24b. Item Card Visual Rules (CRITICAL)
**Two completely separate background resources — never merge them:**

| Resource | Used by | Background style |
|---|---|---|
| `CardItemBg` | Clipboard item cards (ItemsControl) | **Flat solid tint** — the only place items get a distinct color |
| `WidgetBg` | Hardware pills (CPU/RAM/Network/Clock), toolbar buttons | **Subtle overlay** — meant to be barely visible |

**Rules:**
1. **Clipboard item cards use `CardItemBg` (NOT `CardBg`)** — see `MainWindow.xaml:355`. Do not change this binding.
2. **Card background must be a `SolidColorBrush`** — never assign a `LinearGradientBrush` to a variable that later gets cast to `SolidColorBrush` (causes `InvalidCastException` at startup, e.g. `var cardBg = cardItemBg` then `((SolidColorBrush)cardBg).Color`).
3. **No `DropShadowEffect` on item cards** — extends render bounds ~6px around each card, making them appear larger and breaking visual padding consistency across themes (transparent/dark). Per the agent prompt: *"DropShadowEffect extends render bounds BEYOND the window"*.
4. **No `BorderThickness` on `CapsuleBorder`** either (already documented above for shelf).
5. **Padding is uniform across all themes** — `ItemPadding` is set in `SetItemSizes()` and does NOT vary by theme. Card visual differences come from background color, not size.
6. **Hover on item cards** = `ControlBg` background + `AccentColor` border at `BorderThickness=1` (replaces old `BorderColor` border for visual feedback).

### 24c. What's New Window Cards
- **DO NOT use `<ui:Card>`** (WPF-UI 4.3.0) — it has a hardcoded white background that ignores the theme.
- Use themed `<Border>` with `Background="{DynamicResource ControlBg}"`, `BorderBrush="{DynamicResource BorderColor}"`, `BorderThickness="0.5"`, `CornerRadius="8"`.
- All `TextBlock`s must bind `Foreground="{DynamicResource TextColor}"` — explicit binding, never rely on inheritance.

### 25. Global Hotkey
- Default: Ctrl + ` (OemTilde)
- Configurable in Settings
- Toggles shelf visibility
- Registered via NHotkey.Wpf HotkeyManager

### 26. Start with Windows
- Registry key: `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run`
- Key name: `"Totthodhara"`
- Value: path to executable

### 27. File Size Limit
- Default: 50 MB
- 0 = no limit
- Checked in `HandleDroppedFilesAsync`

### 28. History Size Limit
- Default: 30 items
- Auto-trims oldest non-pinned/non-snippet items on each add
- Fetches `max(MaxHistoryItems, 10)` items from DB, sorts, trims

### 29. Auto-Clean
- Default: 1 day
- Removes files older than N days (pinned files preserved)
- Runs on startup `InitializeAsync()`

### 30. Update Notification on Startup
- `AutoCheckUpdates` is `true` by default (`SettingsService.cs:40`)
- On startup, `App.OnStartup` runs an auto-check via `Task.Run`
- If `IsUpdateAvailable` → shows the update notification dialog (or runs silent install if `SilentAutoUpdate` is on)
- If **no** update available → completely silent (only a log line, **NO** "You are up to date" popup on startup)
- The "You're up to date" message only shows on **manual** checks (tray menu / Settings → About → Check for Updates) — not on auto-check
- 1-hour result cache (`update_cache/last_check.json`) avoids hitting GitHub on every launch

### 30. Single Instance Enforcement
- Named `Mutex` (`"Totthodhara-ClipDropPro-Unique-Mutex"`)
- On startup, kills old instances (by process name: Totthodhara or ClipDropPro)
- If mutex already exists, shows message box and exits

### 31. App Icon Handling
- `ApplicationIcon="app.ico"` in csproj
- Window icon: loaded from `app.png` via pack URI
- Tray icon: `app.png` → `CropImageTransparency` → resize to 32x32 → convert to `System.Drawing.Icon`
- `_keepTrayIconAlive` prevents GC from collecting the managed Icon wrapper

---

## P/Invoke Calls (DO NOT remove any)

| DLL | Function | Used For |
|---|---|---|
| `shell32.dll` | `SHAppBarMessage` | AppBar registration & positioning |
| `dwmapi.dll` | `DwmSetWindowAttribute` | Acrylic glass + exclude from peek |
| `user32.dll` | `SetWindowCompositionAttribute` | Acrylic fallback (Win10) |
| `user32.dll` | `SetWindowPos` | Window positioning |
| `user32.dll` | `GetWindowLong` / `SetWindowLong` | Extended window styles |
| `user32.dll` | `RegisterWindowMessageA` | AppBar callback message |
| `user32.dll` | `GetForegroundWindow` | Fullscreen detection |
| `user32.dll` | `GetWindowRect` | Fullscreen detection |
| `user32.dll` | `GetClassName` | Window class detection |
| `user32.dll` | `GetWindowThreadProcessId` | Process ID from window |
| `user32.dll` | `MonitorFromWindow` | Monitor for fullscreen |
| `user32.dll` | `GetMonitorInfo` | Monitor bounds |
| `user32.dll` | `WindowFromPoint` | Context menu click-outside |
| `user32.dll` | `IsChild` | Context menu click-outside |
| `user32.dll` | `AddClipboardFormatListener` | Clipboard monitoring |
| `user32.dll` | `SetWindowsHookEx` / `UnhookWindowsHookEx` / `CallNextHookEx` | Mouse hook (delete bubble) |
| `user32.dll` | `GetCursorPos` | Drag end check |
| `user32.dll` | `SetForegroundWindow` | Force window to front |
| `user32.dll` | `SendInput` | Simulate paste (SimulatePaste in ViewModel) |
| `gdi32.dll` | `DeleteObject` | Free thumbnail HBitmap |

---

## Converters (7 active converters, DO NOT remove)

| Converter | Purpose |
|---|---|
| `FileToSymbolConverter` | Maps file extensions to WPF-UI icons |
| `InvertedBooleanToVisibilityConverter` | Inverted bool→Visibility |
| `PinnedToColorConverter` | Gold for pinned, white for unpinned |
| `PinnedToMenuItemConverter` | "Unpin" / "Pin to top" |
| `SnippetToMenuItemConverter` | "Remove Snippet" / "Save as Snippet" |
| `DebugStatusToVisibilityConverter` | Hide when empty or "Ready" |
| `CountToVisibilityConverter` | Show badge when count > 0 |

Also used in SettingsWindow:
- `StringEqualityToVisibilityConverter` / `StringEqualityToBoolConverter` (RadioButton binding)

---

## Data Model (ClipboardItem.cs)

**SQLite columns:** Id, FileName, FilePath, TextContent, IsFile, IsPinned, IsSnippet, DateAdded, DisplayTitle, IconGlyph, Origin

**Computed properties (NOT in DB):** IsImage, IsVideo, IsPdf, IsAudio, Index, DisplayIndex, DisplayText, ThumbnailSource, ResolutionText, IsUrl, HasIcon, IsFirstUnpinned, IsRemoving, IsNew, IsSelected, IconSource

**IsImage** = IsFile && (ext in .png/.jpg/.jpeg/.gif/.bmp/.ico)
**IsVideo** = IsFile && (ext in .mp4/.avi/.mkv/.mov/.wmv)
**DisplayText** = DisplayTitle ?? FileName ?? TextContent (whitespace normalized)

---

## Services

| Service | Interface | Implementation | Purpose |
|---|---|---|---|
| DataService | IDataService | SqliteDataService | SQLite CRUD |
| FileStorageService | IFileStorageService | FileStorageService | File save/delete/download |
| SettingsService | ISettingsService | SettingsService | JSON settings persistence |
| HotkeyService | IHotkeyService | HotkeyService | Global hotkey via NHotkey |
| GestureService | IGestureService | GestureService | Double-Ctrl detection (UNUSED) |
| StartupService | IStartupService | StartupService | Registry auto-start |
| Logger | (static) | Logger | Async file logging |

### Network speed accuracy — count PHYSICAL adapters only (2026-09-30)
- **Bug fixed:** the old filter (`Up` + `!= Loopback` + description `WFP/Miniport/Filter Driver/LightWeight Filter/QoS/Virtual WiFi`) counted a **VPN tunnel adapter *and* the physical NIC under it**. Measured live: `ProtonVPN` 10.0 KB/s + `Wi-Fi` 10.7 KB/s → shelf showed **20.7 KB/s ≈ 2x real speed**. The tunnel's bytes are the same bytes the NIC carries, so summing them double-counts.
- `SystemMonitorService.IsPhysicalAdapter` now keeps only `NetworkInterfaceType.Ethernet` (6) or `Wireless80211` (71), minus filter-driver / `Wi-Fi Direct` / `Tunnel` descriptions. The physical NIC is the ground truth for what hits the wire — same as IDM and Task Manager.
- **Do NOT exclude by "Virtual"/"Hyper-V"/"VirtualBox" keywords.** Inside a VM the guest's adapter *is* its real link; filtering it out reports 0. Interface **type** is the reliable discriminator, not the name.
- Units are **bytes** (`/1024/elapsed` → KB/s, `FormatSpeed` shows KB/s or MB/s). Matches IDM. An 8 **megabit** line has a ceiling of 8 ÷ 8 = **1.0 MB/s** — `1.0 MB/s` is full speed, not a misread.
- Remaining, unfixable gaps: `GetIPv4Statistics()` is **IPv4-only** (IPv6 uncounted — measured `Diff_Recv/Sent = 0` on this machine, so currently harmless), NIC counters include protocol overhead (~3–4% above payload), and the 2s sampling window averages bursts where IDM samples per-chunk.


## Known Quirks / DON'T CHANGE UNLESS ASKED

1. **Drag threshold is 5 pixels** — small enough for quick drag response, large enough to prevent accidental triggers
2. **SendKeys.SendWait("^v")** for paste — NOT SendInput. SendInput was tried but caused reliability issues with certain apps
3. **100ms delay** before SendKeys — clipboard must be available before paste simulation
4. **Original file path** in drag FileDropList (not temp copy) — temp copies caused permission/access issues in target apps like WhatsApp
5. **DIB format** included alongside FileDrop for image drags — provides preview in image-aware target apps
6. **Window uses `AllowsTransparency="True"` with `Background="Transparent"`** — the CapsuleBorder inside creates the visual, the transparent window bg lets the desktop show through
7. **DropShadowEffect removed from CapsuleBorder** — the shadow extended below the element into the transparent window area, creating a visible gap between the shelf and the taskbar. BorderThickness also set to 0 for the same reason.
8. **Capsule corner radius and margin set dynamically in `SetAppBarPos()`** based on bar size (Small: r=14, Default: r=18, Large: r=23); horizontal margin = 0 (edge-to-edge), vertical margin = 0 (flush) so rounded corners meet screen edges
9. **ShutdownMode="OnExplicitShutdown"** — app must stay alive for tray icon even when shelf is hidden
10. **OnClosing cancels close and hides** — prevents app from closing when user clicks X (they should use tray Exit)
11. **Window width is 800** but stretched by AppBar to screen width
12. **OpacityMask** on ScrollViewer creates fade effect at edges of item list
13. **Context menu uses PlacementTarget.Tag** to find MainViewModel — required because ContextMenu is in a separate visual tree
14. **Item Background is `{DynamicResource CardBg}`** — enables per-mode card color. IsMouseOver trigger uses `ControlBg` to override. Light mode: CardBg=#F0F0F0, ControlBg=#666666; Dark mode: CardBg=#333333, ControlBg=#555555
15. **All `ApplicationThemeManager.Apply()` calls except the single one in App.xaml.cs are removed** — the only Wpf-Ui Apply is `Apply(Light)` as a base theme. All color customization done via UpdateTheme's 12 resource overrides
16. **Toolbar buttons use clean icon-only style** with `Border CornerRadius="8"` and hover background, replacing old circular Ellipse backgrounds
17. **CapsuleBorder CornerRadius is per-position** — bottom shelf: `(radius, radius, 0, 0)` (flat bottom flush with taskbar); top shelf: `(0, 0, radius, radius)` (flat top flush with screen edge). Prevents transparent rounded corners from creating visible gap against the taskbar/screen.
18. **Shelf stacks above taskbar** — `FindWindow("Shell_TrayWnd")` + `GetWindowRect` queries actual taskbar position. Shelf is placed directly above it (for bottom position) instead of at the screen edge.
19. **Outer wrapper Border with `ClipToBounds="True"`** surrounds CapsuleBorder as a safety net against any visual bleed.
20. **AppBar position: only WPF Width/Height set after SetWindowPos** — Left/Top NOT set synchronously to prevent WPF re-layout shifting the window by 1-2px due to DPI rounding.

---

## Session 2026-09-29 — Live OS Theme + Shelf Resizing (DO NOT regress)

### A. Live OS theme follow (System mode) — `Services/OsThemeHelper.cs` (new file)
- `IsAppsLightTheme()` reads **fresh every call**: `AppsUseLightTheme` first, fallback `SystemUsesLightTheme` (`HKCU\...\Themes\Personalize`). Never cache the mode.
- `ApplyWindowTheme(hwnd, isLight)`: sets **both** `DwmSetWindowAttribute` 20 (Win10 20H1+) **and** legacy 19, plus `SetWindowTheme("DarkMode_Explorer"/"Explorer")`. List views stay on Explorer (correct dark scrollbars) — only headers may use `DarkMode_ItemsView`. Custom row colors untouched.
- `GetWallpaperKey()` / `GetWallpaperBottomAverage()`: wallpaper file path + timestamp (or solid `Control Panel\Colors\Background`), bottom-seventh center band downscaled to 1x1. Image is re-read ONLY when the key changes.
- Detection uses **existing channels only, no new timers/sleeps**: `SystemEvents.UserPreferenceChanged`, `HwndHandler` `WM_SETTINGCHANGE / WM_THEMECHANGED / WM_DWMCOLORIZATIONCOLORCHANGED`, OS-state check inside the 50ms fullscreen tick + drag `GiveFeedbackHandler` + scroll animation tick. Key = `light|transparency|wallpaper` (`BuildThemeStateKey`) — any taskbar-affecting change flips it.
- `MainWindow.UpdateTheme()` / `EnableAcrylic()` / `SettingsWindow.ApplyThemeColors()` all use the helper (NOT one-shot startup reads). `ACRYLICBLURBEHIND` stays OFF (rounded-corner fill, verified).
- Every popup reads theme fresh on show + `SourceInitialized` immersive apply: `ThemedMessageBox`, delete bubble, drag ghost, all update dialogs, `WhatsNewWindow`. Visible bubble/ghost re-theme mid-display via `RefreshVisiblePopupThemes()`. Settings window (rebuilt per open) re-applies on flip while open + spinner `CurrentTimeInvalidated` re-check. Delete bubble + drag ghost are **light-aware** (bubble was hardcoded white text — wrong on light).

### B. Bar sizes — `SetAppBarPos()` (exact values, cards stay even-height so centering lands on whole pixels)
| Size | Bar | Card | Call |
|---|---|---|---|
| Small | 26 | 22 | `SetItemSizes(13, 1, 3, 1.5, 12, 24, 12, 16, 17, 60)` |
| Medium | 30 | 24 | `SetItemSizes(15, 2, 4, 1.5, 14, 28, 13, 18, 19, 70)` |
| Large | 36 | 30 | `SetItemSizes(19, 3, 5, 2.5, 15, 30, 15, 22, 22, 78)` |
Signature: `SetItemSizes(circle, margin, padH, padV, font, menuH, arrowW, arrowH, toolIcon, speedW)`. Radii: 10/12/14. Rule: **card height must be even, bar height even** — odd/even mismatch puts centering on a half-pixel and the top edge renders clipped/blurred.
- **Fresh-install default is `BarSize = "Small"`** (`SettingsService.cs`, `Settings.BarSize`) — chosen by the user 2026-09-30. An existing `settings.json` keeps its own saved value; only a first launch reads this default. `AutoCheckUpdates` default stays `true`, `SilentAutoUpdate` stays `false`.
- Toolbar glyph sizes are **explicit per size** (17/19/22 — matched to the hardware `SysMonitorCanvasSize` 18/20/21 so toolbar and monitor glyphs read the same size), NOT font-derived — Small must not change when tuning Medium/Large. Template `Padding="1"` (scroll L/R, search, settings). MultiPaste toggle has no padding (icon straight in box).
- MultiPaste icon is the original `Clipboard24` glyph inside the unified padded-border template (a hand-drawn outline replacement was tried 2026-09-29 and reverted — user prefers the original). Same template shape as the other toolbar buttons.
- LostFocus auto-close has a 400ms grace timer (`_searchFocusGrace`, cancelled by refocus/close): focus often bounces right after the search box is summoned and an instant close would eat the search.
- Fonts: Small 12, Medium 14, Large 15 (11–12px rendered as "pixel art" — 13px+ with the existing ClearType+Display text settings reads clean).
- **Clock pill optical alignment:** the icon (`E121`) and the `DisplayName` label are font glyphs, so WPF centers their *line box*, not their ink — the clock glyph hung ~2px below the value baseline. Both carry a `RenderTransform`/`TranslateTransform` (`Y="-2"` icon, `Y="-1"` label) so icon, label and value share one visual center. Use `RenderTransform`, NOT `Margin` — a margin would change pill layout/height and can clip.

### C. Toolbar spacing — uniform 2px gaps, one shared centerline
- All 9 monitor/clock pills: `Margin="1,0,1,0"`. Monitor/clock outer containers + plugins: edge margins trimmed so every gap (chevrons/pills/gear) = 2px. Chevron-right→monitors region: search/multi columns are permanently collapsed (zero-width, no gaps). Do NOT re-widen pill margins to 3.
- Network value hugs arrow: left-aligned, `Margin="4,0,0,0"` (was `2,0,0,0` — 2026-09-30, user reported the speed looked like it was touching the arrow). The arrow canvas is `12x18` in a `12x18` Viewbox, i.e. **1:1 scale, no scaling**, and the arrowhead ink reaches `x=11.9` — only 0.1px from the box edge. So the true visual gap is margin + 0.1px; 2px read as touching, 4px does not. Applies to all 4 network `TextBlock`s (down/up × left/right panel) in `MainWindow.xaml`. (Right-alignment was tried — it stranded dead space between arrow and value. Do NOT right-align.)
- Speed value box is FIXED per size via `SysMonitorSpeedWidth` (Small 60 / Medium 70 / Large 78 — compact; extreme max-digit strings may touch the edge for one tick, never jump). Do NOT use auto-width (row would jitter as speeds change).
- Right-side order (2026-09-29): originally monitors→gear; briefly swapped gear↔monitors on request, then swapped BACK (same request) — final: PasteAll → monitors (col 7) → Clock (col 8) → Plugins (col 9) → **gear last (col 10)** + divider rect with monitors. Only those two columns ever move; clock/plugins stay between.
- **PasteAll is the one deliberate exception to the 2px rule** (2026-09-30): its right margin is `6`, not `1` (`Margin="1,0,6,0"`). With the uniform 1+1 the accent button visually collided with the download pill — only ~2px of dark bar between them, and the bright accent made them read as touching. Now measures 12px of clear bar (accent ends x=1173, pill border x=1186 at Small). Left margin stays `1`. The button is `Collapsed` when nothing is selected, so this costs nothing the rest of the time. Do NOT "normalize" this back to 1 to satisfy the 2px rule above.

### D. Network arrows (PowerToys-style) — 12x18 canvas, stroke 1.0, per-size Viewbox (`SysMonitorArrowWidth/Height` resources, defaults in App.xaml)
- Down: `M6,2 L6,15 M0.1,11 L6,16 L11.9,11` · Up: `M6,16 L6,3 M0.1,7 L6,2 L11.9,7` — head 11.8w × 5h, both arrows span y 2→16. All 4 instances (left+right panels) identical; left panel previously had off-center variants — do NOT reintroduce them.

### E. Taskbar tone matching (System + transparency only; explicit Light/Dark keep fixed frosts)
- `BuildTaskbarMatchedBrush()`: wallpaper-average blend. Dark = avg×0.34 + `#222226`×0.66, alpha `0xF2`, channels clamped 0x16–0x4D (B to 0x55). Light = avg×0.12 + `#F7F7F7`×0.88, alpha `0xF4`, clamped 0xD8–0xFA/B. Tune blend/clamp if tone drifts — do NOT go back to flat fills in System mode.

### F. Everything launcher search — REMOVED 2026-09-30 (user request)
- Deleted: `Services/EverythingService.cs`, `Everything32.dll` (csproj Content+EmbeddedResource), all MainWindow popup/query/keyboard code, `Ctrl+;` summon hotkey (+`IHotkeyService` id overload), `ShowEverythingResults`/`EverythingTrigger` settings (interface/service/VM/XAML), Plugins settings section + nav tab, shelf plugin column (hard-collapsed), `LoadPlugins` body (early return).
- Also removed 2026-09-30: the app-level `ShowPlugins` setting (`Settings`, `ISettingsService`, `SettingsService` accessor, `SettingsViewModel` field+partial, `MainViewModel` field+`SyncSettings`+partial). Nothing bound it in XAML. Do NOT re-add.
- Still present (dead, invisible): `Plugins/PluginManager.cs` + `Plugins/PluginConfig.cs` and the collapsed `PluginBorder`/`PluginContainer` (grid col 9). `PluginsSettings.ShowPlugins` there is a **separate** class, not the app setting. `LoadPluginsAsync` is never called. Do NOT re-add file search without being asked.
