#include "AppBarService.h"

#include <QCoreApplication>
#include <QDebug>

AppBarService::AppBarService(QObject *parent)
    : QObject(parent)
{
}

AppBarService::~AppBarService()
{
    undock();
    if (QCoreApplication::instance())
        QCoreApplication::instance()->removeNativeEventFilter(this);
}

void AppBarService::dock(QQuickWindow *window)
{
#ifdef Q_OS_WINDOWS
    if (!window)
        return;
    if (!m_filterInstalled && QCoreApplication::instance()) {
        QCoreApplication::instance()->installNativeEventFilter(this);
        m_filterInstalled = true;
    }
    QQuickWindow *old = m_window;
    m_window = window;
    m_hwnd = reinterpret_cast<HWND>(window->winId());
    if (!m_callbackMsg)
        m_callbackMsg = RegisterWindowMessageW(L"AppBarMessage_Totthodhara");
    APPBARDATA abd = {};
    abd.cbSize = sizeof(abd);
    abd.hWnd = reinterpret_cast<HWND>(window->winId());
    abd.uCallbackMessage = m_callbackMsg;
    // Compare BEFORE assigning above: otherwise a different window never
    // registers (ABM_NEW skipped) and the old reservation leaks.
    if (!m_docked || old != window) {
        SHAppBarMessage(ABM_NEW, &abd);
        m_docked = true;
    }
    m_edge = 3; // ABE_BOTTOM
    applyDock();
    emit dockedChanged();
#else
    Q_UNUSED(window);
#endif
}

void AppBarService::dockTop(QQuickWindow *window)
{
#ifdef Q_OS_WINDOWS
    if (!window)
        return;
    if (!m_filterInstalled && QCoreApplication::instance()) {
        QCoreApplication::instance()->installNativeEventFilter(this);
        m_filterInstalled = true;
    }
    QQuickWindow *old = m_window;
    m_window = window;
    m_hwnd = reinterpret_cast<HWND>(window->winId());
    if (!m_callbackMsg)
        m_callbackMsg = RegisterWindowMessageW(L"AppBarMessage_Totthodhara");
    APPBARDATA abd = {};
    abd.cbSize = sizeof(abd);
    abd.hWnd = reinterpret_cast<HWND>(window->winId());
    abd.uCallbackMessage = m_callbackMsg;
    // Compare BEFORE assigning above (same fix as dock()).
    if (!m_docked || old != window) {
        SHAppBarMessage(ABM_NEW, &abd);
        m_docked = true;
    }
    m_edge = 1; // ABE_TOP
    applyDock();
    emit dockedChanged();
#else
    Q_UNUSED(window);
#endif
}

void AppBarService::undock()
{
#ifdef Q_OS_WINDOWS
    if (!m_docked || !m_hwnd)
        return;
    APPBARDATA abd = {};
    abd.cbSize = sizeof(abd);
    abd.hWnd = m_hwnd;
    abd.uCallbackMessage = m_callbackMsg;
    SHAppBarMessage(ABM_REMOVE, &abd);
    m_hwnd = nullptr;
    m_window = nullptr;
    m_docked = false;
    emit dockedChanged();
#endif
}

void AppBarService::refresh()
{
#ifdef Q_OS_WINDOWS
    // Re-reserve for the CURRENT window size (e.g. after a bar-size change
    // changed the height). No re-registration, just reposition.
    if (m_docked && m_window)
        applyDock();
#endif
}

#ifdef Q_OS_WINDOWS
int AppBarService::dpiOf(HWND hwnd)
{
    UINT dpi = GetDpiForWindow(hwnd); // Win10 1607+, no manifest needed here
    return dpi ? int(dpi) : 96;
}

// Reserve our rect on the active edge (Bottom stacks above the real
// taskbar; Top takes the screen top), full-bleed, no side margins.
void AppBarService::applyDock()
{
    if (!m_window)
        return;
    HWND hwnd = reinterpret_cast<HWND>(m_window->winId());
    const double scale = dpiOf(hwnd) / 96.0;

    // Our QML height (DIPs) -> physical pixels, rounded like the WPF build.
    // Full-bleed shelf: the reserved rect spans the whole monitor edge.
    const int barH = int(m_window->height() * scale + 0.5);

    // The shelf's own monitor (primary metrics lie on multi-monitor setups
    // and for negative-offset secondaries). Falls back to primary.
    int monL = 0, monT = 0, monR = GetSystemMetrics(SM_CXSCREEN);
    int monB = GetSystemMetrics(SM_CYSCREEN);
    if (HMONITOR mon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTOPRIMARY)) {
        MONITORINFO mi = {};
        mi.cbSize = sizeof(mi);
        if (GetMonitorInfoW(mon, &mi)) {
            monL = mi.rcMonitor.left;
            monT = mi.rcMonitor.top;
            monR = mi.rcMonitor.right;
            monB = mi.rcMonitor.bottom;
        }
    }

    // Which edge the taskbar eats (default: bottom, identical numbers to
    // before on single-monitor setups). QUERYPOS below settles any dispute.
    enum TaskEdge { EdgeBottom, EdgeTop, EdgeLeft, EdgeRight };
    TaskEdge taskEdge = EdgeBottom;
    int taskLine = monB; // the taskbar-adjacent coordinate
    if (HWND tray = FindWindowW(L"Shell_TrayWnd", nullptr)) {
        RECT tr = {};
        if (GetWindowRect(tray, &tr)) {
            const int tw = tr.right - tr.left;
            const int th = tr.bottom - tr.top;
            if (tr.top <= monT + 2 && th < (monB - monT) / 2) {
                taskEdge = EdgeTop;
                taskLine = tr.bottom;
            } else if (tr.left <= monL + 2 && tw < (monR - monL) / 2) {
                taskEdge = EdgeLeft;
                taskLine = tr.right;
            } else if (tr.right >= monR - 2 && tw < (monR - monL) / 2) {
                taskEdge = EdgeRight;
                taskLine = tr.left;
            } else {
                taskLine = tr.top;
            }
        }
    }

    APPBARDATA abd = {};
    abd.cbSize = sizeof(abd);
    abd.hWnd = hwnd;
    if (m_edge == 1) { // ABE_TOP
        abd.uEdge = 1;
        abd.rc.left = monL;
        abd.rc.top = (taskEdge == EdgeTop) ? taskLine : monT;
        abd.rc.right = monR;
        abd.rc.bottom = abd.rc.top + barH;
    } else { // ABE_BOTTOM
        abd.uEdge = 3;
        abd.rc.left = monL;
        abd.rc.top = (taskEdge == EdgeBottom ? taskLine : monB) - barH;
        abd.rc.right = monR;
        abd.rc.bottom = (taskEdge == EdgeBottom ? taskLine : monB);
    }

    SHAppBarMessage(ABM_QUERYPOS, &abd); // system adjusts the rect
    SHAppBarMessage(ABM_SETPOS, &abd);   // commit it

    SetWindowPos(hwnd, HWND_TOPMOST, abd.rc.left, abd.rc.top,
                 abd.rc.right - abd.rc.left, abd.rc.bottom - abd.rc.top,
                 SWP_NOACTIVATE | SWP_SHOWWINDOW);

    // Sync QML geometry back (DIPs) so drag logic stays consistent.
    m_window->setX(int((abd.rc.left) / scale + 0.5));
    m_window->setY(int((abd.rc.top) / scale + 0.5));

    qInfo() << "AppBar docked at" << abd.rc.left << abd.rc.top
            << (abd.rc.right - abd.rc.left) << "x" << (abd.rc.bottom - abd.rc.top);
}

bool AppBarService::nativeEventFilter(const QByteArray &eventType, void *message,
                                      qintptr *result)
{
    if (eventType != "windows_generic_MSG")
        return false;
    const MSG *msg = static_cast<const MSG *>(message);
    if (msg->message == m_callbackMsg && m_callbackMsg != 0) {
        if (msg->wParam == ABN_POSCHANGED && m_docked)
            applyDock(); // taskbar moved/resized/fullscreen change
        if (result)
            *result = 0;
        return true;
    }
    return false;
}
#endif
