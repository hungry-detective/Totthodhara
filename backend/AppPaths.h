#pragma once

#include <QCoreApplication>
#include <QDebug>
#include <QDir>
#include <QFile>
#include <QFileInfo>
#include <QStandardPaths>
#include <QString>

#include <cstdlib>

// Portable data home: <exe>/data (history.db, clips/, favicons/).
// The portable launcher (deploy root stub) redirects this with the
// TOTTHODHARA_DATA_DIR env var so data/ sits next to the stub while the
// real exe + Qt live in library/.
// Like the WPF single-file build, everything lives next to the app.
// First run migrates the older Roaming profile once, preserving history.
namespace AppPaths {

inline QString dataDir()
{
    static QString dir;
    if (!dir.isEmpty())
        return dir;
    if (const char *env = std::getenv("TOTTHODHARA_DATA_DIR"))
        dir = QString::fromLocal8Bit(env);
    else
        dir = QCoreApplication::applicationDirPath() + QStringLiteral("/data");
    QDir().mkpath(dir);
    QDir().mkpath(dir + QStringLiteral("/clips"));
    QDir().mkpath(dir + QStringLiteral("/favicons"));
    // Update crash recovery + tidiness (exact names only, never user
    // data). dataDir's parent is the install root in every layout. A
    // killed updater leaves a partial new tree: if neither exe runs but
    // backups exist, drop the partial tree and put the backup back — the
    // app always starts whole. Otherwise just sweep stale .bak leftovers.
    {
        QDir rootDir(dir);
        if (rootDir.cdUp()) {
            const QString root = rootDir.absolutePath();
            const QString libExe = root + QStringLiteral("/library/Totthodhara.exe");
            const QString stubExe = root + QStringLiteral("/Totthodhara.exe");
            const QString libBak = root + QStringLiteral("/library.bak");
            const QString stubBak = root + QStringLiteral("/Totthodhara.exe.bak");
            auto clearPartial = [&]() {
                for (const QString &e :
                     rootDir.entryList(QDir::Dirs | QDir::NoDotAndDotDot | QDir::Hidden | QDir::System)) {
                    if (e.compare(QStringLiteral("data"), Qt::CaseInsensitive) == 0)
                        continue;
                    if (e.endsWith(QStringLiteral(".bak"), Qt::CaseInsensitive))
                        continue;
                    QDir(rootDir.filePath(e)).removeRecursively();
                }
                for (const QString &e :
                     rootDir.entryList(QDir::Files | QDir::Hidden | QDir::System)) {
                    if (e.endsWith(QStringLiteral(".bak"), Qt::CaseInsensitive))
                        continue;
                    QFile::remove(rootDir.filePath(e));
                }
            };
            if (!QFile::exists(libExe) && !QFile::exists(stubExe)
                && (QDir(libBak).exists() || QFile::exists(stubBak))) {
                clearPartial();
                if (QDir(libBak).exists())
                    QDir().rename(libBak, root + QStringLiteral("/library"));
                if (QFile::exists(stubBak))
                    QFile::rename(stubBak, stubExe);
            } else {
                QFile::remove(stubBak);
                QDir(libBak).removeRecursively();
            }
            // Stale update stage (killed updater): best effort — locked
            // files simply survive until a later launch.
            QDir(QDir::tempPath() + QStringLiteral("/Totthodhara-update")).removeRecursively();
        }
    }

    const QString legacy =
        QStandardPaths::writableLocation(QStandardPaths::AppDataLocation);
    // One-time legacy import, atomic per file: copy to .tmp + rename, with
    // every step checked. A kill/disk-full mid-copy must never leave a
    // truncated history.db that passes the exists() check forever after.
    if (!legacy.isEmpty() && legacy != dir && QDir(legacy).exists()
        && !QFile::exists(dir + QStringLiteral("/history.db"))
        && QFile::exists(legacy + QStringLiteral("/history.db"))) {
        const QString tmp = dir + QStringLiteral("/history.db.tmp");
        QFile::remove(tmp);
        if (QFile::copy(legacy + QStringLiteral("/history.db"), tmp)
            && QFile::rename(tmp, dir + QStringLiteral("/history.db"))) {
            for (const QString &sub : {QStringLiteral("/clips"), QStringLiteral("/favicons")}) {
                const QDir srcDir(legacy + sub);
                for (const QString &f : srcDir.entryList(QDir::Files)) {
                    const QString dst = dir + sub + QStringLiteral("/") + f;
                    if (!QFile::exists(dst) && !QFile::copy(srcDir.filePath(f), dst))
                        qWarning() << "legacy migrate: copy failed:" << f;
                }
            }
        } else {
            qWarning() << "legacy migrate: history.db copy failed";
            QFile::remove(tmp);
        }
    }
    return dir;
}

} // namespace AppPaths
