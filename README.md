# Totthodhara

Totthodhara is a modern, high-performance clipboard manager designed with a floating, responsive UI. It silently runs in the system tray and allows you to quickly interact with your clipped items.

## Key Features

- **Floating Interface**: Beautiful, transparent "glass" window that automatically positions itself perfectly beside the Taskbar (or near your cursor).
- **SQLite Data Persistence**: All clips are stored locally inside a robust `.sqlite` database, meaning your data effortlessly survives reboots.
- **Smart Formatting**: Differentiates between text, links, and system clipboard elements seamlessly.
- **Advanced Context Menus**: Provides fluid UI elements to clear and interact with pinned items directly securely out of sight.
- **Global Hotkeys**: Instantly summon the clip vault with quick key combinations.
- **Right-click Delete Bubble**: Right-click any item to show a delete confirmation bubble above the shelf bar, centered on the item.
- **Ctrl+Click Delete**: Hold Ctrl and click any item to instantly delete it — faster than drag-to-delete.
- **Drag & Drop**: Drag items from the shelf directly into other applications (text editors, file explorers, image editors) to paste the content.
- **Drag Protection**: Dragging an item back onto the shelf does not create duplicates.
- **Shelf-to-Shelf Reordering**: Ctrl+drag items to reorder or remove them from the shelf with visual feedback.
- **Quick Search (Ctrl+F)**: Press the search button or Ctrl+F to open a search popup. Type to filter items instantly by text content, file name, or display title.
- **Pin to Top**: Pin important items to keep them at the top of the shelf, visually separated. Pinned items always appear before unpinned history.
- **History Size Limit**: Configurable maximum number of items (Settings > Max History Items). Oldest unpinned items are automatically trimmed when the limit is exceeded.
- **Multi-Paste Mode**: Toggle multi-paste mode (📋 button) to queue multiple items by clicking. Press "Paste All" to paste them all in sequence with one click.
- **Snippets**: Save frequently used text as permanent snippet items. Snippets have a ⭐ indicator and are never deleted by Clear History or auto-clean.
- **Smart Fullscreen Detection**: Automatically hides when watching videos or using fullscreen apps. Stays hidden during seeking/playback. Reappears when you switch away.
- **Original File Names**: Dragging or pasting files preserves their original names — no more GUID filenames in your emails or folders.
- **Image Paste Support**: Pasting image items works in WhatsApp, YouTube, and other apps that expect Bitmap clipboard data.
- **Fast Clear History**: Parallel deletion of stored files — instantly clears hundreds of items.
- **Portable Build**: Single-file release with no dependencies — just unzip and run.

## What's New (Recent Fixes)

- **Shelf–Taskbar Gap Eliminated**: Shelf now sits flush against the taskbar with no empty padding. Bottom corners are flat when positioned at the screen edge, and the shelf stacks correctly above the Windows taskbar using `FindWindow("Shell_TrayWnd")` detection.
- **Full-Screen Detection Restored**: Auto-hide when a fullscreen app (VLC, browser, games) covers the monitor. Immediate hide on detection (no 1.5 s delay). Shelf reappears when you switch away.
- **Network Speed Arrows Redesigned**: Replaced bold Unicode arrows with thin, rounded Path vector arrows (`StrokeThickness="1.2"`) matching a clean minimal style. Fixed-width (`48px`) text blocks prevent layout jumps during speed changes.
- **Combined Network Speed Card**: Upload and download combined into a single compact card instead of two separate rows.
- **Smaller Speed Text**: System monitor text size reduced to match regular icon size for a more balanced look.
- **Hardware Left Toggle Independent**: Hardware monitors can be positioned on the left independently of the network speed display.
- **Drag Threshold Increased (2→5 px)**: Prevents accidental drags when clicking items.
- **Pinned Clock Crash Fixed**: Debounced clock rebuild prevents crash when toggling pinned items.
- **Search Improvements**: Live filtering as you type, visible text cursor, dark-mode-compatible dropdown.
- **AmoledDark Theme Removed**: Simplified to Light and Dark themes.
- **Welcome Item Cleanup**: Welcome item only added on first run; cleared during "Clear History".
- **Default Settings Updated**: Hardware left, network right (default off), clock off.
- **CapsuleShape CornerRadius**: Dynamic per-position — rounded top + flat bottom (bottom shelf) or flat top + rounded bottom (top shelf).
- **AppBar Positioning Fixed**: Uses actual taskbar rect to position shelf directly above it; DPI-aware pixel calculations with `Math.Round`.

## Architecture

Built using modern **.NET 10 WPF**, and heavily structured around the **MVVM** pattern (`CommunityToolkit.Mvvm`).
The UI is styled using `WPF-UI` to provide native Microsoft Fluent Design System aesthetics.

## Critical Lessons Learned (AI Agent Reference)

> **READ THIS before modifying shelf positioning, AppBar logic, or visual styling.**
> These issues took hours of debugging. Do NOT repeat the same mistakes.

### 1. Shelf-to-Taskbar Gap — THE Hard Problem

**Symptom:** Visible desktop wallpaper / empty padding between the shelf and the Windows taskbar.

**Root causes (there were THREE, all had to be fixed):**

| # | Cause | Fix |
|---|-------|-----|
| 1 | **DropShadowEffect** on CapsuleBorder extends the render bounds ~20px BELOW the element. With `AllowsTransparency="True"`, this shadow renders in the transparent window area between the shelf and the taskbar. | **Remove DropShadowEffect from CapsuleBorder entirely.** Do NOT add it back. The shadow bleeds through the transparent window. |
| 2 | **BorderThickness="1"** draws a 1px border INSIDE the element. The semi-transparent border color at the bottom edge is visible as a gap line against the taskbar. | **Set BorderThickness="0".** Do NOT add it back without testing against the taskbar edge. |
| 3 | **CornerRadius on all 4 corners** creates transparent areas at the corners of the CapsuleBorder. With `AllowsTransparency="True"`, the transparent window background shows through. | **Use per-position CornerRadius:** bottom shelf = `(radius, radius, 0, 0)` (flat bottom), top shelf = `(0, 0, radius, radius)` (flat top). |

**What did NOT work (do not retry):**
- Setting `Left`/`Top` in WPF after `SetWindowPos` — causes DPI rounding re-layout, shifting window by 1-2px
- Deferring `Left`/`Top` via `Dispatcher.BeginInvoke` — same problem, delayed
- Forcing `abd.rc.top = 0` after `ABM_QUERYPOS` — broke the AppBar entirely, shelf collapsed to a line
- Removing `ABM_QUERYPOS` entirely — shelf disappeared
- Adding outer `Border` with `ClipToBounds="True"` wrapper — doesn't clip DropShadowEffect render bounds
- Setting Window `Background` to match `AppBackground` — breaks the transparent rounded corners visually

### 2. AppBar Positioning — How It Actually Works

**Flow:**
1. `RegisterAppBar()` calls `ABM_NEW` with NO rect (just registers the HWND as an AppBar)
2. `SetAppBarPos()` computes position and calls `ABM_QUERYPOS` then `ABM_SETPOS`
3. `SetWindowPos()` moves the HWND, then only WPF `Width`/`Height` are set (NOT `Left`/`Top`)

**Key:** The shelf must stack ABOVE the Windows taskbar, not at the screen edge. Use `FindWindow("Shell_TrayWnd")` + `GetWindowRect` to find the actual taskbar top, then set `abd.rc.bottom = taskbarTop` for bottom positioning.

**DPI:** Use `PresentationSource.CompositionTarget.TransformToDevice.M22` for DPI factor. Use `(int)Math.Round(value * dpiFactor)` — NOT `(int)(value * dpiFactor)` which truncates.

### 3. AllowsTransparency="True" — The Root of All Evil

This single property causes most visual issues:
- DropShadowEffect extends render bounds BEYOND the window
- BorderThickness creates visible lines against transparent background
- Rounded corners show transparent bleed-through
- Setting WPF `Left`/`Top` triggers re-layout that shifts the HWND

**Rule:** Keep the visual tree as simple as possible inside an `AllowsTransparency` window. No effects, no borders, no unnecessary decoration at the edges that touch screen boundaries.

### 4. Fullscreen Detection

The timer checks every 100ms. Two detection methods:
1. Window rect matches monitor bounds (within 1px)
2. Fallback: no WS_CAPTION + no WS_THICKFRAME + >95% screen width

Process ID check prevents false triggers from child windows. `Shell_TrayWnd`, `WorkerW`, `Progman` classes force-show the shelf.

### 5. P/Invoke Safety

All P/Invoke calls are declared as `static extern` in `MainWindow.xaml.cs`. Key ones:
- `SHAppBarMessage` — AppBar lifecycle
- `SetWindowPos` — HWND positioning
- `DwmSetWindowAttribute` — Acrylic + exclude-from-peek
- `AddClipboardFormatListener` — Clipboard monitoring
- `SetWindowsHookEx` / `UnhookWindowsHookEx` — Mouse hook for delete bubble
- `FindWindow` — Taskbar detection
- `GetWindowRect` — Window/taskbar position queries
- `SystemParametersInfo` — Work area queries

**NEVER remove a P/Invoke** — it may be used elsewhere or needed for future features.
