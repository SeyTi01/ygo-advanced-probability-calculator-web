import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';
const source = readFileSync(new URL('../../YGOProbabilityCalculatorBlazor/wwwroot/js/session-sharing.js', import.meta.url), 'utf8');
const outboundWrite = 'await globalThis.navigator.clipboard.writeText(text);';
function isolatedProbe(sourceText = source) {
    const counters = { clipboardReads: 0, navigationMutations: 0 };
    const denied = () => { counters.navigationMutations++; throw Error('Unexpected inbound navigation'); };
    let clipboard = 'unrelated synthetic text', focused = 0, selected = 0;
    const listeners = new Map();
    const register = (name, callback) => {
        const callbacks = listeners.get(name) ?? [];
        callbacks.push(callback);
        listeners.set(name, callbacks);
    };
    const initialHref = 'https://dev.example.test/sub/#ordinary-anchor';
    const history = {
        length: 1,
        pushState: denied, replaceState: denied, back: denied, forward: denied, go: denied
    };
    const context = vm.createContext({
        isSecureContext: true,
        navigator: { clipboard: {
            read: () => { counters.clipboardReads++; throw Error('Unexpected clipboard read'); },
            readText: () => { counters.clipboardReads++; throw Error('Unexpected clipboard read'); },
            writeText: async value => { clipboard = value; }
        } },
        location: {
            get href() { return initialHref; },
            get hash() { return '#ordinary-anchor'; },
            set href(_) { denied(); }, set hash(_) { denied(); }, assign: denied, replace: denied
        },
        history,
        addEventListener: register,
        document: { addEventListener: register, getElementById: () => ({ focus: () => focused++, select: () => selected++ }) }
    });
    return {
        context,
        counters,
        initialHref,
        initialHistoryLength: history.length,
        clipboard: () => clipboard,
        selection: () => ({ focused, selected }),
        dispatch: name => { for (const callback of listeners.get(name) ?? []) callback(); }
    };
}
function assertOutboundIsolation(probe) {
    assert.equal(probe.counters.clipboardReads, 0, 'outbound sharing must not read the clipboard');
    assert.equal(probe.counters.navigationMutations, 0, 'outbound sharing must not mutate URL or history');
    assert.equal(probe.context.location.href, probe.initialHref);
    assert.equal(probe.context.history.length, probe.initialHistoryLength);
}
test('clipboard receives exact fragment text; denial and missing activation return false', async () => {
    const text = 'https://dev.example.test/sub/#ygo-session=v1.fixture';
    let received;
    const context = vm.createContext({ isSecureContext: true, navigator: { clipboard: { writeText: value => { received = value; return Promise.resolve(); } } } });
    vm.runInContext(source, context);
    assert.equal(await context.sessionSharing.copy(text), true);
    assert.equal(received, text);
    context.navigator.clipboard.writeText = () => Promise.reject(Error('NotAllowedError'));
    assert.equal(await context.sessionSharing.copy(text), false);
    context.isSecureContext = false;
    assert.equal(await context.sessionSharing.copy(text), false);
    delete context.navigator.clipboard;
    assert.equal(await context.sessionSharing.copy(text), false);
});
test('manual fallback focuses and selects its generated link', () => {
    let focused = 0, selected = 0;
    const context = vm.createContext({ document: { getElementById: id => id === 'link' ? { focus: () => focused++, select: () => selected++ } : null } });
    vm.runInContext(source, context);
    context.sessionSharing.select('link'); context.sessionSharing.select('missing');
    assert.equal(focused, 1); assert.equal(selected, 1);
});

test('copy and fallback write exact text without reading the clipboard or mutating URL/history', async () => {
    const text = 'https://dev.example.test/sub/#ygo-session=v1.synthetic';
    const probe = isolatedProbe();
    vm.runInContext(source, probe.context);
    for (let i = 0; i < 2; i++) assert.equal(await probe.context.sessionSharing.copy(text), true);
    assert.equal(probe.clipboard(), text);
    assertOutboundIsolation(probe);

    // Harmless listeners remain allowed; the helper contract concerns their effects.
    probe.context.addEventListener('focus', () => {});
    probe.context.document.addEventListener('visibilitychange', () => {});
    probe.dispatch('focus');
    probe.dispatch('visibilitychange');
    probe.context.navigator.clipboard.writeText = async () => { throw Error('NotAllowedError'); };
    assert.equal(await probe.context.sessionSharing.copy(text), false);
    assertOutboundIsolation(probe);
    probe.context.sessionSharing.select('fallback');
    assert.deepEqual(probe.selection(), { focused: 1, selected: 1 });
    assertOutboundIsolation(probe);
});

test('delayed outbound writes only finish their own copy result', async () => {
    let resolve;
    let reads = 0;
    const context = vm.createContext({
        isSecureContext: true,
        navigator: { clipboard: {
            readText: () => { reads++; throw Error('Unexpected clipboard read'); },
            writeText: () => new Promise(done => { resolve = done; })
        } }
    });
    vm.runInContext(source, context);
    const copy = context.sessionSharing.copy('https://dev.example.test/#ygo-session=v1.synthetic');
    assert.equal(reads, 0);
    resolve();
    assert.equal(await copy, true);
    assert.equal(reads, 0);
});

test('the read guard catches swallowed reads without rejecting harmless listeners', async () => {
    const mutatedSource = source.replace(outboundWrite,
        'try { globalThis.navigator.clipboard.readText(); } catch { /* simulate a swallowed read */ }\n        ' + outboundWrite);
    assert.notEqual(mutatedSource, source, 'the sensitivity mutation must alter the expected call site');
    const probe = isolatedProbe();
    vm.runInContext(mutatedSource, probe.context);
    probe.context.addEventListener('focus', () => {});
    assert.equal(await probe.context.sessionSharing.copy('synthetic link'), true);
    assert.equal(probe.counters.clipboardReads, 1);
    assert.throws(() => assertOutboundIsolation(probe), assert.AssertionError);

    const harmless = isolatedProbe();
    vm.runInContext(source, harmless.context);
    harmless.context.addEventListener('focus', () => {});
    assert.equal(await harmless.context.sessionSharing.copy('synthetic link'), true);
    assertOutboundIsolation(harmless);
});
