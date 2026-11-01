import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import test from 'node:test';

const source = fs.readFileSync(new URL('../../YGOProbabilityCalculatorBlazor/wwwroot/js/locale.js', import.meta.url), 'utf8');
const preference = 'ygo-calculator:language:v1';
const handoff = 'ygo-calculator:language-handoff:v1';
const available = [{ id: 'en', name: 'English' }, { id: 'de', name: 'Test Deutsch' }, { id: 'fr', name: 'Test français' }];
async function host({ saved, languages = ['de-DE'], local = new Map(), tab = new Map(), navigation = 'navigate', fail, silent, registry = available } = {}) {
    if (saved !== undefined) local.set(preference, saved);
    const classes = new Set();
    let reloads = 0;
    const storage = (map, kind) => ({
        getItem: key => map.get(key) ?? null,
        setItem: (key, value) => {
            if (fail === kind) throw new Error('Storage denied');
            if (silent !== kind) map.set(key, value);
        },
        removeItem: key => map.delete(key)
    });
    const root = {
        fetch: async () => ({ ok: true, json: async () => registry }),
        document: { querySelector: () => null, documentElement: { classList: { add: value => classes.add(value), remove: value => classes.delete(value) } } },
        localStorage: storage(local, 'local'), sessionStorage: storage(tab, 'tab'),
        navigator: { languages }, location: { href: 'https://example.test/#ygo-session=sentinel', reload: () => reloads++ },
        performance: { getEntriesByType: () => [{ type: navigation }] }, crypto: { randomUUID: () => 'handoff-id' }
    };
    vm.runInNewContext(source, root);
    await root.ygoLocale.ready;
    return { document: root.document, api: root.ygoLocale, local, tab, classes, reloads: () => reloads };
}

test('saved English overrides a German browser, including a fresh browser context', async () => {
    const first = await host({ saved: 'en' });
    assert.equal(first.api.getStartup().locale, 'en');
    assert.equal((await host({ local: first.local })).api.getStartup().locale, 'en');
    assert.equal(first.api.getStartup().numericCulture, 'de-DE');
});

test('regional preferences, invalid/stale overrides, browser priority and English fallback', async () => {
    assert.equal((await host({ saved: '  FR-ca  ' })).api.getStartup().locale, 'fr');
    assert.equal((await host({ saved: 'missing' })).api.getStartup().locale, 'de');
    assert.equal((await host({ languages: ['xx', 'fr-CH', 'de'] })).api.getStartup().locale, 'fr');
    assert.equal((await host({ saved: '__proto__', languages: ['unknown'] })).api.getStartup().locale, 'en');
    assert.equal((await host({ languages: [] })).api.getStartup().locale, 'en');
});

test('preference and tab handoff are verified before exactly one reload', async () => {
    const first = await host({ saved: 'de' });
    first.local.set('ygo-calculator:session-recovery:v1', 'other tab recovery');
    first.api.change('en', 'accepted workspace');
    assert.equal(first.local.get(preference), 'en');
    assert.equal(first.local.get('ygo-calculator:session-recovery:v1'), 'other tab recovery');
    assert.equal(first.reloads(), 1);
    const second = await host({ local: first.local, tab: first.tab, navigation: 'reload' });
    assert.equal(second.api.getHandoff().workspace, 'accepted workspace');
    assert.equal(second.classes.has('locale-pending'), true);
    second.api.complete('handoff-id');
    assert.equal(second.tab.has(handoff), false);
    assert.equal((await host({ local: first.local, tab: first.tab, navigation: 'reload' })).api.getHandoff(), null);
});

test('unrelated navigation and repeated reloads cannot replay an unconfirmed handoff', async () => {
    const first = await host();
    first.api.change('fr', 'accepted workspace');
    const unrelated = await host({ local: first.local, tab: first.tab });
    assert.equal(unrelated.api.getHandoff(), null);
    assert.equal(first.tab.has(handoff), true);
    const next = await host({ local: first.local, tab: first.tab, navigation: 'reload' });
    assert.equal(next.api.getHandoff(), null);
    const retry = await host({ local: first.local, tab: first.tab, navigation: 'reload' });
    assert.equal(retry.api.getHandoff(), null);
    assert.throws(() => retry.api.change('en', 'accepted workspace'), /previous language handoff/);
});

for (const kind of ['local', 'tab']) {
    for (const mode of ['fail', 'silent']) {
        test(`${mode} ${kind} storage aborts before navigation and keeps live state`, async () => {
            const current = await host({ saved: 'de', [mode]: kind });
            assert.throws(() => current.api.change('en', 'accepted workspace'));
            assert.equal(current.reloads(), 0);
            assert.equal(current.local.get(preference), 'de');
        });
    }
}

test('a preexisting handoff is never overwritten', async () => {
    const current = await host();
    current.tab.set(handoff, 'recoverable earlier record');
    assert.throws(() => current.api.change('fr', 'accepted workspace'), /previous language handoff/);
    assert.equal(current.tab.get(handoff), 'recoverable earlier record');
    assert.equal(current.reloads(), 0);
});

for (const kind of ['open form', 'nonempty draft', 'incomplete number']) {
    test(kind + ' blocks before storage or navigation', async () => {
        const current = await host({ saved: 'en' });
        current.document.querySelector = () => ({
            querySelector: () => kind === 'open form' ? {} : null,
            querySelectorAll: selector => selector.includes('#quickCardName')
                ? (kind === 'nonempty draft' ? [{ value: 'Unsubmitted' }] : [])
                : (kind === 'incomplete number' ? [{ value: '', validity: { valid: true } }] : [])
        });
        assert.throws(() => current.api.change('fr', 'accepted workspace'), /Save\/apply or cancel/);
        assert.equal(current.local.get(preference), 'en');
        assert.equal(current.tab.has(handoff), false);
        assert.equal(current.reloads(), 0);
    });
}

test('malformed, expired, mismatched or claimed checkpoints are kept without replay', async () => {
    const first = await host();
    first.api.change('en', 'accepted workspace');
    const valid = JSON.parse(first.tab.get(handoff));
    for (const raw of ['broken JSON', JSON.stringify({ ...valid, created: 0 }),
        JSON.stringify({ ...valid, url: 'https://example.test/other' }), JSON.stringify({ ...valid, locale: 'fr' }),
        JSON.stringify({ ...valid, workspace: {} }), JSON.stringify({ ...valid, claimed: true })]) {
        const tab = new Map([[handoff, raw]]);
        const next = await host({ saved: 'en', tab, navigation: 'reload' });
        assert.equal(next.api.getHandoff(), null);
        assert.equal(next.api.hasHandoff(), true);
        assert.equal(next.reloads(), 0);
    }
});

const production = JSON.parse(fs.readFileSync(new URL('../../YGOProbabilityCalculatorBlazor/wwwroot/localization/locales.json', import.meta.url), 'utf8'));
for (const locale of production) {
    test('production startup resolves and persists ' + locale.id + ' independently of numeric culture', async () => {
        const regional = locale.id + '-ZZ';
        const first = await host({ registry: production, languages: [regional] });
        assert.equal(first.api.getStartup().locale, locale.id);
        assert.equal(first.api.getStartup().numericCulture, regional);
        const saved = await host({ registry: production, saved: locale.id, languages: ['es-ES'] });
        assert.equal(saved.api.getStartup().locale, locale.id);
        assert.equal(saved.api.getStartup().numericCulture, 'es-ES');
        assert.equal((await host({ registry: production, saved: 'en', languages: [regional] })).api.getStartup().locale, 'en');
    });
}
