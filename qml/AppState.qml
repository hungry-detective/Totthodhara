// Global settings + theme. Single source of truth for appearance.
// Persisted to disk via the Settings element in Main.qml (portable INI
// under <exe>/data, next to history.db) — every property restored there
// survives restarts.
pragma Singleton

import QtQuick
import Totthodhara.Backend 1.0

QtObject {
    // Appearance (mirrors SettingsWindow sections)
    property string theme: "System"    // System | Light | Dark (System follows Windows)
    property string barSize: "Medium"    // Small | Medium | Large
    property string shelfPosition: "Bottom" // Top | Bottom
    property string alignment: "Center"   // Left | Center | Right
    property bool roundedCorners: true
    property bool transparentBar: true
    // Shelf color tone: "auto" (theme/taskbar neutral, default)
    // | "windows" (Windows Personalization accent) | "dark" | "grey"
    // | "light" | "blue" | "custom" (user-picked color).
    // Applies in System theme only; transparency slider drives its alpha,
    // intensity slider blends neutral → full tone.
    property string shelfTone: "auto"
    property real toneIntensity: 0.50
    // Custom picked tone (ColorDialog writes here, persisted as-is).
    property string customTone: "#9d4fd0"
    // Pill visibility style: "border" | "tint" | "hover" | "contrast" | "shadow" | "borderless"
    // ("gradient" retired → "border", "dot" retired → "borderless"; see restorePrefs).
    property string pillStyle: "tint"
    // Image thumbnail frame: "accent" | "none" | "shadow" | "line" | "glow"
    property string thumbStyle: "none"
    // Card pill behind the hardware/speed meter groups.
    property bool pillMeters: false

    // Behavior. alwaysOnTop + copyToDestination are CORE and forced on
    // at restore (their toggles were removed from Settings — users kept
    // breaking the app with them). Never re-add those rows.
    property bool alwaysOnTop: true
    property bool hideClipboard: false
    property bool copyToDestination: true
    property bool checkUpdatesOnStart: true
    property bool runAtStartup: false
    // Master glass amount, driven by the Bar opacity slider
    // (1 = solid, 0.05 = barely there). Frosted glass by default.
    property real glassAlpha: 0.80

    // Monitors / clocks (hardware and network toggle separately)
    property bool showCpuRam: true
    property bool showSpeed: true
    property bool showWorldClock: true
    // World clock zones: "" = device local. Persisted; pushed into SysMon.
    property string zoneA: "America/Phoenix"
    property string zoneB: "UTC"
    // Per-clock pills (master switch above gates the whole group).
    // Clock two ships off: one clock (Phoenix) is the default shelf.
    property bool showClock1: true
    property bool showClock2: false

    // Storage
    property int maxHistoryItems: 50
    property int maxFileSizeMB: 25
    property int autoCleanHours: 2

    // System follows the Windows app theme live (ThemeWatcher singleton).
    readonly property string effectiveTheme: theme === "System"
        ? (ThemeWatcher.systemIsDark ? "Dark" : "Light") : theme

    // Derived palette. System bar stays glassy (acrylic does the blur,
    // tint is light); System cards stay near-solid so text renders pure
    // white instead of washing out over the blur. Explicit Dark/Light
    // keep their solid frosts.
    readonly property color barBg: effectiveTheme === "Dark" ? "#202020" : "#f2f2f2"
    readonly property color cardBg: theme === "System"
        ? (effectiveTheme === "Dark" ? Qt.rgba(0.16, 0.16, 0.19, 0.88) : Qt.rgba(1, 1, 1, 0.72))
        : (effectiveTheme === "Dark" ? "#2d2d2d" : "#ffffff")
    readonly property color cardBorder: theme === "System"
        ? (effectiveTheme === "Dark" ? Qt.rgba(1, 1, 1, 0.28) : Qt.rgba(0, 0, 0, 0.28))
        : (effectiveTheme === "Dark" ? "#3d3d3d" : "#d4d4d4")
    readonly property color text: effectiveTheme === "Dark" ? "#ffffff" : "#1b1b1b"
    readonly property color muted: effectiveTheme === "Dark" ? "#a0a0a0" : "#616161"
    // Signature blue — except in System mode with the taskbar accent
    // switch on, where the shelf wears the Windows accent live
    // (Start-menu style: follows the user's picker + toggle).
    readonly property color accent: (theme === "System" && ThemeWatcher.colorPrevalence) ? ThemeWatcher.systemAccent : "#4cc2ff"
    // Opaque menu card: popup windows (gear/meter/clip menus) never go
    // translucent — with no real blur behind them it reads broken.
    readonly property color menuBg: effectiveTheme === "Dark" ? "#2b2b2b" : "#ffffff"
    readonly property color danger: "#ff5c5c"

    // System keeps a Windows-like airy translucency (the acrylic blur
    // underneath does the heavy lifting); Dark stays deep and solid-ish.
    // That keeps the two apart.
    // Glass lives ONLY in System mode (follows Windows): explicit Dark /
    // Light are solid signature looks, so hidden transparency settings can
    // never ghost-control them.
    // Explicit shelf tone base ("custom" = user-picked color).
    // "windows" wears the Personalization accent color live.
    readonly property color toneBase: shelfTone === "custom" ? customTone
        : shelfTone === "windows" ? ThemeWatcher.systemAccent
        : shelfTone === "dark" ? "#202020"
        : shelfTone === "grey" ? "#3a3a3d"
        : shelfTone === "light" ? "#f2f2f2"
        : shelfTone === "blue" ? "#274e7d" : "#000000"

    readonly property color barFill: {
        // Explicit tone, System theme only: intensity blends
        // neutral → tone, transparency slider drives its alpha
        // (toggle off means solid). Dark/Light keep signature looks.
        if (shelfTone !== "auto" && theme === "System") {
            const k = Math.max(0, Math.min(1, toneIntensity))
            const r = autoFill.r * (1 - k) + toneBase.r * k
            const g = autoFill.g * (1 - k) + toneBase.g * k
            const b = autoFill.b * (1 - k) + toneBase.b * k
            if (!transparentBar)
                return Qt.rgba(r, g, b, 1)
            return Qt.rgba(r, g, b, glassAlpha)
        }
        return autoFill
    }

    // Neutral base = what Auto paints (theme/taskbar path).
    readonly property color autoFill: {
        if (theme !== "System" || !transparentBar || !ThemeWatcher.transparencyOn)
            return barBg
        // Taskbar-style: mostly neutral body — the native acrylic underneath
        // already carries the DWM blue, so only a small accent pull is
        // needed. (Double-tinting reads flat blue next to the real
        // taskbar's dark blue body.) No pull at all while the taskbar
        // accent switch is off.
        const acc = ThemeWatcher.systemAccent
        const t = ThemeWatcher.colorPrevalence ? 0.10 : 0
        if (effectiveTheme === "Dark") {
            return Qt.rgba(0.22 * (1 - t) + acc.r * t,
                           0.22 * (1 - t) + acc.g * t,
                           0.24 * (1 - t) + acc.b * t, glassAlpha)
        }
        return Qt.rgba(0.953 * (1 - t) + acc.r * t,
                       0.953 * (1 - t) + acc.g * t,
                       0.953 * (1 - t) + acc.b * t, glassAlpha)
    }

    readonly property int barHeight: barSize === "Small" ? 26 : barSize === "Large" ? 34 : 28
    readonly property int cardHeight: barHeight - 5

    // WinUI-style glyphs (Segoe Fluent Icons ships with Windows 11).
    // NOTE: QML Font has no families/fallback-chain property — glyphs rely
    // on automatic system fallback, so prefer codepoints present in both
    // Segoe Fluent Icons and Segoe MDL2 Assets.
    readonly property string iconFont: "Segoe Fluent Icons"
    // Perfect pills (stadium geometry): radius is exactly half the height.
    // Bar 26/28/34 -> 13/14/17; cards 22/24/30 -> 11/12/15.
    // The rounded toggle is shelf-only: cards stay pill always.
    readonly property int barRadius: roundedCorners ? Math.round(barHeight / 2) : 0
    readonly property int cardRadius: Math.round(cardHeight / 2)
}
