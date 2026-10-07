#include "FullscreenService.h"

#ifdef Q_OS_WINDOWS
#include <windows.h>
#endif

FullscreenService::FullscreenService(QObject *parent)
    : QObject(parent)
{
    m_timer.setInterval(250);
    connect(&m_timer, &QTimer::timeout, this, &FullscreenService::check);
}

void FullscreenService::start()
{
    check();
    m_timer.start();
}

void FullscreenService::stop()
{
    m_timer.stop();
}

void FullscreenService::check()
{
#ifdef Q_OS_WINDOWS
    bool fs = false;
    if (HWND fg = GetForegroundWindow()) {
        wchar_t cls[256] = {};
        GetClassNameW(fg, cls, 256);
        const QString clsName = QString::fromWCharArray(cls);
        // Desktop / taskbar can never be "fullscreen apps".
        if (clsName != QLatin1String("Shell_TrayWnd")
            && clsName != QLatin1String("WorkerW")
            && clsName != QLatin1String("Progman")) {
            RECT wr = {};
            HMONITOR mon = MonitorFromWindow(fg, MONITOR_DEFAULTTOPRIMARY);
            MONITORINFO mi = {};
            mi.cbSize = sizeof(mi);
            // Query failures fail VISIBLE (fs stays false): a stuck hidden
            // shelf is worse than one tick of overlap on a transient error.
            if (GetWindowRect(fg, &wr) && GetMonitorInfoW(mon, &mi)) {
                const int mw = mi.rcMonitor.right - mi.rcMonitor.left;
                const int mh = mi.rcMonitor.bottom - mi.rcMonitor.top;
                const int ww = wr.right - wr.left;
                const int wh = wr.bottom - wr.top;
                const bool rectMatch = qAbs(wr.left - mi.rcMonitor.left) <= 1
                                       && qAbs(wr.top - mi.rcMonitor.top) <= 1
                                       && qAbs(wr.right - mi.rcMonitor.right) <= 1
                                       && qAbs(wr.bottom - mi.rcMonitor.bottom) <= 1;
                LONG style = GetWindowLongW(fg, GWL_STYLE);
                const bool borderlessBig = !(style & WS_CAPTION) && !(style & WS_THICKFRAME)
                                           && ww > mw * 0.95 && wh > mh * 0.95;
                fs = rectMatch || borderlessBig;
            }
        }
    }
    if (fs != m_fullscreen) {
        m_fullscreen = fs;
        emit fullscreenChanged(fs);
    }
#else
    Q_UNUSED(m_fullscreen);
#endif
}
