#include "backend/UpdateService.h"
#include "backend/AppPaths.h"

#include <QCoreApplication>
#include <QCryptographicHash>
#include <QDateTime>
#include <QDebug>
#include <QDir>
#include <QFile>
#include <QFileInfo>
#include <QJsonArray>
#include <QJsonDocument>
#include <QJsonObject>
#include <QProcess>
#include <QRegularExpression>
#include <QSettings>
#include <QStandardPaths>
#include <QTextStream>
#include <QTimer>

UpdateService::UpdateService(QObject *parent)
    : QObject(parent)
{
}

void UpdateService::setBusy(bool b)
{
    if (m_busy == b)
        return;
    m_busy = b;
    emit busyChanged();
}

int UpdateService::compareVersions(const QString &have, const QString &tag)
{
    auto parts = [](const QString &v) {
        QList<int> out;
        QString t = v.trimmed();
        if (t.startsWith(QLatin1Char('v')) || t.startsWith(QLatin1Char('V')))
            t = t.mid(1);
        for (const QString &p : t.split(QLatin1Char('.'))) {
            int n = 0;
            for (QChar c : p) {
                if (!c.isDigit())
                    break;
                n = n * 10 + c.digitValue();
            }
            out << n;
        }
        return out;
    };
    const QList<int> a = parts(have);
    const QList<int> b = parts(tag);
    const int n = qMax(a.size(), b.size());
    for (int i = 0; i < n; ++i) {
        const int x = i < a.size() ? a.at(i) : 0;
        const int y = i < b.size() ? b.at(i) : 0;
        if (x != y)
            return x < y ? -1 : 1;
    }
    return 0;
}

void UpdateService::setRunAtStartup(bool on)
{
#ifdef Q_OS_WINDOWS
    QSettings run(QStringLiteral(
                      "HKEY_CURRENT_USER\\Software\\Microsoft\\Windows"
                      "\\CurrentVersion\\Run"),
                  QSettings::NativeFormat);
    if (!on) {
        run.remove(QStringLiteral("Totthodhara"));
        run.sync();
    } else {
        // Portable stub when launched through it (friends' installs), else
        // whichever exe is running (dev trees).
        QString exe = QString::fromLocal8Bit(qgetenv("TOTTHODHARA_ROOT"));
        exe = exe.isEmpty() ? QCoreApplication::applicationFilePath()
                            : exe + QStringLiteral("/Totthodhara.exe");
        run.setValue(QStringLiteral("Totthodhara"),
                     QStringLiteral("\"%1\"").arg(QDir::toNativeSeparators(exe)));
        run.sync();
    }
    if (run.status() != QSettings::NoError)
        qWarning() << "run-at-startup registry write failed:" << run.status();
#else
    Q_UNUSED(on);
#endif
}

void UpdateService::checkForUpdates()
{    if (m_busy)
        return;
    // Fresh cache answers instantly: every launch hitting api.github.com
    // would burn the 60/hr anonymous rate limit on shared networks.
    QString tag, url, name, notes;
    if (readCache(tag, url, name, notes)) {
        m_assetUrl = url;
        m_assetName = name;
        m_shaUrl.clear(); // offline answer: zip-only install if they proceed
        m_pendingVersion = tag;
        m_pendingNotes = notes;
        const bool newer = compareVersions(QCoreApplication::applicationVersion(), tag) < 0;
        emit checkFinished(newer, tag, notes);
        return;
    }
    m_assetUrl.clear();
    setBusy(true);
    QNetworkRequest req(QUrl(QStringLiteral("https://api.github.com/repos/%1/%2/releases/latest")
                                 .arg(QLatin1String(kUpdateOwner), QLatin1String(kUpdateRepo))));
    req.setHeader(QNetworkRequest::UserAgentHeader, QStringLiteral("Totthodhara-Updater"));
    req.setHeader(QNetworkRequest::ContentTypeHeader, QStringLiteral("application/json"));
    req.setRawHeader("Accept", "application/vnd.github+json");
    m_reply = m_net.get(req);
    connect(m_reply, &QNetworkReply::finished, this, &UpdateService::onLatestFinished);
}

void UpdateService::onLatestFinished()
{
    QNetworkReply *reply = m_reply;
    m_reply = nullptr;
    setBusy(false);
    if (!reply) {
        emit checkFailed(tr("Update check failed."));
        return;
    }
    reply->deleteLater();
    if (reply->error() != QNetworkReply::NoError) {
        if (reply->error() == QNetworkReply::ContentNotFoundError)
            emit checkFailed(tr("No releases published yet."));
        else
            emit checkFailed(tr("Update check failed: %1").arg(reply->errorString()));
        return;
    }
    const QJsonDocument doc = QJsonDocument::fromJson(reply->readAll());
    if (!doc.isObject()) {
        emit checkFailed(tr("Bad release data."));
        return;
    }
    const QJsonObject rel = doc.object();
    const QString tag = rel.value(QStringLiteral("tag_name")).toString();
    const QString notes = rel.value(QStringLiteral("body")).toString();
    if (tag.isEmpty()) {
        emit checkFailed(tr("Bad release data."));
        return;
    }
    // Exact portable zip only: never cross-install foreign assets (this
    // repo also publishes the WPF edition's .exe/source archives, which
    // must never be mistaken for a QML update). The .sha256 beside it is
    // picked up for integrity verification when present.
    static const QString want = QString::fromLatin1(kAssetName);
    static const QString wantSha = want + QStringLiteral(".sha256");
    QString url;
    QString name;
    QString shaUrl;
    for (const QJsonValue &v : rel.value(QStringLiteral("assets")).toArray()) {
        const QJsonObject a = v.toObject();
        const QString an = a.value(QStringLiteral("name")).toString();
        const QString au = a.value(QStringLiteral("browser_download_url")).toString();
        if (au.isEmpty())
            continue;
        if (an.compare(want, Qt::CaseInsensitive) == 0) {
            url = au;
            name = an;
        } else if (an.compare(wantSha, Qt::CaseInsensitive) == 0) {
            shaUrl = au;
        }
        if (!url.isEmpty() && !shaUrl.isEmpty())
            break;
    }
    if (url.isEmpty()) {
        emit checkFailed(tr("No compatible download in this release."));
        return;
    }
    m_assetUrl = url;
    m_assetName = name;
    m_shaUrl = shaUrl;
    m_pendingVersion = tag;
    m_pendingNotes = notes;
    writeCache(tag, url, name, notes);
    const bool newer = compareVersions(QCoreApplication::applicationVersion(), tag) < 0;
    emit checkFinished(newer, tag, notes);
}

void UpdateService::downloadAndInstall()
{
    if (m_busy)
        return;
    if (m_assetUrl.isEmpty()) {
        emit installFailed(tr("Check for updates first."));
        return;
    }
    const QString stage = QStandardPaths::writableLocation(QStandardPaths::TempLocation)
                          + QStringLiteral("/Totthodhara-update");
    QDir().mkpath(stage);
    m_zipPath = stage + QLatin1Char('/') + m_assetName;
    QFile::remove(m_zipPath);
    setBusy(true);
    QNetworkRequest req{QUrl(m_assetUrl)};
    req.setAttribute(QNetworkRequest::RedirectPolicyAttribute,
                     QNetworkRequest::NoLessSafeRedirectPolicy);
    req.setHeader(QNetworkRequest::UserAgentHeader, QStringLiteral("Totthodhara-Updater"));
    m_reply = m_net.get(req);
    connect(m_reply, &QNetworkReply::downloadProgress,
            this, &UpdateService::onDownloadProgress);
    connect(m_reply, &QNetworkReply::finished, this, &UpdateService::onDownloadFinished);
}

void UpdateService::onDownloadProgress(qint64 received, qint64 total)
{
    emit downloadProgress(received, total);
}

void UpdateService::onDownloadFinished()
{
    QNetworkReply *reply = m_reply;
    m_reply = nullptr;
    if (!reply) {
        setBusy(false);
        emit installFailed(tr("Download failed."));
        return;
    }
    reply->deleteLater();
    if (reply->error() != QNetworkReply::NoError) {
        setBusy(false);
        emit installFailed(tr("Download failed: %1").arg(reply->errorString()));
        return;
    }
    QFile out(m_zipPath);
    if (!out.open(QIODevice::WriteOnly | QIODevice::Truncate)
        || out.write(reply->readAll()) <= 0) {
        setBusy(false);
        emit installFailed(tr("Could not save the download."));
        return;
    }
    out.close();
    // Hash-pinned releases verify before anything executes; hash-less
    // (older) releases install as before.
    if (!m_shaUrl.isEmpty()) {
        QNetworkRequest req{QUrl(m_shaUrl)};
        req.setAttribute(QNetworkRequest::RedirectPolicyAttribute,
                         QNetworkRequest::NoLessSafeRedirectPolicy);
        req.setHeader(QNetworkRequest::UserAgentHeader, QStringLiteral("Totthodhara-Updater"));
        m_reply = m_net.get(req);
        connect(m_reply, &QNetworkReply::finished, this, &UpdateService::onShaFinished);
        return;
    }
    proceedInstall();
}

void UpdateService::onShaFinished()
{
    QNetworkReply *reply = m_reply;
    m_reply = nullptr;
    if (!reply) {
        setBusy(false);
        emit installFailed(tr("Download failed."));
        return;
    }
    reply->deleteLater();
    if (reply->error() != QNetworkReply::NoError) {
        setBusy(false);
        emit installFailed(tr("Download failed: %1").arg(reply->errorString()));
        return;
    }
    // "<hex>  <filename>" — first token only.
    const QString expected = QString::fromLatin1(reply->readAll())
                                 .trimmed()
                                 .split(QRegularExpression(QStringLiteral("\\s+")))
                                 .value(0)
                                 .toLower();
    QFile zip(m_zipPath);
    if (!zip.open(QIODevice::ReadOnly)) {
        setBusy(false);
        emit installFailed(tr("Could not verify the download."));
        return;
    }
    const QString actual = QString::fromLatin1(
        QCryptographicHash::hash(zip.readAll(), QCryptographicHash::Sha256).toHex());
    zip.close();
    if (expected.isEmpty() || expected != actual) {
        QFile::remove(m_zipPath);
        setBusy(false);
        emit installFailed(tr("Update file failed its integrity check."));
        return;
    }
    proceedInstall();
}

void UpdateService::proceedInstall()
{
    if (!stageAndLaunch(m_zipPath)) {
        // stageAndLaunch emitted installFailed already.
        setBusy(false);
        return;
    }
    // Handed off: the updater script finishes the job after we exit.
    // (busy stays true; the process is going away.) The short delay lets
    // the "installing" state actually paint before the windows go away.
    emit installStarted();
    QTimer::singleShot(1200, this, [] { QCoreApplication::quit(); });
}

QString UpdateService::cacheFile()
{
    return AppPaths::dataDir() + QStringLiteral("/update_cache/last_check.json");
}

bool UpdateService::readCache(QString &tag, QString &url, QString &name, QString &notes)
{
    QFile f(cacheFile());
    if (!f.open(QIODevice::ReadOnly))
        return false;
    const QJsonObject o = QJsonDocument::fromJson(f.readAll()).object();
    const qint64 when = o.value(QStringLiteral("checked_at")).toVariant().toLongLong();
    if (when <= 0 || QDateTime::currentSecsSinceEpoch() - when > 3600)
        return false;
    tag = o.value(QStringLiteral("tag")).toString();
    url = o.value(QStringLiteral("url")).toString();
    name = o.value(QStringLiteral("name")).toString();
    notes = o.value(QStringLiteral("notes")).toString();
    return !tag.isEmpty() && !url.isEmpty();
}

void UpdateService::writeCache(const QString &tag, const QString &url,
                              const QString &name, const QString &notes)
{
    QDir().mkpath(QFileInfo(cacheFile()).absolutePath());
    QFile f(cacheFile());
    if (!f.open(QIODevice::WriteOnly | QIODevice::Truncate | QIODevice::Text))
        return;
    QJsonObject o;
    o[QStringLiteral("checked_at")] = QDateTime::currentSecsSinceEpoch();
    o[QStringLiteral("tag")] = tag;
    o[QStringLiteral("url")] = url;
    o[QStringLiteral("name")] = name;
    o[QStringLiteral("notes")] = notes;
    f.write(QJsonDocument(o).toJson(QJsonDocument::Compact));
}

bool UpdateService::stageAndLaunch(const QString &zipPath)
{
    // Portable root: handed down by the launcher stub (the real exe lives
    // one level down in library/, so its own dir is NOT the root).
    // Refuse anywhere else (dev build tree): wiping that would destroy
    // the workspace. data/ (history.db, clips, settings) is never touched.
    QString root = QString::fromLocal8Bit(qgetenv("TOTTHODHARA_ROOT"));
    if (root.isEmpty())
        root = QCoreApplication::applicationDirPath();
    const QString realExe = root + QStringLiteral("/library/Totthodhara.exe");
    if (!QFile::exists(realExe)) {
        emit installFailed(tr("In-app updates need the portable install."));
        return false;
    }
    const QString stage = QFileInfo(zipPath).absolutePath();
    const QString script = stage + QStringLiteral("/apply.cmd");
    QFile f(script);
    if (!f.open(QIODevice::WriteOnly | QIODevice::Truncate | QIODevice::Text)) {
        emit installFailed(tr("Could not stage the updater."));
        return false;
    }
    // Batch single-quotes below break on paths containing ': escape them.
    const QString escZip = QString(zipPath).replace(QLatin1Char('\''), QStringLiteral("''"));
    const QString escStage = QString(stage).replace(QLatin1Char('\''), QStringLiteral("''"));
    QTextStream s(&f);
    s << "@echo off\n";
    s << "title Totthodhara Update\n";
    s << "echo ========================================\n";
    s << "echo  Totthodhara is updating - please wait.\n";
    s << "echo  Do not close this window.\n";
    s << "echo ========================================\n";
    s << "set \"ROOT=" << root << "\"\n";
    s << "set \"STAGE=" << stage << "\"\n";
    s << "set \"ZIP=" << zipPath << "\"\n";
    // 1. Wait until the app (and its file locks + mutex) are really gone.
    s << "echo [1/4] Waiting for the app to exit...\n";
    s << ":waitloop\n";
    s << "taskkill /F /IM Totthodhara.exe >nul 2>&1\n";
    s << "ping -n 2 127.0.0.1 >nul\n";
    s << "copy /B /Y \"%ROOT%\\library\\Totthodhara.exe\" \"%TEMP%\\__tott_updlock\" >nul 2>&1\n";
    s << "if errorlevel 1 goto waitloop\n";
    s << "del /F /Q \"%TEMP%\\__tott_updlock\" >nul 2>&1\n";
    // 2. Unpack the release next to it.
    s << "echo [2/4] Unpacking the new version...\n";
    s << "powershell -NoProfile -Command \"Expand-Archive -Force '" << escZip << "' '" << escStage << "\\new'\"\n";
    s << "if errorlevel 1 goto fail\n";
    // 3. Verify the staged layout BEFORE wiping anything: a wrong-shaped
    // zip (or dead copy) must never brick the install into stub-less limbo.
    s << "echo [3/4] Verifying the package...\n";
    s << "if not exist \"%STAGE%\\new\\library\\Totthodhara.exe\" goto fail\n";
    s << "if not exist \"%STAGE%\\new\\Totthodhara.exe\" goto fail\n";
    // 4. Replace everything except data/ (history.db, clips, settings).
    s << "echo [4/4] Installing files (your clips and settings are kept)...\n";
    s << "for /D %%D in (\"%ROOT%\\*\") do if /I not \"%%~nxD\"==\"data\" rmdir /S /Q \"%%D\"\n";
    s << "del /Q \"%ROOT%\\*\" >nul 2>&1\n";
    s << "xcopy \"%STAGE%\\new\\*\" \"%ROOT%\\\" /E /I /Y >nul\n";
    s << "if errorlevel 1 goto fail\n";
    // 5. Relaunch + clean up the stage.
    s << "echo Done - starting Totthodhara...\n";
    s << "start \"\" \"%ROOT%\\Totthodhara.exe\"\n";
    s << "ping -n 5 127.0.0.1 >nul\n";
    s << "rmdir /S /Q \"%STAGE%\"\n";
    s << "exit /b 0\n";
    // Any failure relaunches the (untouched or restored) install so the
    // user is never left staring at a closed app.
    s << ":fail\n";
    s << "echo Something went wrong - starting your current version instead.\n";
    s << "start \"\" \"%ROOT%\\Totthodhara.exe\"\n";
    s << "ping -n 5 127.0.0.1 >nul\n";
    s << "rmdir /S /Q \"%STAGE%\"\n";
    s << "exit /b 1\n";
    f.close();
    // Plain detached launch: cmd.exe shows the titled console above and
    // narrates the replace phase step by step. (NOTE: do NOT route this
    // through `cmd /c start` with a pre-quoted title — QProcess quotes
    // spaced args itself, double quotes silently break the launch and the
    // script never runs. Verified the hard way.)
    if (!QProcess::startDetached(script, {})) {
        emit installFailed(tr("Could not start the updater."));
        return false;
    }
    return true;
}
