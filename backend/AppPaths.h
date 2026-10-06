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
