#pragma once

#include <QObject>
#include <QPointer>
#include <QMap>
#include <QSet>
#include <QString>
#include <QStringList>
#include <QTimer>
#include <QVariantMap>
#include <QAbstractNativeEventFilter>

class QQuickWindow;
class QNetworkAccessManager;
class QNetworkReply;

// Windows clipboard monitor: listens for WM_CLIPBOARDUPDATE on the shelf
// window and turns new clips into shelf items. Also puts items BACK onto
// the clipboard when clicked (copy-back). File layout mirrors the WPF
// ClipboardItem contract (title/kind/detail/icon).
class ClipboardService : public QObject, public QAbstractNativeEventFilter
{
    Q_OBJECT
    // Max captured file/image size in MB (0 = no limit). Bound from QML so
    // the Storage settings actually gate what lands on the shelf.
    Q_PROPERTY(int maxFileSizeMB READ maxFileSizeMB WRITE setMaxFileSizeMB
                   NOTIFY maxFileSizeMBChanged)
public:
    explicit ClipboardService(QObject *parent = nullptr);
    ~ClipboardService() override;

    int maxFileSizeMB() const { return m_maxFileSizeMB; }
    void setMaxFileSizeMB(int mb);

    Q_INVOKABLE void startMonitoring(QQuickWindow *window);
    Q_INVOKABLE void stopMonitoring();
    void pasteToForeground();

    // Copy-back: click an item -> it becomes the live clipboard content.
    // autoPaste injects Ctrl+V into the window that kept focus (the Tool
    // shelf never activates, so keystrokes land in the target app).
    Q_INVOKABLE void copyText(const QString &text, bool autoPaste);
    Q_INVOKABLE void copyFiles(const QStringList &localPaths, bool autoPaste);
    // Open a link in the default browser (the card's visit button).
    Q_INVOKABLE void openUrl(const QString &url);
    // Drag-out: start an OS drag so the item can drop into any other app
    // (text / link / file). The drag image follows the shelf theme (dark
    // pill vs light pill). Returns true when a target accepted the drop.
    Q_INVOKABLE bool startSystemDrag(const QString &title, const QString &kind,
                                     const QString &detail, bool dark);
    // Re-resolve a link icon (cache hit upgrades instantly, else fetch).
    Q_INVOKABLE void refetchIcon(const QString &detail);
    // Synchronous twin: returns the cached file URL or "" (and starts a
    // fetch when missing). Used by the startup sweep.
    Q_INVOKABLE QString resolveIcon(const QString &detail);

signals:
    // {title, kind, detail, icon} -> ClipStore.addClip()
    // kind: text | url | color | image | file
    void clipCaptured(const QVariantMap &item);
    // Async favicon arrived: ClipStore swaps the globe glyph for it.
    void iconReady(const QString &detail, const QString &iconPath);
    // A capture was refused (over the size limit): shown as a toast.
    void rejected(const QString &message);
    void maxFileSizeMBChanged();

protected:
    bool nativeEventFilter(const QByteArray &eventType, void *message,
                           qintptr *result) override;

private:
    void handleClipboard();
    void trackTarget();
    void fetchFavicon(const QString &pageUrl, const QString &detailKey);
    void fetchFinished(QNetworkReply *reply, const QString &detailKey, bool fallbackTried);
    static QString shortTitle(const QString &text);
    static QString hostOf(const QString &url);
    QString saveImageFile(const QImage &img);

    // Raw window pointer would dangle at teardown (the QML window can die
    // first): QPointer auto-nulls so stopMonitoring/dtor never touch it.
    QPointer<QQuickWindow> m_window;
    bool m_filterInstalled = false;
    // Echo suppression: what WE put on the clipboard must never come back
    // as a new item, no matter how many notifications Windows fires.
    // Cleared as soon as genuinely different content arrives.
    QString m_suppressText;
    QStringList m_suppressUrls;
    QString m_lastText;        // dedupe repeat notifications (text)
    QStringList m_lastUrls;    // dedupe repeat notifications (files)
    quint64 m_lastImageHash = 0; // dedupe repeat notifications (bitmaps)
    quint64 m_clipSeq = 0;       // disambiguates same-millisecond captures
    QMap<QString, QStringList> m_pendingIcons; // host -> card detail keys
                                               // waiting on its favicon fetch
    int m_maxFileSizeMB = 25;    // bound from QML (Storage settings)
    QNetworkAccessManager *m_net = nullptr;
    QTimer m_fgTimer;          // tracks the last non-shelf foreground window
    void *m_lastTarget = nullptr; // HWND to re-activate before pasting
};
