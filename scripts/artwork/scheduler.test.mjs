import test from 'node:test';
import assert from 'node:assert/strict';
import { setImmediate as nextTask } from 'node:timers/promises';
import { createArtworkLoader } from '../../YGOProbabilityCalculatorBlazor/wwwroot/js/card-artwork.mjs';
const url = id => `https://ygo-calculator-artwork.ygo-probability.workers.dev/small/${id}.jpg`;
const deferred = () => { let resolve, reject; const promise = new Promise((a, b) => { resolve = a; reject = b; }); return { promise, resolve, reject }; };
function observation() {
    const values = [], waiting = [];
    return {
        values,
        record(value) {
            values.push(value);
            for (const entry of [...waiting]) if (values.length >= entry.count) {
                waiting.splice(waiting.indexOf(entry), 1); entry.resolve();
            }
        },
        waitFor(count) {
            if (values.length >= count) return Promise.resolve();
            return new Promise(resolve => waiting.push({ count, resolve }));
        }
    };
}
function setup(load = async () => {}, resolve = async id => ({ url: url(id) })) {
    let intersection; const images = observation(), metadata = observation(), results = observation(), scheduled = observation();
    const timers = new Map(); let next = 0;
    const loader = createArtworkLoader({
        observer: callback => { intersection = callback; return { observe() {}, unobserve() {}, disconnect() {} }; },
        loadImage: (src, signal) => { images.record(src); return load(src, signal); },
        later: (fn, delay) => { timers.set(++next, { fn, delay }); scheduled.record(delay); return next; }, cancel: id => timers.delete(id)
    });
    function view(id, version = 1) {
        const element = {};
        const bridge = { async invokeMethodAsync(method, ver, src) {
            assert.equal(ver, version);
            if (method === 'ResolveArtwork') { metadata.record(id); return resolve(id); }
            assert.equal(method, 'ArtworkReady'); results.record({ id, src });
        } };
        return { element, id: loader.observe(element, bridge, id, version) };
    }
    function near(views, isIntersecting = true) { intersection(views.map(v => ({ target: v.element, isIntersecting, boundingClientRect: v.rect, rootBounds: { bottom: 1060 } }))); }
    function tick() { const scheduled = [...timers.values()]; timers.clear(); scheduled.forEach(timer => timer.fn()); }
    return { loader, view, near, tick, timers, images: images.values, metadata: metadata.values, results: results.values,
        imagesStarted: images.waitFor, metadataStarted: metadata.waitFor, artworkReady: results.waitFor, retriesScheduled: scheduled.waitFor };
}

test('automatic visible and near rows load without clicks; distant metadata and bytes stay deferred', async () => {
    const s = setup(); const first = s.view(1), far = s.view(2);
    assert.deepEqual(s.metadata, []); s.near([first]); await s.artworkReady(1);
    assert.deepEqual(s.metadata, [1]); assert.deepEqual(s.images, [url(1)]);
    s.near([far]); await s.artworkReady(2); assert.deepEqual(s.metadata, [1, 2]);
});

test('thumbnail and expanded consumers share metadata/image; name edits/reorder/expansion reuse success', async () => {
    const s = setup(); const thumb = s.view(1), preview = s.view(1);
    s.near([thumb, preview]); await s.artworkReady(2); assert.equal(s.metadata.length, 1); assert.equal(s.images.length, 1);
    s.loader.unobserve(preview.id); const expandedAgain = s.view(1);
    s.near([expandedAgain]); await s.artworkReady(3); s.near([thumb]);
    assert.equal(s.metadata.length, 1); assert.equal(s.images.length, 1);
    assert.ok(s.results.some(x => x.id === 1 && x.src === url(1)));
});

test('canonical aliases deduplicate image bytes using selected artwork URL', async () => {
    const pending = deferred(); const s = setup(() => pending.promise, async () => ({ url: url(77) }));
    s.near([s.view(1), s.view(2)]); await s.imagesStarted(1); assert.equal(s.images.length, 1);
    pending.resolve(); await s.artworkReady(2); assert.equal(s.results.length, 2); assert.equal(s.images.length, 1);
});

test('metadata concurrency is two, images four, and queued work is dropped after disposal', async () => {
    const pending = deferred(); const s = setup(async () => {}, () => pending.promise);
    const views = Array.from({ length: 100 }, (_, i) => s.view(i + 1)); s.near(views); await s.metadataStarted(2);
    assert.equal(s.metadata.length, 2);
    views.forEach(v => s.loader.unobserve(v.id)); pending.resolve({ url: url(1) });
    // Let the explicitly released metadata callbacks finish before checking absent downstream work.
    await nextTask();
    assert.equal(s.metadata.length, 2); assert.equal(s.images.length, 0); assert.equal(s.results.length, 0);
    const bytes = deferred(); const images = setup(() => bytes.promise);
    images.near(Array.from({ length: 10 }, (_, i) => images.view(i + 1))); await images.imagesStarted(4);
    await nextTask(); // Keep image bytes held while all metadata is allowed to compete for image slots.
    assert.equal(images.images.length, 4); bytes.resolve(); await images.artworkReady(10); assert.equal(images.images.length, 10);
});

test('metadata does not block retained image slots; queue refills beyond its 64 job bound', async () => {
    const s = setup(); s.near(Array.from({ length: 100 }, (_, i) => s.view(i + 1)));
    await s.artworkReady(100);
    assert.equal(s.metadata.length, 100); assert.equal(s.images.length, 100);
});

test('transient metadata failures honor Retry-After and reuse successful source after recovery', async () => {
    let calls = 0; const s = setup(async () => {}, async id => ++calls === 1 ? { retryAfter: 180000 } : { url: url(id) });
    s.near([s.view(1)]); await s.retriesScheduled(1); assert.equal(s.images.length, 0);
    assert.equal([...s.timers.values()][0].delay, 180000); s.tick(); await s.artworkReady(1);
    assert.deepEqual(s.images, [url(1)]); assert.equal(s.metadata.length, 2);
});

test('opaque image failures use unchanged URL and stop after three attempts without busy polling', async () => {
    const s = setup(async () => { throw Error('opaque network error'); }); const view = s.view(1);
    s.near([view]); await s.retriesScheduled(1); assert.equal([...s.timers.values()][0].delay, 60000);
    s.tick(); await s.retriesScheduled(2); assert.equal([...s.timers.values()][0].delay, 120000);
    s.tick(); await s.artworkReady(1);
    assert.equal(s.timers.size, 0); assert.deepEqual(s.images, [url(1), url(1), url(1)]);
    assert.equal(s.metadata.length, 1); assert.deepEqual(s.results, [{ id: 1, src: null }]);
    s.near([view]); assert.equal(s.images.length, 3);
});

test('authoritative absent/malformed/provider sources and manual cards never request bytes', async () => {
    for (const result of [{ url: null }, { url: 'https://images.ygoprodeck.com/images/cards_small/1.jpg' }, { url: url(1) + '?retry=1' }]) {
        const s = setup(async () => {}, async () => result); s.near([s.view(0), s.view(1)]); await s.artworkReady(1);
        assert.equal(s.images.length, 0); assert.equal(s.metadata.length, 1); assert.equal(s.timers.size, 0);
    }
});

test('one departing shared consumer cannot cancel another; last departure aborts image and retries', async () => {
    let signal; const pending = deferred(); const s = setup((_, abort) => { signal = abort; return pending.promise; });
    const thumb = s.view(1), preview = s.view(1); s.near([thumb, preview]); await s.imagesStarted(1);
    s.loader.unobserve(thumb.id); assert.equal(signal.aborted, false);
    s.loader.unobserve(preview.id); assert.equal(signal.aborted, true); pending.reject(Error('aborted'));
    await nextTask(); // The rejected held image must not schedule a retry or notify its departed consumers.
    assert.equal(s.timers.size, 0); assert.equal(s.results.length, 0);
});

test('removed/replaced session cannot receive late completion; offscreen retry timers stop', async () => {
    const response = deferred(); const s = setup(async () => {}, () => response.promise);
    const old = s.view(1); s.near([old]); await s.metadataStarted(1); s.loader.unobserve(old.id);
    response.resolve({ url: url(1) }); await nextTask(); assert.equal(s.images.length, 0); assert.equal(s.results.length, 0);
    const retries = setup(async () => { throw Error(); }); const v = retries.view(1);
    retries.near([v]); await retries.retriesScheduled(1); assert.equal(retries.timers.size, 1); retries.near([v], false);
    assert.equal(retries.timers.size, 0); retries.tick(); assert.equal(retries.images.length, 1);
});

test('very long Retry-After stops automatic attempts without retrying too early', async () => {
    const s = setup(async () => {}, async () => ({ retryAfter: 3600000 })); s.near([s.view(1)]); await s.artworkReady(1);
    assert.equal(s.timers.size, 0); assert.equal(s.metadata.length, 1); assert.equal(s.results[0].src, null);
});

test('aborted old image cannot fail or finish a replacement consumer sharing its URL', async () => {
    const first = deferred(), second = deferred(); let calls = 0;
    const s = setup(() => ++calls === 1 ? first.promise : second.promise);
    const old = s.view(1); s.near([old]); await s.imagesStarted(1); s.loader.unobserve(old.id);
    const replacement = s.view(1, 2); s.near([replacement]); await s.imagesStarted(2);
    first.reject(Error('old abort')); await nextTask();
    assert.equal(s.timers.size, 0); assert.equal(s.results.length, 0);
    second.resolve(); await s.artworkReady(1); assert.deepEqual(s.results, [{ id: 1, src: url(1) }]);
});

test('visible rows start before near-margin rows regardless of registration order', async () => {
    const pending = deferred(); const s = setup(async () => {}, () => pending.promise);
    const margin = s.view(3), lower = s.view(2), first = s.view(1);
    margin.rect = { top: 960, bottom: 1000 }; lower.rect = { top: 500, bottom: 540 }; first.rect = { top: 300, bottom: 340 };
    s.near([margin, lower, first]); await s.metadataStarted(2); assert.deepEqual(s.metadata, [1, 2]);
    [margin, lower, first].forEach(v => s.loader.unobserve(v.id)); pending.resolve({ url: url(1) }); await nextTask();
});
