import assert from 'node:assert/strict';
import fs from 'node:fs';
import test from 'node:test';
import vm from 'node:vm';

const source = fs.readFileSync(new URL('../../YGOProbabilityCalculatorBlazor/wwwroot/js/session-loading.js', import.meta.url), 'utf8');

function harness() {
    const timers = new Map();
    const frames = new Map();
    let id = 0;
    const window = {};
    vm.runInNewContext(source, {
        window,
        setTimeout: (callback, delay) => { timers.set(++id, { callback, delay }); return id; },
        clearTimeout: key => timers.delete(key),
        requestAnimationFrame: callback => { frames.set(++id, callback); return id; },
        cancelAnimationFrame: key => frames.delete(key)
    });
    return { window, timers, frames };
}

test('paint boundary settles in a task after the frame and cleans up its fallback', async () => {
    const { window, timers, frames } = harness();
    let completed = false;
    const pending = window.sessionLoading.beforeWork().then(() => { completed = true; });
    frames.values().next().value();
    assert.equal(completed, false);
    [...timers.values()].find(timer => timer.delay === 0).callback();
    await pending;
    assert.equal(completed, true);
    assert.equal(timers.size, 0);
    assert.equal(frames.size, 0);
});

test('inactive-tab fallback settles without a frame and leaves no scheduled work', async () => {
    const { window, timers, frames } = harness();
    const pending = window.sessionLoading.beforeWork();
    [...timers.values()].find(timer => timer.delay === 100).callback();
    await pending;
    assert.equal(timers.size, 0);
    assert.equal(frames.size, 0);
});

test('concurrent paint boundaries settle and clean up independently', async () => {
    const { window, timers, frames } = harness();
    const first = window.sessionLoading.beforeWork();
    const second = window.sessionLoading.beforeWork();
    [...timers.values()][0].callback();
    await first;
    assert.equal(frames.size, 1);
    frames.values().next().value();
    [...timers.values()].find(timer => timer.delay === 0).callback();
    await second;
    assert.equal(timers.size, 0);
    assert.equal(frames.size, 0);
});
