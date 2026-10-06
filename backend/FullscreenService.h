#pragma once

#include <QObject>
#include <QTimer>

// Hides the shelf while a fullscreen app (video player, game) covers the
// screen, shows it again afterwards. Same rules as the WPF build:
// foreground window rect matches the monitor (1px tolerance), or a
// borderless window covering >95% of it. Taskbar/desktop never trigger.
class FullscreenService : public QObject
{
    Q_OBJECT
public:
    explicit FullscreenService(QObject *parent = nullptr);

    Q_INVOKABLE void start();
    Q_INVOKABLE void stop();

signals:
    void fullscreenChanged(bool fullscreen);

private:
    void check();
    bool m_fullscreen = false;
    QTimer m_timer;
};
