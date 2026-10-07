// Dark modern stepper: themed field, centered value, dark step buttons.
// Two-way pattern: parent binds `value:` for the initial number and pushes
// external changes via syncFrom(v); every local change (typing, arrows)
// emits committed(v) and the parent writes it back to the source. The first
// local edit replaces the `value:` binding (assigning a bound property
// always does) — by design all writers then push explicitly through
// syncFrom/committed, so slider <-> box can never desync.
import QtQuick
import QtQuick.Controls

SpinBox {
    id: root
    font.pixelSize: 12
    signal committed(int v)
    property bool _syncing: false
    function syncFrom(v) {
        _syncing = true
        root.value = Math.max(root.from, Math.min(root.to, v))
        if (!spinEdit.activeFocus)
            spinEdit.text = root.value
        _syncing = false
    }
    onValueChanged: {
        if (_syncing)
            return
        if (!spinEdit.activeFocus && spinEdit.text !== String(root.value))
            spinEdit.text = root.value
        // Echoes of external updates write the identical value back
        // (parent no-ops); real local edits land in the source here.
        committed(root.value)
    }

    background: Rectangle {
        implicitWidth: 100
        implicitHeight: 30
        radius: 8
        color: AppState.cardBg
        border.color: root.activeFocus ? AppState.accent : AppState.cardBorder
        border.width: root.activeFocus ? 2 : 1
    }
    contentItem: TextInput {
        id: spinEdit
        text: root.value
        color: AppState.text
        selectionColor: AppState.accent
        selectedTextColor: "white"
        horizontalAlignment: Text.AlignHCenter
        verticalAlignment: Text.AlignVCenter
        validator: IntValidator {
            bottom: 0
            top: 100000
        }
        // Live commit: every user keystroke applies immediately (typing "1"
        // of "15" already moves the shelf). Focus-guarded, so programmatic
        // text syncs never recommit and never break the value binding.
        // Empty/partial text is ignored until it parses.
        onTextChanged: {
            if (!spinEdit.activeFocus || _syncing)
                return
            const v = parseInt(text, 10)
            if (!isNaN(v))
                root.value = Math.max(root.from, Math.min(root.to, v))
        }
        onEditingFinished: {
            const v = parseInt(text, 10)
            if (!isNaN(v))
                root.value = Math.max(root.from, Math.min(root.to, v))
            spinEdit.text = root.value
            focus = false
        }
    }
    up.indicator: Label {
        x: root.width - width - 6
        y: 3
        width: 22
        height: (root.height - 6) / 2
        text: "▴"
        font.pixelSize: 10
        color: root.up.hovered ? AppState.accent : AppState.muted
        horizontalAlignment: Text.AlignHCenter
        verticalAlignment: Text.AlignVCenter
    }
    down.indicator: Label {
        x: root.width - width - 6
        y: root.height / 2
        width: 22
        height: (root.height - 6) / 2
        text: "▾"
        font.pixelSize: 10
        color: root.down.hovered ? AppState.accent : AppState.muted
        horizontalAlignment: Text.AlignHCenter
        verticalAlignment: Text.AlignVCenter
    }
}
