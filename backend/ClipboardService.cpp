#include "ClipboardService.h"
#include "backend/AppPaths.h"

#include <QCoreApplication>
#include <QDateTime>
#include <QDebug>
#include <QDesktopServices>
#include <QDir>
#include <QDrag>
#include <QGuiApplication>
#include <QClipboard>
#include <QImage>
#include <QImageReader>
#include <QNetworkAccessManager>
#include <QNetworkReply>
#include <QNetworkRequest>
#include <QFile>
#include <QFileInfo>
#include <QMimeData>
#include <QPainter>
#include <QPixmap>
#include <QQuickWindow>
#include <QStandardPaths>
#include <QTextStream>
#include <QTimer>
#include <QUrl>

#ifdef Q_OS_WINDOWS
#include <windows.h>
#include <objidl.h>
#include <algorithm>
#include <cstring>
#endif

namespace {
// clip.log opened ONCE per process (was: open/append/close on every
// clipboard event and favicon step — handle churn in normal use).
// Missing C:/Temp => open fails once, writes no-op safely after.
QTextStream &clipLog()
{
    static QFile f(QStringLiteral("C:/Temp/clip.log"));
    static bool opened = false;
    static QTextStream s;
    if (!opened) {
        opened = true;
        if (f.open(QIODevice::Append | QIODevice::Text))
            s.setDevice(&f);
    }
    return s;
}

// Content hash: QImage::cacheKey is per-object (a fresh read of identical
// pixels gets a new key), so hash sampled bytes instead. Fast enough:
// 4K samples even for fullscreen bitmaps.
quint64 imageHash(const QImage &img)
{
    const int n = img.sizeInBytes();
    const uchar *bits = img.constBits();
    quint64 h = quint64(img.width()) * 1000003ULL ^ quint64(img.height());
    const int stride = qMax(1, n / 4096);
    for (int i = 0; i < n; i += stride)
        h = h * 31 + bits[i];
    return h;
}

#ifdef Q_OS_WINDOWS
// CF_HDROP bytes with a native (backslash) path: DROPFILES header +
// UTF-16 path + double null. Byte-equivalent to Explorer.
QByteArray hdropForNative(const QString &nativePath)
{
    QByteArray out(20, 0);
    const quint32 pFiles = 20, fWide = 1;
    memcpy(out.data(), &pFiles, 4);
    memcpy(out.data() + 16, &fWide, 4);
    const int len = nativePath.length();
    const int pos = out.size();
    out.resize(pos + (len + 2) * 2);
    memcpy(out.data() + pos, nativePath.utf16(), size_t(len) * 2);
    memset(out.data() + pos + len * 2, 0, 4);
    return out;
}

// CF_DIB bytes (BITMAPINFOHEADER + bottom-up 32bpp rows) for pixel
// targets. QImage ARGB32 memory is already B,G,R,A per pixel on LE.
// Absurd dimensions are refused up front: w*h runs in 32-bit int, and a
// clipboard-controlled giant would overflow the size and corrupt the heap.
QByteArray dibFor(const QImage &src)
{
    const QImage img = src.convertToFormat(QImage::Format_ARGB32);
    if (img.isNull())
        return {};
    const int w = img.width(), h = img.height();
    if (w <= 0 || h <= 0 || w > 8192 || h > 8192
        || qint64(w) * qint64(h) > qint64(67'108'864)) // 8192^2 px cap
        return {};
    const int stride = w * 4;
    QByteArray out(40 + stride * h, 0);
    auto w32 = [&](int off, quint32 v) {
        out[off] = char(v & 0xff);
        out[off + 1] = char((v >> 8) & 0xff);
        out[off + 2] = char((v >> 16) & 0xff);
        out[off + 3] = char((v >> 24) & 0xff);
    };
    auto w16 = [&](int off, quint16 v) {
        out[off] = char(v & 0xff);
        out[off + 1] = char((v >> 8) & 0xff);
    };
    w32(0, 40);
    w32(4, quint32(w));
    w32(8, quint32(h));
    w16(12, 1);
    w16(14, 32);
    w32(16, 0); // BI_RGB
    w32(20, quint32(stride * h));
    for (int y = 0; y < h; ++y) {
        const uchar *s = img.constScanLine(h - 1 - y);
        uchar *d = reinterpret_cast<uchar *>(out.data()) + 40 + y * stride;
        for (int x = 0; x < w; ++x) {
            d[4 * x + 0] = s[4 * x + 0];
            d[4 * x + 1] = s[4 * x + 1];
            d[4 * x + 2] = s[4 * x + 2];
            d[4 * x + 3] = 0;
        }
    }
    return out;
}

static FORMATETC makeEtc(UINT cf)
{
    FORMATETC f = {};
    f.cfFormat = static_cast<CLIPFORMAT>(cf);
    f.ptd = nullptr;
    f.dwAspect = DVASPECT_CONTENT;
    f.lindex = -1;
    f.tymed = TYMED_HGLOBAL;
    return f;
}

class ShelfFmtEnum : public IEnumFORMATETC
{
public:
    ShelfFmtEnum(const FORMATETC *etcs, ULONG n) : m_ref(1), m_n(n), m_i(0)
    {
        for (ULONG k = 0; k < n && k < 2; ++k)
            m_list[k] = etcs[k];
    }
    STDMETHODIMP QueryInterface(REFIID riid, void **ppv) override
    {
        if (!ppv)
            return E_POINTER;
        if (riid == IID_IUnknown || riid == IID_IEnumFORMATETC) {
            *ppv = static_cast<IEnumFORMATETC *>(this);
            AddRef();
            return S_OK;
        }
        *ppv = nullptr;
        return E_NOINTERFACE;
    }
    STDMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&m_ref); }
    STDMETHODIMP_(ULONG) Release() override
    {
        const ULONG r = InterlockedDecrement(&m_ref);
        if (!r)
            delete this;
        return r;
    }
    STDMETHODIMP Next(ULONG celt, FORMATETC *rgelt, ULONG *fetched) override
    {
        if (!rgelt)
            return E_POINTER;
        ULONG got = 0;
        while (got < celt && m_i < m_n)
            rgelt[got++] = m_list[m_i++];
        if (fetched)
            *fetched = got;
        return got == celt ? S_OK : S_FALSE;
    }
    STDMETHODIMP Skip(ULONG celt) override
    {
        m_i = (std::min)(m_i + celt, m_n);
        return m_i >= m_n ? S_FALSE : S_OK;
    }
    STDMETHODIMP Reset() override
    {
        m_i = 0;
        return S_OK;
    }
    STDMETHODIMP Clone(IEnumFORMATETC **pp) override
    {
        if (!pp)
            return E_POINTER;
        auto *c = new ShelfFmtEnum(m_list, m_n);
        c->m_i = m_i;
        *pp = c;
        return S_OK;
    }

private:
    LONG m_ref;
    ULONG m_n;
    ULONG m_i;
    FORMATETC m_list[2];
};

class ShelfDropSource : public IDropSource
{
public:
    ShelfDropSource() : m_ref(1) {}
    STDMETHODIMP QueryInterface(REFIID riid, void **ppv) override
    {
        if (!ppv)
            return E_POINTER;
        if (riid == IID_IUnknown || riid == IID_IDropSource) {
            *ppv = static_cast<IDropSource *>(this);
            AddRef();
            return S_OK;
        }
        *ppv = nullptr;
        return E_NOINTERFACE;
    }
    STDMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&m_ref); }
    STDMETHODIMP_(ULONG) Release() override
    {
        const ULONG r = InterlockedDecrement(&m_ref);
        if (!r)
            delete this;
        return r;
    }
    STDMETHODIMP QueryContinueDrag(BOOL esc, DWORD keys) override
    {
        if (esc)
            return DRAGDROP_S_CANCEL;
        if (!(keys & MK_LBUTTON))
            return DRAGDROP_S_DROP;
        return S_OK;
    }
    STDMETHODIMP GiveFeedback(DWORD) override
    {
        return DRAGDROP_S_USEDEFAULTCURSORS;
    }

private:
    LONG m_ref;
};

// IDataObject with HDROP (+DIB) and deliberately NO text formats —
// byte-equivalent to an Explorer file drag, so targets upload the file
// instead of inserting link text.
class ShelfDataObject : public IDataObject
{
public:
    ShelfDataObject(const QByteArray &hdrop, const QByteArray &dib)
        : m_ref(1), m_hdrop(hdrop), m_dib(dib) {}
    STDMETHODIMP QueryInterface(REFIID riid, void **ppv) override
    {
        if (!ppv)
            return E_POINTER;
        if (riid == IID_IUnknown || riid == IID_IDataObject) {
            *ppv = static_cast<IDataObject *>(this);
            AddRef();
            return S_OK;
        }
        *ppv = nullptr;
        return E_NOINTERFACE;
    }
    STDMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&m_ref); }
    STDMETHODIMP_(ULONG) Release() override
    {
        const ULONG r = InterlockedDecrement(&m_ref);
        if (!r)
            delete this;
        return r;
    }
    STDMETHODIMP GetData(FORMATETC *fmt, STGMEDIUM *med) override
    {
        if (!fmt || !med)
            return E_POINTER;
        const bool wantFile = (fmt->cfFormat == CF_HDROP);
        const bool wantDib = (fmt->cfFormat == CF_DIB && !m_dib.isEmpty());
        if (!wantFile && !wantDib)
            return DV_E_FORMATETC;
        if (!(fmt->tymed & TYMED_HGLOBAL))
            return DV_E_TYMED;
        const QByteArray &src = wantFile ? m_hdrop : m_dib;
        HGLOBAL h = GlobalAlloc(GMEM_MOVEABLE, src.size());
        if (!h)
            return STG_E_MEDIUMFULL;
        void *dst = GlobalLock(h);
        if (!dst) {
            GlobalFree(h);
            return STG_E_MEDIUMFULL;
        }
        memcpy(dst, src.constData(), src.size());
        GlobalUnlock(h);
        med->tymed = TYMED_HGLOBAL;
        med->hGlobal = h;
        med->pUnkForRelease = nullptr;
        return S_OK;
    }
    STDMETHODIMP GetDataHere(FORMATETC *, STGMEDIUM *) override { return E_NOTIMPL; }
    STDMETHODIMP QueryGetData(FORMATETC *fmt) override
    {
        if (!fmt)
            return E_POINTER;
        if (fmt->cfFormat == CF_HDROP && (fmt->tymed & TYMED_HGLOBAL))
            return S_OK;
        if (fmt->cfFormat == CF_DIB && !m_dib.isEmpty() && (fmt->tymed & TYMED_HGLOBAL))
            return S_OK;
        return DV_E_FORMATETC;
    }
    STDMETHODIMP GetCanonicalFormatEtc(FORMATETC *in, FORMATETC *out) override
    {
        if (!in || !out)
            return E_POINTER;
        *out = *in;
        out->ptd = nullptr;
        return DATA_S_SAMEFORMATETC;
    }
    STDMETHODIMP SetData(FORMATETC *, STGMEDIUM *, WINBOOL) override { return E_NOTIMPL; }
    STDMETHODIMP EnumFormatEtc(DWORD dir, IEnumFORMATETC **pp) override
    {
        if (!pp)
            return E_POINTER;
        *pp = nullptr;
        if (dir != DATADIR_GET)
            return E_NOTIMPL;
        FORMATETC list[2];
        list[0] = makeEtc(CF_HDROP);
        ULONG n = 1;
        if (!m_dib.isEmpty()) {
            list[1] = makeEtc(CF_DIB);
            n = 2;
        }
        *pp = new ShelfFmtEnum(list, n);
        return S_OK;
    }
    STDMETHODIMP DAdvise(FORMATETC *, DWORD, IAdviseSink *, DWORD *) override
    {
        return OLE_E_ADVISENOTSUPPORTED;
    }
    STDMETHODIMP DUnadvise(DWORD) override { return OLE_E_ADVISENOTSUPPORTED; }
    STDMETHODIMP EnumDAdvise(IEnumSTATDATA **) override
    {
        return OLE_E_ADVISENOTSUPPORTED;
    }

private:
    LONG m_ref;
    QByteArray m_hdrop;
    QByteArray m_dib;
};
#endif
} // namespace

ClipboardService::ClipboardService(QObject *parent)
    : QObject(parent)
{
    m_net = new QNetworkAccessManager(this);
    // Remember where to paste: the last foreground window that is NOT one
    // of ours (clicking the shelf may activate it and steal focus).
    connect(&m_fgTimer, &QTimer::timeout, this, &ClipboardService::trackTarget);
    m_fgTimer.setInterval(250);
    m_fgTimer.start();
}

ClipboardService::~ClipboardService()
{
    stopMonitoring();
    if (QCoreApplication::instance())
        QCoreApplication::instance()->removeNativeEventFilter(this);
}

void ClipboardService::setMaxFileSizeMB(int mb)
{
    if (mb == m_maxFileSizeMB)
        return;
    m_maxFileSizeMB = mb;
    emit maxFileSizeMBChanged();
}

void ClipboardService::startMonitoring(QQuickWindow *window)
{
#ifdef Q_OS_WINDOWS
    if (!window)
        return;
    stopMonitoring();
    m_window = window;
    if (!m_filterInstalled && QCoreApplication::instance()) {
        QCoreApplication::instance()->installNativeEventFilter(this);
        m_filterInstalled = true;
    }
    const HWND hwnd = reinterpret_cast<HWND>(window->winId());
    if (!hwnd) {
        qWarning() << "clipboard monitor: no native window yet, captures disabled";
        return;
    }
    if (!AddClipboardFormatListener(hwnd))
        qWarning() << "clipboard monitor: listener failed:" << GetLastError();
#else
    Q_UNUSED(window);
#endif
}

void ClipboardService::stopMonitoring()
{
#ifdef Q_OS_WINDOWS
    if (m_window) {
        RemoveClipboardFormatListener(reinterpret_cast<HWND>(m_window->winId()));
        m_window = nullptr;
    }
#endif
}

void ClipboardService::copyText(const QString &text, bool autoPaste)
{
    m_suppressText = text;
    m_suppressUrls.clear();
    QGuiApplication::clipboard()->setText(text);
    if (autoPaste)
        QTimer::singleShot(150, this, &ClipboardService::pasteToForeground);
}

void ClipboardService::copyFiles(const QStringList &localPaths, bool autoPaste)
{
    // Accepts raw paths AND file:/// URLs (QML passes detail through
    // untouched now): percent-encoding (%20 etc.) is decoded here, so
    // click-paste matches drag-out on spaced/unicode paths.
    QList<QUrl> urls;
    QStringList locals;
    for (const QString &p : localPaths) {
        const QUrl u(p);
        const QString local = u.isLocalFile() ? u.toLocalFile() : p;
        if (local.isEmpty())
            continue;
        urls << QUrl::fromLocalFile(local);
        locals << local;
    }
    if (urls.isEmpty())
        return;
    auto *mime = new QMimeData;
    mime->setUrls(urls);
    // WPF parity: a single image also carries its bitmap, so bitmap-only
    // targets (e.g. Paint) receive pixels, not just a file drop.
    if (locals.size() == 1) {
        const QImage img(locals.first());
        if (!img.isNull())
            mime->setImageData(img);
    }
    m_suppressUrls = locals;
    m_suppressText.clear();
    QGuiApplication::clipboard()->setMimeData(mime);
    if (autoPaste)
        QTimer::singleShot(150, this, &ClipboardService::pasteToForeground);
}

void ClipboardService::trackTarget()
{
#ifdef Q_OS_WINDOWS
    // Remember the last foreground window that is NOT one of ours, so a
    // click on the shelf (which may activate it and steal focus) can be
    // followed by re-activating the target before pasting.
    if (HWND fg = GetForegroundWindow()) {
        DWORD pid = 0;
        GetWindowThreadProcessId(fg, &pid);
        if (pid != GetCurrentProcessId())
            m_lastTarget = fg;
    }
#endif
}

// Re-resolve a link icon: cached file upgrades instantly, otherwise a
// fresh fetch starts (same result path as live copies).
void ClipboardService::refetchIcon(const QString &detail)
{
    const QString path = resolveIcon(detail);
    if (!path.isEmpty())
        emit iconReady(detail, path);
}

QString ClipboardService::resolveIcon(const QString &detail)
{
    clipLog() << "refetch: " << detail.left(40) << Qt::endl;
    const QString host = QUrl(detail).host();
    if (host.isEmpty())
        return {};
    const QString dir = AppPaths::dataDir()
                        + QStringLiteral("/favicons");
    const QString cached = dir + QStringLiteral("/") + host + QStringLiteral(".png");
    clipLog() << "resolve: exists=" << QFile::exists(cached) << " " << cached.left(80) << Qt::endl;
    if (QFile::exists(cached))
        return QUrl::fromLocalFile(cached).toString();
    fetchFavicon(detail, detail);
    return {};
}

void ClipboardService::pasteToForeground()
{
#ifdef Q_OS_WINDOWS
    // Back to the target app first (the click may have activated the
    // shelf), then Ctrl+V. Note: elevated (admin) windows ignore input
    // from unelevated apps.
    if (m_lastTarget)
        SetForegroundWindow(static_cast<HWND>(m_lastTarget));
    QTimer::singleShot(90, this, []() {
        INPUT keys[4] = {};
        for (auto &k : keys)
            k.type = INPUT_KEYBOARD;
        keys[0].ki.wVk = VK_CONTROL;
        keys[1].ki.wVk = 'V';
        keys[2].ki.wVk = 'V';
        keys[2].ki.dwFlags = KEYEVENTF_KEYUP;
        keys[3].ki.wVk = VK_CONTROL;
        keys[3].ki.dwFlags = KEYEVENTF_KEYUP;
        SendInput(4, keys, sizeof(INPUT));
    });
#endif
}

void ClipboardService::openUrl(const QString &url)
{
    QDesktopServices::openUrl(QUrl(url.trimmed()));
}

// Card drag-out. Files/images go through a raw OLE drag whose data
// object carries HDROP (+DIB for images) and NO text formats —
// byte-equivalent to Explorer, so targets upload instead of inserting
// link text. (QDrag/QMimeData always advertises text/uri-list alongside,
// which makes targets prefer the "@///C:/..." link.) Text/links keep a
// plain QDrag, where text IS the right payload. The card lift/fade anim
// runs in QML while the OS call below blocks; the toast + spring-back
// run on release.
bool ClipboardService::startSystemDrag(const QString &title, const QString &kind,
                                       const QString &detail, bool dark)
{
    Q_UNUSED(title);
    const bool isFile = (kind == QStringLiteral("file") || kind == QStringLiteral("image"));
    QString localFile;
    if (isFile) {
        const QUrl u(detail);
        const QString local = u.isLocalFile() ? u.toLocalFile() : detail;
        if (!local.isEmpty() && QFile::exists(local))
            localFile = local;
    }

    // Mirror onto the clipboard (no auto-paste): if a target only takes a
    // paste, Ctrl+V still delivers exactly what a click would (text, image
    // bitmap + file, video file). Echo is suppressed, so no duplicate card.
    if (!localFile.isEmpty())
        copyFiles({localFile}, false);
    else
        copyText(detail, false);

#ifdef Q_OS_WINDOWS
    if (!localFile.isEmpty()) {
        const QByteArray hdrop = hdropForNative(QDir::toNativeSeparators(localFile));
        QByteArray dib;
        if (kind == QStringLiteral("image")) {
            const QImage img(localFile);
            if (!img.isNull())
                dib = dibFor(img);
        }
        auto *data = new ShelfDataObject(hdrop, dib);
        auto *src = new ShelfDropSource();
        DWORD effect = DROPEFFECT_NONE;
        const HRESULT hr = DoDragDrop(data, src, DROPEFFECT_COPY, &effect);
        data->Release();
        src->Release();
        return SUCCEEDED(hr) && effect != DROPEFFECT_NONE;
    }
#endif

    auto *mime = new QMimeData;
    if (kind == QStringLiteral("url")) {
        mime->setText(detail);
        mime->setUrls({QUrl(detail.trimmed())});
    } else {
        mime->setText(detail);
    }

    // Drag pixmap: small pill with the item title (smooth OS drag image),
    // themed like the shelf it came from (a dark pill in light mode
    // looks like a rendering bug on the target).
    QPixmap pix(220, 32);
    pix.fill(Qt::transparent);
    QPainter p(&pix);
    p.setRenderHint(QPainter::Antialiasing);
    p.setBrush(dark ? QColor(0x2d, 0x2d, 0x2d, 235) : QColor(0xff, 0xff, 0xff, 235));
    p.setPen(QColor(0x4c, 0xc2, 0xff));
    p.drawRoundedRect(1, 1, 218, 30, 10, 10);
    p.setPen(dark ? Qt::white : QColor(0x1b, 0x1b, 0x1b));
    QString label = title.trimmed();
    if (label.length() > 28)
        label = label.left(27) + QStringLiteral("…");
    p.drawText(QRect(12, 0, 196, 32), Qt::AlignVCenter | Qt::AlignLeft, label);
    p.end();

    QDrag drag(m_window ? static_cast<QObject *>(m_window) : this);
    drag.setMimeData(mime);
    drag.setPixmap(pix);
    drag.setHotSpot(QPoint(30, 16));
    const Qt::DropAction done = drag.exec(Qt::CopyAction, Qt::CopyAction);
    return done != Qt::IgnoreAction;
}

bool ClipboardService::nativeEventFilter(const QByteArray &eventType, void *message,
                                          qintptr *result)
{
#ifdef Q_OS_WINDOWS
    if (eventType == "windows_generic_MSG") {
        const MSG *msg = static_cast<const MSG *>(message);
        if (msg->message == WM_CLIPBOARDUPDATE) {
            handleClipboard();
            if (result)
                *result = 0;
            return true;
        }
    }
#else
    Q_UNUSED(eventType);
    Q_UNUSED(message);
    Q_UNUSED(result);
#endif
    return false;
}

QString ClipboardService::shortTitle(const QString &text)
{
    // First 4 words + ellipsis (mirrors WPF DisplayTitle). Tabs become
    // spaces too, or titles render with gaping holes.
    const QString flat = QString(text).replace('\r', ' ').replace('\n', ' ')
                               .replace('\t', ' ').trimmed();
    const QStringList words = flat.split(' ', Qt::SkipEmptyParts);
    if (words.size() <= 4)
        return flat.left(60);
    return words.mid(0, 4).join(' ') + QStringLiteral(" …");
}

QString ClipboardService::hostOf(const QString &url)
{
    const QString host = QUrl(url).host();
    return host.isEmpty() ? url.left(40) : host;
}

// Site icon for a copied link: the card shows instantly with a globe,
// then upgrades to the real favicon when it arrives (cached by host).
void ClipboardService::fetchFavicon(const QString &pageUrl, const QString &detailKey)
{
    clipLog() << "fetch called" << Qt::endl;
    const QString host = QUrl(pageUrl).host();
    clipLog() << "host=" << host << Qt::endl;
    if (host.isEmpty())
        return;
    const QString dir = AppPaths::dataDir()
                        + QStringLiteral("/favicons");
    QDir().mkpath(dir);
    const QString cached = dir + QStringLiteral("/") + host + QStringLiteral(".png");
    if (QFile::exists(cached)) {
        emit iconReady(detailKey, QUrl::fromLocalFile(cached).toString());
        return;
    }
    // One fetch per host at a time: parallel fetches save the same file
    // concurrently and persist a torn PNG. Same-host cards that arrive
    // mid-flight queue here and upgrade together when it lands (a bare
    // early-return left them on the globe forever).
    if (m_pendingIcons.contains(host)) {
        if (!m_pendingIcons[host].contains(detailKey))
            m_pendingIcons[host].append(detailKey);
        return;
    }
    m_pendingIcons.insert(host, {detailKey});
    QNetworkRequest req(QUrl(QStringLiteral("https://%1/favicon.ico").arg(host)));
    req.setAttribute(QNetworkRequest::RedirectPolicyAttribute,
                     QNetworkRequest::NoLessSafeRedirectPolicy);
    QNetworkReply *reply = m_net->get(req);
    connect(reply, &QNetworkReply::finished, this, [this, reply, detailKey, host]() {
        fetchFinished(reply, detailKey, false);
        reply->deleteLater();
    });
}

void ClipboardService::fetchFinished(QNetworkReply *reply, const QString &detailKey,
                                      bool fallbackTried)
{
    clipLog() << "fetch finished: err=" << reply->error()
              << " bytes=" << reply->bytesAvailable()
              << " fallback=" << fallbackTried << Qt::endl;
    const QByteArray data = reply->readAll();
    qInfo() << "favicon: got" << data.size() << "bytes, error =" << reply->error();
    const QImage img = QImage::fromData(data);
    const QString host = QUrl(detailKey).host();
    const QString dir = AppPaths::dataDir()
                        + QStringLiteral("/favicons");
    clipLog() << "img null=" << img.isNull() << " host=" << host << Qt::endl;
    if (!img.isNull() && !host.isEmpty()) {
        const QString path = dir + QStringLiteral("/") + host + QStringLiteral(".png");
        if (img.scaledToWidth(64, Qt::SmoothTransformation).save(path, "PNG")) {
            clipLog() << "saved icon" << Qt::endl;
            for (const QString &key : m_pendingIcons.take(host))
                emit iconReady(key, QUrl::fromLocalFile(path).toString());
        } else {
            clipLog() << "SAVE FAILED" << Qt::endl;
            m_pendingIcons.remove(host);
        }
        return;
    }
    if (!fallbackTried && !host.isEmpty()) {
        // Site has no /favicon.ico: use the Google icon service instead.
        // (Waiters stay queued across the retry.)
        QNetworkRequest req(QUrl(QStringLiteral(
            "https://www.google.com/s2/favicons?domain=%1&sz=64").arg(host)));
        QNetworkReply *retry = m_net->get(req);
        connect(retry, &QNetworkReply::finished, this, [this, retry, detailKey]() {
            fetchFinished(retry, detailKey, true);
            retry->deleteLater();
        });
    } else {
        m_pendingIcons.remove(host);
    }
}

QString ClipboardService::saveImageFile(const QImage &img)
{
    if (img.isNull())
        return {};
    const QString dir = AppPaths::dataDir()
                        + QStringLiteral("/clips");
    QDir().mkpath(dir);
    // Timestamp + per-process counter: two captures in the same millisecond
    // must never share a file (second would overwrite the first, and
    // deleting one card would delete the other's image).
    const QString path = dir + QStringLiteral("/clip_%1_%2.png")
                         .arg(QDateTime::currentMSecsSinceEpoch())
                         .arg(++m_clipSeq);
    if (!img.save(path, "PNG"))
        return {};
    return QUrl::fromLocalFile(path).toString();
}

void ClipboardService::handleClipboard()
{
    clipLog() << "handle " << QDateTime::currentDateTime().toString(QStringLiteral("hh:mm:ss")) << Qt::endl;
    const QClipboard *cb = QGuiApplication::clipboard();
    const QMimeData *mime = cb->mimeData();
    if (!mime)
        return;

    // Files (Explorer copy, screenshots-as-files, our image copy-back).
    // Windows fires several WM_CLIPBOARDUPDATE per copy: skip repeats.
    if (mime->hasUrls()) {
        const QList<QUrl> urls = mime->urls();
        QStringList locals;
        for (const QUrl &u : urls) {
            if (u.isLocalFile())
                locals << u.toLocalFile();
        }
        if (!locals.isEmpty()) {
            if (locals == m_suppressUrls || locals == m_lastUrls)
                return;
            m_lastUrls = locals;
            m_lastText.clear();
            m_lastImageHash = 0;
            m_suppressText.clear();
            m_suppressUrls.clear();
            // Storage setting: skip files over the size limit (0 = off).
            QStringList accepted;
            for (const QString &p : locals) {
                if (m_maxFileSizeMB > 0
                    && QFileInfo(p).size() > qint64(m_maxFileSizeMB) * 1024 * 1024)
                    continue;
                accepted << p;
            }
            if (accepted.size() != locals.size())
                emit rejected(tr("File over %1 MB skipped").arg(m_maxFileSizeMB));
            if (accepted.isEmpty())
                return;
            // Newest file first.
            for (int i = accepted.size() - 1; i >= 0; --i) {
                const QString name = QFileInfo(accepted[i]).fileName();
                QVariantMap item;
                item[QStringLiteral("title")] = name;
                item[QStringLiteral("kind")] = QStringLiteral("file");
                item[QStringLiteral("detail")] = QUrl::fromLocalFile(accepted[i]).toString();
                item[QStringLiteral("icon")] = QString(QChar(0xE7C3));
                emit clipCaptured(item);
            }
            return;
        }
    }

    // Bitmap image (PrintScreen, snips, canvas copies). Dedupe by CONTENT:
    // Windows fires several notifications per copy and every fresh read
    // has a new cacheKey, so identical pixels collapse to one card.
    // Single read: title + file + hash all come from this one image, so a
    // clipboard that is still being written can never produce a "0x0" card
    // mismatched with its file. Null reads are ignored — Windows usually
    // fires another notification when the data lands.
    if (mime->hasImage()) {
        const QImage probe = QGuiApplication::clipboard()->image();
        if (probe.isNull())
            return;
        if (imageHash(probe) == m_lastImageHash)
            return;
        // Record BEFORE the size gate: an oversize image fires several
        // notifications, and without this each repeat re-saves, re-deletes
        // and re-toasts. (A failed save below also dedupes: acceptable, the
        // alternative is the spam loop.)
        m_lastImageHash = imageHash(probe);
        const QString saved = saveImageFile(probe);
        if (!saved.isEmpty()) {
            // Storage setting: drop images over the size limit again (the
            // PNG is already on disk — remove it, not the card).
            const QString savedLocal = QUrl(saved).toLocalFile();
            if (m_maxFileSizeMB > 0 && !savedLocal.isEmpty()
                && QFileInfo(savedLocal).size() > qint64(m_maxFileSizeMB) * 1024 * 1024) {
                if (!QFile::remove(savedLocal))
                    qWarning() << "oversize image kept (remove failed):" << savedLocal;
                emit rejected(tr("Image over %1 MB skipped").arg(m_maxFileSizeMB));
                return;
            }
            m_lastText.clear();
            m_lastUrls.clear();
            QVariantMap item;
            item[QStringLiteral("title")] = QStringLiteral("%1x%2")
                                            .arg(probe.width()).arg(probe.height());
            item[QStringLiteral("kind")] = QStringLiteral("image");
            item[QStringLiteral("detail")] = saved;
            item[QStringLiteral("icon")] = QString(QChar(0xEB9F));
            emit clipCaptured(item);
            return;
        }
    }

    // Text / links / colors.
    if (mime->hasText()) {
        const QString text = mime->text();
        if (text.isEmpty() || text == m_lastText || text == m_suppressText)
            return;
        m_lastText = text;
        m_lastUrls.clear();
        m_lastImageHash = 0;
        m_suppressText.clear();
        m_suppressUrls.clear();
        QVariantMap item;
        QString trimmed = text.trimmed();
        // Proven toxin: clipboard text with an embedded NUL (some apps
        // include the terminator) renders as a tofu square and survives
        // SQLite into the shelf. Strip it at the gate.
        trimmed.remove(QChar(0));
        const bool isUrl = trimmed.startsWith(QStringLiteral("http://"), Qt::CaseInsensitive)
                           || trimmed.startsWith(QStringLiteral("https://"), Qt::CaseInsensitive);
        // Favicon fetch runs AFTER clipCaptured below: on a cache hit the
        // icon update is synchronous and the card must exist already.
        const bool needFavicon = isUrl;
        const bool isColor = trimmed.length() == 7 || trimmed.length() == 9;
        bool hexOk = false;
        if (isColor && trimmed.startsWith(u'#'))
            trimmed.mid(1).toUInt(&hexOk, 16);
        if (isUrl) {
            clipLog() << "url branch: " << trimmed.left(50) << Qt::endl;
            item[QStringLiteral("title")] = hostOf(trimmed);
            item[QStringLiteral("kind")] = QStringLiteral("url");
            item[QStringLiteral("detail")] = trimmed;
            item[QStringLiteral("icon")] = QString(QChar(0xE774));
        } else if (isColor && hexOk) {
            item[QStringLiteral("title")] = trimmed;
            item[QStringLiteral("kind")] = QStringLiteral("color");
            item[QStringLiteral("detail")] = trimmed;
            item[QStringLiteral("icon")] = QString(QChar(0xE790));
        } else {
            item[QStringLiteral("title")] = shortTitle(trimmed);
            item[QStringLiteral("kind")] = QStringLiteral("text");
            item[QStringLiteral("detail")] = trimmed;
            item[QStringLiteral("icon")] = QString(QChar(0xE8C8));
        }
        emit clipCaptured(item);
        if (needFavicon)
            fetchFavicon(trimmed, trimmed); // globe -> real site icon
    }
}
