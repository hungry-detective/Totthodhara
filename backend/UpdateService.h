#pragma once

#include <QObject>
#include <QNetworkAccessManager>
#include <QNetworkReply>

// In-app updater (portable builds): checks GitHub Releases for a newer
// windows .zip, downloads it, then hands off to a generated updater script
// that swaps everything except data/ (history + settings survive) and
// restarts the app. No third-party deps: download via QNetworkAccessManager,
// unzip via PowerShell Expand-Archive (ships with Windows 10+).
//
// Publishing contract (see README): tag like v0.2.0 on GitHub Releases +
// upload the Totthodhara-windows-portable.zip that package.cmd produces.
// Bump the version in main.cpp (setApplicationVersion) before tagging.
class UpdateService : public QObject
{
    Q_OBJECT
    Q_PROPERTY(bool busy READ busy NOTIFY busyChanged)
public:
    explicit UpdateService(QObject *parent = nullptr);

    bool busy() const { return m_busy; }

    // Ask GitHub for the latest release. Result arrives via checkFinished
    // (available, version, notes) or checkFailed(error).
    Q_INVOKABLE void checkForUpdates();
    // Run-at-Windows-startup (HKCU...\Run): writes/removes the entry for
    // the portable stub (dev trees register their own exe — harmless).
    Q_INVOKABLE void setRunAtStartup(bool on);
    // Downloads the pending asset, stages the updater script, launches it
    // detached and quits the app. Progress via downloadProgress().
    Q_INVOKABLE void downloadAndInstall();

signals:
    void busyChanged();
    void checkFinished(bool available, const QString &version, const QString &notes);
    void checkFailed(const QString &error);
    void downloadProgress(qint64 received, qint64 total);
    void installStarted();
    void installFailed(const QString &error);

private slots:
    void onLatestFinished();
    void onDownloadProgress(qint64 received, qint64 total);
    void onDownloadFinished();
    void onShaFinished();

private:
    void setBusy(bool b);
    // Numeric dot-part compare ("v0.2.0" > "0.1.0"): <0 below, 0 same, >0 above.
    static int compareVersions(const QString &have, const QString &tag);
    // Writes apply.cmd into the stage dir and launches it detached.
    // Returns false (with installFailed emitted) when staging fails.
    bool stageAndLaunch(const QString &zipPath);
    // 1-hour result cache (data/update_cache/last_check.json): answers
    // repeat checks without burning the anonymous GitHub rate limit.
    static QString cacheFile();
    static bool readCache(QString &tag, QString &url, QString &name, QString &notes);
    static void writeCache(const QString &tag, const QString &url,
                           const QString &name, const QString &notes);
    // Verifies the staged zip against its .sha256 (when the release ships
    // one), then hands off to stageAndLaunch. Old hash-less releases still
    // install (hash verified from the next release on).
    void proceedInstall();

    // >>> Point these at the repo that publishes the releases. <<<
    // The asset name MUST match what package.cmd produces, exactly:
    // anything else in a release is ignored (never cross-installs).
    static constexpr const char *kUpdateOwner = "hungry-detective";
    static constexpr const char *kUpdateRepo = "Totthodhara";
    static constexpr const char *kAssetName = "Totthodhara-windows-portable.zip";

    QNetworkAccessManager m_net;
    QNetworkReply *m_reply = nullptr;
    bool m_busy = false;
    QString m_assetUrl;
    QString m_assetName;
    QString m_shaUrl;
    QString m_pendingVersion;
    QString m_pendingNotes;
    QString m_zipPath;
};
