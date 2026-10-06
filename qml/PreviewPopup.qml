// Floating full preview for image cards. Own window so it escapes the
// 32px shelf. Shown on hover (350ms delay), sized to the image aspect.
import QtQuick

Window {
    id: preview
    flags: Qt.FramelessWindowHint | Qt.Tool | Qt.WindowStaysOnTopHint
    color: "transparent"
    visible: false
    width: 300
    height: 200

    property string source: ""
    property string bodyText: ""
    property bool textMode: false
    property bool fileMode: false
    property string fileGlyph: ""
    property string fileTitle: ""
    property string fileSub: ""
    property real anchorX: 0
    property real shelfTop: 0
    // Last image geometry: reused as the provisional box so the popup
    // never opens at a stale text/file size while the image decodes.
    property real lastW: 420
    property real lastH: 280

    // Unwrapped measurer: implicitWidth goes circular once text wraps
    // (it clamps to the current width, so the bubble never grows and long
    // paragraphs collapse to one elided line). Advance width has no layout
    // feedback — deterministic sizing.
    TextMetrics {
        id: bodyMeasure
        font.pixelSize: 13
        text: preview.bodyText
    }

    function showFor(src, centerX, topY) {
        textMode = false
        fileMode = false
        anchorX = centerX
        shelfTop = topY
        if (img.source !== src)
            img.source = ""
        img.source = src
        // Provisional box from the last image (never a stale text/file
        // bubble); place() refines it the moment the pixels are ready.
        preview.width = lastW
        preview.height = lastH
        preview.x = Math.max(8, Math.min(anchorX - lastW / 2, Screen.width - lastW - 8))
        preview.y = AppState.shelfPosition === "Top" ? shelfTop + AppState.barHeight + 8 : shelfTop - lastH - 8
        // Show the window immediately so the user sees the popup appear.
        preview.show()
        // If cached and already ready, place immediately.
        if (img.status === Image.Ready)
            place()
    }

    // Full-text preview: sized to hug the content (width and height),
    // so one-liners get a small bubble and long paragraphs get room
    // (capped well beyond a sentence before eliding).
    function showText(txt, centerX, topY) {
        textMode = true
        fileMode = false
        bodyText = txt
        anchorX = centerX
        shelfTop = topY
        preview.width = Math.max(120, Math.min(480, bodyMeasure.advanceWidth + 24))
        preview.height = 60
        placeText()
        preview.show()
    }

    function placeText() {
        const w = Math.max(120, Math.min(480, bodyTextItem.implicitWidth + 24))
        // No height cap — show the full text always.
        const h = Math.max(44, bodyTextItem.contentHeight + 24)
        preview.width = w
        preview.height = h
        preview.x = Math.max(8, Math.min(anchorX - w / 2, Screen.width - w - 8))
        preview.y = AppState.shelfPosition === "Top" ? shelfTop + AppState.barHeight + 8 : shelfTop - h - 8
    }

    // TextMetrics for file title/sub measurement (smart sizing).
    TextMetrics {
        id: fileTitleMeasure
        font.pixelSize: 13
        font.bold: true
        text: preview.fileTitle
    }
    TextMetrics {
        id: fileSubMeasure
        font.pixelSize: 12
        text: preview.fileSub
    }

    // File preview: big glyph + filename + kind line, smart sized.
    // Width hugs the longest line (title or sub), so short names get a
    // compact bubble and long filenames grow to fit — never cut off.
    function showFile(glyph, title, sub, centerX, topY) {
        textMode = false
        fileMode = true
        fileGlyph = glyph
        fileTitle = title
        fileSub = sub
        // Measure after text is set (TextMetrics updates synchronously).
        const titleW = fileTitleMeasure.advanceWidth
        const subW = fileSubMeasure.advanceWidth
        const contentW = Math.max(titleW, subW)
        // Clamp: min 200, max 420. Add padding for the bubble edges.
        const w = Math.max(200, Math.min(420, contentW + 36))
        preview.width = w
        // Height: glyph(30) + spacing(4+4) + title lines + sub lines + padding(24)
        const titleLines = Math.ceil(titleW / (w - 24))
        const subLines = Math.ceil(subW / (w - 24))
        preview.height = 30 + 8 + (titleLines * 18) + (subLines * 16) + 24
        preview.x = Math.max(8, Math.min(centerX - w / 2, Screen.width - w - 8))
        preview.y = AppState.shelfPosition === "Top" ? topY + AppState.barHeight + 8 : topY - preview.height - 8
        preview.show()
    }

    function place() {
        const aspect = img.implicitHeight > 0 ? img.implicitWidth / img.implicitHeight : 1
        let w = 420
        let h = 420 / aspect
        if (h > 420) {
            h = 420
            w = 420 * aspect
        }
        w = Math.max(80, w)
        h = Math.max(80, h)
        lastW = w
        lastH = h
        preview.width = w
        preview.height = h
        preview.x = Math.max(8, Math.min(anchorX - w / 2, Screen.width - w - 8))
        preview.y = AppState.shelfPosition === "Top" ? shelfTop + AppState.barHeight + 8 : shelfTop - h - 8
        preview.show()
    }

    Rectangle {
        anchors.fill: parent
        radius: 10
        color: "#141414"
        border.color: "#3d3d3d"
        Image {
            id: img
            visible: !preview.textMode && !preview.fileMode
            anchors.fill: parent
            anchors.margins: 6
            fillMode: Image.PreserveAspectFit
            onStatusChanged: {
                if (status === Image.Ready)
                    preview.place()
            }
        }
        Text {
            id: bodyTextItem
            visible: preview.textMode
            anchors.left: parent.left
            anchors.right: parent.right
            anchors.top: parent.top
            anchors.margins: 12
            text: preview.bodyText
            // PlainText: URLs contain & (e.g. ...&s=10) which AutoText
            // parses as HTML entities and truncates. WrapAnywhere: URLs
            // have no spaces, so WordWrap would show one clipped line.
            textFormat: Text.PlainText
            color: "#ececec"
            font.pixelSize: 13
            wrapMode: Text.WrapAnywhere
            // No elide, no line limit — show the full text always.
            elide: Text.ElideNone
            // Re-hug after layout settles (implicit sizes need one pass).
            // Guarded by textMode: the hidden text item re-wraps whenever
            // the window resizes (e.g. an image preview landing), and an
            // unguarded re-hug stomps image geometry with text geometry —
            // the text-sized bubble with a tiny image inside.
            onImplicitWidthChanged: if (preview.textMode) preview.placeText()
            onContentHeightChanged: if (preview.textMode) preview.placeText()
        }
        Column {
            visible: preview.fileMode
            anchors.centerIn: parent
            spacing: 4
            Text {
                anchors.horizontalCenter: parent.horizontalCenter
                text: preview.fileGlyph
                font.family: AppState.iconFont
                font.pixelSize: 30
                color: AppState.accent
            }
            Text {
                anchors.horizontalCenter: parent.horizontalCenter
                width: preview.width - 24
                text: preview.fileTitle
                textFormat: Text.PlainText
                font.pixelSize: 13
                font.bold: true
                color: "#ececec"
                wrapMode: Text.WrapAnywhere
                horizontalAlignment: Text.AlignHCenter
            }
            Text {
                anchors.horizontalCenter: parent.horizontalCenter
                width: preview.width - 24
                text: preview.fileSub
                textFormat: Text.PlainText
                font.pixelSize: 12
                color: "#a0a0a0"
                wrapMode: Text.WrapAnywhere
                horizontalAlignment: Text.AlignHCenter
            }
        }
    }
}
