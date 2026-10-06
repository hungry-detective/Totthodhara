// Card context menu: own window so it escapes the 30px shelf (a QQuick
// Menu would be clipped to the shelf and render in OS light style).
// Dark card, Fluent glyphs — follows the app theme, not the OS one.
import QtQuick

Window {
    id: menu
    // Qt.Popup: dismisses itself on outside click (same focus-inert shelf
    // issue as the meter menu — Tool + onActiveChanged alone left stale
    // menus behind when clicking back on the shelf).
    flags: Qt.FramelessWindowHint | Qt.Popup | Qt.WindowStaysOnTopHint | Qt.NoDropShadowWindowHint
    color: "transparent"
    width: 210
    height: 134
    visible: false

    property string pinLabel: "Pin"
    property string snippetLabel: "Save as snippet"
    property int itemIndex: -1
    property var onPick: (action, index) => {}

    function showFor(pinText, snipText, index, callback, centerX, shelfTop) {
        pinLabel = pinText
        snippetLabel = snipText
        itemIndex = index
        onPick = callback
        x = Math.max(8, Math.min(centerX - width / 2, Screen.width - width - 8))
        y = AppState.shelfPosition === "Top" ? shelfTop + AppState.barHeight + 8 : shelfTop - height - 8
        show()
        // Take focus so that clicking anywhere else deactivates us and
        // onActiveChanged hides the popup (Tool windows don't grab
        // focus on their own, which left stale menus on screen).
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
                glyph: "\uE718"
                label: menu.pinLabel
                onTriggered: {
                    menu.hide()
                    menu.onPick(0, menu.itemIndex)
                }
            }
            PopupItem {
                glyph: "\uE735"
                label: menu.snippetLabel
                onTriggered: {
                    menu.hide()
                    menu.onPick(1, menu.itemIndex)
                }
            }
            PopupItem {
                glyph: "\uE74D"
                label: "Delete"
                onTriggered: {
                    menu.hide()
                    menu.onPick(2, menu.itemIndex)
                }
            }
        }
    }

    onActiveChanged: {
        if (!active)
            hide()
    }
}
