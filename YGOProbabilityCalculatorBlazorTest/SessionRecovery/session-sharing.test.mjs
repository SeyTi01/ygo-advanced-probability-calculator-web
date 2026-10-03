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
