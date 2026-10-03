import test from 'node:test';
import assert from 'node:assert/strict';
import { createArtworkLoader } from '../../YGOProbabilityCalculatorBlazor/wwwroot/js/card-artwork.mjs';
const url = id => `https://ygo-calculator-artwork.ygo-probability.workers.dev/small/${id}.jpg`;
const deferred = () => { let resolve, reject; const promise = new Promise((a, b) => { resolve = a; reject = b; }); return { promise, resolve, reject }; };
const flush = async () => { for (let i = 0; i < 20; i++) await Promise.resolve(); };
function setup(load = async () => {}, resolve = async id => ({ url: url(id) })) {
    let intersection; const images = [], metadata = [], results = [], timers = new Map(); let next = 0;
    const loader = createArtworkLoader({
        observer: callback => { intersection = callback; return { observe() {}, unobserve() {}, disconnect() {} }; },
        loadImage: (src, signal) => { images.push(src); return load(src, signal); },
        later: (fn, delay) => { timers.set(++next, { fn, delay }); return next; }, cancel: id => timers.delete(id)
    });
    function view(id, version = 1) {
        const element = {};
        const bridge = { async invokeMethodAsync(method, ver, src) {
            assert.equal(ver, version);
            if (method === 'ResolveArtwork') { metadata.push(id); return resolve(id); }
            assert.equal(method, 'ArtworkReady'); results.push({ id, src });
        } };
        return { element, id: loader.observe(element, bridge, id, version) };
    }
    async function near(views, isIntersecting = true) { intersection(views.map(v => ({ target: v.element, isIntersecting, boundingClientRect: v.rect, rootBounds: { bottom: 1060 } }))); await flush(); }
    async function tick() { const scheduled = [...timers.values()]; timers.clear(); scheduled.forEach(t => t.fn()); await flush(); }
    return { loader, view, near, tick, timers, images, metadata, results };
}

test('automatic visible and near rows load without clicks; distant metadata and bytes stay deferred', async () => {
    const s = setup(); const first = s.view(1), far = s.view(2);
    assert.deepEqual(s.metadata, []); await s.near([first]);
    assert.deepEqual(s.metadata, [1]); assert.deepEqual(s.images, [url(1)]);
    await s.near([far]); assert.deepEqual(s.metadata, [1, 2]);
});

test('thumbnail and expanded consumers share metadata/image; name edits/reorder/expansion reuse success', async () => {
    const s = setup(); const thumb = s.view(1), preview = s.view(1);
    await s.near([thumb, preview]); assert.equal(s.metadata.length, 1); assert.equal(s.images.length, 1);
    s.loader.unobserve(preview.id); const expandedAgain = s.view(1);
    await s.near([expandedAgain]); await s.near([thumb]);
    assert.equal(s.metadata.length, 1); assert.equal(s.images.length, 1);
    assert.ok(s.results.some(x => x.id === 1 && x.src === url(1)));
});

test('canonical aliases deduplicate image bytes using selected artwork URL', async () => {
    const pending = deferred(); const s = setup(() => pending.promise, async () => ({ url: url(77) }));
    await s.near([s.view(1), s.view(2)]); assert.equal(s.images.length, 1);
    pending.resolve(); await flush(); assert.equal(s.results.length, 2);
});

test('metadata concurrency is two, images four, and queued work is dropped after disposal', async () => {
    const pending = deferred(); const s = setup(async () => {}, () => pending.promise);
    const views = Array.from({ length: 100 }, (_, i) => s.view(i + 1)); await s.near(views);
    assert.equal(s.metadata.length, 2);
    views.forEach(v => s.loader.unobserve(v.id)); pending.resolve({ url: url(1) }); await flush();
    assert.equal(s.metadata.length, 2); assert.equal(s.images.length, 0); assert.equal(s.results.length, 0);
    const bytes = deferred(); const images = setup(() => bytes.promise);
    await images.near(Array.from({ length: 10 }, (_, i) => images.view(i + 1)));
    assert.equal(images.images.length, 4); bytes.resolve(); await flush(); assert.equal(images.images.length, 10);
});

test('metadata does not block retained image slots; queue refills beyond its 64 job bound', async () => {
    const s = setup(); await s.near(Array.from({ length: 100 }, (_, i) => s.view(i + 1)));
    for (let i = 0; i < 10; i++) await flush();
    assert.equal(s.metadata.length, 100); assert.equal(s.images.length, 100);
});

test('transient metadata failures honor Retry-After and reuse successful source after recovery', async () => {
    let calls = 0; const s = setup(async () => {}, async id => ++calls === 1 ? { retryAfter: 180000 } : { url: url(id) });
    await s.near([s.view(1)]); assert.equal(s.images.length, 0);
    assert.equal([...s.timers.values()][0].delay, 180000); await s.tick();
    assert.deepEqual(s.images, [url(1)]); assert.equal(s.metadata.length, 2);
});

test('opaque image failures use unchanged URL and stop after three attempts without busy polling', async () => {
    const s = setup(async () => { throw Error('opaque network error'); }); const view = s.view(1);
    await s.near([view]); assert.equal([...s.timers.values()][0].delay, 60000);
    await s.tick(); assert.equal([...s.timers.values()][0].delay, 120000); await s.tick();
    assert.equal(s.timers.size, 0); assert.deepEqual(s.images, [url(1), url(1), url(1)]);
    assert.equal(s.metadata.length, 1); assert.deepEqual(s.results, [{ id: 1, src: null }]);
    await s.near([view]); assert.equal(s.images.length, 3);
});

test('authoritative absent/malformed/provider sources and manual cards never request bytes', async () => {
    for (const result of [{ url: null }, { url: 'https://images.ygoprodeck.com/images/cards_small/1.jpg' }, { url: url(1) + '?retry=1' }]) {
        const s = setup(async () => {}, async () => result); await s.near([s.view(0), s.view(1)]);
        assert.equal(s.images.length, 0); assert.equal(s.metadata.length, 1); assert.equal(s.timers.size, 0);
    }
});

test('one departing shared consumer cannot cancel another; last departure aborts image and retries', async () => {
    let signal; const pending = deferred(); const s = setup((_, abort) => { signal = abort; return pending.promise; });
    const thumb = s.view(1), preview = s.view(1); await s.near([thumb, preview]);
    s.loader.unobserve(thumb.id); assert.equal(signal.aborted, false);
    s.loader.unobserve(preview.id); assert.equal(signal.aborted, true); pending.reject(Error('aborted')); await flush();
    assert.equal(s.timers.size, 0); assert.equal(s.results.length, 0);
});

test('removed/replaced session cannot receive late completion; offscreen retry timers stop', async () => {
    const response = deferred(); const s = setup(async () => {}, () => response.promise);
    const old = s.view(1); await s.near([old]); s.loader.unobserve(old.id);
    response.resolve({ url: url(1) }); await flush(); assert.equal(s.images.length, 0); assert.equal(s.results.length, 0);
    const retries = setup(async () => { throw Error(); }); const v = retries.view(1);
    await retries.near([v]); assert.equal(retries.timers.size, 1); await retries.near([v], false);
    assert.equal(retries.timers.size, 0); await retries.tick(); assert.equal(retries.images.length, 1);
});

test('very long Retry-After stops automatic attempts without retrying too early', async () => {
    const s = setup(async () => {}, async () => ({ retryAfter: 3600000 })); await s.near([s.view(1)]);
    assert.equal(s.timers.size, 0); assert.equal(s.metadata.length, 1); assert.equal(s.results[0].src, null);
});

test('aborted old image cannot fail or finish a replacement consumer sharing its URL', async () => {
    const first = deferred(), second = deferred(); let calls = 0;
    const s = setup(() => ++calls === 1 ? first.promise : second.promise);
    const old = s.view(1); await s.near([old]); s.loader.unobserve(old.id);
    const replacement = s.view(1, 2); await s.near([replacement]);
    first.reject(Error('old abort')); await flush();
    assert.equal(s.timers.size, 0); assert.equal(s.results.length, 0);
    second.resolve(); await flush(); assert.deepEqual(s.results, [{ id: 1, src: url(1) }]);
});


test('visible rows start before near-margin rows regardless of registration order', async () => {
    const pending = deferred(); const s = setup(async () => {}, () => pending.promise);
    const margin = s.view(3), lower = s.view(2), first = s.view(1);
    margin.rect = { top: 960, bottom: 1000 }; lower.rect = { top: 500, bottom: 540 }; first.rect = { top: 300, bottom: 340 };
    await s.near([margin, lower, first]); assert.deepEqual(s.metadata, [1, 2]);
    [margin, lower, first].forEach(v => s.loader.unobserve(v.id)); pending.resolve({ url: url(1) }); await flush();
});
