cmake_minimum_required(VERSION 3.21)

project(Totthodhara LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)
set(CMAKE_AUTOMOC ON)

# Global Qt install (see environment report). Override per machine with
# -DCMAKE_PREFIX_PATH=C:/Qt/6.8.3/mingw_64
find_package(Qt6 REQUIRED COMPONENTS Core Gui Qml Quick QuickControls2 Network Sql Widgets)

qt_standard_project_setup()

qt_add_executable(Totthodhara
    main.cpp
    backend/AppBarService.h
    backend/AppBarService.cpp
    backend/FullscreenService.h
    backend/FullscreenService.cpp
    backend/SystemMonitorService.h
    backend/SystemMonitorService.cpp
    backend/ClipboardService.h
    backend/ClipboardService.cpp
    backend/StorageService.h
    backend/StorageService.cpp
    backend/ThemeService.h
    backend/ThemeService.cpp
    backend/UpdateService.h
    backend/UpdateService.cpp
)
target_include_directories(Totthodhara PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}")
if(WIN32)
    # App icon: compiled separately (NOT via the builtin .rc rule, whose
    # injected -I flags break windres when the path contains spaces).
    # This standalone windres call needs no -I flags at all.
    add_custom_command(
        OUTPUT "${CMAKE_CURRENT_BINARY_DIR}/app_icon.o"
        COMMAND "${CMAKE_RC_COMPILER}" -i app.rc -o "${CMAKE_CURRENT_BINARY_DIR}/app_icon.o" -O coff
        WORKING_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}/resources"
        DEPENDS "${CMAKE_CURRENT_SOURCE_DIR}/resources/app.rc" "${CMAKE_CURRENT_SOURCE_DIR}/resources/app.ico"
        VERBATIM)
    target_sources(Totthodhara PRIVATE "${CMAKE_CURRENT_BINARY_DIR}/app_icon.o")
    # Portable launcher stub (deploy/Totthodhara.exe): same app icon,
    # separate object file (two targets must never share one OUTPUT).
    add_custom_command(
        OUTPUT "${CMAKE_CURRENT_BINARY_DIR}/stub_icon.o"
        COMMAND "${CMAKE_RC_COMPILER}" -i app.rc -o "${CMAKE_CURRENT_BINARY_DIR}/stub_icon.o" -O coff
        WORKING_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}/resources"
        DEPENDS "${CMAKE_CURRENT_SOURCE_DIR}/resources/app.rc" "${CMAKE_CURRENT_SOURCE_DIR}/resources/app.ico"
        VERBATIM)
endif()
if(WIN32)
    # Stub has zero Qt linkage (pure Win32): starts fast, no console.
    add_executable(TotthodharaLauncher WIN32
        launcher/stub.cpp
        "${CMAKE_CURRENT_BINARY_DIR}/stub_icon.o")
    # Fully static runtime: the stub sits at the portable root where no
    # MinGW DLLs exist (they live in library/, invisible at stub load).
    target_link_options(TotthodharaLauncher PRIVATE -static)
    # Self-updater window (portable installs): runs from the temp stage
    # while it swaps stub+library, so the main app never needs a console.
    # No Q_OBJECT in updater.cpp (lambdas only) — no MOC run needed.
    # qt_add_executable (not plain add_executable): wires the WIN32
    # entry point correctly under MinGW (else qMain goes undefined).
    qt_add_executable(TotthodharaUpdater WIN32
        updater/updater.cpp)
    # Shared source, shared alias: resources/app.png is ALSO embedded by
    # the main app as :/resources/app.png, so this target MUST use the
    # same alias — Qt keys QT_RESOURCE_ALIAS per source file, and a
    # different alias here would silently re-point the main app's icon.
    qt_add_resources(TotthodharaUpdater "updater_icons"
        PREFIX "/"
        FILES resources/app.png
    )
    target_link_libraries(TotthodharaUpdater PRIVATE Qt6::Widgets)
endif()
if(WIN32)
    # PDH (CPU counter) + IP Helper API (network octet counters) + DWM
    # (glass blur) + OLE (raw file-drag DoDragDrop).
    target_link_libraries(Totthodhara PRIVATE pdh iphlpapi dwmapi ole32)
endif()

# AppState.qml contains "pragma Singleton" but the qmldir generator does
# not auto-detect it: without this property the file registers as a
# normal type and every AppState.* binding reads as undefined.
set_source_files_properties(qml/AppState.qml PROPERTIES QT_QML_SINGLETON_TYPE TRUE)

qt_add_qml_module(Totthodhara
    URI Totthodhara
    VERSION 1.0
    # NO_CACHEGEN: QML stays as source in resources (not AOT-compiled),
    # so QML-only edits rebuild in seconds (rcc + link only).
    # Full rebuilds are only needed for C++/CMake changes.
    NO_CACHEGEN
    QML_FILES
        Main.qml
        qml/AppState.qml
        qml/ClipStore.qml
        qml/ClipCard.qml
        qml/ClipMenu.qml
        qml/CaptionButton.qml
        qml/MeterIcon.qml
        qml/PopupItem.qml
        qml/PreviewPopup.qml
        qml/ShelfButton.qml
        qml/SettingRow.qml
        qml/SettingsWindow.qml
        qml/ThemedSegment.qml
        qml/ThemedSlider.qml
        qml/ThemedSpin.qml
        qml/ThemedSwitch.qml
        qml/ThemedCombo.qml
)

qt_add_resources(Totthodhara "app_resources"
    PREFIX "/"
    BASE "${CMAKE_CURRENT_SOURCE_DIR}"
    FILES resources/app.png
)

target_link_libraries(Totthodhara PRIVATE
    Qt6::Core
    Qt6::Gui
    Qt6::Qml
    Qt6::Quick
    Qt6::QuickControls2
    Qt6::Network
    Qt6::Sql
)

set_target_properties(Totthodhara PROPERTIES WIN32_EXECUTABLE TRUE)
