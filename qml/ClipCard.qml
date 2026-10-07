// One clip card on the shelf. Emits signals; ClipStore owns the logic.
import QtQuick
import QtQuick.Controls
import QtQuick.Layouts

Rectangle {
    id: card
    // Width hugs the content (one-word clips don't swim in a 160px box).
    // Padding covers the row margins (9 + 10) plus breathing slack.
    // Locked while inline feedback shows ("Pasted!") so neighbors never move.
    width: lockedW > 0 ? lockedW : Math.max(52, Math.min(200, contentRow.implicitWidth + 23))
    height: AppState.cardHeight
    radius: AppState.cardRadius
    // Pill style variants — controlled by Settings > Clipboard Style.
    // Each style changes how the pill reads against the bar.
    readonly property bool isBorder: AppState.pillStyle === "border"
    readonly property bool isTint: AppState.pillStyle === "tint"
    readonly property bool isHover: AppState.pillStyle === "hover"
    readonly property bool isContrast: AppState.pillStyle === "contrast"
    readonly property bool isBorderless: AppState.pillStyle === "borderless"
    readonly property bool isShadow: AppState.pillStyle === "shadow"
    // Hover lift works for both "hover" and "tint" styles
    readonly property bool hasHoverLift: isHover || isTint

    // Fill: transparent for border/hover/shadow, accent-tint for tint, cardBg for contrast.
    // Borderless selected deepens its tint so selection reads without an edge.
    color: isTint ? Qt.rgba(AppState.accent.r, AppState.accent.g, AppState.accent.b, 0.08)
        : isBorderless ? Qt.rgba(AppState.accent.r, AppState.accent.g, AppState.accent.b, selected ? 0.16 : 0.08)
        : isContrast ? AppState.cardBg
        : "transparent"
    // Border: accent for border style, cardBorder otherwise (hidden for contrast/shadow).
    // Borderless: no border at all. Selected keeps the 1px width — the
    // accent color + wash carry the state, never thickness.
    border.width: isBorderless ? 0 : 1
    border.color: isBorder ? AppState.accent : (selected ? AppState.accent : AppState.cardBorder)
    // Tactile press: shrink clearly, spring back on release.
    // Dragging to another app: lift + fade while the OS drag runs.
    // Hover lift style: scale up slightly on hover.
    scale: dragging ? 0.92 : (hoverArea.pressed ? 0.93 : (hasHoverLift && hoverArea.containsMouse ? 1.04 : 1))
    // Hover lift: soft accent glow beneath
    Rectangle {
        anchors.fill: parent
        radius: AppState.cardRadius
        color: AppState.accent
        opacity: hasHoverLift && hoverArea.containsMouse ? 0.15 : 0
        z: -1
        Behavior on opacity {
            NumberAnimation { duration: 150 }
        }
    }
    opacity: dragging ? 0.45 : 1
    Behavior on scale {
        NumberAnimation { duration: 130; easing.type: Easing.OutCubic }
    }
    Behavior on opacity {
        NumberAnimation { duration: 160; easing.type: Easing.OutCubic }
    }

    // Soft shadow beneath pill (shadow style)
    Rectangle {
        anchors.fill: parent
        radius: AppState.cardRadius
        color: "#000000"
        opacity: isShadow ? 0.3 : 0
        z: -1
        Behavior on opacity {
            NumberAnimation { duration: 150 }
        }
    }

    // Only the selected state washes the card. Hover never washes the
    // whole pill — only the visit-url glyph lights up (see openGlyph).
    Rectangle {
        anchors.fill: parent
        radius: AppState.cardRadius
        color: AppState.accent
        opacity: selected ? 0.28 : (hasHoverLift && hoverArea.containsMouse ? 0.12 : 0)
        Behavior on opacity {
            NumberAnimation { duration: 150 }
        }
    }

    // Click flash: bright accent bloom so the clicked card pops
    // unmistakably, then melts away. Fired from the delegate.
    property real flash: 0
    function pulse() { clickPulse.restart() }
    Rectangle {
        anchors.fill: parent
        radius: AppState.cardRadius
        color: AppState.accent
        opacity: card.flash * 0.65
    }
    NumberAnimation {
        id: clickPulse
        target: card
        property: "flash"
        from: 1
        to: 0
        duration: 480
        easing.type: Easing.OutCubic
    }

    property string title
    property string kind
    property string detail
    property string icon
    property bool pinned
    property bool snippet
    property bool selected
    property int cardIndex
    // Inline action feedback ("Pasted!" swaps the title briefly): set by
    // the store, auto-clears via flashClear below. Width locks while
    // flashing so neighbors never shift.
    property string flashText: ""
    property int addedId: -1
    property real lockedW: 0
    onFlashTextChanged: {
        if (flashText !== "") {
            if (lockedW <= 0)
                lockedW = width
            pulse()
            flashClear.restart()
        } else {
            lockedW = 0
        }
    }
    Timer {
        id: flashClear
        interval: 1200
        onTriggered: {
            if (card.storeRef && card.addedId >= 0)
                card.storeRef.clearFlashById(card.addedId)
            else
                card.flashText = ""
        }
    }

    // OS drag-out: view row + store bridge (set by the Main.qml delegate).
    property int viewIndex: -1
    property var storeRef: null
    // Shelf list bridge: while a card is pressed the strip must not flick
    // sideways under the pointer (press jitter would side-scroll the shelf
    // before the OS drag takes over).
    property var listRef: null
    property bool dragging: false
    property bool suppressClick: false
    // Favicon fetch failed (dead host, offline): show the globe instantly
    // instead of a blank/broken image slot.
    property bool faviconFailed: false
    onIconChanged: faviconFailed = false
    // Clip-file thumbnail failed (missing/corrupt): IMG badge instead.
    property bool thumbFailed: false
    onDetailChanged: thumbFailed = false
    // The full-card hoverArea sits above the link glyph and steals Qt hover,
    // so track "over the visit button" manually for a reliable highlight.
    property bool openHovered: false
    function updateOpenHover(mx, my) {
        if (!openGlyph.visible) {
            openHovered = false
            return
        }
        const lp = openGlyph.mapFromItem(card, mx, my)
        openHovered = lp.x >= -4 && lp.y >= -6
            && lp.x <= openGlyph.width + 4 && lp.y <= openGlyph.height + 6
    }

    signal clicked(bool shift, bool ctrl)
    signal openUrl()
    signal previewRequested(Item card)
    signal previewHidden()
    signal menuRequested(Item card)
    // Favicon/file failed to decode: fall back to the globe glyph.
    signal iconFailed()

    // Hover any card with content: images show large, text/links show
    // full content, files show an info card (all except color swatches).
    Timer {
        id: previewTimer
        interval: 350
        onTriggered: {
            if (hoverArea.containsMouse && card.kind !== "color")
                card.previewRequested(card)
        }
    }

    // Safety net: a card can never stay faded, and a stale suppressClick
    // can never eat a future click. Restarted AFTER each drop (not before
    // the drag): clearing it mid-drag would un-suppress the phantom
    // post-drop click and double-fire.
    Timer {
        id: dragWatchdog
        interval: 1500
        onTriggered: {
            card.dragging = false
            card.suppressClick = false
        }
    }

    // Title row: index + visual fixed, title stretches (elides), open
    // button fixed. Tight spacing: no dead gaps between number/icon.
    RowLayout {
        id: contentRow
        anchors.fill: parent
        anchors.leftMargin: 9
        anchors.rightMargin: 10
        spacing: 4

        Text {
            text: card.cardIndex
            font.pixelSize: AppState.barSize === "Small" ? 12 : 13
            font.bold: true
            font.weight: Font.Black
            color: AppState.text
            verticalAlignment: Text.AlignVCenter
            Layout.preferredWidth: 14
            Layout.fillHeight: true
        }

        // Kind visual, only when the kind has one (plain text shows none):
        // fitted thumbnail (image), swatch (color), site icon (link),
        // extension badge (file, e.g. JS / MP3 / PDF).
        Item {
            id: iconSlot
            visible: card.kind !== "text" && card.flashText === ""
            Layout.preferredWidth: card.kind === "text" ? 0 : 28
            Layout.fillHeight: true
            property bool iconIsImage: card.kind !== "image" && card.kind !== "color"
                && (card.icon.startsWith("file:") || card.icon.startsWith("qrc:/")
                    || card.icon.startsWith("http"))
            property string fileExt: {
                if (card.kind !== "file")
                    return ""
                const name = card.detail.split("/").pop().split("?")[0]
                const dot = name.lastIndexOf(".")
                return dot >= 0 ? name.substring(dot + 1).toUpperCase() : "FILE"
            }
            property bool isVideoExt: ["MP4", "AVI", "MKV", "MOV", "WMV"].indexOf(iconSlot.fileExt) >= 0
            property bool isAudioExt: ["MP3", "WAV", "WMA", "M4A", "OGG", "FLAC", "AAC"].indexOf(iconSlot.fileExt) >= 0
            property bool isCodeExt: ["JS", "TS", "PY", "CS", "CPP", "H", "C", "JAVA", "GO", "RS", "RB", "PHP", "SWIFT", "KT", "SQL", "SH", "PS1", "BAT", "CMD", "JSON", "XML", "HTML", "CSS", "SCSS", "YML", "YAML"].indexOf(iconSlot.fileExt) >= 0
            Text {
                anchors.centerIn: parent
                visible: card.kind === "file" && (iconSlot.isVideoExt || iconSlot.isAudioExt)
                text: iconSlot.isVideoExt ? "\uE714" : "\uE767"
                font.family: AppState.iconFont
                font.pixelSize: 16
                color: AppState.text
            }
            MeterIcon {
                anchors.centerIn: parent
                visible: card.kind === "file" && iconSlot.isCodeExt
                kind: "code"
            }
            // Image thumbnail: rounded tile with accent glow border —
            // makes the thumb pop in dark/system mode. Extreme aspects
            // letterbox inside instead of collapsing.
            Item {
                anchors.centerIn: parent
                visible: card.kind === "image"
                width: 26
                height: Math.max(16, AppState.cardHeight - 6)
                Rectangle {
                    id: thumbTile
                    anchors.fill: parent
                    radius: 6
                    color: AppState.barBg
                    border.width: AppState.thumbStyle === "glow" ? 2
                        : (AppState.thumbStyle === "accent" || AppState.thumbStyle === "line") ? 1 : 0
                    border.color: AppState.thumbStyle === "line" ? AppState.cardBorder : AppState.accent
                }
                // Accent halo behind the tile (glow frame style).
                Rectangle {
                    anchors.fill: parent
                    anchors.margins: -1
                    radius: 7
                    color: AppState.accent
                    opacity: 0.35
                    visible: AppState.thumbStyle === "glow"
                    z: -1
                }
                // Offset soft shade behind the tile (shadow frame style).
                Rectangle {
                    x: 1
                    y: 1
                    width: parent.width
                    height: parent.height
                    radius: 6
                    color: "#000000"
                    opacity: 0.45
                    visible: AppState.thumbStyle === "shadow"
                    z: -1
                }
                Image {
                    anchors.fill: thumbTile
                    anchors.margins: 2
                    visible: !card.thumbFailed
                    source: card.kind === "image" && !card.thumbFailed ? card.detail : ""
                    fillMode: Image.PreserveAspectFit
                    onStatusChanged: {
                        // Missing/corrupt clip file (cleaned externally):
                        // badge below instead of an empty dark tile.
                        if (status === Image.Error)
                            card.thumbFailed = true
                    }
                }
                // Broken-thumbnail badge: same language as file badges, so
                // a dead image still reads as an image, not a black hole.
                Rectangle {
                    anchors.centerIn: thumbTile
                    visible: card.thumbFailed
                    width: 26
                    height: 16
                    radius: 4
                    color: "transparent"
                    border.color: AppState.text
                    border.width: 1
                    Text {
                        anchors.centerIn: parent
                        text: "IMG"
                        font.pixelSize: 8
                        font.bold: true
                        color: AppState.text
                    }
                }
            }
            Image {
                id: faviconImage
                anchors.centerIn: parent
                visible: iconSlot.iconIsImage && !card.faviconFailed
                source: iconSlot.iconIsImage && !card.faviconFailed ? card.icon : ""
                width: 16
                height: 16
                fillMode: Image.PreserveAspectFit
                onStatusChanged: {
                    // Dead icon file / dead http favicon (e.g. hosts with
                    // no icon, offline): globe fallback, retry next launch.
                    if (status === Image.Error) {
                        card.faviconFailed = true
                        card.iconFailed()
                    }
                }
            }
            Rectangle {
                anchors.centerIn: parent
                visible: card.kind === "color"
                width: 16
                height: 16
                radius: 8
                color: card.detail
                border.color: AppState.cardBorder
            }
            // Plain files (PDF included) share one extension badge.
            Rectangle {
                anchors.centerIn: parent
                visible: card.kind === "file" && !iconSlot.isVideoExt && !iconSlot.isAudioExt && !iconSlot.isCodeExt
                width: 26
                height: 16
                radius: 4
                color: "transparent"
                border.color: AppState.text
                border.width: 1
                Text {
                    anchors.centerIn: parent
                    text: iconSlot.fileExt
                    font.pixelSize: 8
                    font.bold: true
                    color: AppState.text
                }
            }
            Text {
                anchors.centerIn: parent
                visible: card.kind !== "image" && card.kind !== "color"
                    && card.kind !== "file" && (!iconSlot.iconIsImage || card.faviconFailed)
                // World marker for links: globe in accent when no site icon
                // could be captured (empty/failed), 16px like other glyphs.
                text: (card.faviconFailed || card.icon === "") ? "\uE774" : card.icon
                font.family: AppState.iconFont
                font.pixelSize: 16
                color: card.kind === "url" ? AppState.accent : AppState.text
            }
        }

        Text {
            Layout.fillWidth: true
            Layout.maximumWidth: 130
            Layout.fillHeight: true
            verticalAlignment: Text.AlignVCenter
            horizontalAlignment: card.flashText !== "" ? Text.AlignHCenter : Text.AlignLeft
            text: card.flashText !== "" ? card.flashText : card.title
            textFormat: Text.PlainText
            font.pixelSize: AppState.barSize === "Small" ? 11 : 13
            font.bold: card.flashText !== ""
            // Feedback follows the theme like every title (dark = pure
            // white, light = near-black): accent is reserved for the
            // click-bloom halo behind it, never for meaningful text.
            color: AppState.text
            elide: Text.ElideRight
        }

        Text {
            id: openGlyph
            visible: card.kind === "url" && card.flashText === ""
            Layout.preferredWidth: card.kind === "url" ? 16 : 0
            Layout.fillHeight: true
            verticalAlignment: Text.AlignVCenter
            text: "\uE8A7"
            font.family: AppState.iconFont
            font.pixelSize: 15
            font.underline: card.openHovered
            color: card.openHovered ? Qt.lighter(AppState.accent, 1.45) : AppState.accent
            Rectangle {
                anchors.centerIn: parent
                width: 22
                height: 22
                radius: 11
                color: AppState.accent
                opacity: card.openHovered ? 0.34 : 0
                Behavior on opacity {
                    NumberAnimation { duration: 120 }
                }
            }
            MouseArea {
                id: openMouse
                anchors.fill: parent
                hoverEnabled: true
                cursorShape: Qt.PointingHandCursor
                onClicked: card.openUrl()
            }
        }
    }

    // Badges: snippet star top-right (left of pin when both show).
    // Never over the index number on the left.
    Text {
        anchors.right: parent.right
        anchors.rightMargin: card.pinned ? 14 : 2
        anchors.top: parent.top
        anchors.topMargin: 2
        visible: card.snippet
        text: "\uE734"
        font.family: AppState.iconFont
        font.pixelSize: 9
        color: "#ffd659"
    }
    Text {
        anchors.right: parent.right
        anchors.top: parent.top
        anchors.margins: 2
        visible: card.pinned
        text: "\uE718"
        font.family: AppState.iconFont
        font.pixelSize: 9
        color: AppState.accent
    }

    MouseArea {
        id: hoverArea
        anchors.fill: parent
        acceptedButtons: Qt.LeftButton | Qt.RightButton
        hoverEnabled: true
        cursorShape: card.openHovered ? Qt.PointingHandCursor : Qt.ArrowCursor
        property point pressPos: Qt.point(0, 0)
        // Let taps on the link-open glyph fall through to its own
        // MouseArea (this area is stacked above it and would steal them).
        onPressed: (mouse) => {
            pressPos = Qt.point(mouse.x, mouse.y)
            if (card.listRef)
                card.listRef.interactive = false
            if (mouse.button === Qt.LeftButton && openGlyph.visible) {
                const lp = openGlyph.mapFromItem(card, mouse.x, mouse.y)
                if (lp.x >= -2 && lp.y >= -4
                    && lp.x <= openGlyph.width + 2 && lp.y <= openGlyph.height + 4)
                    mouse.accepted = false
            }
        }
        // Drag out to any other app: past the threshold the card lifts
        // (dragging anim) and the OS takes the mime payload. Drop result
        // flashes inline on the card via the store; the spring-back anim runs on release.
        // No drag with Ctrl/Shift held (those are delete/select clicks) and
        // a 16px threshold: press jitter past a small threshold used to
        // start a blocking OS drag that ate the release, so the click
        // (e.g. Ctrl+click delete) never fired and nothing happened.
        onPositionChanged: (mouse) => {
            card.updateOpenHover(mouse.x, mouse.y)
            if ((pressedButtons & Qt.LeftButton) && !card.dragging && card.storeRef) {
                if (mouse.modifiers & (Qt.ControlModifier | Qt.ShiftModifier))
                    return
                const dx = mouse.x - pressPos.x
                const dy = mouse.y - pressPos.y
                if (Math.sqrt(dx * dx + dy * dy) > 16 && card.viewIndex >= 0) {
                    card.dragging = true
                    card.suppressClick = true
                    // Drop the QML grab BEFORE the OS takes the mouse: the
                    // release lands in the other app, so anything still
                    // grabbed here sticks "pressed" (dead-looking card until
                    // the next click). try/finally: the visuals always reset,
                    // even if the store call threw.
                    hoverArea.enabled = false
                    try {
                        card.storeRef.beginSystemDrag(card.viewIndex)
                    } finally {
                        card.dragging = false
                        if (card.listRef)
                            card.listRef.interactive = true
                        hoverArea.enabled = true
                        // Arm the watchdog now (not before the drag): the
                        // drop is over, so a stale suppressClick from here
                        // on can only be a missed phantom click.
                        dragWatchdog.restart()
                    }
                }
            }
        }
        onClicked: (mouse) => {
            if (card.suppressClick) {
                card.suppressClick = false
                return
            }
            if (mouse.button === Qt.RightButton) {
                card.menuRequested(card)
                return
            }
            // The link-open glyph handles its own taps (nested MouseArea):
            // ignore this region here so a click never double-fires.
            if (openGlyph.visible) {
                const lp = openGlyph.mapFromItem(card, mouse.x, mouse.y)
                if (lp.x >= -2 && lp.y >= -4
                    && lp.x <= openGlyph.width + 2 && lp.y <= openGlyph.height + 4)
                    return
            }
            card.clicked(mouse.modifiers & Qt.ShiftModifier,
                         mouse.modifiers & Qt.ControlModifier);
        }
        onReleased: {
            if (card.listRef)
                card.listRef.interactive = true
        }
        onCanceled: {
            if (card.listRef)
                card.listRef.interactive = true
        }
        onContainsMouseChanged: {
            if (containsMouse) {
                if (card.kind !== "color")
                    previewTimer.restart()
            } else {
                card.openHovered = false
                previewTimer.stop()
                card.previewHidden()
            }
        }
    }
}
