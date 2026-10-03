// One observer/scheduler per page. Metadata and image queues have independent
// limits so metadata lookups do not occupy retained-image delivery slots.
const origin = 'https://ygo-calculator-artwork.ygo-probability.workers.dev';
export function createArtworkLoader({ observer, loadImage, later = setTimeout, cancel = clearTimeout } = {}) {
    const views = new Map(), jobs = new Map(), sources = new Map();
    let nextId = 0, metadataActive = 0, imageActive = 0;
    const maxJobs = 64, maxCache = 256;
    function needed(job) { return [...views.values()].some(v => v.near && !v.complete && v.externalId === job.externalId); }
    function notify(job) {
        for (const v of views.values()) if (v.near && !v.complete && v.externalId === job.externalId) {
            v.complete = true;
            v.bridge.invokeMethodAsync('ArtworkReady', v.version, job.url ?? null).catch(() => {});
        }
    }
    function trim(map) {
        for (const [key, item] of map) {
            if (map.size <= maxCache) break;
            if (item.state === 'done' && (!item.externalId || !needed(item))) map.delete(key);
        }
    }
    function retry(job, milliseconds) {
        if (!needed(job)) { jobs.delete(job.externalId); return; }
        // No early retry if Retry-After exceeds our ten-minute automatic horizon.
        if (++job.failures >= 3 || milliseconds > 600000) {
            job.state = 'done'; job.url = null; notify(job); return;
        }
        job.state = 'retry';
        job.timer = later(() => { job.timer = null; job.state = job.url ? 'image' : 'metadata'; pump(); },
            Math.max(milliseconds || 0, 60000 * 2 ** (job.failures - 1)));
    }
    async function resolve(job) {
        job.state = 'resolving'; metadataActive++;
        try {
            const view = [...views.values()].find(v => v.near && v.externalId === job.externalId);
            const result = await view.bridge.invokeMethodAsync('ResolveArtwork', view.version);
            if (!needed(job)) return;
            if (result?.retryAfter != null) retry(job, result.retryAfter);
            else if (result?.url && new RegExp(`^${origin.replaceAll('.', '\\.')}\\/small\\/[1-9]\\d{0,9}\\.jpg$`).test(result.url)) {
                job.url = result.url; job.state = 'image';
            } else { job.state = 'done'; job.url = null; notify(job); }
        } catch { retry(job, 60000); }
        finally { metadataActive--; if (!needed(job) && job.state !== 'done') jobs.delete(job.externalId); pump(); }
    }
    async function deliver(source) {
        source.state = 'loading'; imageActive++;
        try {
            await loadImage(source.url, source.controller.signal);
            if (source.controller.signal.aborted) return;
            source.state = 'done';
            for (const job of jobs.values()) if (job.url === source.url && job.state === 'image') {
                job.state = 'done'; notify(job);
            }
        } catch {
            if (source.controller.signal.aborted) return;
            if (sources.get(source.url) === source) sources.delete(source.url);
            for (const job of jobs.values()) if (job.url === source.url && job.state === 'image') retry(job, 60000);
        } finally { imageActive--; trim(sources); pump(); }
    }
    function pump() {
        // Drop unused queued work and timers; a metadata call already underway may
        // finish, but cannot start image bytes or retries without a live consumer.
        for (const [id, job] of jobs) if (!needed(job) && job.state !== 'done' && job.state !== 'resolving') {
            if (job.timer) cancel(job.timer);
            jobs.delete(id);
        }
        for (const source of sources.values()) if (source.state !== 'done' &&
            ![...jobs.values()].some(j => j.url === source.url && j.state === 'image' && needed(j))) {
            source.controller.abort(); sources.delete(source.url);
        }
        for (const v of [...views.values()].sort((a, b) => a.priority - b.priority)) {
            if (v.complete || !v.near || v.externalId <= 0 || jobs.has(v.externalId)) continue;
            if ([...jobs.values()].filter(j => j.state !== 'done').length >= maxJobs) break;
            jobs.set(v.externalId, { externalId: v.externalId, state: 'metadata', url: null, failures: 0 });
        }
        trim(jobs);
        const priority = job => Math.min(...[...views.values()].filter(v => v.near && v.externalId === job.externalId).map(v => v.priority));
        for (const job of [...jobs.values()].sort((a, b) => priority(a) - priority(b))) {
            if (!needed(job)) continue;
            if (job.state === 'metadata' && metadataActive < 2) void resolve(job);
            if (job.state === 'image') {
                let source = sources.get(job.url);
                if (source?.state === 'done') { job.state = 'done'; notify(job); continue; }
                if (!source) {
                    source = { url: job.url, state: 'queued', controller: new AbortController() };
                    sources.set(job.url, source);
                }
                if (source.state === 'queued' && imageActive < 4) void deliver(source);
            }
        }
    }
    const intersection = observer(changes => {
        for (const change of changes) {
            const view = [...views.values()].find(v => v.element === change.target);
            if (!view) continue;
            view.near = change.isIntersecting;
            const rect = change.boundingClientRect;
            const viewportBottom = (change.rootBounds?.bottom ?? 160) - 160;
            view.priority = rect ? rect.bottom < 0 ? 100000 - rect.bottom :
                rect.top > viewportBottom ? 100000 + rect.top - viewportBottom : Math.max(0, rect.top) : 0;
            const job = jobs.get(view.externalId);
            if (view.near && job?.state === 'done') notify(job);
        }
        pump();
    });
    return {
        observe(element, bridge, externalId, version) {
            const id = ++nextId;
            views.set(id, { element, bridge, externalId, version, near: false, complete: false, priority: 0 });
            intersection.observe(element);
            return id;
        },
        unobserve(id) {
            const view = views.get(id);
            if (!view) return;
            intersection.unobserve(view.element); views.delete(id); pump();
            if (!views.size) { intersection.disconnect(); }
        }
    };
}

function loadImage(url, signal) {
    return new Promise((resolve, reject) => {
        const image = new Image();
        const timeout = setTimeout(() => finish(false), 25000);
        const abort = () => finish(false);
        function finish(ok) {
            clearTimeout(timeout); signal.removeEventListener('abort', abort);
            image.onload = image.onerror = null;
            if (!ok) image.removeAttribute('src');
            ok ? resolve() : reject(new Error('Artwork unavailable'));
        }
        image.onload = () => finish(true);
        image.onerror = () => finish(false);
        signal.addEventListener('abort', abort, { once: true });
        image.decoding = 'async'; image.src = url;
    });
}
let loader;
export function observe(element, bridge, externalId, version) {
    loader ??= createArtworkLoader({
        observer: callback => new IntersectionObserver(callback, { rootMargin: '160px 0px' }), loadImage
    });
    return loader.observe(element, bridge, externalId, version);
}
export function unobserve(id) { loader?.unobserve(id); }
