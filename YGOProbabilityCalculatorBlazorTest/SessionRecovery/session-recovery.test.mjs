import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

const source = readFileSync(new URL('../../YGOProbabilityCalculatorBlazor/wwwroot/js/session-recovery.js', import.meta.url), 'utf8');
const key = 'ygo-calculator:session-recovery:v1';
function fixture(initial = null) {
    const data = new Map([['theme', 'dark'], ['cardCache', '{"fixture":1}']]);
    if (initial !== null) data.set(key, initial);
    const timers = new Map(), listeners = new Map(), statuses = [];
    let clock = 0, timerId = 0, writes = 0, fail = false;
    const storage = {
        getItem: k => { if (fail) throw Error('denied'); return data.get(k) ?? null; },
        setItem: (k, v) => { if (fail) throw Error('quota'); data.set(k, v); writes++; },
        removeItem: k => { if (fail) throw Error('denied'); data.delete(k); }
    };
    const events = {
        addEventListener: (name, fn) => { if (!listeners.has(name)) listeners.set(name, new Set()); listeners.get(name).add(fn); },
        removeEventListener: (name, fn) => listeners.get(name)?.delete(fn)
    };
    const document = { ...events, visibilityState: 'visible' };
    const context = vm.createContext({ localStorage: storage, document, ...events,
        setTimeout: (fn, ms) => { const id = ++timerId; timers.set(id, { fn, at: clock + ms }); return id; },
        clearTimeout: id => timers.delete(id) });
    vm.runInContext(source, context);
    const api = context.sessionRecovery;
    return { api, data, statuses, document,
        start: id => api.initialize(id, { invokeMethodAsync: (_, generation, message) => { statuses.push(message); return Promise.resolve(); } }),
        tick: ms => { clock += ms; for (const [id, timer] of [...timers]) if (timer.at <= clock) { timers.delete(id); timer.fn(); } },
        event: name => { for (const fn of listeners.get(name) ?? []) fn(); },
        fail: value => { fail = value; },
        writes: () => writes,
        payload: () => data.has(key) ? JSON.parse(data.get(key)).payload : null,
        listeners: () => [...listeners.values()].reduce((n, set) => n + set.size, 0),
        timers: () => timers.size
    };
}
const envelope = (payload = 'previous', savedAt = '2026-10-03T08:00:00Z') => JSON.stringify({ version: 1, savedAt, payload });

test('750ms debounce coalesces captured bytes and rejects delayed earlier interop', () => {
    const f = fixture();
    assert.equal(f.start('a').exists, false);
    f.api.update('a', 1, 'first'); f.tick(500);
    f.api.update('a', 3, 'latest'); f.api.update('a', 2, 'late earlier snapshot');
    f.tick(749); assert.equal(f.writes(), 0);
    f.tick(1); assert.equal(f.payload(), 'latest'); assert.equal(f.writes(), 1);
    f.api.update('a', 3, 'rerender'); f.tick(1000); assert.equal(f.writes(), 1);
});
test('inspection and idle startup preserve previous data until explicit replacement', () => {
    const raw = envelope(); const f = fixture(raw);
    assert.equal(f.start('a').payload, 'previous');
    f.api.update('a', 1, 'default'); f.tick(1000); assert.equal(f.data.get(key), raw);
    f.api.update('a', 2, 'accepted example', true); f.tick(750); assert.equal(f.payload(), 'accepted example');
    f.api.update('a', 1, 'old recovery', true); f.tick(1000); assert.equal(f.payload(), 'accepted example');
});
test('discard fences delayed writes, removes only its key, next edit resumes', () => {
    const f = fixture(); f.start('a'); f.api.update('a', 1, 'old'); f.tick(750);
    f.api.update('a', 2, 'pending'); assert.equal(f.api.discard('a', 3), true);
    f.api.update('a', 2, 'late'); f.tick(1000); assert.equal(f.payload(), null);
    assert.equal(f.data.get('theme'), 'dark'); assert.equal(f.data.get('cardCache'), '{"fixture":1}');
    f.api.update('a', 4, 'new edit'); f.tick(750); assert.equal(f.payload(), 'new edit');
});
test('invalid envelopes and oversized parsing stay bounded and preserve unreadable data', () => {
    for (const raw of ['{', '{"version":9,"payload":"future"}', 'x'.repeat(524289)]) {
        const f = fixture(raw); assert.equal(f.start('a').exists, true); assert.ok(f.start('a').error);
        f.api.update('a', 1, 'edit'); f.tick(750); assert.equal(f.data.get(key), raw);
        assert.equal(f.api.discard('a', 2), true); assert.equal(f.payload(), null);
    }
});
test('invalid timestamp is omitted without rejecting valid session bytes', () => {
    const f = fixture(envelope('session', 'invalid')); assert.equal(f.start('a').savedAt, null);
    assert.equal(f.start('a').payload, 'session');
});
test('quota, unavailable storage, and size failures keep last successful record', () => {
    const f = fixture(); f.start('a'); f.api.update('a', 1, 'good'); f.tick(750);
    const previous = f.data.get(key); f.fail(true); f.api.update('a', 2, 'quota'); f.tick(750);
    assert.equal(f.data.get(key), previous); assert.match(f.statuses.at(-1), /could not be saved/);
    f.fail(false); f.api.update('a', 3, 'x'.repeat(524288)); f.tick(750);
    assert.equal(f.data.get(key), previous); assert.match(f.statuses.at(-1), /too large/);
    const blocked = fixture(); blocked.fail(true); assert.equal(blocked.start('a').exists, true);
});
test('visibility/pagehide flush pending snapshots and disposal removes timers/listeners', () => {
    const f = fixture(); f.start('a'); f.api.update('a', 1, 'hidden');
    f.document.visibilityState = 'hidden'; f.event('visibilitychange'); assert.equal(f.payload(), 'hidden');
    f.api.update('a', 2, 'navigated'); f.event('pagehide'); assert.equal(f.payload(), 'navigated');
    f.api.update('a', 3, 'disposed'); f.api.dispose('a'); assert.equal(f.payload(), 'disposed');
    assert.equal(f.listeners(), 0); assert.equal(f.timers(), 0);
    f.api.update('a', 4, 'late'); f.tick(1000); assert.equal(f.payload(), 'disposed');
});
test('multi-tab conflict pauses stale writers and protects discard until explicit decision', () => {
    const f = fixture(); f.start('a'); f.start('b');
    f.api.update('a', 1, 'tab a'); f.tick(750);
    f.api.update('b', 1, 'tab b'); f.tick(750); assert.equal(f.payload(), 'tab a');
    assert.match(f.statuses.at(-1), /Another tab/); assert.equal(f.api.discard('b', 2), false);
    f.api.update('b', 3, 'explicit tab b', true); f.tick(750); assert.equal(f.payload(), 'explicit tab b');
    f.api.update('a', 2, 'stale tab a'); f.tick(750); assert.equal(f.payload(), 'explicit tab b');
});
