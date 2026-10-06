// Settings dashboard: frameless window, custom caption bar (icon,
// title, minimize / maximize-restore / close), left icon sidebar,
// content cards on the right. Opens centered via open().
import QtQuick
import QtQuick.Controls
import QtQuick.Layouts
import QtQuick.Dialogs
import Totthodhara.Backend 1.0

Window {
    id: settings
    width: 720
    height: 560
    minimumWidth: 600
    minimumHeight: 440
    title: "Totthodhara Settings"
    color: "transparent"

    flags: Qt.FramelessWindowHint

    property bool maximized: false
    property int section: 0
    // Settings content column width, derived from the window itself
    // (720 default = 720 − 158 sidebar − 1 divider − 28 card margins
    // − 36 stack margins). Never depends on ScrollView/StackLayout
    // geometry, so rows always span the panel.
    readonly property int colWidth: width - 223
    // World clock zones: Local + UTC first, then every real-world zone
    // from the system database ordered by UTC offset (Windows-picker
    // style: "(UTC+06:00) Dhaka"). Built once; SysMon.timeZoneList()
    // supplies id/city/offset.
    readonly property var zoneModel: {
        const base = [{ value: "", title: "Local", city: "Local" },
                      { value: "UTC", title: "(UTC) UTC", city: "UTC" }]
        const extra = SysMon.timeZoneList()
        for (let i = 0; i < extra.length; i++) {
            if (extra[i] && extra[i].id !== "UTC")
                base.push({ value: extra[i].id, title: extra[i].offset + " " + extra[i].city, city: extra[i].city })
        }
        return base
    }

    function toggleMaximize() {
        if (maximized) {
            showNormal()
            maximized = false
        } else {
            showMaximized()
            maximized = true
        }
    }

    // Center on screen on first open (frameless windows don't auto-center).
    function open() {
        if (!visible) {
            x = Math.max(0, (Screen.width - width) / 2)
            y = Math.max(0, (Screen.height - height) / 2)
        }
        show()
        raise()
        requestActivate()
        // About always has an answer: silent instant check while empty
        // (cache-backed, no network when fresh). Busy-gated check calls
        // simply resolve with the in-flight one — the button dims + pulses
        // meanwhile, and the 30s/60s network guards always release it.
        if (updateButton.updNote === "" && updateButton.updState === "idle") {
            updateButton.updState = "checking"
            Updater.checkForUpdates()
        }
    }

    // Custom tone picker: any color the user wants.
    ColorDialog {
        id: tonePicker
        title: "Pick shelf tone"
        selectedColor: AppState.customTone
        onAccepted: {
            AppState.customTone = selectedColor
            AppState.shelfTone = "custom"
        }
    }

    Rectangle {
        id: body
        anchors.fill: parent
        radius: settings.maximized ? 0 : 12
        color: AppState.barBg
        border.width: 0

        ColumnLayout {
            anchors.fill: parent
            spacing: 0

            // --- Caption bar (48px): icon left, title centered, controls right ---
            Item {
                id: caption
                Layout.fillWidth: true
                Layout.preferredHeight: 48

                MouseArea {
                    anchors.fill: parent
                    acceptedButtons: Qt.LeftButton
                    onPressed: (mouse) => settings.startSystemMove()
                    onDoubleClicked: settings.toggleMaximize()
                }

                RowLayout {
                    anchors.fill: parent
                    anchors.leftMargin: 14
                    anchors.rightMargin: 8
                    spacing: 10

                    Image {
                        source: "qrc:/resources/app.png"
                        Layout.preferredWidth: 26
                        Layout.preferredHeight: 26
                    }
                    Label {
                        text: "Totthodhara Settings"
                        font.pixelSize: 14
                        font.bold: true
                        color: AppState.text
                        horizontalAlignment: Text.AlignHCenter
                        Layout.fillWidth: true
                    }

                    CaptionButton {
                        symbol: settings.maximized ? "" : ""
                        onClicked: settings.toggleMaximize()
                    }
                    CaptionButton {
                        symbol: ""
                        closeButton: true
                        onClicked: settings.close()
                    }
                }
            }

            Rectangle {
                Layout.fillWidth: true
                Layout.preferredHeight: 1
                color: "transparent"
            }

            // --- Sidebar + content ---
            RowLayout {
                Layout.fillWidth: true
                Layout.fillHeight: true
                spacing: 0

                Column {
                    Layout.preferredWidth: 158
                    Layout.fillHeight: true
                    spacing: 4
                    topPadding: 12
                    leftPadding: 10
                    rightPadding: 10

                    PopupItem {
                        anchors.leftMargin: 10
                        anchors.rightMargin: 10
                        glyph: ""
                        glyphSize: 17
                        label: "Appearance"
                        selected: settings.section === 0
                        onTriggered: settings.section = 0
                    }
                    PopupItem {
                        anchors.leftMargin: 10
                        anchors.rightMargin: 10
                        glyph: ""
                        glyphSize: 17
                        label: "Behavior"
                        selected: settings.section === 1
                        onTriggered: settings.section = 1
                    }
                    PopupItem {
                        anchors.leftMargin: 10
                        anchors.rightMargin: 10
                        glyph: "☰"
                        glyphSize: 17
                        label: "Items on Bar"
                        selected: settings.section === 2
                        onTriggered: settings.section = 2
                    }
                    PopupItem {
                        anchors.leftMargin: 10
                        anchors.rightMargin: 10
                        glyph: "⬤"
                        glyphSize: 17
                        label: "Clipboard Style"
                        selected: settings.section === 3
                        onTriggered: settings.section = 3
                    }
                    PopupItem {
                        anchors.leftMargin: 10
                        anchors.rightMargin: 10
                        glyph: "ⓘ"
                        glyphSize: 17
                        label: "About"
                        selected: settings.section === 4
                        onTriggered: settings.section = 4
                    }
                }

                Rectangle {
                    width: 1
                    Layout.fillHeight: true
                    color: "transparent"
                }

                // Content card.
                Rectangle {
                    Layout.fillWidth: true
                    Layout.fillHeight: true
                    Layout.margins: 14
                    radius: 12
                    color: AppState.cardBg
                    border.width: 0

                    StackLayout {
                        anchors.fill: parent
                        anchors.margins: 18
                        currentIndex: settings.section

                        // --- Appearance ---
                        ScrollView {
                            id: appearanceScroll
                            ScrollBar.vertical: ScrollBar { policy: ScrollBar.AlwaysOff }
                            ColumnLayout {
                                width: settings.colWidth
                                spacing: 10
                                Label {
                                    text: "APPEARANCE"
                                    color: AppState.muted
                                    font.pixelSize: 11
                                    font.bold: true
                                    font.letterSpacing: 1.2
                                }
                                SettingRow {
                                    Layout.fillWidth: true
                                    title: "Theme"
                                    subtitle: "System follows Windows"
                                    control: ThemedSegment {
                                        options: ["System", "Dark", "Light"]
                                        current: ["System", "Dark", "Light"].indexOf(AppState.theme)
                                        onPicked: (i) => AppState.theme = ["System", "Dark", "Light"][i]
                                    }
                                }
                                SettingRow {
                                    Layout.fillWidth: true
                                    title: "Bar size"
                                    subtitle: "Shelf height"
                                    control: ThemedSegment {
                                        options: ["Small", "Medium", "Large"]
                                        current: ["Small", "Medium", "Large"].indexOf(AppState.barSize)
                                        onPicked: (i) => AppState.barSize = ["Small", "Medium", "Large"][i]
                                    }
                                }
                                SettingRow {
                                    Layout.fillWidth: true
                                    title: "Shelf position"
                                    subtitle: "Top or bottom edge"
                                    control: ThemedSegment {
                                        options: ["Bottom", "Top"]
                                        current: ["Bottom", "Top"].indexOf(AppState.shelfPosition)
                                        onPicked: (i) => AppState.shelfPosition = ["Bottom", "Top"][i]
                                    }
                                }
                                SettingRow {
                                    Layout.fillWidth: true
                                    title: "Shelf alignment"
                                    subtitle: "New clips land left, centered, or right"
                                    control: ThemedSegment {
                                        options: ["Left", "Center", "Right"]
                                        current: ["Left", "Center", "Right"].indexOf(AppState.alignment)
                                        onPicked: (i) => AppState.alignment = ["Left", "Center", "Right"][i]
                                    }
                                }
                                RowLayout {
                                    Layout.fillWidth: true
                                    spacing: 10
                                    Rectangle {
                                        Layout.fillWidth: true
                                        implicitHeight: 46
                                        radius: 10
                                        color: AppState.barBg
                                        border.width: 0
                                        ColumnLayout {
                                            anchors.fill: parent
                                            anchors.leftMargin: 12
                                            anchors.rightMargin: 12
                                            anchors.topMargin: 8
                                            anchors.bottomMargin: 8
                                            spacing: 4
                                            RowLayout {
                                                Layout.fillWidth: true
                                                spacing: 10
                                                Label {
                                                    text: "Rounded shelf"
                                                    font.bold: true
                                                    font.pixelSize: 11
                                                    color: AppState.text
                                                    Layout.fillWidth: true
                                                    Layout.alignment: Qt.AlignVCenter
                                                }
                                                ThemedSwitch {
                                                    compact: true
                                                    Layout.alignment: Qt.AlignVCenter
                                                    checked: AppState.roundedCorners
                                                    onToggled: AppState.roundedCorners = checked
                                                }
                                            }
                                        }
                                    }
                                }
                                RowLayout {
                                    Layout.fillWidth: true
                                    visible: AppState.theme === "System"
                                    spacing: 10
                                    // Narrower fixed card: toggle sits closer to
                                    // its label; the transparency card takes
                                    // the freed width.
                                    Rectangle {
                                        Layout.preferredWidth: 200
                                        implicitHeight: 46
                                        radius: 10
                                        color: AppState.barBg
                                        border.width: 0
                                        RowLayout {
                                            anchors.fill: parent
                                            anchors.leftMargin: 12
                                            anchors.rightMargin: 12
                                            spacing: 10
                                            Label {
                                                text: "Transparent bar"
                                                font.bold: true
                                                font.pixelSize: 11
                                                color: AppState.text
                                                Layout.fillWidth: true
                                                Layout.alignment: Qt.AlignVCenter
                                            }
                                            ThemedSwitch {
                                                compact: true
                                                Layout.alignment: Qt.AlignVCenter
                                                checked: AppState.transparentBar
                                                onToggled: AppState.transparentBar = checked
                                            }
                                        }
                                    }
                                    Rectangle {
                                        Layout.fillWidth: true
                                        implicitHeight: 46
                                        radius: 10
                                        color: AppState.barBg
                                        border.width: 0
                                        visible: AppState.transparentBar
                                        RowLayout {
                                            anchors.fill: parent
                                            anchors.leftMargin: 12
                                            anchors.rightMargin: 12
                                            spacing: 10
                                            Label {
                                                text: "Bar opacity"
                                                font.bold: true
                                                font.pixelSize: 11
                                                color: AppState.text
                                                Layout.fillWidth: true
                                                Layout.alignment: Qt.AlignVCenter
                                            }
                                            ThemedSlider {
                                                id: glassSlider
                                                Layout.preferredWidth: 100
                                                from: 0
                                                to: 100
                                                stepSize: 1
                                                value: Math.round(AppState.glassAlpha * 100)
                                                onMoved: {
                                                    AppState.glassAlpha = Math.max(0.05, value / 100)
                                                    glassSpin.syncFrom(value)
                                                }
                                            }
                                            // Direct percent input: mirrors the slider both ways.
                                            ThemedSpin {
                                                id: glassSpin
                                                Layout.preferredWidth: 64
                                                from: 0
                                                to: 100
                                                value: Math.round(AppState.glassAlpha * 100)
                                                onCommitted: (v) => AppState.glassAlpha = Math.max(0.05, v / 100)
                                            }
                                        }
                                    }
                                }
                                // Next line: tone dots + tone intensity bar, so the
                                // tone color strength tunes like transparency does.
                                // System theme only: Dark/Light keep signature looks.
                                RowLayout {
                                    Layout.fillWidth: true
                                    visible: AppState.theme === "System"
                                    spacing: 10
                                    Rectangle {
                                        id: toneDotsCard
                                        Layout.preferredWidth: 250
                                        implicitHeight: toneDotsCol.implicitHeight + 16
                                        radius: 10
                                        color: AppState.barBg
                                        border.width: 0
                                        ColumnLayout {
                                            id: toneDotsCol
                                            anchors.left: parent.left
                                            anchors.right: parent.right
                                            anchors.top: parent.top
                                            anchors.leftMargin: 12
                                            anchors.rightMargin: 12
                                            anchors.topMargin: 8
                                            spacing: 8
                                            Label {
                                                Layout.fillWidth: true
                                                text: "Shelf tone"
                                                font.bold: true
                                                font.pixelSize: 11
                                                color: AppState.text
                                            }
                                            Label {
                                                Layout.fillWidth: true
                                                text: "2nd follows Windows • + is any color"
                                                font.pixelSize: 10
                                                color: AppState.muted
                                            }
                                            Flow {
                                                Layout.fillWidth: true
                                                spacing: 8
                                                Repeater {
                                                    model: [
                                                        { value: "auto", c: "" },
                                                        { value: "windows", c: "" },
                                                        { value: "dark", c: "#202020" },
                                                        { value: "grey", c: "#3a3a3d" },
                                                        { value: "light", c: "#f2f2f2" },
                                                        { value: "blue", c: "#274e7d" },
                                                        { value: "custom", c: "" }
                                                    ]
                                                    delegate: Rectangle {
                                                        width: modelData.value === "auto" ? 46 : 26
                                                        height: 26
                                                        radius: 13
                                                        color: modelData.value === "auto"
                                                            ? (AppState.shelfTone === "auto" ? AppState.accent : AppState.cardBg)
                                                            : modelData.value === "windows" ? ThemeWatcher.systemAccent
                                                            : modelData.value === "custom" ? AppState.customTone : modelData.c
                                                        border.color: AppState.shelfTone === modelData.value ? AppState.accent : AppState.cardBorder
                                                        border.width: AppState.shelfTone === modelData.value ? 2 : 1
                                                        Text {
                                                            anchors.centerIn: parent
                                                            visible: modelData.value === "auto" || modelData.value === "custom"
                                                            text: modelData.value === "auto" ? "Auto" : "+"
                                                            font.pixelSize: modelData.value === "auto" ? 11 : 15
                                                            font.bold: true
                                                            color: modelData.value === "auto"
                                                                ? (AppState.shelfTone === "auto" ? "white" : AppState.text)
                                                                : "white"
                                                        }
                                                        TapHandler {
                                                            onTapped: modelData.value === "custom"
                                                                ? tonePicker.open()
                                                                : AppState.shelfTone = modelData.value
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    Rectangle {
                                        Layout.fillWidth: true
                                        implicitHeight: toneDotsCard.implicitHeight
                                        radius: 10
                                        color: AppState.barBg
                                        border.width: 0
                                        RowLayout {
                                            anchors.fill: parent
                                            anchors.leftMargin: 12
                                            anchors.rightMargin: 12
                                            spacing: 10
                                            Label {
                                                text: "Intensity"
                                                font.bold: true
                                                font.pixelSize: 11
                                                color: AppState.text
                                                Layout.fillWidth: true
                                                Layout.alignment: Qt.AlignVCenter
                                            }
                                            ThemedSlider {
                                                id: toneSlider
                                                Layout.preferredWidth: 70
                                                from: 0
                                                to: 100
                                                stepSize: 1
                                                value: Math.round(AppState.toneIntensity * 100)
                                                onMoved: {
                                                    AppState.toneIntensity = value / 100
                                                    toneSpin.syncFrom(value)
                                                }
                                            }
                                            // Direct percent input: mirrors the slider both ways.
                                            ThemedSpin {
                                                id: toneSpin
                                                Layout.preferredWidth: 64
                                                from: 0
                                                to: 100
                                                value: Math.round(AppState.toneIntensity * 100)
                                                onCommitted: (v) => AppState.toneIntensity = v / 100
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        // --- Behavior ---
                        ScrollView {
                            id: behaviorScroll
                            ScrollBar.vertical: ScrollBar { policy: ScrollBar.AlwaysOff }
                            ColumnLayout {
                                width: settings.colWidth
                                spacing: 10
                                Label {
                                    text: "BEHAVIOR"
                                    color: AppState.muted
                                    font.pixelSize: 11
                                    font.bold: true
                                    font.letterSpacing: 1.2
                                }
                                SettingRow {
                                    Layout.fillWidth: true
                                    title: "Check updates on start"
                                    subtitle: "Silent unless an update exists"
                                    control: ThemedSwitch {
                                        checked: AppState.checkUpdatesOnStart
                                        onToggled: AppState.checkUpdatesOnStart = checked
                                    }
                                }
                                SettingRow {
                                    Layout.fillWidth: true
                                    title: "Run at Windows startup"
                                    subtitle: "Start with Windows login"
                                    control: ThemedSwitch {
                                        checked: AppState.runAtStartup
                                        onToggled: {
                                            AppState.runAtStartup = checked
                                            Updater.setRunAtStartup(checked)
                                        }
                                    }
                                }
                                Label {
                                    text: "STORAGE"
                                    color: AppState.muted
                                    font.pixelSize: 11
                                    font.bold: true
                                    font.letterSpacing: 1.2
                                }
                                SettingRow {
                                    Layout.fillWidth: true
                                    title: "Max history items"
                                    subtitle: "Pinned and snippets are exempt"
                                    control: ThemedSpin {
                                        from: 1
                                        to: 500
                                        value: AppState.maxHistoryItems
                                        onCommitted: (v) => AppState.maxHistoryItems = v
                                    }
                                }
                                SettingRow {
                                    Layout.fillWidth: true
                                    title: "Max file size (MB)"
                                    subtitle: "0 = no limit"
                                    control: ThemedSpin {
                                        from: 0
                                        to: 500
                                        value: AppState.maxFileSizeMB
                                        onCommitted: (v) => AppState.maxFileSizeMB = v
                                    }
                                }
                                SettingRow {
                                    Layout.fillWidth: true
                                    title: "Auto-clean files"
                                    subtitle: "Drop old files after N hours (0 = off)"
                                    control: ThemedSpin {
                                        from: 0
                                        to: 720
                                        value: AppState.autoCleanHours
                                        onCommitted: (v) => AppState.autoCleanHours = v
                                    }
                                }
                            }
                        }


                        // --- Items on Bar ---
                        ScrollView {
                            id: itemsOnBarScroll
                            ScrollBar.vertical: ScrollBar { policy: ScrollBar.AlwaysOff }
                            ColumnLayout {
                                width: settings.colWidth
                                spacing: 10
                                Label {
                                    text: "METERS"
                                    color: AppState.muted
                                    font.pixelSize: 11
                                    font.bold: true
                                    font.letterSpacing: 1.2
                                }
                                SettingRow {
                                    Layout.fillWidth: true
                                    title: "Hardware meter"
                                    subtitle: "CPU and RAM group"
                                    control: ThemedSwitch {
                                        checked: AppState.showCpuRam
                                        onToggled: AppState.showCpuRam = checked
                                    }
                                }
                                SettingRow {
                                    Layout.fillWidth: true
                                    title: "Speed meter"
                                    subtitle: "Upload and download group"
                                    control: ThemedSwitch {
                                        checked: AppState.showSpeed
                                        onToggled: AppState.showSpeed = checked
                                    }
                                }
                                Label {
                                    text: "CLIPBOARD"
                                    color: AppState.muted
                                    font.pixelSize: 11
                                    font.bold: true
                                    font.letterSpacing: 1.2
                                }
                                SettingRow {
                                    Layout.fillWidth: true
                                    title: "Hide clipboard items"
                                    subtitle: "Toolbar-only compact mode"
                                    control: ThemedSwitch {
                                        checked: AppState.hideClipboard
                                        onToggled: AppState.hideClipboard = checked
                                    }
                                }
                                Label {
                                    text: "CLOCK"
                                    color: AppState.muted
                                    font.pixelSize: 11
                                    font.bold: true
                                    font.letterSpacing: 1.2
                                }
                                SettingRow {
                                    Layout.fillWidth: true
                                    title: "World clock"
                                    subtitle: "Master switch for clock pills"
                                    control: ThemedSwitch {
                                        checked: AppState.showWorldClock
                                        onToggled: AppState.showWorldClock = checked
                                    }
                                }
                                SettingRow {
                                    Layout.fillWidth: true
                                    title: "Clock one"
                                    subtitle: "First clock pill"
                                    control: RowLayout {
                                        spacing: 8
                                        ThemedCombo {
                                            Layout.preferredWidth: 230
                                            items: settings.zoneModel
                                            value: AppState.zoneA
                                            onPicked: (v) => AppState.zoneA = v
                                        }
                                        ThemedSwitch {
                                            compact: true
                                            Layout.alignment: Qt.AlignVCenter
                                            checked: AppState.showClock1
                                            onToggled: AppState.showClock1 = checked
                                        }
                                    }
                                }
                                SettingRow {
                                    Layout.fillWidth: true
                                    title: "Clock two"
                                    subtitle: "Second clock pill"
                                    control: RowLayout {
                                        spacing: 8
                                        ThemedCombo {
                                            Layout.preferredWidth: 230
                                            items: settings.zoneModel
                                            value: AppState.zoneB
                                            onPicked: (v) => AppState.zoneB = v
                                        }
                                        ThemedSwitch {
                                            compact: true
                                            Layout.alignment: Qt.AlignVCenter
                                            checked: AppState.showClock2
                                            onToggled: AppState.showClock2 = checked
                                        }
                                    }
                                }
                            }
                        }

                        // --- Clipboard Style ---
                        ScrollView {
                            id: pillStyleScroll
                            ScrollBar.vertical: ScrollBar { policy: ScrollBar.AlwaysOff }
                            ColumnLayout {
                                width: settings.colWidth
                                spacing: 10
                                Label {
                                    text: "CLIPBOARD STYLE"
                                    color: AppState.muted
                                    font.pixelSize: 11
                                    font.bold: true
                                    font.letterSpacing: 1.2
                                }
                                // Single-choice radio grid (3 per row): one pillStyle
                                // value at a time, selected card gets the
                                // accent ring + filled tick.
                                GridLayout {
                                    Layout.fillWidth: true
                                    columns: 3
                                    rowSpacing: 10
                                    columnSpacing: 10
                                    Repeater {
                                        model: [
                                            { value: "border", title: "Accent border", sub: "1px accent glow border" },
                                            { value: "tint", title: "Accent tint", sub: "Faint accent-tinted back" },
                                            { value: "hover", title: "Hover lift", sub: "Scale up + glow on hover" },
                                            { value: "contrast", title: "Contrast fill", sub: "Lighter/darker than bar" },
                                            { value: "borderless", title: "Borderless", sub: "Tinted fill, no edge" },
                                            { value: "shadow", title: "Soft shadow", sub: "Drop shadow under pill" }
                                        ]
                                        delegate: Rectangle {
                                            Layout.fillWidth: true
                                            Layout.preferredHeight: 58
                                            radius: 10
                                            color: AppState.pillStyle === modelData.value ? Qt.rgba(AppState.accent.r, AppState.accent.g, AppState.accent.b, 0.18) : AppState.barBg
                                            border.width: 0
                                            Column {
                                                anchors.left: parent.left
                                                anchors.leftMargin: 12
                                                anchors.right: tick.left
                                                anchors.rightMargin: 8
                                                anchors.verticalCenter: parent.verticalCenter
                                                spacing: 2
                                                Label {
                                                    width: parent.width
                                                    text: modelData.title
                                                    elide: Text.ElideRight
                                                    font.bold: true
                                                    font.pixelSize: 12
                                                    color: AppState.text
                                                }
                                                Label {
                                                    width: parent.width
                                                    text: modelData.sub
                                                    elide: Text.ElideRight
                                                    color: AppState.muted
                                                    font.pixelSize: 11
                                                }
                                            }
                                            // Radio tick: hollow ring, accent
                                            // fill + white tick when picked.
                                            Rectangle {
                                                id: tick
                                                anchors.right: parent.right
                                                anchors.rightMargin: 10
                                                anchors.verticalCenter: parent.verticalCenter
                                                width: 18
                                                height: 18
                                                radius: 9
                                                color: AppState.pillStyle === modelData.value ? AppState.accent : "transparent"
                                                border.color: AppState.pillStyle === modelData.value ? AppState.accent : AppState.muted
                                                border.width: 2
                                                Text {
                                                    anchors.centerIn: parent
                                                    visible: AppState.pillStyle === modelData.value
                                                    text: "✓"
                                                    font.pixelSize: 11
                                                    font.bold: true
                                                    color: "white"
                                                }
                                            }
                                            TapHandler { onTapped: AppState.pillStyle = modelData.value }
                                        }
                                    }
                                }
                                Label {
                                    text: "THUMBNAIL FRAME"
                                    color: AppState.muted
                                    font.pixelSize: 11
                                    font.bold: true
                                    font.letterSpacing: 1.2
                                }
                                // Thumbnail frame picker: same radio-card
                                // language, drives AppState.thumbStyle.
                                GridLayout {
                                    Layout.fillWidth: true
                                    columns: 3
                                    rowSpacing: 10
                                    columnSpacing: 10
                                    Repeater {
                                        model: [
                                            { value: "accent", title: "Accent border", sub: "1px accent edge" },
                                            { value: "none", title: "Borderless", sub: "No edge, flat tile" },
                                            { value: "shadow", title: "Soft shadow", sub: "Floating offset shade" },
                                            { value: "line", title: "Subtle line", sub: "Theme hairline edge" },
                                            { value: "glow", title: "Accent glow", sub: "Thick edge + halo" }
                                        ]
                                        delegate: Rectangle {
                                            Layout.fillWidth: true
                                            Layout.preferredHeight: 58
                                            radius: 10
                                            color: AppState.thumbStyle === modelData.value ? Qt.rgba(AppState.accent.r, AppState.accent.g, AppState.accent.b, 0.18) : AppState.barBg
                                            border.width: 0
                                            Column {
                                                anchors.left: parent.left
                                                anchors.leftMargin: 12
                                                anchors.right: thumbTick.left
                                                anchors.rightMargin: 8
                                                anchors.verticalCenter: parent.verticalCenter
                                                spacing: 2
                                                Label {
                                                    width: parent.width
                                                    text: modelData.title
                                                    elide: Text.ElideRight
                                                    font.bold: true
                                                    font.pixelSize: 12
                                                    color: AppState.text
                                                }
                                                Label {
                                                    width: parent.width
                                                    text: modelData.sub
                                                    elide: Text.ElideRight
                                                    color: AppState.muted
                                                    font.pixelSize: 11
                                                }
                                            }
                                            Rectangle {
                                                id: thumbTick
                                                anchors.right: parent.right
                                                anchors.rightMargin: 10
                                                anchors.verticalCenter: parent.verticalCenter
                                                width: 18
                                                height: 18
                                                radius: 9
                                                color: AppState.thumbStyle === modelData.value ? AppState.accent : "transparent"
                                                border.color: AppState.thumbStyle === modelData.value ? AppState.accent : AppState.muted
                                                border.width: 2
                                                Text {
                                                    anchors.centerIn: parent
                                                    visible: AppState.thumbStyle === modelData.value
                                                    text: "✓"
                                                    font.pixelSize: 11
                                                    font.bold: true
                                                    color: "white"
                                                }
                                            }
                                            TapHandler { onTapped: AppState.thumbStyle = modelData.value }
                                        }
                                    }
                                }
                                Label {
                                    text: "PILL SHAPE"
                                    color: AppState.muted
                                    font.pixelSize: 11
                                    font.bold: true
                                    font.letterSpacing: 1.2
                                }
                                SettingRow {
                                    Layout.fillWidth: true
                                    title: "Pill-shaped meters"
                                    subtitle: "Card pill behind hardware and speed meters"
                                    control: ThemedSwitch {
                                        checked: AppState.pillMeters
                                        onToggled: AppState.pillMeters = checked
                                    }
                                }
                            }
                        }

                        // --- About ---
                        ScrollView {
                            id: aboutScroll
                            ScrollBar.vertical: ScrollBar { policy: ScrollBar.AlwaysOff }
                            ColumnLayout {
                                width: settings.colWidth
                                spacing: 10
                                Label {
                                    text: "ABOUT"
                                    color: AppState.muted
                                    font.pixelSize: 11
                                    font.bold: true
                                    font.letterSpacing: 1.2
                                }
                                Image {
                                    Layout.alignment: Qt.AlignHCenter
                                    Layout.preferredWidth: 72
                                    Layout.preferredHeight: 72
                                    source: "qrc:/resources/app.png"
                                }
                                Label {
                                    Layout.alignment: Qt.AlignHCenter
                                    text: "Totthodhara"
                                    font.pixelSize: 22
                                    font.bold: true
                                    color: AppState.text
                                }
                                Label {
                                    Layout.alignment: Qt.AlignHCenter
                                    text: updateButton.updLatest === ""
                                        ? "Version " + Qt.application.version
                                        : "Version " + Qt.application.version + "  •  latest " + updateButton.updLatest
                                    color: AppState.text
                                    font.pixelSize: 12
                                    font.bold: true
                                }
                                Label {
                                    Layout.alignment: Qt.AlignHCenter
                                    Layout.preferredWidth: 420
                                    text: "Your second clipboard — every copy lives on the shelf, one click away from any app."
                                    color: AppState.muted
                                    horizontalAlignment: Text.AlignHCenter
                                    wrapMode: Text.WordWrap
                                }
                                RowLayout {
                                    Layout.alignment: Qt.AlignHCenter
                                    spacing: 10
                                    // Plain tap-rectangle (NOT a Button): same look,
                                    // wired through TapHandler like every other
                                    // working control in this app.
                                    Rectangle {
                                        id: updateButton
                                        property string updState: "idle"
                                        property string updVersion: ""
                                        property string updLatest: ""
                                        property string updNote: ""
                                        Layout.preferredWidth: 210
                                        Layout.preferredHeight: 34
                                        radius: 8
                                        enabled: (updState === "idle" || updState === "available") && !Updater.busy
                                        // Disabled reads disabled (a live-looking dead
                                        // button was reported as "nothing happens").
                                        opacity: enabled ? 1 : 0.45
                                        color: tap.pressed ? Qt.darker(AppState.accent, 1.15)
                                            : tapHover.hovered && enabled ? Qt.lighter(AppState.accent, 1.1) : AppState.accent
                                        Text {
                                            anchors.centerIn: parent
                                            text: updateButton.updState === "checking" ? "Checking…"
                                                : updateButton.updState === "available" ? "Download " + updateButton.updVersion + " & restart"
                                                : updateButton.updState === "downloading" ? "Downloading…"
                                                : updateButton.updState === "installing" ? "Installing…"
                                                : "Check for updates"
                                            font.bold: true
                                            font.pixelSize: 13
                                            color: "white"
                                        }
                                        HoverHandler { id: tapHover }
                                        TapHandler {
                                            id: tap
                                            enabled: updateButton.enabled
                                            onTapped: {
                                                if (updateButton.updState === "available")
                                                    Updater.downloadAndInstall()
                                                else {
                                                    updateButton.updState = "checking"
                                                    Updater.checkForUpdates()
                                                }
                                            }
                                        }
                                    }
                                    // Checking pulse: visible motion while the
                                    // check is in flight, so a tap always answers.
                                    Rectangle {
                                        Layout.preferredWidth: 12
                                        Layout.preferredHeight: 12
                                        Layout.alignment: Qt.AlignVCenter
                                        radius: 6
                                        color: AppState.accent
                                        visible: updateButton.updState === "checking"
                                        opacity: 0.3
                                        SequentialAnimation on opacity {
                                            running: updateButton.updState === "checking"
                                            loops: Animation.Infinite
                                            NumberAnimation { to: 1; duration: 350 }
                                            NumberAnimation { to: 0.3; duration: 350 }
                                        }
                                    }
                                }
                                ProgressBar {
                                    id: dlBar
                                    Layout.alignment: Qt.AlignHCenter
                                    Layout.preferredWidth: 260
                                    implicitWidth: 260
                                    visible: updateButton.updState === "downloading"
                                        || updateButton.updState === "installing"
                                    from: 0
                                    to: 100
                                    value: updateButton.updState === "installing" ? 100 : dlPct
                                    property real dlPct: 0
                                    background: Rectangle {
                                        implicitWidth: 260
                                        implicitHeight: 8
                                        radius: 4
                                        color: AppState.cardBg
                                    }
                                    contentItem: Item {
                                        Rectangle {
                                            width: parent.width * (dlBar.value / 100)
                                            height: parent.height
                                            radius: 4
                                            color: AppState.accent
                                        }
                                    }
                                }
                                Label {
                                    id: updateLabel
                                    Layout.alignment: Qt.AlignHCenter
                                    Layout.preferredWidth: 420
                                    text: updateButton.updNote
                                    horizontalAlignment: Text.AlignHCenter
                                    wrapMode: Text.WordWrap
                                    color: AppState.muted
                                }
                                Connections {
                                    target: Updater
                                    function onCheckFinished(available, version, notes) {
                                        updateButton.updLatest = version
                                        if (available) {
                                            updateButton.updState = "available"
                                            updateButton.updVersion = version
                                            const first = String(notes).split("\n")[0].substring(0, 140)
                                            updateButton.updNote = "New in " + version + (first ? ": " + first : "")
                                        } else {
                                            updateButton.updState = "idle"
                                            updateButton.updNote = "You are up to date."
                                        }
                                    }
                                    function onCheckFailed(error) {
                                        updateButton.updState = "idle"
                                        updateButton.updNote = error
                                    }
                                    function onDownloadProgress(received, total) {
                                        updateButton.updState = "downloading"
                                        const pct = total > 0 ? Math.round(received / total * 100) : 0
                                        dlBar.dlPct = pct
                                        const got = (received / 1048576).toFixed(1) + " MB"
                                        updateButton.updNote = total > 0
                                            ? "Downloading… " + pct + "%  (" + got + " / " + (total / 1048576).toFixed(1) + " MB)"
                                            : "Downloading… " + got
                                    }
                                    function onInstallStarted() {
                                        updateButton.updState = "installing"
                                        dlBar.dlPct = 100
                                        updateButton.updNote = "Verified — installing, the app will restart…"
                                    }
                                    function onInstallFailed(error) {
                                        updateButton.updState = "idle"
                                        updateButton.updNote = error
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        // Resize grip (bottom-right corner).
        MouseArea {
            anchors.right: parent.right
            anchors.bottom: parent.bottom
            width: 20
            height: 20
            cursorShape: Qt.SizeFDiagCursor
            enabled: !settings.maximized
            onPressed: (mouse) => settings.startSystemResize(Qt.RightEdge | Qt.BottomEdge)
        }
    }
}
