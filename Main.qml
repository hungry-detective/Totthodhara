// Floating shelf bar: full-width AppBar docked at the screen edge.
// Layout: hardware pill | scroll | cards | search | paste | net pill | gear.
// Rounded floating capsule; bar fills the window so the reserved AppBar
// rect matches the visible bar. Backend: AppBarService + FullscreenService.
import QtQuick
import QtQuick.Controls
import QtQuick.Layouts
import QtCore as QC
import Qt.labs.platform as Labs
import Totthodhara.Backend 1.0

Window {
    id: shelf
    visible: true
    width: Screen.width
    // Exact fit: the window IS the bar, so maximized apps stop flush at
    // its edge with no dead gap. No outer shadow (flat capsule).
    height: AppState.barHeight
    color: "transparent"
    title: "Totthodhara"

    flags: Qt.FramelessWindowHint | Qt.WindowStaysOnTopHint | Qt.Tool

    Component.onCompleted: {
        restorePrefs()
        placeShelf()
        // Dev convenience: Totthodhara.exe --settings opens Settings on start
        // (flag evaluated in C++; see main.cpp openSettingsOnStart; the
        // actual open is deferred via settingsOpener, never from here).
        fullScreen.start()
        refreshGlass()
        store.monitor(shelf)
        // Startup update check (silent unless an update actually exists).
        if (AppState.checkUpdatesOnStart) {
            autoUpdChecking = true
            Updater.checkForUpdates()
        }
    }

    // True only while the STARTUP check is in flight (manual About checks
    // drive their own UI and must not toast).
    property bool autoUpdChecking: false
    Connections {
        target: Updater
        function onCheckFinished(available, version, notes) {
            if (shelf.autoUpdChecking) {
                shelf.autoUpdChecking = false
                if (available)
                    store.toast("Update " + version + " available — see About")
            }
        }
    }

    ClipStore { id: store }

    // Settings persistence: restored from disk into AppState once on
    // start, saved back on every change (portable INI under <exe>/data,
    // next to history.db), so the user's picks survive restarts.
    // Plain properties + explicit copy: property ALIAS cannot target a
    // singleton (verified fatal: "Unable to find id AppState").
    QC.Settings {
        id: prefs
        category: "Shelf"
        property string theme: "System"
        property string barSize: "Medium"
        property string shelfPosition: "Bottom"
        property string alignment: "Center"
        property bool roundedCorners: true
        property bool transparentBar: true
        property string shelfTone: "auto"
        property real toneIntensity: 0.50
        property string customTone: "#9d4fd0"
        property string pillStyle: "tint"
        property string thumbStyle: "none"
        property bool pillMeters: false
        property bool alwaysOnTop: true
        property bool hideClipboard: false
        property bool copyToDestination: true
        property real glassAlpha: 0.80
        property bool showCpuRam: true
        property bool showSpeed: true
        property bool showWorldClock: true
        property string zoneA: "America/Phoenix"
        property string zoneB: "UTC"
        property bool showClock1: true
        property bool showClock2: false
        property int maxHistoryItems: 50
        property int maxFileSizeMB: 25
        property int autoCleanHours: 2
        property bool hwOnLeft: true
        property bool netOnLeft: false
        property string orderKeys: "clock,hw,net"
        property bool clockOnLeft: true
    }
    // Restore once: Settings values are already read from disk when
    // this runs (creation finishes before onCompleted).
    function restorePrefs() {
        AppState.theme = prefs.theme
        AppState.barSize = prefs.barSize
        AppState.shelfPosition = prefs.shelfPosition
        AppState.alignment = prefs.alignment
        AppState.roundedCorners = prefs.roundedCorners
        AppState.transparentBar = prefs.transparentBar
        AppState.shelfTone = prefs.shelfTone
        AppState.toneIntensity = prefs.toneIntensity
        AppState.customTone = prefs.customTone
        // Retired values fall back ("gradient"→"border", "dot"→its replacement).
        if (prefs.pillStyle === "gradient")
            AppState.pillStyle = "border"
        else if (prefs.pillStyle === "dot")
            AppState.pillStyle = "borderless"
        else
            AppState.pillStyle = prefs.pillStyle
        AppState.thumbStyle = prefs.thumbStyle
        AppState.pillMeters = prefs.pillMeters
        AppState.alwaysOnTop = prefs.alwaysOnTop
        AppState.hideClipboard = prefs.hideClipboard
        AppState.copyToDestination = prefs.copyToDestination
        AppState.glassAlpha = prefs.glassAlpha
        AppState.showCpuRam = prefs.showCpuRam
        AppState.showSpeed = prefs.showSpeed
        AppState.showWorldClock = prefs.showWorldClock
        AppState.zoneA = prefs.zoneA
        AppState.zoneB = prefs.zoneB
        AppState.showClock1 = prefs.showClock1
        AppState.showClock2 = prefs.showClock2
        // Zones live in the backend service (QML has no tz database).
        SysMon.zoneA = AppState.zoneA
        SysMon.zoneB = AppState.zoneB
        AppState.maxHistoryItems = prefs.maxHistoryItems
        AppState.maxFileSizeMB = prefs.maxFileSizeMB
        AppState.autoCleanHours = prefs.autoCleanHours
        shelf.hwOnLeft = prefs.hwOnLeft
        shelf.netOnLeft = prefs.netOnLeft
        shelf.orderKeys = prefs.orderKeys
        shelf.clockOnLeft = prefs.clockOnLeft
    }
    // Save-back: any AppState change flows into Settings (which persists).
    Connections {
        target: AppState
        function onThemeChanged() { prefs.theme = AppState.theme }
        function onBarSizeChanged() { prefs.barSize = AppState.barSize }
        function onShelfPositionChanged() { prefs.shelfPosition = AppState.shelfPosition }
        function onAlignmentChanged() { prefs.alignment = AppState.alignment }
        function onRoundedCornersChanged() { prefs.roundedCorners = AppState.roundedCorners }
        function onTransparentBarChanged() { prefs.transparentBar = AppState.transparentBar }
        function onShelfToneChanged() { prefs.shelfTone = AppState.shelfTone }
        function onToneIntensityChanged() { prefs.toneIntensity = AppState.toneIntensity }
        function onCustomToneChanged() { prefs.customTone = AppState.customTone }
        function onPillStyleChanged() { prefs.pillStyle = AppState.pillStyle }
        function onThumbStyleChanged() { prefs.thumbStyle = AppState.thumbStyle }
        function onPillMetersChanged() { prefs.pillMeters = AppState.pillMeters }
        function onAlwaysOnTopChanged() { prefs.alwaysOnTop = AppState.alwaysOnTop }
        function onHideClipboardChanged() { prefs.hideClipboard = AppState.hideClipboard }
        function onCopyToDestinationChanged() { prefs.copyToDestination = AppState.copyToDestination }
        function onGlassAlphaChanged() { prefs.glassAlpha = AppState.glassAlpha }
        function onShowCpuRamChanged() { prefs.showCpuRam = AppState.showCpuRam }
        function onShowSpeedChanged() { prefs.showSpeed = AppState.showSpeed }
        function onShowWorldClockChanged() { prefs.showWorldClock = AppState.showWorldClock }
        function onShowClock1Changed() { prefs.showClock1 = AppState.showClock1 }
        function onShowClock2Changed() { prefs.showClock2 = AppState.showClock2 }
        function onZoneAChanged() {
            prefs.zoneA = AppState.zoneA
            SysMon.zoneA = AppState.zoneA
        }
        function onZoneBChanged() {
            prefs.zoneB = AppState.zoneB
            SysMon.zoneB = AppState.zoneB
        }
        function onMaxHistoryItemsChanged() { prefs.maxHistoryItems = AppState.maxHistoryItems }
        function onMaxFileSizeMBChanged() { prefs.maxFileSizeMB = AppState.maxFileSizeMB }
        function onAutoCleanHoursChanged() { prefs.autoCleanHours = AppState.autoCleanHours }
    }
    // Save-back for meter arrangement (shelf-owned, not AppState).
    Connections {
        target: shelf
        function onHwOnLeftChanged() { prefs.hwOnLeft = shelf.hwOnLeft }
        function onNetOnLeftChanged() { prefs.netOnLeft = shelf.netOnLeft }
        function onOrderKeysChanged() { prefs.orderKeys = shelf.orderKeys }
        function onClockOnLeftChanged() { prefs.clockOnLeft = shelf.clockOnLeft }
    }

    // Startup settings (dev --settings): deferred past first polish.
    // show() issued inside onCompleted silently no-ops (verified twice:
    // no HWND, no error) — the native/show path needs a spinning loop.
    Timer {
        id: settingsOpener
        interval: 600
        running: openSettingsOnStart
        onTriggered: settingsWindow.open()
    }
    // Late glass settle: Qt finishes native window setup after first show,
    // which can stomp the DWM backdrop applied at startup (verified: type
    // reads back NONE without this). One deferred re-apply, then the
    // visible/theme watchers own it from there.
    Timer {
        id: glassSettle
        interval: 1500
        running: true
        repeat: false
        onTriggered: refreshGlass()
    }

    // Taskbar-like glass: real acrylic blur behind the translucent bar.
    // Re-tinted live when the OS flips Light/Dark under System theme.
    ThemeService { id: themeGlass }
    function refreshGlass() {
        themeGlass.applyGlass(shelf, AppState.transparentBar && AppState.theme === "System",
                              AppState.effectiveTheme === "Dark",
                              AppState.barRadius)
    }
    Connections {
        target: AppState
        function onThemeChanged() { refreshGlass() }
        function onTransparentBarChanged() { refreshGlass() }
        function onEffectiveThemeChanged() { refreshGlass() }
        function onBarRadiusChanged() { refreshGlass() }
        function onShelfPositionChanged() { placeShelf() }
    }
    Connections {
        target: ThemeWatcher
        function onSystemTintChanged() { refreshGlass() }
        function onTransparencyChanged() { refreshGlass() }
        function onColorPrevalenceChanged() { refreshGlass() }
    }

    // Reserve real screen space like the taskbar (AppBar docking).
    // Hidden shelf releases the space so other windows reclaim it.
    // Bottom docks above the taskbar; Top docks the screen top edge.
    AppBarService { id: appBar }
    function placeShelf() {
        shelf.x = 0
        if (AppState.shelfPosition === "Bottom") {
            shelf.y = Screen.height - shelf.height - 52   // above the taskbar
            if (shelf.visible)
                appBar.dock(shelf)
        } else {
            shelf.y = 0
            if (shelf.visible)
                appBar.dockTop(shelf)
            else
                appBar.undock()
        }
    }
    onVisibleChanged: {
        if (visible) {
            if (AppState.shelfPosition === "Bottom")
                appBar.dock(shelf)
            else
                appBar.dockTop(shelf)
            // Re-apply glass here too: at startup refreshGlass() can run
            // before the native HWND exists (no-op), leaving blur off until
            // the next theme toggle. Visible implies a valid HWND.
            refreshGlass()
        } else
            appBar.undock()
    }
    // Bar-size changes resize the window via binding; re-place (Bottom y
    // tracks height) and re-reserve so no gap (or overlap) is left behind.
    // The blur region follows the window size, so re-apply glass too.
    onHeightChanged: {
        if (visible) {
            placeShelf()
            appBar.refresh()
            refreshGlass()
        }
    }
    // Display changes resize the window width: same region refresh.
    onWidthChanged: {
        if (visible)
            refreshGlass()
    }
    onClosing: appBar.undock()

    // Fullscreen apps (video/players) auto-hide the shelf. Manual hides
    // via tray are remembered so auto-show never fights the user.
    property bool userHidden: false
    // Search field visibility: explicit flag (an invisible field can never
    // take focus, so activeFocus alone deadlocks the Ctrl+F entry).
    property bool searchOpen: false
    // Meter placement: hardware, network and clock groups each live LEFT
    // or RIGHT (drag a group across the shelf midpoint, or right-click it).
    // orderKeys is the full display priority (factory default mirrors a
    // real user setup: clock first): drop a group anywhere to splice it
    // into that rank on its side.
    property bool hwOnLeft: true
    property bool netOnLeft: false
    property string orderKeys: "clock,hw,net"
    // World clock group side (local + UTC pills, one draggable group).
    property bool clockOnLeft: true
    // Groups per side in display order. Slot loaders below render by
    // position, so order flips never touch the drag gesture.
    // Groups per side in display order: orderKeys priority filtered by
    // side + visibility. Slot loaders below render by position, so order
    // flips never touch the drag gesture. Unknown keys are ignored; a
    // visible group missing from a hand-edited list docks last.
    readonly property var leftGroups: {
        const all = shelf.orderKeys.split(",")
        const out = []
        for (const k of all) {
            if (k === "hw" && shelf.hwOnLeft && AppState.showCpuRam)
                out.push(k)
            else if (k === "net" && shelf.netOnLeft && AppState.showSpeed)
                out.push(k)
            else if (k === "clock" && shelf.clockOnLeft && AppState.showWorldClock)
                out.push(k)
        }
        if (shelf.hwOnLeft && AppState.showCpuRam && out.indexOf("hw") < 0)
            out.push("hw")
        if (shelf.netOnLeft && AppState.showSpeed && out.indexOf("net") < 0)
            out.push("net")
        if (shelf.clockOnLeft && AppState.showWorldClock && (AppState.showClock1 || AppState.showClock2) && out.indexOf("clock") < 0)
            out.push("clock")
        return out
    }
    readonly property var rightGroups: {
        const all = shelf.orderKeys.split(",")
        const out = []
        for (const k of all) {
            if (k === "hw" && !shelf.hwOnLeft && AppState.showCpuRam)
                out.push(k)
            else if (k === "net" && !shelf.netOnLeft && AppState.showSpeed)
                out.push(k)
            else if (k === "clock" && !shelf.clockOnLeft && AppState.showWorldClock)
                out.push(k)
        }
        if (!shelf.hwOnLeft && AppState.showCpuRam && out.indexOf("hw") < 0)
            out.push("hw")
        if (!shelf.netOnLeft && AppState.showSpeed && out.indexOf("net") < 0)
            out.push("net")
        if (!shelf.clockOnLeft && AppState.showWorldClock && (AppState.showClock1 || AppState.showClock2) && out.indexOf("clock") < 0)
            out.push("clock")
        return out
    }
    // Group name to its component (unknown/empty falls back to hw).
    // Shelf scope: slot loaders live outside meterDrag and cannot see
    // functions declared inside it.
    function compFor(group) {
        if (group === "net")
            return netMeters
        if (group === "clock")
            return clockMeters
        return hwMeters
    }
    FullscreenService {
        id: fullScreen
        onFullscreenChanged: (fs) => {
            if (fs)
                shelf.hide()
            else if (!shelf.userHidden)
                shelf.show()
        }
    }

    // Status toast above the bar.
    Rectangle {
        id: toast
        visible: false
        anchors.bottom: bar.top
        anchors.bottomMargin: 6
        anchors.horizontalCenter: parent.horizontalCenter
        width: toastText.width + 24
        height: 28
        radius: 14
        color: AppState.cardBg
        border.color: AppState.accent
        Text {
            id: toastText
            anchors.centerIn: parent
            color: AppState.text
            font.pixelSize: 12
        }
        Timer {
            id: toastTimer
            interval: 1400
            onTriggered: toast.visible = false
        }
    }
    Connections {
        target: store
        function onToast(message) {
            toastText.text = message
            toast.visible = true
            toastTimer.restart()
        }
    }

    // The bar: fully rounded floating capsule, no border (the edge against
    // the taskbar must stay perfectly clean, no strip line). No shadow.
    Rectangle {
        id: bar
        anchors.left: parent.left
        anchors.right: parent.right
        anchors.bottom: parent.bottom
        height: AppState.barHeight
        radius: AppState.barRadius
        // Transparent frost follows the theme (System = airy Windows tone,
        // Dark/Light = deeper frosts). See AppState.barFill.
        color: AppState.barFill
    }

    // Popup height: menus/previews open ABOVE the bar at the bottom edge
    // and BELOW it at the top edge (above would leave the screen).
    function popupY(h) {
        return AppState.shelfPosition === "Top" ? shelf.y + shelf.height + 8 : shelf.y - h - 8
    }

    // Shelf is docked (AppBar): not draggable. Meters move instead —
    // drag the CPU/RAM or network group left/right to arrange sides.

    RowLayout {
        anchors.fill: bar
        anchors.leftMargin: 7
        anchors.rightMargin: 7
        // Air between sections (meters | strip | gear) so scrolled cards
        // never kiss the meter pills — the fade alone reads as a smudge
        // when they touch.
        spacing: 8

        // System meters: hardware (CPU/RAM) and network (up/down) groups.
        // Each group lives LEFT or RIGHT (right-click a meter to move it).
        // Value texts have FIXED widths so changing digits never shift
        // neighboring icons. Fed live by the SysMon C++ singleton.
        // Center spacers: when the clipboard hides, the meters cluster
        // per Shelf alignment (Left docks both left, Right both right,
        // Center shares the space) instead of stranding at the edges.
        Item {
            visible: AppState.hideClipboard && AppState.alignment !== "Left"
            Layout.fillWidth: true
        }
        RowLayout {
            id: leftMeters
            visible: shelf.leftGroups.length > 0
            // Same height as the item pills so icons never dwarf/outgrow cards.
            Layout.preferredHeight: AppState.cardHeight
            Layout.alignment: Qt.AlignVCenter
            spacing: 6
            // Slot loaders render by POSITION (not group): slot N shows
            // leftGroups[N]. Order flips rebuild the slot items —
            // safe mid-drag, the overlay owns the gesture.
            Loader {
                id: leftSlot0
                Layout.fillHeight: true
                visible: active
                active: shelf.leftGroups.length > 0
                sourceComponent: compFor(shelf.leftGroups.length > 0 ? shelf.leftGroups[0] : "")
            }
            Loader {
                id: leftSlot1
                Layout.fillHeight: true
                visible: active
                active: shelf.leftGroups.length > 1
                sourceComponent: compFor(shelf.leftGroups.length > 1 ? shelf.leftGroups[1] : "")
            }
            Loader {
                id: leftSlot2
                Layout.fillHeight: true
                visible: active
                active: shelf.leftGroups.length > 2
                sourceComponent: compFor(shelf.leftGroups.length > 2 ? shelf.leftGroups[2] : "")
            }
        }

        // Clip strip: collapses fully in toolbar-only mode so no dead
        // middle splits the meter groups apart.
        // Chevron gutter: fixed 30px slot beside the strip, reserved while
        // the strip can scroll (stable width mid-scroll — no jumps), so
        // cards never slide behind the button itself.
        Item {
            Layout.preferredWidth: 30
            Layout.preferredHeight: AppState.cardHeight
            Layout.alignment: Qt.AlignVCenter
            visible: !AppState.hideClipboard
            Rectangle {
                width: 22
                height: 22
                radius: 11
                anchors.centerIn: parent
                // Same hover lift as the clip cards: grow + accent glow.
                scale: chevHoverL.hovered ? 1.08 : 1
                Behavior on scale {
                    NumberAnimation { duration: 130; easing.type: Easing.OutCubic }
                }
                // Hover glow beneath, like the card lift bloom.
                Rectangle {
                    anchors.fill: parent
                    radius: 11
                    color: AppState.accent
                    opacity: chevHoverL.hovered ? 0.15 : 0
                    z: -1
                    Behavior on opacity {
                        NumberAnimation { duration: 150 }
                    }
                }
                // Hover wash follows the card language too.
                Rectangle {
                    anchors.fill: parent
                    radius: 11
                    color: AppState.accent
                    opacity: chevHoverL.hovered ? 0.12 : 0
                    Behavior on opacity {
                        NumberAnimation { duration: 150 }
                    }
                }
                HoverHandler { id: chevHoverL }
                // Always shown; dimmed when this side can't scroll.
                opacity: cards.contentX > 4 ? 1 : 0.35
                Behavior on opacity {
                    NumberAnimation { duration: 150 }
                }
                // Fill/border follow the selected clipboard item style.
                color: (AppState.pillStyle === "tint" || AppState.pillStyle === "borderless") ? Qt.rgba(AppState.accent.r, AppState.accent.g, AppState.accent.b, 0.08)
                    : AppState.pillStyle === "contrast" ? AppState.cardBg : "transparent"
                border.color: AppState.pillStyle === "border" ? AppState.accent : AppState.cardBorder
                border.width: AppState.pillStyle === "borderless" ? 0 : 1
                Text {
                    anchors.centerIn: parent
                    text: "\uE76B"
                    font.family: AppState.iconFont
                    font.pixelSize: 12
                    color: AppState.text
                }
                MouseArea {
                    anchors.fill: parent
                    enabled: cards.contentX > 4
                    onClicked: {
                        const step = Math.max(120, cards.width * 0.6)
                        stripScrollAnim.to = Math.max(cards.originX, cards.contentX - step)
                        stripScrollAnim.restart()
                    }
                }
            }
        }
        Item {
            visible: !AppState.hideClipboard
            Layout.fillWidth: true
            Layout.preferredHeight: AppState.cardHeight
            Layout.alignment: Qt.AlignVCenter

            ListView {
                id: cards
                visible: !AppState.hideClipboard
                anchors.fill: parent
            orientation: ListView.Horizontal
            spacing: 6
            clip: true
            model: store.view
            // Strip alignment: Left stacks from the chevron, Center pads the
            // header so a short row sits centered, Right pads it flush to
            // the chevron — model order never changes (no RTL mirroring).
            // Deliberately NO highlight range: it fights scrolling.
            // End spacers double as the alignment pads (see padHead).
            header: Item { width: padHead; height: 1 }
            footer: Item { width: 20; height: 1 }

            // Mouse wheel / touchpad scrolls the shelf sideways.
            WheelHandler {
                acceptedDevices: PointerDevice.Mouse | PointerDevice.TouchPad
                onWheel: (event) => {
                    if (event.modifiers !== Qt.NoModifier)
                        return
                    const delta = event.pixelDelta.y !== 0 ? event.pixelDelta.y : event.angleDelta.y
                    const lo = cards.originX
                    const hi = Math.max(lo, lo + cards.contentWidth - cards.width)
                    cards.contentX = Math.max(lo, Math.min(hi, cards.contentX - delta))
                    event.accepted = true
                }
            }
            delegate: ClipCard {
                id: clip
                title: model.title
                kind: model.kind
                detail: model.detail
                icon: model.icon
                pinned: model.pinned
                snippet: model.snippet
                selected: model.selected
                cardIndex: model.pos
                viewIndex: index
                storeRef: store
                listRef: cards
                // Actions use the delegate's attached `index` (view row),
                // never model.index (indistinguishable from data roles).
                onClicked: (shift, ctrl) => {
                    clip.pulse()
                    store.clickItem(index, shift, ctrl)
                }
                onOpenUrl: store.openUrl(index)
                // Menu acts by stable item id, not view row: the menu lives
                // in its own window while the shelf keeps updating, so a
                // captured row would hit the wrong item (or nothing) after
                // any refresh. A vanished item reports and does nothing.
                onMenuRequested: (item) => {
                    const pt = item.mapToItem(shelf.contentItem, 0, 0)
                    clipMenu.showFor(
                        model.pinned ? "Unpin" : "Pin",
                        model.snippet ? "Remove snippet" : "Save as snippet",
                        model.added,
                        (action, added) => {
                            const idx = store.indexOfAdded(added)
                            if (idx < 0) {
                                store.toast("Item is gone")
                                return
                            }
                            if (action === 0)
                                store.togglePin(idx)
                            else if (action === 1)
                                store.toggleSnippet(idx)
                            else
                                store.deleteItem(idx)
                        },
                        shelf.x + pt.x + item.width / 2, shelf.y)
                }
                onPreviewRequested: (item) => {
                    // TEMP-DIAG: proves what arrives at the popup (revert).
                    store.toast("pv:" + item.detail.split("\n").length + "L/" + item.detail.length + "ch")
                    const pt = item.mapToItem(shelf.contentItem, 0, 0)
                    const cx = shelf.x + pt.x + item.width / 2
                    if (item.kind === "image") {
                        imagePreview.showFor(item.detail, cx, shelf.y)
                    } else if (item.kind === "file") {
                        const name = item.detail.split("/").pop().split("?")[0]
                        const ext = name.includes(".") ? name.split(".").pop().toUpperCase() : "FILE"
                        let glyph = "\uE7C3"
                        let label = ext + " file"
                        if (["MP4", "AVI", "MKV", "MOV", "WMV"].indexOf(ext) >= 0) {
                            glyph = "\uE714"
                            label = "Video file"
                        } else if (["MP3", "WAV", "WMA", "M4A", "OGG", "FLAC", "AAC"].indexOf(ext) >= 0) {
                            glyph = "\uE767"
                            label = "Audio file"
                        } else if (ext === "PDF") {
                            label = "PDF document"
                        }
                        imagePreview.showFile(glyph, item.title, name + "  •  " + label, cx, shelf.y)
                    } else {
                        imagePreview.showText(item.detail, cx, shelf.y)
                    }
                }
                onPreviewHidden: imagePreview.hide()
                onIconFailed: store.resetIcon(index)
            }

            // New arrivals slide in, removals fade out (quick, subtle).
            add: Transition {
                NumberAnimation { properties: "x"; from: -32; duration: 200; easing.type: Easing.OutCubic }
                NumberAnimation { property: "opacity"; from: 0; to: 1; duration: 200 }
            }
            addDisplaced: Transition {
                NumberAnimation { properties: "x"; duration: 200; easing.type: Easing.OutCubic }
            }
            remove: Transition {
                NumberAnimation { property: "opacity"; to: 0; duration: 160 }
            }

            // Edge fades: cards dissolve instead of clipping hard at the
            // chevron buttons (same trick as the WPF fade masks). Each fade
            // only shows while content is actually clipped on its side —
            // at rest item 1 (and the last card) stay crisp, not veiled.
            Rectangle {
                width: 40
                anchors.left: parent.left
                anchors.top: parent.top
                anchors.bottom: parent.bottom
                z: 2
                opacity: cards.contentX > 4 ? 1 : 0
                Behavior on opacity {
                    NumberAnimation { duration: 150 }
                }
                gradient: Gradient {
                    orientation: Gradient.Horizontal
                    GradientStop { position: 0.0; color: bar.color }
                    GradientStop { position: 1.0; color: "transparent" }
                }
            }
            Rectangle {
                width: 40
                anchors.right: parent.right
                anchors.top: parent.top
                anchors.bottom: parent.bottom
                z: 2
                opacity: cards.contentX < cards.originX + cards.contentWidth - cards.width - 4 ? 1 : 0
                Behavior on opacity {
                    NumberAnimation { duration: 150 }
                }
                gradient: Gradient {
                    orientation: Gradient.Horizontal
                    GradientStop { position: 0.0; color: "transparent" }
                    GradientStop { position: 1.0; color: bar.color }
                }
            }
            // Chevron steppers live in 30px gutters beside the strip (below),
            // so cards never slide behind the buttons themselves.
            NumberAnimation {
                id: stripScrollAnim
                target: cards
                property: "contentX"
                duration: 220
                easing.type: Easing.OutCubic
            }
        }
        }
        Item {
            Layout.preferredWidth: 30
            Layout.preferredHeight: AppState.cardHeight
            Layout.alignment: Qt.AlignVCenter
            visible: !AppState.hideClipboard
            Rectangle {
                width: 22
                height: 22
                radius: 11
                anchors.centerIn: parent
                // Same hover lift as the clip cards: grow + accent glow.
                scale: chevHoverR.hovered ? 1.08 : 1
                Behavior on scale {
                    NumberAnimation { duration: 130; easing.type: Easing.OutCubic }
                }
                Rectangle {
                    anchors.fill: parent
                    radius: 11
                    color: AppState.accent
                    opacity: chevHoverR.hovered ? 0.15 : 0
                    z: -1
                    Behavior on opacity {
                        NumberAnimation { duration: 150 }
                    }
                }
                Rectangle {
                    anchors.fill: parent
                    radius: 11
                    color: AppState.accent
                    opacity: chevHoverR.hovered ? 0.12 : 0
                    Behavior on opacity {
                        NumberAnimation { duration: 150 }
                    }
                }
                HoverHandler { id: chevHoverR }
                // Always shown; dimmed when this side can't scroll.
                opacity: cards.contentX < cards.originX + cards.contentWidth - cards.width - 4 ? 1 : 0.35
                Behavior on opacity {
                    NumberAnimation { duration: 150 }
                }
                // Fill/border follow the selected clipboard item style.
                color: (AppState.pillStyle === "tint" || AppState.pillStyle === "borderless") ? Qt.rgba(AppState.accent.r, AppState.accent.g, AppState.accent.b, 0.08)
                    : AppState.pillStyle === "contrast" ? AppState.cardBg : "transparent"
                border.color: AppState.pillStyle === "border" ? AppState.accent : AppState.cardBorder
                border.width: AppState.pillStyle === "borderless" ? 0 : 1
                Text {
                    anchors.centerIn: parent
                    text: ""
                    font.family: AppState.iconFont
                    font.pixelSize: 12
                    color: AppState.text
                }
                MouseArea {
                    anchors.fill: parent
                    enabled: cards.contentX < cards.originX + cards.contentWidth - cards.width - 4
                    onClicked: {
                        const lo = cards.originX
                        const hi = Math.max(lo, lo + cards.contentWidth - cards.width)
                        const step = Math.max(120, cards.width * 0.6)
                        stripScrollAnim.to = Math.max(lo, Math.min(hi, cards.contentX + step))
                        stripScrollAnim.restart()
                    }
                }
            }
        }

        // Search field (no shelf button; Ctrl+F or gear menu focuses it).
        // Height follows the bar so Small never overflows. Fully themed
        // (rounded like the pills) so no square native frame ever shows.
        // searchOpen gates visibility explicitly: an invisible field can
        // never take focus, so relying on activeFocus alone deadlocks.
        TextField {
            id: searchBox
            visible: shelf.searchOpen || searchBox.activeFocus || store.searchText !== ""
            Layout.preferredWidth: 130
            Layout.preferredHeight: AppState.cardHeight
            Layout.alignment: Qt.AlignVCenter
            placeholderText: "Search…"
            placeholderTextColor: AppState.muted
            color: AppState.text
            selectionColor: AppState.accent
            selectedTextColor: "white"
            text: store.searchText
            onTextChanged: store.searchText = text
            onActiveFocusChanged: {
                if (!activeFocus && text === "")
                    shelf.searchOpen = false
            }
            background: Rectangle {
                radius: 8
                color: AppState.cardBg
                border.color: searchBox.activeFocus ? AppState.accent : AppState.cardBorder
                border.width: 1
            }
            Keys.onEscapePressed: {
                store.searchText = ""
                searchBox.focus = false
                shelf.searchOpen = false
            }
        }
        // Store-driven text sync (typing destroys the text binding above,
        // so external clears like Esc would otherwise leave stale text).
        Connections {
            target: store
            function onSearchTextChanged() {
                if (searchBox.text !== store.searchText)
                    searchBox.text = store.searchText
            }
        }
        Shortcut {
            sequence: "Ctrl+F"
            onActivated: {
                shelf.searchOpen = true
                searchBox.forceActiveFocus()
            }
        }

        Button {
            visible: store.selectedCount() > 1 && !AppState.hideClipboard
            text: "Paste All"
            onClicked: store.pasteAll()
            Layout.preferredHeight: AppState.cardHeight
            Layout.alignment: Qt.AlignVCenter
            background: Rectangle {
                implicitHeight: AppState.cardHeight
                radius: 8
                color: parent.hovered || parent.pressed ? Qt.lighter(AppState.accent, 1.1) : AppState.accent
            }
            contentItem: Label {
                text: parent.text
                font.bold: true
                font.pixelSize: 12
                color: "white"
                horizontalAlignment: Text.AlignHCenter
                verticalAlignment: Text.AlignVCenter
            }
        }
        Button {
            visible: store.selectedCount() > 0 && !AppState.hideClipboard
            text: "Unselect all"
            onClicked: {
                store.clearSelection()
                store.toast("Selection cleared")
            }
            Layout.preferredHeight: AppState.cardHeight
            Layout.alignment: Qt.AlignVCenter
            background: Rectangle {
                implicitHeight: AppState.cardHeight
                radius: 8
                color: AppState.cardBg
                border.color: AppState.cardBorder
            }
            contentItem: Label {
                text: parent.text
                font.bold: true
                font.pixelSize: 12
                color: AppState.text
                horizontalAlignment: Text.AlignHCenter
                verticalAlignment: Text.AlignVCenter
            }
        }

        // (Network now lives in the combined pill on the left.)

        RowLayout {
            id: rightMeters
            visible: shelf.rightGroups.length > 0
            Layout.preferredHeight: AppState.cardHeight
            Layout.alignment: Qt.AlignVCenter
            spacing: 6
            Loader {
                id: rightSlot0
                Layout.fillHeight: true
                visible: active
                active: shelf.rightGroups.length > 0
                sourceComponent: compFor(shelf.rightGroups.length > 0 ? shelf.rightGroups[0] : "")
            }
            Loader {
                id: rightSlot1
                Layout.fillHeight: true
                visible: active
                active: shelf.rightGroups.length > 1
                sourceComponent: compFor(shelf.rightGroups.length > 1 ? shelf.rightGroups[1] : "")
            }
            Loader {
                id: rightSlot2
                Layout.fillHeight: true
                visible: active
                active: shelf.rightGroups.length > 2
                sourceComponent: compFor(shelf.rightGroups.length > 2 ? shelf.rightGroups[2] : "")
            }
        }

        Item {
            visible: AppState.hideClipboard && AppState.alignment !== "Right"
            Layout.fillWidth: true
        }

        ShelfButton {
            id: gearButton
            glyph: ""
            glyphSize: 16
            onClicked: gearPopup.toggle()
        }
    }

    // Strip alignment pads: in Center/Right modes the header absorbs the
    // free space (half for Center, all of it for Right) so the strip docks
    // accordingly; Left keeps 4px. Footer stays fixed. Imperative only,
    // hysteresis-guarded, coalesced — never bound to contentWidth, so no
    // binding loop is possible. Overflow strips scroll freely: no highlight
    // range, no currentIndex enforcement, nothing fights the flick.
    property real padHead: 4
    // Calls coalesce through callLater: a model rebuild fires N
    // contentWidth signals in one frame, and reacting to each one bursts
    // evaluation (trips "binding loop detected" false positives). One
    // deferred run sees final numbers and converges at once.
    function requestRecenter() { Qt.callLater(recenter) }
    function recenter() {
        let target = 12
        if (AppState.alignment !== "Left") {
            // Items width excludes the header pad and the fixed footer.
            const itemsW = cards.contentWidth - padHead - 20
            if (AppState.alignment === "Right")
                target = Math.max(12, cards.width - itemsW - 20)
            else
                target = Math.max(12, (cards.width - itemsW - 20) / 2)
        }
        if (Math.abs(target - padHead) > 0.5)
            padHead = target
    }
    Connections {
        target: AppState
        function onAlignmentChanged() { recenter() }
    }
    Connections {
        target: cards
        // contentWidth settles after layout (count changes first with stale
        // numbers), so this is the trigger that actually sees final values.
        function onContentWidthChanged() { requestRecenter() }
    }

    // Meter drag manager: full-bar overlay ABOVE the content row. It owns
    // the gesture from outside the Loaders, so flipping a group mid-press
    // never destroys the drag — the shelf rearranges LIVE under the cursor.
    // Every press outside a meter group is rejected, so cards, chevrons,
    // search and gear keep working untouched.
    MouseArea {
        id: meterDrag
        anchors.fill: bar
        acceptedButtons: Qt.LeftButton
        hoverEnabled: false
        property string group: ""
        property point pressG: Qt.point(0, 0)
        property bool dragging: false
        // Live dragged chip (tracks side flips): the dimmed original
        // keys off identity, so it never sticks to a dead instance.
        property var dragItem: null
        // Dragged chip width + its press-time slot origin (contentItem
        // coords): sizes and anchors the floating ghost.
        property real dragW: 0
        property point slotAbs: Qt.point(0, 0)

        // Slot loaders render by POSITION, so resolve a group to whichever
        // loader currently hosts it (null when that group is hidden).
        // (Components resolve via shelf.compFor — sibling scope.)
        function slotLoader(group, left) {
            const groups = left ? shelf.leftGroups : shelf.rightGroups
            const pos = groups.indexOf(group)
            if (pos < 0)
                return null
            if (left) {
                if (pos === 0)
                    return leftSlot0
                return pos === 1 ? leftSlot1 : leftSlot2
            }
            if (pos === 0)
                return rightSlot0
            return pos === 1 ? rightSlot1 : rightSlot2
        }
        function activeItem(group) {
            const left = group === "hw" ? shelf.hwOnLeft : group === "net" ? shelf.netOnLeft : shelf.clockOnLeft
            const l = slotLoader(group, left)
            return l ? l.item : null
        }
        function hitTest(gx, gy) {
            if (AppState.hideClipboard)
                return ""
            for (let gi = 0; gi < 3; gi++) {
                const g = gi === 0 ? "hw" : gi === 1 ? "net" : "clock"
                const it = activeItem(g)
                if (!it)
                    continue
                const p = it.mapToItem(shelf.contentItem, 0, 0)
                if (gx >= p.x - 4 && gx <= p.x + it.width + 4
                    && gy >= p.y - 10 && gy <= p.y + it.height + 10)
                    return g
            }
            return ""
        }

        onPressed: (mouse) => {
            const g = mapToItem(shelf.contentItem, mouse.x, mouse.y)
            const hit = hitTest(g.x, g.y)
            if (hit === "") {
                mouse.accepted = false
                return
            }
            group = hit
            pressG = g
            dragging = false
            dragItem = activeItem(hit)
            if (dragItem) {
                dragW = dragItem.implicitWidth
                slotAbs = dragItem.mapToItem(shelf.contentItem, 0, 0)
            }
        }
        onPositionChanged: (mouse) => {
            if (group === "" || !(pressedButtons & Qt.LeftButton))
                return
            const cur = mapToItem(shelf.contentItem, mouse.x, mouse.y)
            if (!dragging) {
                const dx0 = cur.x - pressG.x
                const dy0 = cur.y - pressG.y
                if (Math.sqrt(dx0 * dx0 + dy0 * dy0) < 10)
                    return
                dragging = true
            }
            const left = cur.x < shelf.width / 2
            // LIVE rearrange: flip the side the moment the cursor crosses
            // the midpoint — the shelf itself is the feedback, no badge.
            if (group === "hw")
                shelf.hwOnLeft = left
            else if (group === "net")
                shelf.netOnLeft = left
            else
                shelf.clockOnLeft = left
            // Free order: dropping left/right of a sibling's center splices
            // this group into that rank on its side (10px hysteresis so it
            // never flaps at a boundary). Slot items rebuild around it —
            // safe, the overlay owns the gesture and the ghost is
            // independent. Works for all three groups on either side.
            if (dragging) {
                const myLeft = group === "hw" ? shelf.hwOnLeft : group === "net" ? shelf.netOnLeft : shelf.clockOnLeft
                const mine = myLeft ? shelf.leftGroups : shelf.rightGroups
                // Sibling centers on this side (skip while any is mid-rebuild
                // from the side flip above: a partial map would yank order).
                const seq = []
                for (const k of mine) {
                    if (k === group)
                        continue
                    const it = activeItem(k)
                    if (!it)
                        continue
                    seq.push([k, it.mapToItem(shelf.contentItem, it.width / 2, 0).x])
                }
                if (seq.length >= mine.length - 1) {
                    let rank = 0
                    let hold = false
                    for (const s of seq) {
                        if (cur.x > s[1] + 10)
                            rank++
                        else if (cur.x > s[1] - 10) {
                            hold = true
                            break
                        }
                    }
                    if (!hold) {
                        const keys = shelf.orderKeys.split(",")
                        const here = keys.indexOf(group)
                        if (here >= 0)
                            keys.splice(here, 1)
                        let seen = 0
                        let at = keys.length
                        for (let i = 0; i <= keys.length; i++) {
                            if (seen === rank) {
                                at = i
                                break
                            }
                            if (i < keys.length && seq.some((s) => s[0] === keys[i]))
                                seen++
                        }
                        keys.splice(at, 0, group)
                        const next = keys.join(",")
                        if (next !== shelf.orderKeys)
                            shelf.orderKeys = next
                    }
                }
            }
            // Track side flips: the live chip instance changes when the
            // group rearranges under the cursor.
            dragItem = activeItem(group)
            // Ghost follows the cursor exactly (overlay coords are the
            // same coords as the drag math: zero offset error, no lag).
            meterGhost.x = slotAbs.x + (cur.x - pressG.x)
            meterGhost.y = slotAbs.y + (cur.y - pressG.y)
        }
        onReleased: {
            group = ""
            dragging = false
            dragItem = null
        }
        onCanceled: {
            group = ""
            dragging = false
            dragItem = null
        }
    }

    // Floating drag ghost: an overlay copy of the dragged meter group
    // that follows the cursor exactly (same coords as the drag math, no
    // layout writes, no animation lag). The real chip stays visible but
    // dimmed in its slot; the ghost carries the drop shadow.
    Item {
        id: meterGhost
        visible: meterDrag.dragging
        width: meterDrag.dragW
        height: AppState.cardHeight
        Rectangle {
            x: 2
            y: 3
            width: parent.width
            height: parent.height
            radius: AppState.cardRadius
            color: "#000000"
            opacity: 0.4
        }
        Rectangle {
            anchors.fill: parent
            radius: AppState.cardRadius
            color: AppState.cardBg
            border.color: AppState.cardBorder
            border.width: 1
            visible: AppState.pillMeters
        }
        RowLayout {
            visible: meterDrag.group === "hw"
            anchors.fill: parent
            anchors.leftMargin: AppState.pillMeters ? 10 : 0
            anchors.rightMargin: AppState.pillMeters ? 10 : 0
            spacing: 4
            MeterIcon {
                kind: "cpu"
                Layout.alignment: Qt.AlignVCenter
            }
            Text {
                text: SysMon.cpuText
                font.pixelSize: 13
                color: AppState.text
                Layout.alignment: Qt.AlignVCenter
                Layout.preferredWidth: 32
            }
            MeterIcon {
                kind: "ram"
                Layout.alignment: Qt.AlignVCenter
            }
            Text {
                text: SysMon.memText
                font.pixelSize: 13
                color: AppState.text
                Layout.alignment: Qt.AlignVCenter
                Layout.preferredWidth: 28
            }
        }
        RowLayout {
            visible: meterDrag.group === "clock"
            anchors.fill: parent
            anchors.leftMargin: AppState.pillMeters ? 10 : 0
            anchors.rightMargin: AppState.pillMeters ? 10 : 0
            spacing: 4
            MeterIcon {
                kind: "clock"
                visible: AppState.showClock1 || AppState.showClock2
                Layout.alignment: Qt.AlignVCenter
            }
            Text {
                text: SysMon.zoneATag
                visible: AppState.showClock1
                font.pixelSize: 12
                color: AppState.text
                Layout.alignment: Qt.AlignVCenter
                Layout.preferredWidth: 30
            }
            Text {
                text: SysMon.zoneAText
                visible: AppState.showClock1
                font.pixelSize: 13
                color: AppState.text
                Layout.alignment: Qt.AlignVCenter
                Layout.preferredWidth: 58
            }
            MeterIcon {
                kind: "clock"
                visible: AppState.showClock2
                Layout.alignment: Qt.AlignVCenter
            }
            Text {
                text: SysMon.zoneBTag
                visible: AppState.showClock2
                font.pixelSize: 12
                color: AppState.text
                Layout.alignment: Qt.AlignVCenter
                Layout.preferredWidth: 30
            }
            Text {
                text: SysMon.zoneBText
                visible: AppState.showClock2
                font.pixelSize: 13
                color: AppState.text
                Layout.alignment: Qt.AlignVCenter
                Layout.preferredWidth: 58
            }
        }
        RowLayout {
            visible: meterDrag.group === "net"
            anchors.fill: parent
            anchors.leftMargin: AppState.pillMeters ? 10 : 0
            anchors.rightMargin: AppState.pillMeters ? 10 : 0
            spacing: 4
            MeterIcon {
                kind: "up"
                Layout.alignment: Qt.AlignVCenter
            }
            Text {
                text: SysMon.upText
                font.pixelSize: 14
                color: AppState.text
                Layout.alignment: Qt.AlignVCenter
                Layout.preferredWidth: 62
            }
            MeterIcon {
                kind: "down"
                Layout.alignment: Qt.AlignVCenter
            }
            Text {
                text: SysMon.downText
                font.pixelSize: 14
                color: AppState.text
                Layout.alignment: Qt.AlignVCenter
                Layout.preferredWidth: 62
            }
        }
    }

    // Meter groups (instantiated left and/or right via Loaders above).
    // Drag a group across the shelf midpoint to dock it left/right.
    // Right-click opens the move menu (own window, escapes the shelf).
    Component {
        id: hwMeters
        // Wrapper Item: gives the drag MouseArea a real rect. (A MouseArea
        // placed directly in the RowLayout binds width to the layout width
        // and collapses to a zero hit-area, which is why dragging did nothing.)
        Item {
            id: hwRoot
            implicitWidth: hwInner.implicitWidth + (AppState.pillMeters ? 20 : 0)
            implicitHeight: AppState.cardHeight
            // Dimmed while dragged: the floating ghost carries the drag.
            opacity: (meterDrag.dragging && meterDrag.group === "hw" && meterDrag.dragItem === hwRoot) ? 0.45 : 1
            Behavior on opacity {
                NumberAnimation { duration: 120 }
            }
            // Pill shell mirroring ClipCard for the active item style.
            // Explicit card height: identical to clipboard pills even if
            // the wrapper ever stretches.
            readonly property bool mTint: AppState.pillStyle === "tint"
            readonly property bool mContrast: AppState.pillStyle === "contrast"
            readonly property bool mBorder: AppState.pillStyle === "border"
            readonly property bool mBorderless: AppState.pillStyle === "borderless"
            readonly property bool mShadow: AppState.pillStyle === "shadow"
            readonly property bool mHover: AppState.pillStyle === "hover"
            // Soft shadow beneath (shadow style).
            Rectangle {
                anchors.left: parent.left
                anchors.right: parent.right
                anchors.verticalCenter: parent.verticalCenter
                height: AppState.cardHeight
                radius: AppState.cardRadius
                color: "#000000"
                opacity: 0.3
                visible: AppState.pillMeters && mShadow
            }
            Rectangle {
                anchors.left: parent.left
                anchors.right: parent.right
                anchors.verticalCenter: parent.verticalCenter
                height: AppState.cardHeight
                radius: AppState.cardRadius
                visible: AppState.pillMeters
                color: (mTint || mBorderless) ? Qt.rgba(AppState.accent.r, AppState.accent.g, AppState.accent.b, 0.08)
                    : mContrast ? AppState.cardBg : "transparent"
                border.color: mBorder ? AppState.accent : AppState.cardBorder
                border.width: mBorderless ? 0 : 1
            }
            // Hover wash (hover style).
            Rectangle {
                anchors.left: parent.left
                anchors.right: parent.right
                anchors.verticalCenter: parent.verticalCenter
                height: AppState.cardHeight
                radius: AppState.cardRadius
                color: AppState.accent
                opacity: AppState.pillMeters && mHover && hwMouse.containsMouse ? 0.12 : 0
                Behavior on opacity {
                    NumberAnimation { duration: 150 }
                }
            }
            RowLayout {
                id: hwInner
                anchors.fill: parent
                anchors.leftMargin: AppState.pillMeters ? 10 : 0
                anchors.rightMargin: AppState.pillMeters ? 10 : 0
                spacing: 4
            MeterIcon {
                kind: "cpu"
                Layout.alignment: Qt.AlignVCenter
            }
            Text {
                text: SysMon.cpuText
                font.pixelSize: 13
                color: AppState.text
                Layout.alignment: Qt.AlignVCenter
                Layout.preferredWidth: 32
            }
            MeterIcon {
                kind: "ram"
                Layout.alignment: Qt.AlignVCenter
            }
            Text {
                text: SysMon.memText
                font.pixelSize: 13
                color: AppState.text
                Layout.alignment: Qt.AlignVCenter
                Layout.preferredWidth: 28
            }
            }
            // Left-drag is owned by the bar-level meterDrag overlay (it can
            // flip sides mid-press without being destroyed); this area only
            // right-clicks and shows the grab cursor.
            MouseArea {
                id: hwMouse
                anchors.fill: parent
                acceptedButtons: Qt.RightButton
                hoverEnabled: true
                cursorShape: Qt.OpenHandCursor
                onClicked: (mouse) => {
                    if (mouse.button === Qt.RightButton) {
                        const pt = mapToItem(shelf.contentItem, mouse.x, mouse.y)
                        meterMenu.showFor(
                            [shelf.hwOnLeft ? "Move to right" : "Move to left"],
                            (i) => shelf.hwOnLeft = !shelf.hwOnLeft,
                            shelf.x + pt.x, shelf.y)
                    }
                }
            }
        }
    }
    Component {
        id: netMeters
        Item {
            id: netRoot
            implicitWidth: netInner.implicitWidth + (AppState.pillMeters ? 20 : 0)
            implicitHeight: AppState.cardHeight
            // Dimmed while dragged: the floating ghost carries the drag.
            opacity: (meterDrag.dragging && meterDrag.group === "net" && meterDrag.dragItem === netRoot) ? 0.45 : 1
            Behavior on opacity {
                NumberAnimation { duration: 120 }
            }
            // Pill shell mirroring ClipCard for the active item style.
            // Explicit card height: identical to clipboard pills even if
            // the wrapper ever stretches.
            readonly property bool mTint: AppState.pillStyle === "tint"
            readonly property bool mContrast: AppState.pillStyle === "contrast"
            readonly property bool mBorder: AppState.pillStyle === "border"
            readonly property bool mBorderless: AppState.pillStyle === "borderless"
            readonly property bool mShadow: AppState.pillStyle === "shadow"
            readonly property bool mHover: AppState.pillStyle === "hover"
            // Soft shadow beneath (shadow style).
            Rectangle {
                anchors.left: parent.left
                anchors.right: parent.right
                anchors.verticalCenter: parent.verticalCenter
                height: AppState.cardHeight
                radius: AppState.cardRadius
                color: "#000000"
                opacity: 0.3
                visible: AppState.pillMeters && mShadow
            }
            Rectangle {
                anchors.left: parent.left
                anchors.right: parent.right
                anchors.verticalCenter: parent.verticalCenter
                height: AppState.cardHeight
                radius: AppState.cardRadius
                visible: AppState.pillMeters
                color: (mTint || mBorderless) ? Qt.rgba(AppState.accent.r, AppState.accent.g, AppState.accent.b, 0.08)
                    : mContrast ? AppState.cardBg : "transparent"
                border.color: mBorder ? AppState.accent : AppState.cardBorder
                border.width: mBorderless ? 0 : 1
            }
            // Hover wash (hover style).
            Rectangle {
                anchors.left: parent.left
                anchors.right: parent.right
                anchors.verticalCenter: parent.verticalCenter
                height: AppState.cardHeight
                radius: AppState.cardRadius
                color: AppState.accent
                opacity: AppState.pillMeters && mHover && netMouse.containsMouse ? 0.12 : 0
                Behavior on opacity {
                    NumberAnimation { duration: 150 }
                }
            }
            RowLayout {
                id: netInner
                anchors.fill: parent
                anchors.leftMargin: AppState.pillMeters ? 10 : 0
                anchors.rightMargin: AppState.pillMeters ? 10 : 0
                spacing: 4
            MeterIcon {
                kind: "up"
                Layout.alignment: Qt.AlignVCenter
            }
            Text {
                text: SysMon.upText
                font.pixelSize: 14
                color: AppState.text
                Layout.alignment: Qt.AlignVCenter
                Layout.preferredWidth: 62
            }
            MeterIcon {
                kind: "down"
                Layout.alignment: Qt.AlignVCenter
            }
            Text {
                text: SysMon.downText
                font.pixelSize: 14
                color: AppState.text
                Layout.alignment: Qt.AlignVCenter
                Layout.preferredWidth: 62
            }
            }
            MouseArea {
                id: netMouse
                anchors.fill: parent
                acceptedButtons: Qt.RightButton
                hoverEnabled: true
                cursorShape: Qt.OpenHandCursor
                onClicked: (mouse) => {
                    if (mouse.button === Qt.RightButton) {
                        const pt = mapToItem(shelf.contentItem, mouse.x, mouse.y)
                        meterMenu.showFor(
                            [shelf.netOnLeft ? "Move to right" : "Move to left"],
                            (i) => shelf.netOnLeft = !shelf.netOnLeft,
                            shelf.x + pt.x, shelf.y)
                    }
                }
            }
        }
    }

    // World clock group (local + UTC pills): one draggable group, same
    // gesture/placement rules as the meter groups (midpoint side flip,
    // right-click move, ghost + dim while dragging). Always docks last
    // on its side.
    Component {
        id: clockMeters
        Item {
            id: clockRoot
            implicitWidth: clockInner.implicitWidth + (AppState.pillMeters ? 20 : 0)
            implicitHeight: AppState.cardHeight
            // Dimmed while dragged: the floating ghost carries the drag.
            opacity: (meterDrag.dragging && meterDrag.group === "clock" && meterDrag.dragItem === clockRoot) ? 0.45 : 1
            Behavior on opacity {
                NumberAnimation { duration: 120 }
            }
            // Pill shell mirroring ClipCard for the active item style.
            readonly property bool mTint: AppState.pillStyle === "tint"
            readonly property bool mContrast: AppState.pillStyle === "contrast"
            readonly property bool mBorder: AppState.pillStyle === "border"
            readonly property bool mBorderless: AppState.pillStyle === "borderless"
            readonly property bool mShadow: AppState.pillStyle === "shadow"
            readonly property bool mHover: AppState.pillStyle === "hover"
            // Soft shadow beneath (shadow style).
            Rectangle {
                anchors.left: parent.left
                anchors.right: parent.right
                anchors.verticalCenter: parent.verticalCenter
                height: AppState.cardHeight
                radius: AppState.cardRadius
                color: "#000000"
                opacity: 0.3
                visible: AppState.pillMeters && mShadow
            }
            Rectangle {
                anchors.left: parent.left
                anchors.right: parent.right
                anchors.verticalCenter: parent.verticalCenter
                height: AppState.cardHeight
                radius: AppState.cardRadius
                visible: AppState.pillMeters
                color: (mTint || mBorderless) ? Qt.rgba(AppState.accent.r, AppState.accent.g, AppState.accent.b, 0.08)
                    : mContrast ? AppState.cardBg : "transparent"
                border.color: mBorder ? AppState.accent : AppState.cardBorder
                border.width: mBorderless ? 0 : 1
            }
            // Hover wash (hover style).
            Rectangle {
                anchors.left: parent.left
                anchors.right: parent.right
                anchors.verticalCenter: parent.verticalCenter
                height: AppState.cardHeight
                radius: AppState.cardRadius
                color: AppState.accent
                opacity: AppState.pillMeters && mHover && clockMouse.containsMouse ? 0.12 : 0
                Behavior on opacity {
                    NumberAnimation { duration: 150 }
                }
            }
            RowLayout {
                id: clockInner
                anchors.fill: parent
                anchors.leftMargin: AppState.pillMeters ? 10 : 0
                anchors.rightMargin: AppState.pillMeters ? 10 : 0
                spacing: 4
            MeterIcon {
                kind: "clock"
                visible: AppState.showClock1 || AppState.showClock2
                Layout.alignment: Qt.AlignVCenter
            }
            Text {
                text: SysMon.zoneATag
                visible: AppState.showClock1
                font.pixelSize: 12
                color: AppState.text
                Layout.alignment: Qt.AlignVCenter
                Layout.preferredWidth: 30
            }
            Text {
                text: SysMon.zoneAText
                visible: AppState.showClock1
                font.pixelSize: 13
                color: AppState.text
                Layout.alignment: Qt.AlignVCenter
                Layout.preferredWidth: 58
            }
            MeterIcon {
                kind: "clock"
                visible: AppState.showClock2
                Layout.alignment: Qt.AlignVCenter
            }
            Text {
                text: SysMon.zoneBTag
                visible: AppState.showClock2
                font.pixelSize: 12
                color: AppState.text
                Layout.alignment: Qt.AlignVCenter
                Layout.preferredWidth: 30
            }
            Text {
                text: SysMon.zoneBText
                visible: AppState.showClock2
                font.pixelSize: 13
                color: AppState.text
                Layout.alignment: Qt.AlignVCenter
                Layout.preferredWidth: 58
            }
            }
            // Left-drag is owned by the bar-level meterDrag overlay (it can
            // flip sides mid-press without being destroyed); this area only
            // right-clicks and shows the grab cursor.
            MouseArea {
                id: clockMouse
                anchors.fill: parent
                acceptedButtons: Qt.RightButton
                hoverEnabled: true
                cursorShape: Qt.OpenHandCursor
                onClicked: (mouse) => {
                    if (mouse.button === Qt.RightButton) {
                        const pt = mapToItem(shelf.contentItem, mouse.x, mouse.y)
                        meterMenu.showFor(
                            [shelf.clockOnLeft ? "Move to right" : "Move to left", "Change time"],
                            (i) => {
                                if (i === 1) {
                                    settingsWindow.section = 2
                                    settingsWindow.open()
                                } else {
                                    shelf.clockOnLeft = !shelf.clockOnLeft
                                }
                            },
                            shelf.x + pt.x, shelf.y)
                    }
                }
            }
        }
    }

    // Generic mini context menu (own window so it escapes the shelf).
    // Qt.Popup: dismisses itself on outside click. A focus-based close
    // (onActiveChanged) is NOT enough here — the focus-inert Tool shelf
    // never steals activation, so shelf clicks would leave it stuck open.
    Window {
        id: meterMenu
        flags: Qt.FramelessWindowHint | Qt.Popup | Qt.WindowStaysOnTopHint
        color: "transparent"
        width: 170
        // Fits the entries (clock menu carries two): fixed heights clip.
        height: 14 + meterMenu.entries.length * 40
        visible: false

        property var entries: []
        property var onPick: (i) => {}

        function showFor(items, callback, screenX, shelfTop) {
            entries = items
            onPick = callback
            x = Math.max(8, Math.min(screenX - 40, Screen.width - width - 8))
            y = popupY(height)
            show()
            requestActivate()
        }

        Rectangle {
            anchors.fill: parent
            radius: 10
            color: AppState.menuBg
            border.color: AppState.cardBorder
            Column {
                anchors.fill: parent
                anchors.margins: 8
                Repeater {
                    model: meterMenu.entries
                    PopupItem {
                        glyph: "⇄"
                        label: modelData
                        onTriggered: {
                            meterMenu.hide()
                            meterMenu.onPick(index)
                        }
                    }
                }
            }
        }

        onActiveChanged: {
            if (!active)
                hide()
        }
    }

    // Gear popup: own window so it escapes the 36px shelf (a QQuick Menu
    // would be clipped to the shelf). Opens above the gear button.
    Window {
        id: gearPopup
        flags: Qt.FramelessWindowHint | Qt.Tool | Qt.WindowStaysOnTopHint
        color: "transparent"
        width: 170
        height: 176
        visible: false

        function toggle() {
            if (visible) {
                hide()
            } else {
                x = shelf.x + shelf.width - width - 8
                y = popupY(height)
                show()
                requestActivate()
            }
        }

        Rectangle {
            anchors.fill: parent
            radius: 10
            color: AppState.menuBg
            border.color: AppState.cardBorder

            Column {
                anchors.fill: parent
                anchors.margins: 8
                spacing: 2
                PopupItem {
                    glyph: ""
                    label: "Search  (Ctrl+F)"
                    onTriggered: {
                        gearPopup.hide()
                        shelf.searchOpen = true
                        searchBox.forceActiveFocus()
                    }
                }
                PopupItem {
                    glyph: ""
                    label: "Clear history"
                    onTriggered: {
                        gearPopup.hide()
                        store.clearHistory()
                    }
                }
                PopupItem {
                    glyph: ""
                    label: "Settings"
                    onTriggered: {
                        gearPopup.hide()
                        settingsWindow.open()
                    }
                }
                PopupItem {
                    glyph: ""
                    label: "Exit"
                    onTriggered: Qt.quit()
                }
            }
        }

        onActiveChanged: {
            if (!active)
                hide()
        }
    }

    SettingsWindow { id: settingsWindow }

    PreviewPopup { id: imagePreview }

    ClipMenu { id: clipMenu }

    // System tray (labs.platform works without Qt Widgets). The context
    // menu is our own themed popup (not Labs.Menu): the native menu always
    // renders in OS light style and never follows the app theme.
    Labs.SystemTrayIcon {
        visible: true
        icon.name: "totthodhara"
        icon.source: "qrc:/resources/app.png"
        tooltip: "Totthodhara is running"
        onActivated: (reason) => {
            if (reason === Labs.SystemTrayIcon.Trigger
                || reason === Labs.SystemTrayIcon.DoubleClick) {
                if (shelf.visible) {
                    shelf.userHidden = true
                    shelf.hide()
                } else {
                    shelf.userHidden = false
                    shelf.show()
                }
            } else if (reason === Labs.SystemTrayIcon.Context) {
                trayMenu.showAtCursor()
            }
        }
    }

    // Themed tray menu (own window so it escapes screen edges cleanly).
    Window {
        id: trayMenu
        flags: Qt.FramelessWindowHint | Qt.Popup | Qt.WindowStaysOnTopHint
        color: "transparent"
        width: 170
        height: 140
        visible: false

        function showAtCursor() {
            const p = ThemeWatcher.cursorPos()
            x = Math.max(8, Math.min(p.x - width / 2, Screen.width - width - 8))
            y = Math.max(8, Screen.desktopAvailableHeight - height - 8)
            show()
            requestActivate()
        }

        Rectangle {
            anchors.fill: parent
            radius: 10
            color: AppState.menuBg
            border.color: AppState.cardBorder

            Column {
                anchors.fill: parent
                anchors.margins: 8
                spacing: 2
                PopupItem {
                    glyph: ""
                    label: shelf.visible ? "Hide Shelf" : "Show Shelf"
                    onTriggered: {
                        trayMenu.hide()
                        if (shelf.visible) {
                            shelf.userHidden = true
                            shelf.hide()
                        } else {
                            shelf.userHidden = false
                            shelf.show()
                        }
                    }
                }
                PopupItem {
                    glyph: ""
                    label: "Settings"
                    onTriggered: {
                        trayMenu.hide()
                        settingsWindow.open()
                    }
                }
                PopupItem {
                    glyph: ""
                    label: "Exit"
                    onTriggered: Qt.quit()
                }
            }
        }

        onActiveChanged: {
            if (!active)
                hide()
        }
    }
}