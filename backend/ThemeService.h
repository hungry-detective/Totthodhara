#pragma once

#include <QColor>
#include <QObject>
#include <QPoint>
#include <QTimer>
#include <QAbstractNativeEventFilter>

class QQuickWindow;

// Watches the Windows system theme (SystemUsesLightTheme — what the
// taskbar follows — falling back to AppsUseLightTheme) so the "System"
// shelf theme flips live when the OS toggles Light/Dark. Also exposes the
// Windows accent color + cursor position for Start-menu-style following.
class ThemeService : public QObject, public QAbstractNativeEventFilter
{
    Q_OBJECT
    Q_PROPERTY(bool systemIsDark READ systemIsDark NOTIFY systemThemeChanged)
    Q_PROPERTY(bool transparencyOn READ transparencyOn NOTIFY transparencyChanged)
    Q_PROPERTY(QColor systemAccent READ systemAccent NOTIFY systemAccentChanged)
    // "Show accent color on Start and taskbar" (Personalize\ColorPrevalence):
    // OFF means the taskbar wears no accent, so neither should the shelf.
    Q_PROPERTY(bool colorPrevalence READ colorPrevalence NOTIFY colorPrevalenceChanged)
public:
    explicit ThemeService(QObject *parent = nullptr);

    bool systemIsDark() const { return m_isDark; }
    // Windows Settings > Personalization > Transparency effects.
    bool transparencyOn() const { return m_transparencyOn; }
    bool colorPrevalence() const { return m_colorPrevalence; }
    // Windows Settings > Personalization > Accent color (DWM colorization).
    QColor systemAccent() const { return m_accent; }
    // Cursor in screen coords (positions the custom tray menu).
    Q_INVOKABLE QPoint cursorPos() const;

    // Taskbar glass: true DWM acrylic (DWMSBT_TRANSIENTWINDOW) with the
    // QML tint on top; SetWindowCompositionAttribute fallback, then plain
    // blurbehind. Respects the OS transparency-effects switch (solid when
    // off). No-op off Windows.
    Q_INVOKABLE void applyGlass(QQuickWindow *window, bool enable, bool dark,
                                int radius);

signals:
    void systemThemeChanged();
    // Windows accent/colorization tint drifted (accent change, contrast
    // flip): re-apply the glass so the shelf tracks the taskbar tone.
    void systemTintChanged();
    // Windows accent color itself changed: AppState.accent follows it.
    void systemAccentChanged();
    // Transparency effects toggled in Windows Settings.
    void transparencyChanged();
    // "Show accent color on Start and taskbar" toggled.
    void colorPrevalenceChanged();

protected:
    bool nativeEventFilter(const QByteArray &eventType, void *message,
                           qintptr *result) override;

private:
    void checkTheme();

    bool m_isDark = true;
    bool m_transparencyOn = true;
    bool m_colorPrevalence = false;
    unsigned int m_tintRgb = 0; // last DWM colorization RGB (accent watch)
    QColor m_accent = QColor(QStringLiteral("#4cc2ff")); // fallback signature blue
    QTimer m_poll;
};
