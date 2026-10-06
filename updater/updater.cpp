// Totthodhara self-updater: a tiny GUI that swaps the portable install.
//
// Launched (detached) by the app as  stage/TotthodharaUpdater.exe
//   --root DIR --zip FILE --version TAG
// right before the app quits. It MUST run from the temp stage dir — never
// from the install tree it wipes — narrates every step in its own window,
// preserves data/ (clips, history, settings), relaunches the stub, cleans
// up after itself, then exits. Exit 0 ok, 1 failed (old install relaunched
// when still intact).
//
// No Q_OBJECT here on purpose (lambdas only): this file needs no MOC run.
#include <QApplication>
#include <QDir>
#include <QDirIterator>
#include <QFile>
#include <QFileInfo>
#include <QLabel>
#include <QProcess>
#include <QProgressBar>
#include <QTextStream>
#include <QTimer>
#include <QVBoxLayout>
#include <QWidget>

#include <algorithm>

namespace {

QStringList collectFiles(const QString &dir, const QString &skipTop)
{
    QStringList out;
    QDirIterator it(dir,
                    QDir::Files | QDir::Hidden | QDir::System,
                    QDirIterator::Subdirectories);
    while (it.hasNext()) {
        it.next();
        const QString rel = QDir(dir).relativeFilePath(it.filePath());
        if (!skipTop.isEmpty()
            && (rel == skipTop || rel.startsWith(skipTop + QLatin1Char('/'))))
            continue;
        out << it.filePath();
    }
    out.sort();
    return out;
}

QStringList collectDirs(const QString &dir, const QString &skipTop)
{
    QStringList out;
    QDirIterator it(dir,
                    QDir::Dirs | QDir::NoDotAndDotDot | QDir::Hidden | QDir::System,
                    QDirIterator::Subdirectories);
    while (it.hasNext()) {
        it.next();
        const QString rel = QDir(dir).relativeFilePath(it.filePath());
        if (!skipTop.isEmpty()
            && (rel == skipTop || rel.startsWith(skipTop + QLatin1Char('/'))))
            continue;
        out << it.filePath();
    }
    // Deepest first so every rmdir lands on an empty dir.
    std::sort(out.begin(), out.end(),
              [](const QString &a, const QString &b) { return a.length() > b.length(); });
    return out;
}

} // namespace

class UpdaterWindow : public QWidget
{
public:
    UpdaterWindow(const QString &root, const QString &zip, const QString &version)
        : m_root(root)
        , m_zip(zip)
    {
        setWindowTitle(QStringLiteral("Totthodhara Update"));
        setWindowFlags(windowFlags() | Qt::WindowStaysOnTopHint);
        setFixedSize(430, 190);

        auto *lay = new QVBoxLayout(this);
        lay->setContentsMargins(18, 14, 18, 14);
        lay->setSpacing(8);

        m_title = new QLabel(tr("Updating to %1 — clips and settings are kept.").arg(version), this);
        m_title->setWordWrap(true);
        lay->addWidget(m_title);

        m_status = new QLabel(tr("Starting…"), this);
        QFont f = m_status->font();
        f.setBold(true);
        f.setPixelSize(13);
        m_status->setFont(f);
        lay->addWidget(m_status);

        m_bar = new QProgressBar(this);
        m_bar->setRange(0, 100);
        m_bar->setValue(0);
        m_bar->setTextVisible(true);
        lay->addWidget(m_bar);

        m_detail = new QLabel(this);
        m_detail->setWordWrap(false);
        lay->addWidget(m_detail);
        lay->addStretch(1);
    }

    void start()
    {
        // Portable guard FIRST: a wrong-shaped root must never be wiped.
        // (The app checks this too; defense in depth — this process does
        // the deleting.) data/ is never touched by any step below.
        if (!QFile::exists(m_root + QStringLiteral("/library/Totthodhara.exe"))
            || !QFile::exists(m_zip)) {
            fail(tr("Not a portable install (or the download is missing). "
                    "Nothing was changed — start the app manually."));
            return;
        }
        stepWait();
    }

private:
    void setStep(const QString &text, int base, int span)
    {
        m_status->setText(text);
        m_base = base;
        m_span = span;
        m_bar->setValue(base);
        QApplication::processEvents();
    }

    void setProgress(int i, int n, const QString &detail = QString())
    {
        if (n > 0)
            m_bar->setValue(m_base + m_span * i / n);
        if (!detail.isEmpty()) {
            QString d = detail;
            if (d.length() > 64)
                d = QStringLiteral("…") + d.right(63);
            m_detail->setText(d);
        }
        QApplication::processEvents();
    }

    // 1. Wait until the app (locks + mutex) is really gone.
    void stepWait()
    {
        setStep(tr("Waiting for the app to exit…"), 0, 5);
        m_ticks = 0;
        auto *t = new QTimer(this);
        connect(t, &QTimer::timeout, this, [this, t] {
            QProcess::startDetached(QStringLiteral("taskkill"),
                                    {QStringLiteral("/F"), QStringLiteral("/IM"),
                                     QStringLiteral("Totthodhara.exe")});
            const QString probe = QDir::tempPath() + QStringLiteral("/__tott_updlock");
            QFile::remove(probe);
            if (QFile::copy(m_root + QStringLiteral("/library/Totthodhara.exe"), probe)) {
                QFile::remove(probe);
                t->stop();
                t->deleteLater();
                stepUnpack();
                return;
            }
            if (++m_ticks > 180) { // 3 minutes: something is wedged.
                t->stop();
                t->deleteLater();
                fail(tr("The old version would not exit."));
            }
        });
        t->start(1000);
    }

    // 2. Unpack the release next to the stage.
    void stepUnpack()
    {
        setStep(tr("Unpacking the new version…"), 5, 20);
        m_bar->setRange(0, 0); // busy: Expand-Archive gives no progress
        auto *p = new QProcess(this);
        connect(p,
                QOverload<int, QProcess::ExitStatus>::of(&QProcess::finished),
                this, [this, p](int code, QProcess::ExitStatus) {
                    p->deleteLater();
                    m_bar->setRange(0, 100);
                    if (code != 0) {
                        fail(tr("Could not unpack the download."));
                        return;
                    }
                    stepVerify();
                });
        const QString stage = QFileInfo(m_zip).absolutePath();
        p->start(QStringLiteral("powershell"),
                 {QStringLiteral("-NoProfile"), QStringLiteral("-Command"),
                  QStringLiteral("Expand-Archive -Force '%1' '%2\\new'")
                      .arg(m_zip, stage)});
    }

    // 3. Verify the staged layout BEFORE wiping anything.
    void stepVerify()
    {
        setStep(tr("Verifying the package…"), 25, 5);
        const QString stage = QFileInfo(m_zip).absolutePath();
        if (!QFile::exists(stage + QStringLiteral("/new/library/Totthodhara.exe"))
            || !QFile::exists(stage + QStringLiteral("/new/Totthodhara.exe"))) {
            fail(tr("The download looks wrong — nothing was changed."));
            return;
        }
        stepWipe();
    }

    // 4. Replace everything except data/.
    void stepWipe()
    {
        setStep(tr("Removing old files (clips and settings are kept)…"), 30, 20);
        const QStringList files = collectFiles(m_root, QStringLiteral("data"));
        const QStringList dirs = collectDirs(m_root, QStringLiteral("data"));
        const int total = files.size() + dirs.size();
        int i = 0;
        for (const QString &fp : files)
            QFile::remove(fp), setProgress(++i, total);
        for (const QString &dp : dirs)
            QDir().rmdir(dp), setProgress(++i, total);
        // Root-level files (the old stub).
        QDir rootDir(m_root);
        for (const QString &fn :
             rootDir.entryList(QDir::Files | QDir::Hidden | QDir::System))
            QFile::remove(rootDir.filePath(fn));
        stepCopy();
    }

    void stepCopy()
    {
        setStep(tr("Copying new files…"), 50, 45);
        const QString stage = QFileInfo(m_zip).absolutePath();
        const QString src = stage + QStringLiteral("/new");
        const QStringList files = collectFiles(src, QString());
        int i = 0;
        for (const QString &fp : files) {
            const QString rel = QDir(src).relativeFilePath(fp);
            const QString dst = m_root + QLatin1Char('/') + rel;
            QDir().mkpath(QFileInfo(dst).absolutePath());
            QFile::remove(dst);
            if (!QFile::copy(fp, dst)) {
                fail(tr("Could not write %1 — your data/ is untouched. "
                         "Re-extract the portable zip by hand to recover.")
                         .arg(rel));
                return;
            }
            setProgress(++i, files.size(), rel);
        }
        stepDone();
    }

    void stepDone()
    {
        setStep(tr("Done — starting Totthodhara…"), 95, 5);
        m_detail->clear();
        QProcess::startDetached(m_root + QStringLiteral("/Totthodhara.exe"), {});
        // Give this process a head start out, then remove the stage
        // (including our own exe — a running exe cannot delete itself).
        // A script FILE, not an inline `cmd /c` chain: inline chains
        // re-quote nested paths and silently do nothing (verified live).
        const QString stage = QFileInfo(m_zip).absolutePath();
        const QString cleaner = stage + QStringLiteral("/cleanup.cmd");
        QFile f(cleaner);
        if (f.open(QIODevice::WriteOnly | QIODevice::Truncate | QIODevice::Text)) {
            QTextStream s(&f);
            s << "@echo off\n";
            s << "ping -n 4 127.0.0.1 >nul\n";
            s << "rmdir /S /Q \"" << QDir::toNativeSeparators(stage) << "\"\n";
            s << "exit /b 0\n";
            f.close();
            QProcess::startDetached(cleaner, {});
        }
        QTimer::singleShot(800, this, [] { QApplication::quit(); });
    }

    void fail(const QString &why)
    {
        m_status->setText(why);
        QPalette pal = m_status->palette();
        pal.setColor(QPalette::WindowText, QColor(255, 110, 110));
        m_status->setPalette(pal);
        m_detail->clear();
        // Best effort: hand the user a running app if one still exists.
        QProcess::startDetached(m_root + QStringLiteral("/Totthodhara.exe"), {});
        QTimer::singleShot(8000, this, [] { QApplication::quit(); });
    }

    QString m_root;
    QString m_zip;
    QLabel *m_title = nullptr;
    QLabel *m_status = nullptr;
    QLabel *m_detail = nullptr;
    QProgressBar *m_bar = nullptr;
    int m_base = 0;
    int m_span = 0;
    int m_ticks = 0;
};

int main(int argc, char *argv[])
{
    QApplication app(argc, argv);
    app.setApplicationName(QStringLiteral("Totthodhara Update"));
    QString root, zip, version;
    for (int i = 1; i + 1 < argc; i += 2) {
        const QString k = QString::fromLocal8Bit(argv[i]);
        const QString v = QString::fromLocal8Bit(argv[i + 1]);
        if (k == QStringLiteral("--root"))
            root = v;
        else if (k == QStringLiteral("--zip"))
            zip = v;
        else if (k == QStringLiteral("--version"))
            version = v;
    }
    UpdaterWindow w(root, QDir::fromNativeSeparators(zip), version.isEmpty() ? QStringLiteral("?") : version);
    w.show();
    w.start();
    return app.exec();
}
