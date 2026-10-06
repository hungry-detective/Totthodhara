#include "SystemMonitorService.h"

#include <QDateTime>
#include <QTimeZone>
#include <QVariantMap>

#include <algorithm>
#include <utility>

#ifdef Q_OS_WINDOWS
#include <windows.h>
#include <pdh.h>
#include <pdhmsg.h>
#include <iphlpapi.h>
#endif

namespace {
// IDM-style units: plain bytes (KB/s, MB/s — never bits), one shared
// unit for up+down so neighbors never shift between K and M.
void formatPair(double upBytes, double downBytes, QString &up, QString &down)
{
    if (qMax(upBytes, downBytes) >= 1048576.0) {
        up = QString::asprintf("%.1f MB/s", upBytes / 1048576.0);
        down = QString::asprintf("%.1f MB/s", downBytes / 1048576.0);
    } else {
        up = QString::asprintf("%.1f KB/s", upBytes / 1024.0);
        down = QString::asprintf("%.1f KB/s", downBytes / 1024.0);
    }
}
} // namespace

SystemMonitorService::SystemMonitorService(QObject *parent)
    : QObject(parent)
{
#ifdef Q_OS_WINDOWS
    // CPU: total processor time counter (needs two samples, one per tick).
    PDH_HQUERY query = nullptr;
    PDH_HCOUNTER counter = nullptr;
    if (PdhOpenQueryW(nullptr, 0, &query) == ERROR_SUCCESS
        && PdhAddEnglishCounterW(query, L"\\Processor(_Total)\\% Processor Time",
                                 0, &counter) == ERROR_SUCCESS) {
        m_cpuQuery = query;
        m_cpuCounter = counter;
        PdhCollectQueryData(query);
    } else if (query) {
        // Counter failed: don't leak the half-open query.
        PdhCloseQuery(query);
    }
#endif
    connect(&m_timer, &QTimer::timeout, this, &SystemMonitorService::tick);
    m_timer.setInterval(1000);
    m_timer.start();
    tick(); // immediate first paint ("--" until counters warm up)
}

SystemMonitorService::~SystemMonitorService()
{
#ifdef Q_OS_WINDOWS
    if (m_cpuQuery)
        PdhCloseQuery(static_cast<PDH_HQUERY>(m_cpuQuery));
#endif
}

void SystemMonitorService::tick()
{
#ifdef Q_OS_WINDOWS
    // --- CPU ---
    if (m_cpuQuery) {
        PdhCollectQueryData(static_cast<PDH_HQUERY>(m_cpuQuery));
        PDH_FMT_COUNTERVALUE value = {};
        if (PdhGetFormattedCounterValue(static_cast<PDH_HCOUNTER>(m_cpuCounter),
                                       PDH_FMT_DOUBLE, nullptr, &value) == ERROR_SUCCESS
            && value.CStatus == ERROR_SUCCESS) {
            m_cpuText = QStringLiteral("%1%").arg(qRound(value.doubleValue));
        }
    }

    // --- RAM ---
    MEMORYSTATUSEX mem = {};
    mem.dwLength = sizeof(mem);
    if (GlobalMemoryStatusEx(&mem) && mem.ullTotalPhys > 0) {
        const double used = 100.0 * (mem.ullTotalPhys - mem.ullAvailPhys)
                            / double(mem.ullTotalPhys);
        m_memText = QStringLiteral("%1%").arg(qRound(used));
    }

    // --- Network: sum octets over operational, non-loopback interfaces ---
    DWORD size = 0;
    if (GetIfTable(nullptr, &size, TRUE) == ERROR_INSUFFICIENT_BUFFER) {
        if (MIB_IFTABLE *table = static_cast<MIB_IFTABLE *>(malloc(size))) {
            if (GetIfTable(table, &size, TRUE) == NO_ERROR) {
                unsigned long long inBytes = 0;
                unsigned long long outBytes = 0;
                int ifCount = 0;
                for (DWORD i = 0; i < table->dwNumEntries; ++i) {
                    const MIB_IFROW &row = table->table[i];
                    // Operational real NICs only. Tunnel/PPP/loopback/proprietary-
                    // virtual are skipped: VPN traffic also crosses the
                    // physical NIC, so counting virtual adapters doubles (or
                    // worse) the speed. propVirtual(53) is by definition not
                    // a physical link (VM guests present as ethernet here).
                    if (row.dwOperStatus != IF_OPER_STATUS_OPERATIONAL
                        || row.dwType == IF_TYPE_SOFTWARE_LOOPBACK
                        || row.dwType == IF_TYPE_TUNNEL
                        || row.dwType == IF_TYPE_PPP
                        || row.dwType == 53)
                        continue;
                    const QString descr = QString::fromLatin1(
                        reinterpret_cast<const char *>(row.bDescr),
                        int(row.dwDescrLen)); // NOT NUL-terminated: bound it
                    const QString low = descr.toLower();
                    // NDIS filter drivers (QoS, WFP, WiFi filters) register
                    // SEPARATE rows mirroring the physical NIC 1:1 — six of
                    // them turned 5 MB/s into 37 MB/s on one machine.
                    // Name match too: VPN shims (Proton/Wintun/WireGuard,
                    // NordLynx) often register as plain ethernet, not tunnel
                    // type. Never exclude by "virtual"/"hyper-v" alone: inside
                    // a VM the guest NIC *is* the real link.
                    if (low.contains(QStringLiteral("vpn"))
                        || low.contains(QStringLiteral("wintun"))
                        || low.contains(QStringLiteral("wireguard"))
                        || low.contains(QStringLiteral("lynx"))
                        || low.contains(QStringLiteral("tap"))
                        || low.contains(QStringLiteral("tun"))
                        || low.contains(QStringLiteral("pseudo"))
                        || low.contains(QStringLiteral("loopback"))
                        || low.contains(QStringLiteral("teredo"))
                        || low.contains(QStringLiteral("isatap"))
                        || low.contains(QStringLiteral("filter"))
                        || low.contains(QStringLiteral("qos"))
                        || low.contains(QStringLiteral("wfp"))
                        || low.contains(QStringLiteral("lightweight"))
                        || low.contains(QStringLiteral("virtual wifi"))
                        || low.contains(QStringLiteral("hosted")))
                        continue;
                    {
                        inBytes += row.dwInOctets;
                        outBytes += row.dwOutOctets;
                        ifCount++;
                    }
                }
                // Real elapsed (not assumed 1s) -> exact per-second rate.
                // Interface set changed (VPN on/off) -> re-baseline silently
                // instead of flashing one bogus spike.
                const double seconds = m_netClock.isValid()
                                           ? m_netClock.restart() / 1000.0
                                           : 0.0;
                if (!m_netClock.isValid())
                    m_netClock.start();
                if (m_havePrevNet && seconds > 0.05 && ifCount == m_prevIfCount) {
                    // Counters are 32-bit and wrap: mask the delta.
                    // Raw per-second deltas (no smoothing): the number shown
                    // is exactly what moved in the last second.
                    const auto delta = [](unsigned long long cur, unsigned long long prev) {
                        return cur >= prev ? cur - prev : (0x100000000ULL - prev) + cur;
                    };
                    formatPair(double(delta(outBytes, m_prevOut)) / seconds,
                               double(delta(inBytes, m_prevIn)) / seconds,
                               m_upText, m_downText);
                }
                m_prevIn = inBytes;
                m_prevOut = outBytes;
                m_havePrevNet = true;
                m_prevIfCount = ifCount;
            }
            free(table);
        }
    }
#endif
    // World clock faces (zone A + B), refreshed on the same 1s tick.
    m_zoneAText = zoneTime(m_zoneA);
    m_zoneBText = zoneTime(m_zoneB);
    m_zoneTagA = zoneTag(m_zoneA);
    m_zoneTagB = zoneTag(m_zoneB);
    emit updated();
}

void SystemMonitorService::setZoneA(const QString &z)
{
    if (m_zoneA == z)
        return;
    m_zoneA = z;
    emit zonesChanged();
}

void SystemMonitorService::setZoneB(const QString &z)
{
    if (m_zoneB == z)
        return;
    m_zoneB = z;
    emit zonesChanged();
}

QString SystemMonitorService::zoneTime(const QString &zone)
{
    // Convert properly: reinterpret the UTC instant in the target zone via
    // its offset (DST-aware for that instant). Simply re-labeling the UTC
    // wall clock (the old bug) shows UTC everywhere outside Local.
    const QDateTime utc = QDateTime::currentDateTimeUtc();
    if (zone.isEmpty())
        return QDateTime::currentDateTime().toString(QStringLiteral("h:mm AP"));
    if (zone == QStringLiteral("UTC"))
        return utc.toString(QStringLiteral("h:mm AP"));
    const QTimeZone tz(zone.toLatin1());
    if (!tz.isValid())
        return utc.toString(QStringLiteral("h:mm AP"));
    return utc.addSecs(tz.offsetFromUtc(utc)).toString(QStringLiteral("h:mm AP"));
}

QString SystemMonitorService::zoneTag(const QString &zone)
{
    static const std::pair<const char *, const char *> table[] = {
        {"", "LOC"}, {"UTC", "UTC"},
        {"America/Los_Angeles", "LA"}, {"America/Phoenix", "PHX"},
        {"America/Denver", "DEN"}, {"America/Chicago", "CHI"},
        {"America/New_York", "NYC"}, {"America/Sao_Paulo", "SAO"},
        {"America/Anchorage", "ANC"}, {"America/Mexico_City", "MEX"},
        {"Pacific/Honolulu", "HNL"},
        {"Europe/London", "LON"}, {"Europe/Paris", "PAR"},
        {"Europe/Moscow", "MSK"}, {"Africa/Cairo", "CAI"},
        {"Asia/Dubai", "DXB"}, {"Asia/Karachi", "KHI"},
        {"Asia/Dhaka", "DHK"}, {"Asia/Bangkok", "BKK"},
        {"Asia/Singapore", "SIN"}, {"Asia/Tokyo", "TYO"},
        {"Australia/Sydney", "SYD"}, {"Pacific/Auckland", "AKL"},
    };
    const QString z = zone;
    for (const auto &e : table) {
        if (z == QLatin1String(e.first))
            return QString::fromLatin1(e.second);
    }
    // Fallback: city part, first 3 letters ("New_York" -> "NEW").
    const QString city = zone.section(QLatin1Char('/'), -1).replace(QLatin1Char('_'), QLatin1Char(' '));
    return city.left(3).toUpper();
}

QVariantList SystemMonitorService::timeZoneList() const
{
    struct Entry {
        QString id;
        int offset;
    };
    QList<Entry> all;
    const QDateTime now = QDateTime::currentDateTimeUtc();
    for (const QByteArray &raw : QTimeZone::availableTimeZoneIds()) {
        const QString id = QString::fromLatin1(raw);
        // Skip administrative aliases (Windows shows real regions only).
        if (id.startsWith(QStringLiteral("Etc/")) || id.startsWith(QStringLiteral("SystemV/"))
            || id.startsWith(QStringLiteral("GMT")))
            continue;
        const QTimeZone tz(raw);
        if (!tz.isValid())
            continue;
        all << Entry{id, tz.offsetFromUtc(now)};
    }
    std::sort(all.begin(), all.end(), [](const Entry &a, const Entry &b) {
        return a.offset != b.offset ? a.offset < b.offset : a.id < b.id;
    });
    QVariantList out;
    for (const Entry &e : all) {
        const int totalMin = e.offset / 60;
        const int hh = qAbs(totalMin) / 60;
        const int mm = qAbs(totalMin) % 60;
        QVariantMap m;
        m[QStringLiteral("id")] = e.id;
        m[QStringLiteral("city")] = e.id.section(QLatin1Char('/'), -1).replace(QLatin1Char('_'), QLatin1Char(' '));
        m[QStringLiteral("offset")] = QStringLiteral("(UTC%1%2:%3)")
                                          .arg(e.offset < 0 ? QStringLiteral("-") : QStringLiteral("+"))
                                          .arg(hh, 2, 10, QLatin1Char('0'))
                                          .arg(mm, 2, 10, QLatin1Char('0'));
        out << m;
    }
    return out;
}
