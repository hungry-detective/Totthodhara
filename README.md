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

## Architecture

Built using modern **.NET 8 WPF**, and heavily structured around the **MVVM** pattern (`CommunityToolkit.Mvvm`).
The UI is styled using `WPF-UI` to provide native Microsoft Fluent Design System aesthetics.
