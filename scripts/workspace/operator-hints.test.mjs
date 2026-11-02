import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';

const code = await readFile(new URL('../../YGOProbabilityCalculatorBlazor/wwwroot/js/operator-hints.mjs', import.meta.url), 'utf8');

function harness() {
    const handlers = new Map();
    const bubbles = [];
    const timers = new Map();
    let timerId = 0;
    class Element {
        dataset = {};
        style = {};
        isConnected = true;
        closest() { return this.dataset.operatorHint ? this : null; }
        contains(target) { return target === this; }
        cloneNode() { return new Element(); }
        removeAttribute() {}
        setAttribute() {}
        addEventListener(name, handler) { this[name] = handler; }
        getBoundingClientRect() { return { left: 360, top: 100, bottom: 132, width: 180, height: 36 }; }
        remove() { bubbles.splice(bubbles.indexOf(this), 1); }
    }
    const trigger = new Element();
    trigger.dataset.operatorHint = 'hint';
    const document = {
        activeElement: null,
        body: { append: bubble => bubbles.push(bubble) },
        getElementById: () => new Element(),
        addEventListener: (name, handler) => handlers.set(name, handler)
    };
    vm.runInNewContext(code, {
        Element, document,
        window: { innerWidth: 390, innerHeight: 844, addEventListener() {} },
        setTimeout: callback => { timers.set(++timerId, callback); return timerId; },
        clearTimeout: id => timers.delete(id), requestAnimationFrame: callback => callback()
    });
    return {
        trigger, bubbles, document,
        outside: new Element(),
        event: (name, args = {}) => handlers.get(name)({ target: trigger, ...args }),
        flush: () => { for (const callback of timers.values()) callback(); timers.clear(); }
    };
}

test('keyboard focus shows a viewport-clamped hint; Escape dismisses without reaching the editor', () => {
    const h = harness();
    h.document.activeElement = h.trigger;
    h.event('focusin');
    assert.equal(h.bubbles.length, 1);
    assert.equal(h.bubbles[0].style.left, '202px');
    h.event('scroll');
    assert.equal(h.bubbles.length, 1, 'focus-induced scrolling must not discard the hint');
    let stopped = false;
    h.event('keydown', { key: 'Escape', stopPropagation: () => stopped = true, preventDefault() {} });
    assert.equal(h.bubbles.length, 0);
    assert.equal(stopped, true);
});

test('touch retains the hint through automatic editor focus; an outside tap dismisses it', () => {
    const h = harness();
    h.event('pointerdown', { pointerType: 'touch' });
    h.event('focusout');
    h.event('focusin', { target: h.outside });
    h.flush();
    assert.equal(h.bubbles.length, 1);
    h.event('pointerdown', { pointerType: 'touch', target: h.outside });
    assert.equal(h.bubbles.length, 0);
});

test('hover remains available over the hint and closes after leaving it', () => {
    const h = harness();
    h.event('pointerover', { pointerType: 'mouse' });
    h.event('pointerout');
    h.bubbles[0].pointerenter();
    h.flush();
    assert.equal(h.bubbles.length, 1);
    h.bubbles[0].pointerleave();
    h.flush();
    assert.equal(h.bubbles.length, 0);
});
