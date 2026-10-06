// Flat shelf button with guaranteed theme-colored Fluent glyph.
// A child Text is used (not contentItem): the native Windows style ignores
// contentItem overrides, which left these buttons dark-on-dark.
import QtQuick
import QtQuick.Controls

Button {
    property string glyph
    property int glyphSize: 16

    text: ""
    flat: true
    implicitWidth: 32
    implicitHeight: 24

    Rectangle {
        anchors.centerIn: parent
        width: 30
        height: 24
        radius: 8
        color: AppState.text
        opacity: hover.hovered ? 0.12 : 0
        Behavior on opacity {
            NumberAnimation { duration: 120 }
        }
    }
    HoverHandler { id: hover }

    Text {
        anchors.centerIn: parent
        text: glyph
        font.family: AppState.iconFont
        font.pixelSize: glyphSize
        color: AppState.text
    }
}
