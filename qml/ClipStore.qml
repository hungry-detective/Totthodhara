// Clip store: the ViewModel contract for the shelf.
// Owns the C++ ClipboardService: real copies land here via clipCaptured,
// clicks copy back to the clipboard. Pin/snippet/delete stay local until
// the SQLite stage. Roles: title, kind, detail, icon, pinned, snippet,
// selected, index.
import QtQuick
import Totthodhara.Backend 1.0

// Non-visual store (Item root so it can own the C++ service object).
Item {
    id: root

    signal toast(string message)

    property string searchText: ""
    property ListModel view: ListModel {}
    property int seq: 1000  // recency counter (backend items sort by it)

    ClipboardService {
        id: clipboard
        maxFileSizeMB: AppState.maxFileSizeMB
        onClipCaptured: (item) => root.addClip(item)
        onRejected: (message) => root.toast(message)
        // Async favicon: swap the globe glyph for the site icon, in place.
        // (A refresh() here would replay the arrival animation on every
        // card each time any icon lands.) Twins share the URL, so every
        // match upgrades (id-first elsewhere, URL here — same icon either
        // way, and patching only the first row desyncs model from view).
        onIconReady: (detail, path) => {
            let hit = false
            for (let k = 0; k < items.length; k++) {
                if (items[k].detail === detail) {
                    items[k] = Object.assign({}, items[k], { icon: path })
                    hit = true
                }
            }
            if (hit) {
                items = items.slice(); // notify model truth (element writes don't)
                for (let r = 0; r < view.count; r++) {
                    if (view.get(r).detail === detail)
                        view.setProperty(r, "icon", path)
                }
            }
        }
    }

    StorageService { id: storage }

    // Storage limits apply the moment they change (not just on restart),
    // so setting Max history to 3 trims the shelf to 3 right away.
    // Search filters live too: every keystroke rebuilds the view (a
    // structural refresh is explicitly allowed for search).
    onSearchTextChanged: refresh()
    Connections {
        target: AppState
        function onMaxHistoryItemsChanged() {
            trimHistory()
            refresh()
            persist()
        }
        function onAutoCleanHoursChanged() {
            if (pruneOld())
                persist()
            trimHistory()
            refresh()
        }
    }

    // Every mutation persists (cheap full rewrite, see backend).
    function persist() {
        storage.saveItems(items)
    }
    // Drop the on-disk payload of a removed clip (images/files in
    // data/clips). Text lives in the DB (rewritten without the row);
    // favicons stay (shared per-host cache). Explorer originals are
    // never touched (containment is enforced in C++).
    function dropFiles(m) {
        if (m && (m.kind === "image" || m.kind === "file"))
            storage.removeItemFiles(m.detail)
    }

    // Called once from Main with the shelf window.
    function monitor(window) {
        clipboard.startMonitoring(window)
    }

    // Mirror of the WPF sort: snippets -> pinned -> recent.
    function sortItems(items) {
        return items.slice().sort((a, b) => {
            const rank = (x) => (x.snippet ? 0 : x.pinned ? 1 : 2);
            return rank(a) - rank(b) || b.added - a.added;
        });
    }

    function refresh() {
        const q = searchText.trim().toLowerCase();
        const list = sortItems(items.filter((it) => {
            if (!q)
                return true;
            return (it.title + " " + it.detail).toLowerCase().includes(q);
        }));
        view.clear();
        list.forEach((it, i) => view.append({
            title: it.title,
            kind: it.kind,
            detail: it.detail,
            icon: it.icon,
            pinned: it.pinned,
            snippet: it.snippet,
            selected: it.selected,
            added: it.added,
            // Inline action feedback ("Pasted!") lives here, never as a
            // structural rebuild: always reset on refresh so a pending
            // flash can never stick past a strip rebuild.
            flashText: (it.flashText !== undefined) ? it.flashText : "",
            pos: i + 1
        }));
    }

    // NOTE: objects loaded from C++ (loadItems) do NOT accept in-place
    // property writes — always REPLACE the whole object instead.
    // Locate by VALUE (stable `added` id), never by object identity:
    // identity match fails on C++-sourced rows, which silently broke
    // selection/pin/delete (verified: replace k=-1 on loaded items).
    function indexOfItem(m) {
        if (!m)
            return -1;
        if (m.added !== undefined) {
            const k = items.findIndex((x) => x && x.added === m.added);
            if (k >= 0)
                return k;
        }
        return items.findIndex((x) => x && x.title === m.title && x.detail === m.detail);
    }
    function replaceItem(m, patch) {
        const k = indexOfItem(m);
        if (k < 0)
            return;
        const next = items.slice();
        next[k] = Object.assign({}, next[k], patch);
        // Reassign (not just the slot): element writes never notify `var`
        // bindings, so without this the Paste All button (bound to
        // selectedCount()) stays dead after Shift+click. Reassigning also
        // upgrades C++-sourced storage to a real JS array.
        items = next;
    }

    function findItem(i) {
        const v = view.get(i);
        if (!v)
            return null;
        // Unique id first: duplicate contents (same text copied twice) must
        // never resolve to the wrong twin — Ctrl+click deleted whichever
        // twin find() hit first. Title+detail is only a legacy fallback.
        if (v.added !== undefined) {
            const m = items.find((x) => x.added === v.added);
            if (m)
                return m;
        }
        return items.find((m) => m.title === v.title && m.detail === v.detail);
    }

    // View row for a stable item id (-1 when gone). The context menu lives
    // in its own window while the shelf keeps updating, so it resolves its
    // captured id fresh instead of trusting a stale row.
    function indexOfAdded(a) {
        for (let k = 0; k < view.count; k++) {
            if (view.get(k).added === a)
                return k;
        }
        return -1;
    }

    // Backend entry: a fresh copy from Windows.
    // No inline flash here: the new card sliding in IS the feedback.
    function addClip(item) {
        items.unshift({
            title: item.title,
            kind: item.kind,
            detail: item.detail,
            icon: item.icon,
            pinned: false,
            snippet: false,
            selected: false,
            added: seq++
        });
        trimHistory();
        refresh();
        persist();
    }

    function trimHistory() {
        // Keep pinned/snippets; drop oldest excess (WPF: MaxHistoryItems).
        // Rebuild by identity within ONE read (never indexOf across reads:
        // identity match fails on C++-sourced rows and splice(-1,1) would
        // silently drop the WRONG (last) item).
        const keep = [];
        const drop = [];
        for (const m of sortItems(items)) {
            if (m.pinned || m.snippet || keep.length < AppState.maxHistoryItems)
                keep.push(m);
            else
                drop.push(m);
        }
        if (drop.length === 0)
            return;
        for (const d of drop)
            dropFiles(d);
        const gone = new Set(drop);
        items = items.filter((x) => !gone.has(x));
    }

    // Auto-clean: drop file-backed items (images in data/clips/) older
    // than autoCleanHours, unless pinned/snippet/selected. Referenced
    // Explorer originals are NEVER deleted (dropFiles only removes our own
    // clips/ payloads): their rows just fall off the shelf. Text/links
    // carry no files, so they are exempt. Returns true when anything left.
    function pruneOld() {
        if (AppState.autoCleanHours <= 0)
            return false;
        let dropped = false;
        for (let k = items.length - 1; k >= 0; k--) {
            const m = items[k];
            if (m.pinned || m.snippet || m.selected)
                continue;
            if ((m.kind !== "image" && m.kind !== "file")
                || !String(m.detail).startsWith("file:"))
                continue;
            if (storage.fileAgeHours(m.detail) > AppState.autoCleanHours) {
                dropFiles(m);
                items.splice(k, 1);
                dropped = true;
            }
        }
        if (dropped)
            items = items.slice(); // notify (in-place splices don't)
        return dropped;
    }

    // Click = copy back + auto-paste. Ctrl+click = instant delete
    // (WPF behavior). Shift+click = multi-select toggle. Selection edits are surgical
    // (one view row): a full refresh replays the new-arrival animation on
    // EVERY card, which is distracting on a plain click.
    function clickItem(i, shift, ctrl) {
        const m = findItem(i);
        if (!m) {
            // Row with no backing item (stale card): rebuild the strip
            // from the item truth so the ghost vanishes instead of
            // silently ignoring the click.
            toast("Item is gone");
            refresh();
            return;
        }
        if (ctrl) {
            deleteItem(i);
            return;
        }
        if (shift) {
            const on = !m.selected;
            replaceItem(m, { selected: on });
            view.setProperty(i, "selected", on);
            setFlash(i, on ? "Selected" : "Deselected");
        } else {
            clearSelection();
            let ok = true;
            if (m.kind === "image" || m.kind === "file") {
                if (m.detail.startsWith("file:///"))
                    clipboard.copyFiles([m.detail],
                                        AppState.copyToDestination);
                else {
                    ok = false;
                    setFlash(i, "Not a file");
                }
            } else {
                clipboard.copyText(m.detail, AppState.copyToDestination);
            }
            if (ok)
                setFlash(i, AppState.copyToDestination ? "Pasted!" : "Copied!");
        }
    }

    // Drops every selection tick without touching order — no animation.
    function clearSelection() {
        let changed = false;
        for (const x of items) {
            if (x.selected) {
                changed = true;
                break;
            }
        }
        if (!changed)
            return;
        items = items.map((x) => Object.assign({}, x, { selected: false }));
        for (let k = 0; k < view.count; k++) {
            if (view.get(k).selected)
                view.setProperty(k, "selected", false);
        }
    }

    // Inline card feedback: swaps the card title for ~1.2s ("Pasted!"),
    // then the card heals itself. Surgical like selection edits — never
    // refresh(), so neighbors never move or replay animations. The card's
    // own timer clears by stable `added` id (never by row: rows shift).
    // Model role is `flashText` (card already uses `flash` for its click bloom).
    function setFlash(i, text) {
        const m = findItem(i);
        if (!m)
            return;
        replaceItem(m, { flashText: text });
        view.setProperty(i, "flashText", text);
    }
    function setFlashById(added, text) {
        const r = indexOfAdded(added);
        if (r >= 0)
            setFlash(r, text);
    }
    function clearFlashById(added) {
        const r = indexOfAdded(added);
        if (r < 0)
            return;
        const m = findItem(r);
        if (m)
            replaceItem(m, { flashText: "" });
        view.setProperty(r, "flashText", "");
    }

    function togglePin(i) {
        const m = findItem(i);
        if (m) {
            const on = !m.pinned;
            replaceItem(m, { pinned: on });
            refresh();
            setFlashById(m.added, on ? "Pinned" : "Unpinned");
            persist();
        }
    }

    function toggleSnippet(i) {
        const m = findItem(i);
        if (m) {
            const on = !m.snippet;
            replaceItem(m, { snippet: on });
            refresh();
            setFlashById(m.added, on ? "Saved as snippet" : "Snippet removed");
            persist();
        }
    }

    // Instant removal, no "Deleted!" beat: the row animating out IS the
    // feedback. Surgical: exactly this row plays the remove transition
    // and followers renumber in place. A full refresh() here rebuilds
    // every delegate (all replay arrival animations + renumber at once),
    // which reads as "deleted the wrong item".
    function deleteItem(i) {
        const m = findItem(i);
        if (!m) {
            // Backing item already gone (ghost row): drop the row and
            // rebuild so view and items agree again.
            if (i >= 0 && i < view.count)
                view.remove(i, 1);
            toast("Item is gone");
            refresh();
            persist();
            return;
        }
        // Splice by value-keyed index on a copy: indexOf-by-identity fails
        // on C++-sourced rows and splice(-1,1) would drop the WRONG (last)
        // item while the view row still goes away (ghost desync).
        const k = indexOfItem(m);
        dropFiles(m);
        const next = items.slice();
        if (k >= 0)
            next.splice(k, 1);
        items = next;
        view.remove(i, 1);
        for (let j = i; j < view.count; j++)
            view.setProperty(j, "pos", j + 1);
        persist();
    }

    // Clear history flashes "Deleted!" inline on every removed card
    // (same language as Pasted!), then the rows go away together.
    // Pinned/snippets stay and never flash. The strip emptying plus the
    // inline beat is the feedback — never a center toast.
    property bool clearPending: false
    Timer {
        id: clearTimer
        interval: 600
        onTriggered: root.finishClearHistory()
    }
    function clearHistory() {
        if (root.clearPending)
            return;
        let ids = 0;
        for (let k = 0; k < view.count; k++) {
            const m = findItem(k);
            if (m && !m.pinned && !m.snippet) {
                setFlash(k, "Deleted!");
                ids++;
            }
        }
        if (ids === 0) {
            searchText = "";
            refresh();
            return;
        }
        root.clearPending = true;
        clearTimer.restart();
    }
    function finishClearHistory() {
        root.clearPending = false;
        for (let k = items.length - 1; k >= 0; k--) {
            if (!items[k].pinned && !items[k].snippet) {
                dropFiles(items[k]);
                items.splice(k, 1);
            }
        }
        items = items.slice(); // notify (in-place splices don't)
        searchText = "";
        refresh();
        persist();
    }

    function selectedCount() {
        return items.filter((m) => m.selected).length;
    }

    // Bulk copy-back: text-like selections join with newlines into one
    // paste; file/image selections copy as files. Mixed selections are
    // refused (one clipboard payload) — pick one kind. Actually writes to
    // the clipboard (the old version only toasted and cleared).
    function pasteAll() {
        const sel = items.filter((m) => m.selected);
        if (sel.length === 0) {
            toast("Nothing selected");
            return;
        }
        const files = [];
        const texts = [];
        for (const m of sel) {
            if ((m.kind === "image" || m.kind === "file")
                && String(m.detail).startsWith("file:///"))
                files.push(m.detail);
            else if (m.kind === "text" || m.kind === "url" || m.kind === "color")
                texts.push(m.detail);
        }
        if (files.length > 0 && texts.length > 0) {
            toast("Select text or files, not both");
            return;
        }
        if (files.length > 0)
            clipboard.copyFiles(files, AppState.copyToDestination);
        else if (texts.length > 0)
            clipboard.copyText(texts.join("\n"), AppState.copyToDestination);
        else {
            const skipped = sel.filter((m) => m.kind !== "text" && m.kind !== "url" && m.kind !== "color").length;
            toast(skipped > 0 ? "Demo items can't be pasted" : "Nothing to paste");
            return;
        }
        const msg = AppState.copyToDestination ? "Pasted!" : "Copied!";
        for (const m of sel)
            setFlashById(m.added, msg);
        clearSelection();
        persist();
    }

    // Icon file failed to decode (corrupt download etc): fall back to
    // the globe glyph for this run (no persist, so next launch retries).
    function resetIcon(i) {
        const m = findItem(i);
        if (m && m.icon !== "\uE774") {
            replaceItem(m, { icon: "\uE774" });
            view.setProperty(i, "icon", "\uE774");
        }
    }

    function openUrl(i) {
        const m = findItem(i);
        if (m) {
            clipboard.openUrl(m.detail);
            setFlash(i, "Opening...");
        }
    }

    // Drag-out to another app: OS takes the mime payload (text / link /
    // file). The card lift/fade anim runs in ClipCard.dragging; the inline
    // flash here is the after-drop feedback on the same card.
    function beginSystemDrag(i) {
        const m = findItem(i);
        if (!m)
            return;
        const added = m.added;
        const dropped = clipboard.startSystemDrag(m.title, m.kind, m.detail,
                                                      AppState.effectiveTheme === "Dark");
        setFlashById(added, dropped ? "Dropped!" : "Cancelled");
    }

    // Starter content: none. Real copies land here via the backend.
    // (Cleared on request; only what you copy stays.)
    property var items: []

    Component.onCompleted: {
        // Restore portable history, then continue the recency counter.
        // Normalize to a real JS array up front (identity ops are
        // unreliable on raw C++-sourced rows — see indexOfItem).
        items = storage.loadItems().slice();
        for (const m of items)
            seq = Math.max(seq, (m.added || 0) + 1);
        const pruned = pruneOld();
        trimHistory();
        // Heal link cards stuck on the globe: resolve synchronously
        // (no signal round-trip to lose).
        for (let k = 0; k < items.length; k++) {
            const m = items[k];
            if (m.kind === "url" && !String(m.icon).startsWith("file:")) {
                const p = clipboard.resolveIcon(m.detail);
                if (p)
                    items[k] = Object.assign({}, m, { icon: p });
            }
        }
        refresh();
        if (pruned)
            persist();
    }
}
