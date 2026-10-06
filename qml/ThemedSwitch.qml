// Dark modern toggle: accent track when on, quiet gray when off,
// sliding white knob. Drop-in Switch replacement.
import QtQuick
import QtQuick.Controls

Switch {
    id: root
    // Compact variant for tight rows (mini-cards): 32x18 track.
    property bool compact: false
    background: null
    padding: 0
    leftPadding: 0
    rightPadding: 0
    topPadding: 0
    bottomPadding: 0
    indicator: Rectangle {
        implicitWidth: root.compact ? 32 : 40
        implicitHeight: root.compact ? 18 : 22
        x: root.leftPadding
        y: root.height / 2 - height / 2
        radius: height / 2
        color: root.checked ? AppState.accent : AppState.cardBorder
        Behavior on color {
            ColorAnimation { duration: 150 }
        }
        Rectangle {
            x: root.checked ? parent.width - width - 3 : 3
            y: 3
            width: root.compact ? 12 : 16
            height: root.compact ? 12 : 16
            radius: width / 2
            color: "white"
            Behavior on x {
                NumberAnimation { duration: 150; easing.type: Easing.OutCubic }
            }
        }
    }
}
