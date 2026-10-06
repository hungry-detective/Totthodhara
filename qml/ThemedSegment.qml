// Segmented picker: a row of pill buttons with exactly one selected.
// Faster than a dropdown for 2-3 options. Drop-in `control` for SettingRow:
// options + current index in, picked(index) out.
import QtQuick
import QtQuick.Controls
import QtQuick.Layouts

RowLayout {
    id: root
    property var options: []
    property int current: 0
    signal picked(int index)

    spacing: 6

    Repeater {
        model: root.options
        Button {
            text: modelData
            checked: root.current === index
            onClicked: root.picked(index)
            background: Rectangle {
                implicitWidth: 78
                implicitHeight: 30
                radius: 8
                color: parent.checked ? AppState.accent : (parent.hovered || parent.pressed ? Qt.lighter(AppState.cardBg, 1.1) : AppState.cardBg)
                border.color: parent.checked ? AppState.accent : AppState.cardBorder
                border.width: parent.checked ? 0 : 1
            }
            contentItem: Label {
                text: parent.text
                font.pixelSize: 12
                font.bold: parent.checked
                color: parent.checked ? "white" : AppState.text
                horizontalAlignment: Text.AlignHCenter
                verticalAlignment: Text.AlignVCenter
            }
        }
    }
}
