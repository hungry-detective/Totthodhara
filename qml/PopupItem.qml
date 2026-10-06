// One row of the gear popup menu / settings sidebar.
import QtQuick

Rectangle {
    property string glyph
    property string label
    property bool selected: false
    property int glyphSize: 14
    signal triggered

    anchors.left: parent.left
    anchors.right: parent.right
    height: 38
    radius: 8
    color: "transparent"

    // Selected wash + hover wash (accent at low opacity, theme-agnostic).
    Rectangle {
        anchors.fill: parent
        radius: 8
        color: AppState.accent
        opacity: selected ? 0.25 : (hover.hovered ? 0.12 : 0)
    }

    Row {
        anchors.fill: parent
        anchors.leftMargin: 12
        spacing: 12
        Text {
            anchors.verticalCenter: parent.verticalCenter
            text: glyph
            font.family: AppState.iconFont
            font.pixelSize: glyphSize
            color: AppState.text
        }
        Text {
            anchors.verticalCenter: parent.verticalCenter
            text: label
            font.pixelSize: 13
            color: AppState.text
        }
    }

    HoverHandler { id: hover }
    TapHandler { onTapped: triggered() }
}
