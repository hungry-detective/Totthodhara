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
// Look matches the Settings window: near-black card, white text, blue
// accent progress. App icon is embedded (updater.qrc) so the title bar and
// taskbar wear it even from the temp stage.
//
// No Q_OBJECT here on purpose (lambdas only): this file needs no MOC run.
#include <QApplication>
#include <QDateTime>
#include <QDir>
#include <QDirIterator>
#include <QFile>
#include <QFileInfo>
#include <QIcon>
#include <QLabel>
#include <QPainter>
#include <QPointer>
#include <QProcess>
#include <QProgressBar>
#include <QTextStream>
#include <QThread>
#include <QTimer>
#include <QVBoxLayout>
#include <QWidget>

#include <algorithm>

#ifdef Q_OS_WINDOWS
#include <windows.h>
#endif

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

// Orbital dot spinner: twelve fading dots chasing each other —
// unmissable motion while work happens. Plain QWidget (paint only,
// timer-driven) — needs no MOC run.
class Spinner : public QWidget
{
public:
    explicit Spinner(QWidget *parent = nullptr)
        : QWidget(parent)
    {
        setFixedSize(56, 56);
        auto *t = new QTimer(this);
        connect(t, &QTimer::timeout, this, [this] {
            m_head = (m_head + 1) % 12;
            update();
        });
        t->start(80);
    }

protected:
    void paintEvent(QPaintEvent *) override
    {
        QPainter p(this);
        p.setRenderHint(QPainter::Antialiasing);
        p.setPen(Qt::NoPen);
        const QPointF c(28, 28);
        for (int i = 0; i < 12; ++i) {
            // Trail behind the head: newest opaque, oldest faint.
            const int age = (m_head - i + 12) % 12;
            QColor dot(0x4c, 0xc2, 0xff);
            dot.setAlphaF(1.0 - age * 0.075);
            const double a = (i * 30.0 - 90.0) * 3.141592653589793 / 180.0;
            const double r = (i == m_head) ? 5.2 : 4.2;
            p.setBrush(dot);
            p.drawEllipse(QPointF(c.x() + 19 * qCos(a), c.y() + 19 * qSin(a)), r, r);
        }
    }

private:
    int m_head = 0;
};

class UpdaterWindow : public QWidget
{
public:
    UpdaterWindow(const QString &root, const QString &zip, const QString &version)
        : m_root(root)
        , m_zip(zip)
    {
        setWindowTitle(QStringLiteral("Totthodhara Update"));
        setWindowIcon(QIcon(QStringLiteral(":/resources/app.png")));
        setWindowFlags(windowFlags() | Qt::WindowStaysOnTopHint);
        setFixedSize(470, 265);
        // Settings-window look: near-black card, white text, blue accent.
        setStyleSheet(QStringLiteral(
            "QWidget { background-color: #202020; color: #ffffff; font-size: 12px; }"
            "QLabel[muted=\"true\"] { color: #a0a0a0; }"
            "QProgressBar { background-color: #2d2d2d; border: none;"
            " border-radius: 5px; min-height: 10px; max-height: 10px;"
            " text-align: center; color: #ffffff; font-size: 11px; }"
            "QProgressBar::chunk { background-color: #4cc2ff; border-radius: 5px; }"));

        auto *lay = new QVBoxLayout(this);
        lay->setContentsMargins(22, 16, 22, 16);
        lay->setSpacing(8);

        // Centered header: spinner, titles, status — About-page style.
        auto *spin = new Spinner(this);
        lay->addWidget(spin, 0, Qt::AlignHCenter);
        auto *name = new QLabel(tr("Totthodhara Update"), this);
        QFont nf = name->font();
        nf.setBold(true);
        nf.setPixelSize(15);
        name->setFont(nf);
        name->setAlignment(Qt::AlignHCenter);
        lay->addWidget(name);
        auto *sub = new QLabel(tr("Hold tight, we are updating your app."), this);
        sub->setProperty("muted", true);
        sub->setWordWrap(true);
        sub->setAlignment(Qt::AlignHCenter);
        lay->addWidget(sub);

        m_status = new QLabel(tr("Starting…"), this);
        QFont f = m_status->font();
        f.setBold(true);
        f.setPixelSize(13);
        m_status->setFont(f);
        m_status->setAlignment(Qt::AlignHCenter);
        lay->addWidget(m_status);

        // Full-width bar, centered by the layout itself.
        m_bar = new QProgressBar(this);
        m_bar->setRange(0, 100);
        m_bar->setValue(0);
        m_bar->setTextVisible(true);
        m_bar->setSizePolicy(QSizePolicy::Expanding, QSizePolicy::Fixed);
        lay->addWidget(m_bar);

        m_detail = new QLabel(this);
        m_detail->setProperty("muted", true);
        m_detail->setWordWrap(false);
        m_detail->setAlignment(Qt::AlignHCenter);
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
        m_bar->setRange(0, 100);
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
        // Expand-Archive reports no progress: breathe the bar 5→25 while
        // it works so a big zip never reads as stuck.
        m_pulse = 0;
        auto *pulse = new QTimer(this);
        connect(pulse, &QTimer::timeout, this, [this, pulse] {
            if (!pulse->isActive())
                return;
            m_pulse = (m_pulse + 4) % 20;
            m_bar->setValue(5 + m_pulse);
        });
        pulse->start(150);
        auto *p = new QProcess(this);
        QPointer<QProcess> proc(p);
        // Start failure (no shell) and hangs must surface, not spin forever:
        // `finished` alone never fires when the process won't start.
        connect(p, &QProcess::errorOccurred, this, [this](QProcess::ProcessError) {
            fail(tr("Could not start the unpacker — nothing was changed."));
        });
        QTimer::singleShot(600000, this, [this, proc] {
            if (proc && proc->state() != QProcess::NotRunning) {
                proc->kill();
                fail(tr("Unpacking took too long — nothing was changed."));
            }
        });
        connect(p,
                QOverload<int, QProcess::ExitStatus>::of(&QProcess::finished),
                this, [this, p, pulse](int code, QProcess::ExitStatus) {
                    pulse->stop();
                    pulse->deleteLater();
                    p->deleteLater();
                    if (code != 0) {
                        fail(tr("Could not unpack the download."));
                        return;
                    }
                    stepVerify();
                });
        // Single-quoted PowerShell paths: embedded ' becomes '' and the
        // zip travels via -LiteralPath (wildcards/brackets in profile
        // paths like O'Brien must not break the unpack).
        const QString stage = QFileInfo(m_zip).absolutePath();
        const QString escZip = QString(m_zip).replace(QLatin1Char('\''), QStringLiteral("''"));
        const QString escNew = (stage + QStringLiteral("/new")).replace(QLatin1Char('\''), QStringLiteral("''"));
        p->start(QStringLiteral("powershell"),
                 {QStringLiteral("-NoProfile"), QStringLiteral("-Command"),
                  QStringLiteral("Expand-Archive -LiteralPath '%1' -DestinationPath '%2' -Force")
                      .arg(escZip, escNew)});
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

    // Removes everything under root except data/ and *.bak backups
    // (a path is skipped when ANY segment is/ends .bak — the backup tree
    // itself must survive, including files inside library.bak/).
    // Returns the number of entries that refused to go (read-only files
    // get their flag cleared first). Base/span drive the progress bar.
    int clearInstallTree(int base, int span)
    {
        auto isBak = [](const QString &p) {
            const QStringList segs = p.split(QLatin1Char('/'));
            for (const QString &s : segs) {
                if (s.endsWith(QStringLiteral(".bak"), Qt::CaseInsensitive))
                    return true;
            }
            return false;
        };
        const QStringList files = collectFiles(m_root, QStringLiteral("data"));
        const QStringList dirs = collectDirs(m_root, QStringLiteral("data"));
        const int total = files.size() + dirs.size() + 1;
        int i = 0;
        int failures = 0;
        QString firstFailure;
        for (const QString &fp : files) {
            if (isBak(fp)) {
                setProgress(++i, total);
                continue;
            }
            QFile::setPermissions(fp, QFile::WriteUser); // clear read-only
            if (!QFile::remove(fp)) {
                failures++;
                if (firstFailure.isEmpty())
                    firstFailure = QStringLiteral("file:") + fp;
            }
            setProgress(++i, total);
        }
        for (const QString &dp : dirs) {
            if (isBak(dp)) {
                setProgress(++i, total);
                continue;
            }
            if (!QDir().rmdir(dp)) {
                failures++;
                if (firstFailure.isEmpty())
                    firstFailure = QStringLiteral("dir:") + dp;
            }
            setProgress(++i, total);
        }
        // Root-level files (the old stub). Directories fail del silently.
        QDir rootDir(m_root);
        for (const QString &fn :
             rootDir.entryList(QDir::Files | QDir::Hidden | QDir::System)) {
            if (fn.endsWith(QStringLiteral(".bak"), Qt::CaseInsensitive))
                continue;
            QFile::setPermissions(rootDir.filePath(fn), QFile::WriteUser);
            if (!QFile::remove(rootDir.filePath(fn))) {
                failures++;
                if (firstFailure.isEmpty())
                    firstFailure = QStringLiteral("root:") + fn;
            }
        }
        setProgress(total, total);
        return failures;
    }

    // Puts the .bak tree back after a failed install (best effort).
    // No-op when no backup exists (pre-backup failures must NOT wipe a
    // healthy tree!). One retry after a breath: lock failures are usually
    // a scanner holding a file for a second, not a real dead end.
    void restoreBackup()
    {
        const bool hasLib = QDir(m_root + QStringLiteral("/library.bak")).exists();
        const bool hasStub = QFile::exists(m_root + QStringLiteral("/Totthodhara.exe.bak"));
        if (!hasLib && !hasStub)
            return;
        for (int attempt = 0; attempt < 2; ++attempt) {
            clearInstallTree(0, 0);
            if (hasLib)
                QDir().rename(m_root + QStringLiteral("/library.bak"),
                              m_root + QStringLiteral("/library"));
            if (hasStub)
                QFile::rename(m_root + QStringLiteral("/Totthodhara.exe.bak"),
                              m_root + QStringLiteral("/Totthodhara.exe"));
            if (!QDir(m_root + QStringLiteral("/library.bak")).exists()
                && !QFile::exists(m_root + QStringLiteral("/Totthodhara.exe.bak")))
                return;
            QThread::sleep(3);
        }
    }

    // 4. Replace: instant same-volume backup first, then wipe, so a failed
    // install restores instead of bricking.
    void stepWipe()
    {
        // Stale backups from a crashed run go first (frees the .bak names).
        QFile::remove(m_root + QStringLiteral("/Totthodhara.exe.bak"));
        QDir(m_root + QStringLiteral("/library.bak")).removeRecursively();
        setStep(tr("Backing up the current install…"), 28, 4);
        QApplication::processEvents();
        bool ok = true;
        if (QFile::exists(m_root + QStringLiteral("/Totthodhara.exe")))
            ok = QFile::rename(m_root + QStringLiteral("/Totthodhara.exe"),
                               m_root + QStringLiteral("/Totthodhara.exe.bak"))
                 && ok;
        if (QFile::exists(m_root + QStringLiteral("/library")))
            ok = QDir().rename(m_root + QStringLiteral("/library"),
                               m_root + QStringLiteral("/library.bak"))
                 && ok;
        if (!ok) {
            fail(tr("Could not back up the current install — nothing was changed."), false);
            return;
        }
        setStep(tr("Removing old files (clips and settings are kept)…"), 32, 18);
        if (clearInstallTree(32, 18) > 0) {
            fail(tr("Could not remove old files — your previous version was restored."), true);
            return;
        }
        stepCopy();
    }

    void stepCopy()
    {
        setStep(tr("Copying new files…"), 50, 35);
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
                fail(tr("Could not write %1 — your previous version was restored.")
                         .arg(rel),
                     true);
                return;
            }
            setProgress(++i, files.size(), rel);
        }
        // The staged tree landed: assert the two load-bearing files are
        // real executables (MZ header), not just present — a truncated
        // copy passes an exists() check and bricks the relaunch with an
        // OS "cannot start" dialog instead.
        auto isExe = [](const QString &p) {
            QFile f(p);
            if (!f.open(QIODevice::ReadOnly))
                return false;
            char magic[2] = {};
            return f.read(magic, 2) == 2 && magic[0] == 'M' && magic[1] == 'Z';
        };
        if (!isExe(m_root + QStringLiteral("/library/Totthodhara.exe"))
            || !isExe(m_root + QStringLiteral("/Totthodhara.exe"))) {
            fail(tr("The new files look wrong — your previous version was restored."), true);
            return;
        }
        stepCleanup();
    }

    void stepCleanup()
    {
        setStep(tr("Cleaning up…"), 85, 10);
        // Drop the backup now that the new tree is verified (progress:
        // it is a full second tree). Any remnant also dies with the stage
        // and in the app's next-launch sweep — no leftovers either way.
        const QStringList files = collectFiles(m_root + QStringLiteral("/library.bak"), QString());
        const QStringList dirs = collectDirs(m_root + QStringLiteral("/library.bak"), QString());
        const int total = files.size() + dirs.size() + 1;
        int i = 0;
        for (const QString &fp : files) {
            QFile::remove(fp);
            setProgress(++i, total);
        }
        for (const QString &dp : dirs) {
            QDir().rmdir(dp);
            setProgress(++i, total);
        }
        QFile::remove(m_root + QStringLiteral("/Totthodhara.exe.bak"));
        setProgress(total, total);
        stepDone();
    }

    void stepDone()
    {
        setStep(tr("Done — starting Totthodhara…"), 95, 5);
        m_detail->clear();
        QProcess::startDetached(m_root + QStringLiteral("/Totthodhara.exe"), {});
        // Remove the stage ourselves (a running exe cannot delete itself:
        // everything else goes now). Our own binary is registered for
        // deletion at next boot, and the app's next-launch sweep removes
        // any stage remnants too — no batch file, no quoting hazards.
        const QString stage = QFileInfo(m_zip).absolutePath();
        const QString self = QCoreApplication::applicationFilePath();
#ifdef Q_OS_WINDOWS
        MoveFileExW(reinterpret_cast<LPCWSTR>(QDir::toNativeSeparators(self).utf16()),
                    nullptr, MOVEFILE_DELAY_UNTIL_REBOOT);
#endif
        QDir(stage).removeRecursively();
        QTimer::singleShot(800, this, [] { QApplication::quit(); });
    }

    // restore=true puts the .bak tree back first (for failures after the
    // wipe); pre-wipe failures pass false — nothing was touched.
    void fail(const QString &why, bool restore = false)
    {
        m_status->setText(why);
        QPalette pal = m_status->palette();
        pal.setColor(QPalette::WindowText, QColor(255, 110, 110));
        m_status->setPalette(pal);
        m_detail->clear();
        if (restore)
            restoreBackup();
        // Best effort: hand the user a running app if one still exists.
        QProcess::startDetached(m_root + QStringLiteral("/Totthodhara.exe"), {});
        QTimer::singleShot(8000, this, [] { QApplication::quit(); });
    }

    QString m_root;
    QString m_zip;
    QLabel *m_status = nullptr;
    QLabel *m_detail = nullptr;
    QProgressBar *m_bar = nullptr;
    int m_base = 0;
    int m_span = 0;
    int m_ticks = 0;
    int m_pulse = 0;
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
