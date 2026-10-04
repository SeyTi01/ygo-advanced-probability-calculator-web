import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';
const source = readFileSync(new URL('../../YGOProbabilityCalculatorBlazor/wwwroot/js/session-sharing.js', import.meta.url), 'utf8');
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

test('copy and fallback never read clipboard, register focus listeners, or mutate URL/history', async () => {
    const text = 'https://dev.example.test/sub/#ygo-session=v1.synthetic';
    let clipboard = text, reads = 0, writes = 0, focused = 0, selected = 0, inboundTouches = 0;
    const forbidden = () => { inboundTouches++; throw Error('Outbound sharing touched inbound navigation'); };
    const context = vm.createContext({
        isSecureContext: true,
        navigator: { clipboard: {
            read: () => { reads++; throw Error('Unexpected clipboard read'); },
            readText: () => { reads++; throw Error('Unexpected clipboard read'); },
            writeText: async value => { writes++; clipboard = value; }
        } },
        location: {
            get href() { return 'https://dev.example.test/sub/#ordinary-anchor'; }, set href(_) { forbidden(); },
            set hash(_) { forbidden(); }, assign: forbidden, replace: forbidden
        },
        history: { pushState: forbidden, replaceState: forbidden },
        addEventListener: forbidden,
        document: {
            addEventListener: forbidden,
            getElementById: () => ({ focus: () => focused++, select: () => selected++ })
        }
    });
    vm.runInContext(source, context);
    for (let i = 0; i < 2; i++) assert.equal(await context.sessionSharing.copy(text), true);
    assert.equal(clipboard, text);
    assert.equal(writes, 2);
    context.navigator.clipboard.writeText = async () => { throw Error('NotAllowedError'); };
    assert.equal(await context.sessionSharing.copy(text), false);
    context.sessionSharing.select('fallback');
    assert.equal(focused, 1);
    assert.equal(selected, 1);
    assert.equal(reads, 0);
    assert.equal(inboundTouches, 0);
    assert.equal(context.location.href, 'https://dev.example.test/sub/#ordinary-anchor');
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
