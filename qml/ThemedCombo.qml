// Themed dropdown (dark card + dark popup list, no native frame).
// Reintroduced by explicit user request for the clock time-zone pickers
// (the old file was deleted to keep Settings dropdown-free).
import QtQuick
import QtQuick.Controls
import QtQuick.Layouts

ComboBox {
    id: combo
    // [{ value, title }]: single source, shared with nobody (parent passes
    // settings.zoneModel straight in).
    property var items: []
    property string value: ""
    signal picked(string v)

    implicitWidth: 150
    implicitHeight: 34

    // Imperative sync both ways (a currentIndex binding would be clobbered
    // by user picks and go stale).
    Component.onCompleted: syncIndex()
    onValueChanged: syncIndex()
    function syncIndex() {
        let want = -1
        for (let i = 0; i < items.length; i++) {
            if (items[i] && items[i].value === value) {
                want = i
                break
            }
        }
        if (want >= 0 && currentIndex !== want)
            currentIndex = want
    }
    // Single selection path (taps AND native activation): without this,
    // TapHandler-called signal emissions can vanish silently and the
    // field updates while the value never propagates.
    function select(i) {
        if (i < 0 || i >= items.length || !items[i])
            return
        currentIndex = i
        picked(items[i].value)
    }
    function labels() {
        const out = []
        for (let i = 0; i < items.length; i++)
            out.push(items[i] ? items[i].title : "")
        return out
    }
    model: labels()
    onActivated: (i) => select(i)

    background: Rectangle {
        implicitWidth: 150
        implicitHeight: 34
        radius: 8
        color: AppState.cardBg
        border.color: combo.activeFocus || combo.popup.visible ? AppState.accent : AppState.cardBorder
        border.width: combo.activeFocus || combo.popup.visible ? 2 : 1
    }
    contentItem: Text {
        leftPadding: 12
        rightPadding: 28
        text: combo.displayText
        elide: Text.ElideRight
        verticalAlignment: Text.AlignVCenter
        font.pixelSize: 13
        color: AppState.text
    }
    indicator: Text {
        x: combo.width - width - 10
        anchors.verticalCenter: parent.verticalCenter
        text: "\uE70D"
        font.family: AppState.iconFont
        font.pixelSize: 12
        color: AppState.muted
    }
    popup: Popup {
        y: combo.height + 4
        width: combo.width
        implicitHeight: Math.min(320, combo.count * 24 + 16)
        padding: 8
        // Type-to-jump: typing while open jumps to the first match
        // (Backspace trims, 1s idle resets) — the quick way through a
        // 300-row zone list.
        property string findPrefix: ""
        Timer {
            id: findReset
            interval: 1000
            onTriggered: combo.popup.findPrefix = ""
        }
        onOpened: {
            popupList.currentIndex = combo.currentIndex
            popupList.positionViewAtIndex(popupList.currentIndex, ListView.Center)
            popupList.forceActiveFocus()
        }
        background: Rectangle {
            radius: 10
            color: AppState.cardBg
            border.color: AppState.cardBorder
        }
        contentItem: ListView {
            id: popupList
            clip: true
            // Plain string array (NOT combo.delegateModel — that carries the
            // native ItemDelegate and renders light-style rows over ours).
            // Highlight is list-local: typing/moving only moves the cursor;
            // nothing is picked until tap or Enter.
            model: combo.labels()
            ScrollBar.vertical: ScrollBar { policy: ScrollBar.AlwaysOff }
            Keys.onPressed: (event) => {
                if (event.key === Qt.Key_Escape)
                    return
                if (event.key === Qt.Key_Return || event.key === Qt.Key_Enter) {
                    combo.select(popupList.currentIndex)
                    combo.popup.close()
                    event.accepted = true
                    return
                }
                if (event.key === Qt.Key_Backspace) {
                    combo.popup.findPrefix = combo.popup.findPrefix.slice(0, -1)
                } else if (event.text !== "") {
                    combo.popup.findPrefix += event.text.toLowerCase()
                } else {
                    return
                }
                findReset.restart()
                const needle = combo.popup.findPrefix
                if (needle === "")
                    return
                // City-first: "dh" jumps to Dhaka even though the row
                // starts with its "(UTC+06:00)" offset.
                const names = combo.labels()
                for (let i = 0; i < combo.count; i++) {
                    const item = combo.items[i] || {}
                    const city = String(item.city || names[i]).toLowerCase()
                    const label = String(names[i]).toLowerCase()
                    if (city.startsWith(needle) || label.includes(needle)) {
                        popupList.positionViewAtIndex(i, ListView.Beginning)
                        popupList.currentIndex = i
                        break
                    }
                }
                event.accepted = true
            }
            delegate: Rectangle {
                width: combo.width - 16
                height: 24
                radius: 6
                color: "transparent"
                Rectangle {
                    anchors.fill: parent
                    radius: 6
                    color: AppState.accent
                    opacity: index === popupList.currentIndex ? 0.25 : (rowHover.hovered ? 0.12 : 0)
                }
                RowLayout {
                    anchors.fill: parent
                    anchors.leftMargin: 10
                    anchors.rightMargin: 10
                    spacing: 8
                    Text {
                        Layout.fillWidth: true
                        text: modelData
                        elide: Text.ElideRight
                        verticalAlignment: Text.AlignVCenter
                        font.pixelSize: 13
                        color: AppState.text
                    }
                    Text {
                        visible: index === combo.currentIndex
                        text: "✓"
                        font.pixelSize: 12
                        font.bold: true
                        color: AppState.accent
                    }
                }
                HoverHandler { id: rowHover }
                TapHandler {
                    onTapped: {
                        combo.select(index)
                        combo.popup.close()
                    }
                }
            }
        }
    }
}
