// Totthodhara (QML edition) entry point. GUI-first build: all data is
// mocked in QML (see qml/ClipStore.qml). The C++ backend (clipboard hook,
// SQLite, hotkeys) will be added later behind the same ClipStore contract.
#include <QGuiApplication>
#include <QQmlApplicationEngine>
#include <QQmlContext>
#include <QQuickStyle>
#include <QDebug>
#include <QDir>
#include <QFile>
#include <QSettings>
#include <QSharedMemory>
#include <QTextStream>
#include <QtQml>

#include "backend/AppBarService.h"
#include "backend/AppPaths.h"
#include "backend/ClipboardService.h"
#include "backend/StorageService.h"
#include "backend/FullscreenService.h"
#include "backend/SystemMonitorService.h"
#include "backend/ThemeService.h"
#include "backend/UpdateService.h"

#ifdef Q_OS_WINDOWS
#include <windows.h>
#endif

namespace {
QFile *g_log = nullptr;

void fileLog(const QString &line)
{
    if (!g_log) {
        g_log = new QFile(QStringLiteral("C:/Temp/tott_startup.log"));
        g_log->open(QIODevice::WriteOnly | QIODevice::Truncate | QIODevice::Text);
    }
    QTextStream out(g_log);
    out << line << Qt::endl;
    out.flush();
}

void logToFile(QtMsgType, const QMessageLogContext &, const QString &msg)
{
    fileLog(msg);
#ifdef Q_OS_WINDOWS
    // DebugView fallback: with no C:/Temp every message is otherwise lost
    // (WIN32 GUI builds have no console).
    OutputDebugStringW(reinterpret_cast<const wchar_t *>(msg.utf16()));
    OutputDebugStringW(L"\n");
#endif
}
} // namespace

int main(int argc, char *argv[])
{
    // All Qt messages (warnings AND runtime TypeErrors, which are silent
    // in WIN32 builds) go to the startup log.
    qInstallMessageHandler(logToFile);
    // Single instance: a second launch would double-dock the AppBar and
    // fight over the clipboard/DB. Later this can focus the first window.
    // A stale segment (previous crash) fails exactly like a live one, so
    // reap it once before giving up — otherwise the app can never start.
    QSharedMemory singleInstance(QStringLiteral("TotthodharaShelfV1"));
    if (!singleInstance.create(1)) {
        singleInstance.attach();
        singleInstance.detach();
        if (!singleInstance.create(1)) {
            qInfo() << "another instance is already running";
            return 0;
        }
    }

    fileLog(QStringLiteral("stage: main entered"));
    qInfo() << "stage: creating QGuiApplication";
    QGuiApplication app(argc, argv);
    app.setOrganizationName(QStringLiteral("Totthodhara"));
    app.setApplicationName(QStringLiteral("Totthodhara"));
    app.setApplicationVersion(QStringLiteral("0.2.0"));
    // Fully portable prefs: settings live next to history.db as an INI
    // file instead of the registry, so the whole app (history + prefs)
    // moves with the folder. Same dir AppPaths resolves (stub env aware).
    // QML Settings follows the default format/path. Must run before the
    // engine loads (first QSettings use wins).
    const QString dataDir = AppPaths::dataDir();
    QSettings::setDefaultFormat(QSettings::IniFormat);
    QSettings::setPath(QSettings::IniFormat, QSettings::UserScope, dataDir);
    fileLog(QStringLiteral("argv: ") + QCoreApplication::arguments().join(QStringLiteral("|")));

    // C++ services visible to QML (backend stage grows here).
    qmlRegisterType<AppBarService>("Totthodhara.Backend", 1, 0, "AppBarService");
    qmlRegisterType<FullscreenService>("Totthodhara.Backend", 1, 0, "FullscreenService");
    qmlRegisterType<ClipboardService>("Totthodhara.Backend", 1, 0, "ClipboardService");
    qmlRegisterType<StorageService>("Totthodhara.Backend", 1, 0, "StorageService");
    qmlRegisterType<ThemeService>("Totthodhara.Backend", 1, 0, "ThemeService");
    // Live meters singleton (one instance for the whole app).
    auto *sysMon = new SystemMonitorService(&app);
    qmlRegisterSingletonInstance("Totthodhara.Backend", 1, 0, "SysMon", sysMon);
    // Windows Light/Dark watcher for the "System" shelf theme.
    auto *themeWatcher = new ThemeService(&app);
    qmlRegisterSingletonInstance("Totthodhara.Backend", 1, 0, "ThemeWatcher", themeWatcher);
    // In-app updater (About > Check for updates).
    auto *updater = new UpdateService(&app);
    qmlRegisterSingletonInstance("Totthodhara.Backend", 1, 0, "Updater", updater);
    qInfo() << "stage: platform =" << QGuiApplication::platformName();

    // Pinned control style: the native Windows style IGNORES custom
    // background/contentItem/indicator (verified: deploy ran native and all
    // themed controls fell back to white). Basic is fully customizable, so
    // settings look identical in dev and portable builds. Must run before
    // any QML loads.
    QQuickStyle::setStyle(QStringLiteral("Basic"));

    QQmlApplicationEngine engine;
    // `--settings` is evaluated in C++ (argc/argv): the QML-side
    // Qt.application.arguments check proved unreliable across builds.
    engine.rootContext()->setContextProperty(
        QStringLiteral("openSettingsOnStart"),
        QCoreApplication::arguments().contains(QStringLiteral("--settings")));
    fileLog(QStringLiteral("stage: engine created"));
    qInfo() << "stage: engine created";
    QObject::connect(
        &engine, &QQmlApplicationEngine::objectCreationFailed,
        &app, [](const QUrl &url) {
            fileLog(QStringLiteral("QML FAILED: ") + url.toString());
            qCritical().noquote() << QStringLiteral("QML FAILED: %1").arg(url.toString());
            QCoreApplication::exit(-1);
        },
        Qt::QueuedConnection);
    QObject::connect(&engine, &QQmlApplicationEngine::warnings,
                     [](const QList<QQmlError> &warnings) {
                         for (const auto &w : warnings) {
                             fileLog(QStringLiteral("QML warning: ") + w.toString());
                             qWarning().noquote() << "QML warning:" << w.toString();
                         }
                     });
    fileLog(QStringLiteral("stage: loading module"));
    qInfo() << "stage: loading module";
    engine.loadFromModule("Totthodhara", "Main");
    // Note: creation is asynchronous; objectCreationFailed above is the
    // real guard. Do NOT check rootObjects() here (false alarm).
    return app.exec();
}
