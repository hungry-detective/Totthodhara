#pragma once

#include <QObject>
#include <QString>
#include <QVariantList>

// Portable history: a single SQLite file (history.db) next to the other
// app data. Same role as the WPF .sqlite store — clips survive reboots.
// Strategy is deliberately simple: full rewrite on every mutation
// (a few hundred small rows = milliseconds) instead of id bookkeeping.
class StorageService : public QObject
{
    Q_OBJECT
public:
    explicit StorageService(QObject *parent = nullptr);

    // Rows: {title, kind, detail, icon, pinned, snippet, added}.
    Q_INVOKABLE QVariantList loadItems();
    Q_INVOKABLE void saveItems(const QVariantList &items);
    // Hours since the file behind a file:// URL was modified
    // (-1 when missing/unreadable). Powers the auto-clean setting.
    Q_INVOKABLE double fileAgeHours(const QString &fileUrl);
    // Delete the local file behind a file:// URL. False when missing.
    Q_INVOKABLE bool removeFile(const QString &fileUrl);
    // Delete the on-disk payload of a removed clip (image/file card).
    // Only files inside <data>/clips/ are touched — Explorer originals
    // and the shared per-host favicon cache are never deleted.
    Q_INVOKABLE void removeItemFiles(const QString &detail);

private:
    void ensureOpen();
    QString m_dbPath;
    bool m_open = false;
};
