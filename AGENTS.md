# Totthodhara — AI Context Preservation File

**DO NOT remove, modify, or delete any feature described below unless the user explicitly asks.**
**DO NOT change any working code to "improve" it unless asked.**
**DO NOT refactor working code.**
**DO NOT add new features unless asked.**
**Always preserve the exact behavior of drag-and-drop, click-to-paste, animations, and acrylic glass.**

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
- **DISABLED** — `EnableAcrylic()` is a no-op. Both modes use solid colors.
- `RootGrid.Background` is `Transparent` (window bg shows through).
- All theme colors are solid, no acrylic/blur in either mode.

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

### 12. Multi-Paste Mode
- Toggle button in toolbar (`IsMultiPasteMode`)
- In this mode, clicking items toggles their `IsSelected` state
- Check marks and blue accent background on selected items
- Paste All button pastes all selected sequentially

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

### 20. Search (Ctrl+F)
- Opens a popup TextBox with watermark "Search items..."
- Filters items by TextContent, FileName, DisplayTitle, DisplayText (case-insensitive)
- Shows result count badge
- Escape closes, Enter also closes
- Ctrl+F toggles (same key to close)

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
- **3 modes:** Light, Dark, System (reads registry)
- Sets 12 resource brushes: `AppBackground`, `TextColor`, `IconColor`, `CardBg`, `ControlBg`, `BorderColor`, `MenuBg`, `ToolTipBg`, `AccentColor`, `WindowBg`, `ShadowOpacity`, `ShadowColor`
- **DO NOT** remove any of the resource keys — each is used in XAML

**Light Mode Colors:**
- Capsule bar (AppBackground): `#FFFFFF` solid (no acrylic, no blur)
- CardBg: `#1A000000` (black 10% overlay — semi-transparent surface)
- ControlBg (hover): `#33000000` (black 20% overlay)
- TextColor: `#222222`
- IconColor: `#222222`
- BorderColor: `rgba(0,0,0,30)`
- AccentColor: `#0078D4`
- MenuBg: `#FAFAFA`
- ToolTipBg: `rgba(250,250,250,245)`
- ShadowOpacity: `0.2`
- ShadowColor: Black

**Dark Mode Colors:**
- Capsule bar (AppBackground): `#141414` solid (no acrylic, no blur)
- CardBg: `#1AFFFFFF` (white 10% overlay — semi-transparent surface)
- ControlBg (hover): `#33FFFFFF` (white 20% overlay)
- TextColor: White
- IconColor: White
- BorderColor: `rgba(255,255,255,40)`
- AccentColor: `#60CDFF`
- MenuBg: `#1E1E1E`
- ToolTipBg: `rgba(30,30,30,245)`
- ShadowOpacity: `0.45`
- ShadowColor: Black

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
