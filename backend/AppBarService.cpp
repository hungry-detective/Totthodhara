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
    if (!m_docked || !m_window)
        return;
    APPBARDATA abd = {};
    abd.cbSize = sizeof(abd);
    abd.hWnd = reinterpret_cast<HWND>(m_window->winId());
    abd.uCallbackMessage = m_callbackMsg;
    SHAppBarMessage(ABM_REMOVE, &abd);
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

    // Our QML size (DIPs) -> physical pixels, rounded like the WPF build.
    const int barW = int(m_window->width() * scale + 0.5);
    const int barH = int(m_window->height() * scale + 0.5);
    // Full-bleed shelf: no side margins, rect matches the visible bar.
    const int marginR = 0;

    const int scrW = GetSystemMetrics(SM_CXSCREEN);
    const int scrH = GetSystemMetrics(SM_CYSCREEN);

    // Real taskbar top (works when taskbar is at the bottom).
    int taskbarTop = scrH;
    if (HWND tray = FindWindowW(L"Shell_TrayWnd", nullptr)) {
        RECT tr = {};
        if (GetWindowRect(tray, &tr))
            taskbarTop = tr.top;
    }

    APPBARDATA abd = {};
    abd.cbSize = sizeof(abd);
    abd.hWnd = hwnd;
    if (m_edge == 1) { // ABE_TOP
        abd.uEdge = 1;
        abd.rc.left = scrW - barW - marginR;
        abd.rc.top = 0;
        abd.rc.right = scrW - marginR;
        abd.rc.bottom = barH;
    } else { // ABE_BOTTOM
        // Real taskbar top (works when taskbar is at the bottom).
        int taskbarTop = scrH;
        if (HWND tray = FindWindowW(L"Shell_TrayWnd", nullptr)) {
            RECT tr = {};
            if (GetWindowRect(tray, &tr))
                taskbarTop = tr.top;
        }
        abd.uEdge = 3;
        abd.rc.left = scrW - barW - marginR;
        abd.rc.top = taskbarTop - barH;
        abd.rc.right = scrW - marginR;
        abd.rc.bottom = taskbarTop;
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
