// One record, at most 1 MiB of UTF-16 storage. LocalStorage operations are synchronous:
// the timer and visibility flush commit captured strings, never live .NET models.
(function (root) {
    const key = 'ygo-calculator:session-recovery:v1';
    const maxLength = 524288;
    const debounceMs = 750;
    const clients = new Map();
    const report = (c, message) => {
        c.callback?.invokeMethodAsync('AutosaveStatus', c.generation, message)?.catch(() => {});
    };
    const cancel = c => { clearTimeout(c.timer); c.timer = null; c.pending = null; };
    const flush = c => {
        clearTimeout(c.timer);
        c.timer = null;
        if (!c.pending || c.paused || c.suspended) return;
        const record = c.pending;
        c.pending = null;
        try {
            // An idle or stale tab never replaces a different tab's newer draft.
            if (root.localStorage.getItem(key) !== c.expected) {
                c.paused = true;
                report(c, 'Another tab changed the recovery draft. Autosave is paused. Save a session file or explicitly use this workspace.');
                return;
            }
            root.localStorage.setItem(key, record);
            c.expected = record;
            report(c, null);
        } catch {
            report(c, 'Local recovery could not be saved. Save a session file to keep your work.');
        }
    };
    root.sessionRecovery = {
        initialize(id, callback) {
            if (clients.has(id)) return clients.get(id).inspection;
            const c = { callback, generation: 0, expected: null, pending: null, paused: true };
            clients.set(id, c);
            let inspection;
            try {
                c.expected = root.localStorage.getItem(key);
                if (c.expected === null) inspection = { exists: false };
                else {
                    if (c.expected.length > maxLength) throw new Error();
                    const envelope = JSON.parse(c.expected);
                    if (envelope.version !== 1 || typeof envelope.payload !== 'string') throw new Error();
                    const time = typeof envelope.savedAt === 'string' && /^\d{4}-\d{2}-\d{2}T/.test(envelope.savedAt)
                        && Number.isFinite(Date.parse(envelope.savedAt)) ? envelope.savedAt : null;
                    inspection = { exists: true, payload: envelope.payload, savedAt: time };
                }
            } catch {
                inspection = { exists: true, error: 'The local recovery draft cannot be read. It is kept until you discard it or explicitly replace it.' };
            }
            c.inspection = inspection;
            c.paused = inspection.exists;
            // Native fragment navigation can precede the .NET render that offers a link.
            c.navigation = () => {
                if (root.location?.hash?.startsWith('#ygo-session=')) { c.suspended = true; cancel(c); }
            };
            c.navigation();
            root.addEventListener('hashchange', c.navigation);
            root.addEventListener('popstate', c.navigation);
            c.visibility = () => { if (root.document.visibilityState === 'hidden') flush(c); };
            c.pagehide = () => flush(c);
            root.document.addEventListener('visibilitychange', c.visibility);
            root.addEventListener('pagehide', c.pagehide);
            return inspection;
        },
        update(id, generation, payload, replace = false) {
            const c = clients.get(id);
            if (!c || generation <= c.generation) return;
            c.generation = generation;
            cancel(c);
            if (c.suspended) return;
            if (c.paused && !replace) return;
            try {
                if (replace) c.expected = root.localStorage.getItem(key);
                const record = JSON.stringify({ version: 1, savedAt: new Date().toISOString(), payload });
                if (record.length > maxLength) {
                    report(c, 'This session is too large for local recovery. Save a session file to keep your work.');
                    return;
                }
                c.paused = false;
                c.pending = record;
                c.timer = setTimeout(() => flush(c), debounceMs);
            } catch { report(c, 'Local recovery is unavailable. Save a session file to keep your work.'); }
        },
        suspend(id, generation, suspended) {
            const c = clients.get(id);
            if (!c || generation < c.generation || generation <= (c.suspensionGeneration ?? -1)) return;
            if (!suspended && root.location?.hash?.startsWith('#ygo-session=')) return;
            c.suspensionGeneration = generation;
            c.generation = Math.max(c.generation, suspended ? generation : generation - 1);
            c.suspended = suspended;
            if (suspended) cancel(c);
        },
        discard(id, generation) {
            const c = clients.get(id);
            if (!c || generation <= c.generation) return false;
            c.generation = generation;
            cancel(c);
            try {
                // Do not erase a different tab's draft following an old recovery notice.
                if (root.localStorage.getItem(key) !== c.expected) {
                    report(c, 'Another tab changed the recovery draft. Reload to inspect it before discarding.');
                    return false;
                }
                root.localStorage.removeItem(key);
                c.expected = null;
                c.paused = false;
                return true;
            } catch { report(c, 'The recovery draft could not be discarded.'); return false; }
        },
        dispose(id) {
            const c = clients.get(id);
            if (!c) return;
            flush(c);
            cancel(c);
            root.document.removeEventListener('visibilitychange', c.visibility);
            root.removeEventListener('pagehide', c.pagehide);
            root.removeEventListener('hashchange', c.navigation);
            root.removeEventListener('popstate', c.navigation);
            clients.delete(id);
        }
    };
})(globalThis);
