// Dark modern slider: thin track with accent fill, white knob that
// lights up while dragged. Drop-in `control` for SettingRow.
import QtQuick
import QtQuick.Controls

Slider {
    id: root
    implicitWidth: 150

    background: Rectangle {
        x: root.leftPadding
        y: root.topPadding + root.availableHeight / 2 - height / 2
        implicitWidth: 150
        implicitHeight: 6
        width: root.availableWidth
        height: 6
        radius: 3
        color: AppState.cardBorder
        Rectangle {
            width: root.visualPosition * parent.width
            height: parent.height
            radius: 3
            color: AppState.accent
        }
    }
    handle: Rectangle {
        x: root.leftPadding + root.visualPosition * (root.availableWidth - width)
        y: root.topPadding + root.availableHeight / 2 - height / 2
        implicitWidth: 16
        implicitHeight: 16
        radius: 8
        color: root.pressed ? AppState.accent : "white"
        border.color: AppState.accent
        border.width: root.pressed ? 0 : 1
    }
}
