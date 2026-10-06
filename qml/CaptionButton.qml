// Caption-bar button (min/max/close). Red hover for close.
import QtQuick

Rectangle {
    property string symbol
    property bool closeButton: false
    property bool isCaptionButton: true
    signal clicked

    implicitWidth: 44
    implicitHeight: 30
    radius: 6
    color: hover.hovered ? (closeButton ? "#e81123" : AppState.cardBorder) : "transparent"

    Text {
        anchors.centerIn: parent
        text: symbol
        font.family: AppState.iconFont
        font.pixelSize: 11
        color: hover.hovered && closeButton ? "white" : AppState.text
    }
    HoverHandler { id: hover }
    TapHandler { onTapped: clicked() }
}
