#pragma once

#include <QObject>
#include <QString>
#include <QTimer>
#include <QElapsedTimer>
#include <QVariantList>

// Live system meters for the shelf pill: CPU %, RAM %, network up/down.
// Windows implementation: PDH (CPU), GlobalMemoryStatusEx (RAM),
// GetIfTable octet deltas (network). Ticks once per second.
class SystemMonitorService : public QObject
{
    Q_OBJECT
    Q_PROPERTY(QString cpuText READ cpuText NOTIFY updated)
    Q_PROPERTY(QString memText READ memText NOTIFY updated)
    Q_PROPERTY(QString upText READ upText NOTIFY updated)
    Q_PROPERTY(QString downText READ downText NOTIFY updated)
    // World clock zones (IANA id, "" = device local, "UTC" = universal).
    // Set from QML (AppState.zoneA/zoneB); texts/tags follow each tick.
    Q_PROPERTY(QString zoneA READ zoneA WRITE setZoneA NOTIFY zonesChanged)
    Q_PROPERTY(QString zoneB READ zoneB WRITE setZoneB NOTIFY zonesChanged)
    Q_PROPERTY(QString zoneAText READ zoneAText NOTIFY updated)
    Q_PROPERTY(QString zoneBText READ zoneBText NOTIFY updated)
    Q_PROPERTY(QString zoneATag READ zoneATag NOTIFY updated)
    Q_PROPERTY(QString zoneBTag READ zoneBTag NOTIFY updated)
public:
    explicit SystemMonitorService(QObject *parent = nullptr);
    ~SystemMonitorService() override;

    QString cpuText() const { return m_cpuText; }
    QString memText() const { return m_memText; }
    QString upText() const { return m_upText; }
    QString downText() const { return m_downText; }
    QString zoneA() const { return m_zoneA; }
    QString zoneB() const { return m_zoneB; }
    QString zoneAText() const { return m_zoneAText; }
    QString zoneBText() const { return m_zoneBText; }
    QString zoneATag() const { return m_zoneTagA; }
    QString zoneBTag() const { return m_zoneTagB; }
    void setZoneA(const QString &z);
    void setZoneB(const QString &z);
    // Every real-world zone (Windows-picker style): [{id, city, offset}],
    // ordered by current UTC offset. Called once for the dropdowns.
    Q_INVOKABLE QVariantList timeZoneList() const;

signals:
    void updated();
    void zonesChanged();

private:
    void tick();
    static QString zoneTime(const QString &zone);
    static QString zoneTag(const QString &zone);

    QString m_cpuText = QStringLiteral("--");
    QString m_memText = QStringLiteral("--");
    QString m_upText = QStringLiteral("--");
    QString m_downText = QStringLiteral("--");
    QString m_zoneA;
    QString m_zoneB = QStringLiteral("UTC");
    QString m_zoneAText = QStringLiteral("--:--");
    QString m_zoneBText = QStringLiteral("--:--");
    QString m_zoneTagA = QStringLiteral("LOC");
    QString m_zoneTagB = QStringLiteral("UTC");
    QTimer m_timer;

#ifdef Q_OS_WINDOWS
    void *m_cpuQuery = nullptr;   // PDH_HQUERY (opaque here, no pdh.h in header)
    void *m_cpuCounter = nullptr; // PDH_HCOUNTER
    unsigned long long m_prevIn = 0;
    unsigned long long m_prevOut = 0;
    bool m_havePrevNet = false;
    int m_prevIfCount = -1;
    QElapsedTimer m_netClock; // real elapsed per tick: exact per-second rates
#endif
};
