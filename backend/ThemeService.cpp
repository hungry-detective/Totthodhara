#include "backend/ThemeService.h"

#include <QCoreApplication>
#include <QCursor>
#include <QDebug>
#include <QQuickWindow>
#include <QSettings>

#ifdef Q_OS_WINDOWS
#include <windows.h>
#include <dwmapi.h>
#endif

ThemeService::ThemeService(QObject *parent)
    : QObject(parent)
{
    if (QCoreApplication::instance())
        QCoreApplication::instance()->installNativeEventFilter(this);
    checkTheme();
    // Safety net: 1.5s poll catches theme flips the window message misses.
    connect(&m_poll, &QTimer::timeout, this, &ThemeService::checkTheme);
    m_poll.setInterval(1500);
    m_poll.start();
}

bool ThemeService::nativeEventFilter(const QByteArray &eventType, void *message,
                                     qintptr *result)
{
#ifdef Q_OS_WINDOWS
    if (eventType == "windows_generic_MSG") {
        const MSG *msg = static_cast<const MSG *>(message);
        // Live flip: Settings > Personalization broadcasts these.
        if (msg->message == WM_SETTINGCHANGE
            || msg->message == 0x031A // WM_THEMECHANGED
            || msg->message == 0x0320) { // WM_DWMCOLORIZATIONCOLORCHANGED
            checkTheme();
        }
    }
#else
    Q_UNUSED(eventType);
    Q_UNUSED(message);
    Q_UNUSED(result);
#endif
    return false;
}

void ThemeService::checkTheme()
{
#ifdef Q_OS_WINDOWS
    const QSettings s(QStringLiteral(
                          "HKEY_CURRENT_USER\\Software\\Microsoft\\Windows"
                          "\\CurrentVersion\\Themes\\Personalize"),
                      QSettings::NativeFormat);
    // Taskbar theme first (SystemUsesLightTheme is what the taskbar
    // follows), app theme as fallback. Missing keys = Light.
    const int sysLight = s.value(QStringLiteral("SystemUsesLightTheme"),
                                 s.value(QStringLiteral("AppsUseLightTheme"), 1)).toInt();
    const bool dark = sysLight == 0;
    // Transparency effects switch (Windows Settings > Personalization).
    const bool effects =
        s.value(QStringLiteral("EnableTransparency"), 1).toInt() != 0;
    // "Show accent color on Start and taskbar" switch (same page).
    // OFF (Windows default) = taskbar wears no accent.
    const bool prevalence =
        s.value(QStringLiteral("ColorPrevalence"), 0).toInt() != 0;
    // Taskbar tone watch: same DWM colorization the taskbar tints with.
    // Accent edits only flip this (no theme message), hence the poll.
    DWORD col = 0;
    BOOL opaque = FALSE;
    const unsigned int tint =
        SUCCEEDED(DwmGetColorizationColor(&col, &opaque)) ? (col & 0x00ffffffU) : 0U;
#else
    const bool dark = true;
    const bool effects = true;
    const bool prevalence = false;
    const unsigned int tint = 0U;
#endif
    if (dark != m_isDark) {
        m_isDark = dark;
        emit systemThemeChanged();
    }
    if (effects != m_transparencyOn) {
        m_transparencyOn = effects;
        emit transparencyChanged();
    }
    if (prevalence != m_colorPrevalence) {
        m_colorPrevalence = prevalence;
        emit colorPrevalenceChanged();
    }
    if (tint != m_tintRgb) {
        m_tintRgb = tint;
        emit systemTintChanged();
    }
    // Windows accent picker: wear it as the app accent (Start-menu style).
    // DWM hands 0xRRGGBB (masked above); construct opaque explicitly —
    // QColor::fromRgb(uint) would read the zeroed high byte as alpha 0.
    const QColor accent = tint != 0U
        ? QColor(int((tint >> 16) & 0xffU), int((tint >> 8) & 0xffU), int(tint & 0xffU))
        : QColor(QStringLiteral("#4cc2ff"));
    if (accent != m_accent) {
        m_accent = accent;
        emit systemAccentChanged();
    }
}

QPoint ThemeService::cursorPos() const
{
#ifdef Q_OS_WINDOWS
    return QCursor::pos();
#else
    return QPoint();
#endif
}

namespace {
// Windows Settings > Personalization > Transparency effects, read live
// (used on every applyGlass so the shelf goes solid the moment the OS
// does — same source the taskbar uses).
bool windowsEffectsOn()
{
#ifdef Q_OS_WINDOWS
    const QSettings s(QStringLiteral(
                          "HKEY_CURRENT_USER\\Software\\Microsoft\\Windows"
                          "\\CurrentVersion\\Themes\\Personalize"),
                      QSettings::NativeFormat);
    return s.value(QStringLiteral("EnableTransparency"), 1).toInt() != 0;
#else
    return true;
#endif
}
} // namespace

void ThemeService::applyGlass(QQuickWindow *window, bool enable, bool dark,
                               int radius)
{
#ifdef Q_OS_WINDOWS
    if (!window)
        return;
    HWND hwnd = reinterpret_cast<HWND>(window->winId());
    if (!hwnd)
        return;
    using SetCompAttrFn = BOOL(WINAPI *)(HWND, void *);
    struct AccentPolicy {
        int accentState;
        int accentFlags;
        int gradientColor;
        int animationId;
    };
    struct CompAttrData {
        int attribute;
        void *data;
        size_t dataSize;
    };
    auto glassColor = [&]() {
        // Low alpha on purpose: the QML barFill carries the tone, the native
        // layer is only blur. A strong native tint would paint a visible
        // rectangle past the rounded pill ends (legacy acrylic covers the
        // whole window, not the QML silhouette) — System-mode-only artifact,
        // explicit modes never enable glass.
        const int alpha = 0x20;
        DWORD col = 0;
        BOOL opaque = FALSE;
        if (FAILED(DwmGetColorizationColor(&col, &opaque)))
            col = dark ? 0x002A2A2E : 0x00F2F2F2;
        const int ar = (col >> 16) & 0xff, ag = (col >> 8) & 0xff, ab = col & 0xff;
        const int br = dark ? 0x2A : 0xF2, bg = dark ? 0x2A : 0xF2, bb = dark ? 0x2E : 0xF2;
        const int r = (br * 55 + ar * 45) / 100;
        const int g = (bg * 55 + ag * 45) / 100;
        const int b = (bb * 55 + ab * 45) / 100;
        return static_cast<int>((unsigned(alpha) << 24) | (unsigned(b) << 16)
                                | (unsigned(g) << 8) | unsigned(r));
    };
    SetCompAttrFn setAttr = nullptr;
    if (HMODULE user32 = GetModuleHandleW(L"user32.dll"))
        setAttr = reinterpret_cast<SetCompAttrFn>(
            GetProcAddress(user32, "SetWindowCompositionAttribute"));
    auto setAcrylic = [&](bool on, int tint) {
        if (!setAttr)
            return false;
        AccentPolicy policy = {
            on ? 4 /*ACRYLICBLURBEHIND*/ : 0 /*DISABLED*/,
            0,
            tint,
            0,
        };
        CompAttrData attr = {19 /*WCA_ACCENT_POLICY*/, &policy, sizeof(policy)};
        return setAttr(hwnd, &attr) != FALSE;
    };
    // DWM constants, numeric: older MinGW headers may lack the enums.
    constexpr int ATTR_BACKDROP = 20; // DWMWA_SYSTEMBACKDROP_TYPE
    constexpr int ATTR_CORNER = 33;   // DWMWA_WINDOW_CORNER_PREFERENCE
    constexpr int ACRYLIC = 3;        // DWMSBT_TRANSIENTWINDOW: real DWM
                                      // acrylic, the taskbar material
    constexpr int BACKDROP_NONE = 1;
    constexpr int SQUARE = 1;         // DWMWCP_DONOTROUND
    if (!enable || !windowsEffectsOn()) {
        // Solid like the taskbar with effects off: no backdrop, no blur,
        // no frost — the QML side paints opaque barBg (see AppState.barFill).
        int none = BACKDROP_NONE;
        DwmSetWindowAttribute(hwnd, ATTR_BACKDROP, &none, sizeof(none));
        int square = SQUARE;
        DwmSetWindowAttribute(hwnd, ATTR_CORNER, &square, sizeof(square));
        setAcrylic(false, 0);
        DWM_BLURBEHIND off = {};
        off.dwFlags = DWM_BB_ENABLE;
        off.fEnable = FALSE;
        DwmEnableBlurBehindWindow(hwnd, &off);
        return;
    }
    // Blur clipped to the pill: DWM blur-behind with a rounded-rect region
    // matching the QML silhouette (device pixels). Full-window effects
    // (DWMWA acrylic — refused on layered windows anyway, verified — and
    // the legacy composition tint) paint past the rounded ends, leaving
    // ears on transparency. Region blur + the QML veil on top: tone from
    // QML, blur exactly under the pill, true empty everywhere else.
    // Never system-clip the HWND corners: the clip runs at 8px and would
    // cut the QML pill curve (13-17px) plus stamp a halo seam that reads
    // as a border. The pill silhouette alone stays clean.
    {
        const qreal scale = window->devicePixelRatio() > 0.0
                                ? window->devicePixelRatio()
                                : 1.0;
        const int W = int(window->width() * scale + 0.5);
        const int H = int(window->height() * scale + 0.5);
        const int R = int(radius * scale + 0.5);
        HRGN rgn = nullptr;
        if (W > 0 && H > 0 && R > 0)
            rgn = CreateRoundRectRgn(0, 0, W + 1, H + 1, R * 2, R * 2);
        DWM_BLURBEHIND bb = {};
        bb.dwFlags = DWM_BB_ENABLE | DWM_BB_BLURREGION;
        bb.fEnable = TRUE;
        bb.hRgnBlur = rgn;
        bb.fTransitionOnMaximized = FALSE;
        const bool ok = SUCCEEDED(DwmEnableBlurBehindWindow(hwnd, &bb));
        if (rgn)
            DeleteObject(rgn);
        qInfo() << "glass: region blur enable=" << enable << "dark=" << dark
                << "hwnd=" << hwnd << "ok=" << ok;
        int corner = SQUARE;
        DwmSetWindowAttribute(hwnd, ATTR_CORNER, &corner, sizeof(corner));
    }
#else
    Q_UNUSED(window);
    Q_UNUSED(enable);
    Q_UNUSED(dark);
    Q_UNUSED(radius);
#endif
}
