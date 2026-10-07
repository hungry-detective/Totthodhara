#pragma once

#include <QObject>
#include <QPointer>
#include <QQuickWindow>
#include <QAbstractNativeEventFilter>

#ifdef Q_OS_WINDOWS
#include <windows.h>
#include <shellapi.h>
#endif

// Docks the shelf as a Windows AppBar (like the taskbar): the reserved
// screen area stays free, so maximized windows stop above the shelf.
// Mirrors the WPF lessons: dock above the real taskbar rect
// (FindWindow("Shell_TrayWnd")), round DPI pixels, re-dock on
// ABN_POSCHANGED. Bottom edge only for now.
class AppBarService : public QObject, public QAbstractNativeEventFilter
{
    Q_OBJECT
    Q_PROPERTY(bool docked READ docked NOTIFY dockedChanged)
public:
    explicit AppBarService(QObject *parent = nullptr);
    ~AppBarService() override;

    bool docked() const { return m_docked; }

    // Pin the given window as a bottom AppBar. Safe to call when visible.
    // Re-calling with the same window only re-applies the reserved rect
    // (used when the shelf height changes).
    Q_INVOKABLE void dock(QQuickWindow *window);
    // Same, but reserves the TOP edge (Shelf position = Top).
    Q_INVOKABLE void dockTop(QQuickWindow *window);
    Q_INVOKABLE void undock();
    // Re-reserve the AppBar rect for the window's CURRENT size.
    Q_INVOKABLE void refresh();

signals:
    void dockedChanged();

protected:
    // Listens for the AppBar callback (taskbar moved/resized -> re-dock).
    bool nativeEventFilter(const QByteArray &eventType, void *message,
                           qintptr *result) override;

private:
#ifdef Q_OS_WINDOWS
    void applyDock();
    static int dpiOf(HWND hwnd);
#endif

    // Raw window pointer would dangle at teardown (the QML window can die
    // first): QPointer auto-nulls so undock/dtor never touches it.
    QPointer<QQuickWindow> m_window;
    // HWND kept separately (Windows only): ABM_REMOVE must still go out
    // after the QWindow is gone, or the OS keeps the reserved strip.
#ifdef Q_OS_WINDOWS
    HWND m_hwnd = nullptr;
#endif
    bool m_docked = false;
#ifdef Q_OS_WINDOWS
    UINT m_edge = 3; // ABE_BOTTOM (3); ABE_TOP (1) for top shelf
    UINT m_callbackMsg = 0;
    bool m_filterInstalled = false;
#endif
};
