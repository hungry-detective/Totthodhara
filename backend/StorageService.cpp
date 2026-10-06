#include "StorageService.h"
#include "backend/AppPaths.h"

#include <QDir>
#include <QDateTime>
#include <QFileInfo>
#include <QSqlDatabase>
#include <QSqlError>
#include <QSqlQuery>
#include <QStandardPaths>
#include <QUrl>
#include <QVariantMap>
#include <QtDebug>

StorageService::StorageService(QObject *parent)
    : QObject(parent)
{
    m_dbPath = AppPaths::dataDir()
               + QStringLiteral("/history.db");
    QDir().mkpath(QFileInfo(m_dbPath).absolutePath());
    ensureOpen();
}

void StorageService::ensureOpen()
{
    if (m_open)
        return;
    QSqlDatabase db = QSqlDatabase::addDatabase(QStringLiteral("QSQLITE"), "clips");
    db.setDatabaseName(m_dbPath);
    if (!db.open()) {
        qWarning() << "history db open failed:" << db.lastError().text();
        return;
    }
    QSqlQuery q(db);
    // added = recency counter (sort key), no other ids needed.
    // m_open flips ONLY on success: a half-made schema must retry, never
    // silently no-op every later load/save.
    if (!q.exec(QStringLiteral("CREATE TABLE IF NOT EXISTS clips ("
                               "title TEXT, kind TEXT, detail TEXT, icon TEXT,"
                               "pinned INTEGER, snippet INTEGER, added INTEGER)"))) {
        qWarning() << "history schema failed:" << q.lastError().text();
        return;
    }
    m_open = true;
}

QVariantList StorageService::loadItems()
{
    QVariantList out;
    if (!m_open)
        ensureOpen();
    if (!m_open)
        return out;
    QSqlQuery q(QSqlDatabase::database(QStringLiteral("clips")));
    if (!q.exec(QStringLiteral("SELECT title,kind,detail,icon,pinned,snippet,added"
                               " FROM clips ORDER BY added DESC LIMIT 500"))) {
        qWarning() << "history load failed:" << q.lastError().text();
        return out;
    }
    while (q.next()) {
        QVariantMap m;
        QString title = q.value(0).toString();
        // Heal legacy rows: strip embedded NULs (see handleClipboard).
        title.remove(QChar(0));
        const QString kind = q.value(1).toString();
        // Legacy rows stored "Image WxH": display bare resolution now.
        if (kind == QStringLiteral("image") && title.startsWith(QStringLiteral("Image ")))
            title = title.mid(6);
        m[QStringLiteral("title")] = title;
        m[QStringLiteral("kind")] = kind;
        QString detail = q.value(2).toString();
        detail.remove(QChar(0));
        m[QStringLiteral("detail")] = detail;
        m[QStringLiteral("icon")] = q.value(3).toString();
        m[QStringLiteral("pinned")] = q.value(4).toBool();
        m[QStringLiteral("snippet")] = q.value(5).toBool();
        m[QStringLiteral("selected")] = false;
        m[QStringLiteral("added")] = q.value(6).toLongLong();
        out << m;
    }
    return out;
}

void StorageService::saveItems(const QVariantList &items)
{
    if (!m_open)
        ensureOpen();
    if (!m_open)
        return;
    QSqlDatabase db = QSqlDatabase::database(QStringLiteral("clips"));
    QSqlQuery q(db);
    // Full rewrite: a failed commit after a successful DELETE is total
    // history loss, so every step is checked and any failure rolls back.
    if (!db.transaction()) {
        qWarning() << "history save failed: no transaction:" << db.lastError().text();
        return;
    }
    if (!q.exec(QStringLiteral("DELETE FROM clips"))) {
        qWarning() << "history save failed: no clear:" << q.lastError().text();
        db.rollback();
        return;
    }
    q.prepare(QStringLiteral("INSERT INTO clips"
                             " (title,kind,detail,icon,pinned,snippet,added)"
                             " VALUES (?,?,?,?,?,?,?)"));
    for (const QVariant &v : items) {
        const QVariantMap m = v.toMap();
        q.addBindValue(m.value(QStringLiteral("title")));
        q.addBindValue(m.value(QStringLiteral("kind")));
        q.addBindValue(m.value(QStringLiteral("detail")));
        q.addBindValue(m.value(QStringLiteral("icon")));
        q.addBindValue(m.value(QStringLiteral("pinned"), false).toBool() ? 1 : 0);
        q.addBindValue(m.value(QStringLiteral("snippet"), false).toBool() ? 1 : 0);
        q.addBindValue(m.value(QStringLiteral("added"), 0).toLongLong());
        if (!q.exec()) {
            qWarning() << "history save failed: no insert:" << q.lastError().text();
            db.rollback();
            return;
        }
    }
    if (!db.commit()) {
        qWarning() << "history save failed: no commit:" << db.lastError().text();
        db.rollback();
    }
}

double StorageService::fileAgeHours(const QString &fileUrl)
{
    const QString local = QUrl(fileUrl).isLocalFile()
                              ? QUrl(fileUrl).toLocalFile()
                              : fileUrl;
    const QFileInfo info(local);
    if (!info.exists())
        return -1.0;
    const qint64 ms = QDateTime::currentDateTime().toMSecsSinceEpoch()
                      - info.lastModified().toMSecsSinceEpoch();
    return double(ms) / 3600000.0;
}

bool StorageService::removeFile(const QString &fileUrl)
{
    const QString local = QUrl(fileUrl).isLocalFile()
                              ? QUrl(fileUrl).toLocalFile()
                              : fileUrl;
    if (local.isEmpty() || !QFile::exists(local))
        return false;
    return QFile::remove(local);
}

void StorageService::removeItemFiles(const QString &detail)
{
    if (!detail.startsWith(QStringLiteral("file:"), Qt::CaseInsensitive))
        return;
    const QUrl url(detail);
    const QString local = url.isLocalFile() ? url.toLocalFile() : detail;
    if (local.isEmpty())
        return;
    // Strict containment (case-insensitive: Windows FS): only our own
    // clips/ payloads, never user files.
    const QString clips = QDir(AppPaths::dataDir() + QStringLiteral("/clips")).absolutePath();
    const QString target = QDir(local).absolutePath();
    if (!target.startsWith(clips + QLatin1Char('/'), Qt::CaseInsensitive))
        return;
    QFile::remove(target);
}
