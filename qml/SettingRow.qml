// Dashboard card row: rounded tile with a left accent bar,
// title/subtitle, control pinned to the right corner. Interior uses
// plain anchors (not nested layouts), so every control lands on one
// shared right edge no matter its width.
import QtQuick
import QtQuick.Controls

Rectangle {
    property string title
    property string subtitle
    property Component control

    radius: 10
    color: AppState.barBg
    border.width: 0
    implicitHeight: subtitle !== "" ? 58 : 46

    Column {
        id: textCol
        anchors.left: parent.left
        anchors.leftMargin: 14
        anchors.right: slot.left
        anchors.rightMargin: 12
        anchors.verticalCenter: parent.verticalCenter
        spacing: 2
        Label {
            width: textCol.width
            text: title
            elide: Text.ElideRight
            font.bold: true
            font.pixelSize: 13
            color: AppState.text
        }
        Label {
            visible: subtitle !== ""
            width: textCol.width
            text: subtitle
            elide: Text.ElideRight
            color: AppState.muted
            font.pixelSize: 12
        }
    }
    // Control slot: right-anchored, grows leftward as the control loads,
    // so every control shares one right edge.
    Item {
        id: slot
        anchors.right: parent.right
        anchors.rightMargin: 12
        anchors.verticalCenter: parent.verticalCenter
        width: slotLoader.implicitWidth
        height: slotLoader.implicitHeight
        Loader {
            id: slotLoader
            anchors.fill: parent
            sourceComponent: control
        }
    }
}
