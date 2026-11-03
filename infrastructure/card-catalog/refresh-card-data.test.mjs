import assert from 'node:assert/strict';
import test from 'node:test';
import { buildRefresh, candidateAudit, refresh, dataRoot, auditRoot, stabilizeCanonicalIds, assertCurrentRefreshWorkflow } from './refresh-card-data.mjs';
import { mkdtemp, mkdir, readFile, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, dirname } from 'node:path';
import { createHash } from 'node:crypto';
import { buildCatalog } from './prepare-catalog.mjs';
import { createResolver } from './archetype-membership.mjs';

const card = (id, name, desc = 'Ordinary effect.') => ({ id, name, type: 'Effect Monster', desc });
function fixture() {
    const cards = [card(1, 'Lightsworn Example'), card(2, 'Solar Recharge', 'Draw 2 cards mentioning "Lightsworn".')];
    return { provider: { data: [...cards, card(3, 'Lightsworn Newcomer')] },
        yamlBytes: Buffer.from(JSON.stringify(cards.map(item => ({ password: item.id, name: { en: item.name, de: item.name } })))),
        manifest: { revision: 'a'.repeat(40), retrievedAt: '2026-11-01T00:00:00Z' },
        series: { Lightsworn: { de: 'Lichtverpflichtet' } }, vocabulary: [{ archetype_name: 'Lightsworn' }],
        previousCatalog: buildCatalog({ data: cards }), previousNames: { de: { version: 1, locale: 'de', names: [] } },
        example: { Cards: [] }, minimumCards: 1, minimumEligible: 1 };
}

const locales = ['de', 'ja', 'fr', 'it', 'pt'];
const label = (locale, id) => locale === 'ja' ? `名前${id}` : `${locale} name ${id}`;

test('old workflow reruns cannot combine current generation with stale publication steps', async () => {
    const env = { GITHUB_ACTIONS: 'true', GITHUB_WORKFLOW: 'Monthly card data refresh', GITHUB_SHA: 'a'.repeat(40) };
    const read = async () => 'current\n';
    await assertCurrentRefreshWorkflow(env, read, () => 'current\r\n');
    await assert.rejects(assertCurrentRefreshWorkflow(env, read, () => 'old German-only publication\n'), /new workflow_dispatch/);
    await assertCurrentRefreshWorkflow({}, () => { throw new Error('Local replay must not require GitHub'); });
});
function multilingualFixture() {
    const input = fixture();
    input.example = { Cards: [{ ExternalCardId: 1, Name: input.provider.data[0].name }] };
    input.yamlBytes = Buffer.from(JSON.stringify(input.provider.data.map(card => ({ password: card.id,
        name: { en: card.name, ...Object.fromEntries(locales.map(locale => [locale,
            locale === 'ja' && card.id === 1 ? '<ruby>名前<rt>なまえ</rt></ruby>1' : label(locale, card.id)])) } }))));
    input.previousNames = Object.fromEntries(locales.map(locale => [locale, { version: 1, locale,
        names: input.previousCatalog.cards.map(card => [card.id, card.id === 1 ? `${locale} previous` : label(locale, card.id)]) }]));
    return input;
}

test('new known-archetype cards classify offline without treating mentions as memberships', () => {
    const input = fixture();
    const result = buildRefresh(input);
    assert.deepEqual(result.catalog.cards.find(item => item.id === 3).resolvedArchetypes, ['Lightsworn']);
    assert.deepEqual(result.catalog.cards.find(item => item.id === 2).resolvedArchetypes, []);
    assert.deepEqual(result.audit.changes.map(item => item.id), [3]);
    assert.deepEqual(buildRefresh(input), result);
    assert.deepEqual(buildRefresh({ ...input, provider: { data: [...input.provider.data].reverse() } }), result);
});

test('unknown declarations remain unknown until a reviewed rule is supplied', () => {
    const input = fixture();
    input.provider.data.push(card(4, 'Unrelated', '(This card is always treated as a "New Family" card.)'));
    input.series['New Family'] = { de: 'Neue Familie' };
    const result = buildRefresh(input);
    assert.equal(result.catalog.cards.find(item => item.id === 4).resolvedArchetypes, null);
    assert.ok(result.audit.candidates.reviewRequired.includes('New Family'));
    assert.ok(result.audit.membership.unresolved.some(item => item.id === 4));
    const reviewed = createResolver({ version: 1, candidates: [{ name: 'New Family' }] });
    assert.deepEqual(reviewed(input.provider.data.at(-1)).archetypes, ['New Family']);
});

test('removals, duplicate IDs, partial responses and lost labels fail before publication', () => {
    const input = fixture();
    assert.throws(() => buildRefresh({ ...input, provider: { data: input.provider.data.slice(1) } }), /regression/);
    assert.throws(() => buildRefresh({ ...input, provider: { data: [...input.provider.data, input.provider.data[0]] } }), /duplicate/);
    assert.throws(() => buildRefresh({ ...input, minimumCards: 100 }), /incomplete/);
    assert.throws(() => buildRefresh({ ...input, previousNames: { de: { locale: 'de', names: [[99, 'Lost']] } } }), /regression/);
    assert.throws(() => buildRefresh({ ...input, yamlBytes: Buffer.from('[]') }), /Invalid/);
});

test('candidate changes carry source evidence without changing policy', () => {
    const result = candidateAudit([{ archetype_name: 'New Family' }], { 'New Family': { de: 'Name' } }, ['Old Family']);
    assert.deepEqual(result.added, ['New Family']);
    assert.deepEqual(result.removed, ['Old Family']);
    assert.deepEqual(result.evidence, [{ name: 'New Family', provider: true, yamlYugi: true }]);
    assert.throws(() => candidateAudit([{}], {}), /Invalid/);
});

test('provider primary-artwork changes preserve canonical IDs only through unique numeric ownership', () => {
    const previous = { cards: [card(1, 'Existing')] };
    const source = card(2, 'Existing');
    source.card_images = [{ id: 1 }, { id: 2 }];
    const result = stabilizeCanonicalIds({ data: [source] }, previous);
    assert.equal(result.provider.data[0].id, 1);
    assert.equal(result.identityChanges[0].providerId, 2);
    assert.throws(() => stabilizeCanonicalIds({ data: [source, { ...source, id: 3 }] }, previous), /Ambiguous/);
    assert.throws(() => stabilizeCanonicalIds({ data: [{ ...source, name: 'Different' }] }, previous), /Changed/);
    assert.equal(stabilizeCanonicalIds({ data: [{ ...source, card_images: [] }] }, previous).provider.data[0].id, 2);
});

test('released beta cards retain canonical IDs, official artwork and translated password joins', () => {
    const input = fixture();
    const released = { ...input.provider.data[0], id: 10, card_images: [{ id: 10 }], misc_info: [{ beta_id: 1 }] };
    input.provider.data = [released, input.provider.data[1]];
    input.yamlBytes = Buffer.from(JSON.stringify([{ password: 10, name: { en: released.name, de: 'Neuer Name' } }]));
    input.previousNames.de.names = [[1, 'Alter Name']];
    const result = buildRefresh(input);
    const retained = result.catalog.cards.find(item => item.id === 1);
    assert.equal(retained.canonicalCardId, 1);
    assert.deepEqual(retained.artworkImageIds, [10]);
    assert.ok(!result.catalog.cards.some(item => item.id === 10));
    assert.deepEqual(result.names.de.names, [[1, 'Neuer Name']]);
    assert.deepEqual(result.audit.identityChanges, [{ canonicalId: 1, providerId: 10, name: released.name, evidence: 'unique-beta-id' }]);
    assert.equal(released.id, 10);
    assert.deepEqual(buildRefresh({ ...input, provider: { data: [...input.provider.data].reverse() } }), result);
});

test('beta links reject ambiguous owners, changed identities and many-to-one remaps', () => {
    const previous = { cards: [card(1, 'Existing')] };
    const source = { ...card(10, 'Existing'), misc_info: [{ beta_id: 1 }] };
    assert.throws(() => stabilizeCanonicalIds({ data: [source, { ...source, id: 20 }] }, previous), /Ambiguous/);
    assert.throws(() => stabilizeCanonicalIds({ data: [source, { ...card(20, 'Existing'), card_images: [{ id: 1 }] }] }, previous), /Ambiguous/);
    for (const changed of [{ name: 'Different' }, { type: 'Spell Card' }]) {
        assert.throws(() => stabilizeCanonicalIds({ data: [{ ...source, ...changed }] }, previous), /Changed/);
    }
    assert.throws(() => stabilizeCanonicalIds({ data: [{ ...source, misc_info: [{ beta_id: 1 }, { beta_id: 2 }] }] },
        { cards: [card(1, 'Existing'), card(2, 'Existing')] }), /Changed/);
});

test('beta support never accepts name-only matches, nonnumeric links or newly excluded cards', () => {
    const input = fixture();
    const source = { ...input.provider.data[0], id: 10 };
    for (const misc_info of [[], [{ beta_id: '1' }], [{ beta_id: 99 }], [{ beta_name: source.name }],
        [{ beta_id: 1, formats: ['Speed Duel'] }]]) {
        assert.throws(() => buildRefresh({ ...input, provider: { data: [{ ...source, misc_info }, ...input.provider.data.slice(1)] } }), /Canonical coverage regression: 1/);
    }
});

test('every locale refreshes from the pinned source with its own changes, coverage and normalization evidence', () => {
    const input = multilingualFixture();
    const result = buildRefresh(input);
    for (const locale of locales) {
        assert.deepEqual(result.names[locale].names, [1, 2, 3].map(id => [id, label(locale, id)]));
        assert.equal(result.snapshots[locale].revision, input.manifest.revision);
        assert.equal(result.snapshots[locale].locale, locale);
        assert.equal(result.audit.localizedNames[locale].example.matched, 1);
        assert.deepEqual(result.audit.nameChanges[locale].map(change => change.id), [1, 3]);
    }
    assert.equal(result.snapshots.ja.cards[0].name, '<ruby>名前<rt>なまえ</rt></ruby>1');
    assert.equal(result.audit.localizedNames.ja.normalizedNames[0].name, '名前1');
    assert.deepEqual(buildRefresh({ ...input, provider: { data: [...input.provider.data].reverse() } }), result);
});

test('every locale rejects lost names and coverage-ratio regressions independently', () => {
    for (const locale of locales) {
        const input = multilingualFixture();
        const source = JSON.parse(input.yamlBytes);
        delete source[0].name[locale];
        assert.throws(() => buildRefresh({ ...input, yamlBytes: Buffer.from(JSON.stringify(source)) }),
            new RegExp(`${locale}: Localized name coverage regression: 1`));
        source[0].name[locale] = label(locale, 1);
        delete source[2].name[locale];
        assert.throws(() => buildRefresh({ ...input, previousNames: { [locale]: input.previousNames[locale] },
            yamlBytes: Buffer.from(JSON.stringify(source)) }), new RegExp(`${locale}: coverage ratio regression`));
    }
});

test('multilingual offline replay handles later refreshes, locale-only changes and validation failures before writing', async () => {
    const root = await mkdtemp(join(tmpdir(), 'card-refresh-'));
    try {
        const input = multilingualFixture();
        const sources = join(root, 'sources');
        await mkdir(sources);
        const put = async (path, value) => { await mkdir(dirname(path), { recursive: true }); await writeFile(path, typeof value === 'string' ? value : JSON.stringify(value)); };
        await put(join(root, dataRoot, 'card-catalog.v1.json'), input.previousCatalog);
        for (const [locale, names] of Object.entries(input.previousNames)) {
            await put(join(root, dataRoot, `card-names.${locale}.v1.json`), names);
        }
        await put(join(root, 'YGOProbabilityCalculatorBlazor/wwwroot/sample-data/example_session_state.json'), input.example);
        await put(join(root, 'YGOProbabilityCalculatorBlazor/wwwroot/localization/locales.json'),
            [{ id: 'en' }, { id: 'es', name: 'Español' },
                ...locales.map(id => ({ id, cardNames: { asset: `data/card-names.${id}.v1.json` } }))]);
        const historicalPath = join(root, 'infrastructure/card-catalog/snapshots/historical.audit.json');
        await put(historicalPath, 'preserve historical evidence');
        const values = { 'cardinfo.json': input.provider, 'archetypes.json': input.vocabulary,
            'yaml-cards.json': JSON.parse(input.yamlBytes), 'series.json': input.series, 'yaml-yugi.tar.gz': 'fixture' };
        const manifest = { ...input.manifest, version: 1 };
        const saveSources = async () => {
            manifest.sources = [];
            for (const [file, value] of Object.entries(values)) {
                await put(join(sources, file), value);
                const bytes = await readFile(join(sources, file));
                manifest.sources.push({ file, bytes: bytes.length, sha256: createHash('sha256').update(bytes).digest('hex') });
            }
            await put(join(sources, 'sources.json'), manifest);
        };
        await saveSources();
        assert.equal(await refresh(sources, root, 1, 1), true);
        await assert.rejects(readFile(join(root, dataRoot, 'card-names.es.v1.json')),
            { code: 'ENOENT' }, 'UI-only locales do not trigger card-name asset generation');
        const protectedFiles = [`${dataRoot}/card-catalog.v1.json`,
            ...locales.flatMap(locale => [`${dataRoot}/card-names.${locale}.v1.json`, `${auditRoot}/names.${locale}.json`]),
            `${auditRoot}/audit.json`];
        const outputs = () => Promise.all(protectedFiles.map(path => readFile(join(root, path), 'utf8')));
        let acceptedFiles = await outputs();
        manifest.retrievedAt = '2026-12-01T00:00:00Z';
        await saveSources();
        assert.equal(await refresh(sources, root, 1, 1), false);
        assert.deepEqual(await outputs(), acceptedFiles);
        // Reproduce an incomplete commit: current fingerprint/snapshots but stale shipped names.
        for (const locale of locales) {
            await put(join(root, dataRoot, `card-names.${locale}.v1.json`), input.previousNames[locale]);
            assert.equal(await refresh(sources, root, 1, 1), true, `${locale}: repairs stale output despite matching fingerprint`);
            assert.deepEqual(JSON.parse(await readFile(join(root, dataRoot, `card-names.${locale}.v1.json`))).names,
                [1, 2, 3].map(id => [id, label(locale, id)]));
            assert.equal(await refresh(sources, root, 1, 1), false);
        }
        await rm(join(root, auditRoot, 'names.pt.json'));
        assert.equal(await refresh(sources, root, 1, 1), true, 'repairs a missing retained projection');
        assert.equal(await refresh(sources, root, 1, 1), false);
        const german = await readFile(join(root, dataRoot, 'card-names.de.v1.json'), 'utf8');
        // Only a non-German label changes: it must still publish the complete bundle.
        values['yaml-cards.json'][0].name.fr = 'Nom corrigé';
        await saveSources();
        assert.equal(await refresh(sources, root, 1, 1), true);
        assert.equal(await readFile(join(root, dataRoot, 'card-names.de.v1.json'), 'utf8'), german);
        let audit = JSON.parse(await readFile(join(root, auditRoot, 'audit.json'), 'utf8'));
        assert.deepEqual(audit.nameChanges.fr, [{ id: 1, before: 'fr name 1', after: 'Nom corrigé' }]);
        for (const locale of locales) {
            assert.equal(audit.localizedNames[locale].canonicalRecords, 3);
            assert.deepEqual(audit.localizedNames[locale].missingIds, []);
            assert.equal(JSON.parse(await readFile(join(root, auditRoot, `names.${locale}.json`))).revision, manifest.revision);
        }
        assert.equal(await refresh(sources, root, 1, 1), false);
        // A later month adds a new card and translations in every registered locale.
        values['cardinfo.json'].data.push(card(4, 'Later card'));
        values['yaml-cards.json'].push({ password: 4, name: { en: 'Later card', ...Object.fromEntries(locales.map(locale => [locale, label(locale, 4)])) } });
        manifest.revision = 'b'.repeat(40);
        manifest.retrievedAt = '2027-01-01T00:00:00Z';
        await saveSources();
        assert.equal(await refresh(sources, root, 1, 1), true);
        audit = JSON.parse(await readFile(join(root, auditRoot, 'audit.json'), 'utf8'));
        for (const locale of locales) {
            assert.equal(audit.localizedNames[locale].canonicalRecords, 4);
            assert.deepEqual(audit.nameChanges[locale], [{ id: 4, before: null, after: label(locale, 4) }]);
        }
        assert.equal(await refresh(sources, root, 1, 1), false);
        acceptedFiles = await outputs();
        // Even the last locale failing must leave every accepted output intact.
        delete values['yaml-cards.json'][0].name.pt;
        await saveSources();
        await assert.rejects(refresh(sources, root, 1, 1), /pt: Localized name coverage regression: 1/);
        assert.deepEqual(await outputs(), acceptedFiles);
        values['yaml-cards.json'][0].name.pt = label('pt', 1);
        await saveSources();
        await put(join(sources, 'cardinfo.json'), '{}');
        await assert.rejects(refresh(sources, root, 1, 1), /hash mismatch/);
        assert.deepEqual(await outputs(), acceptedFiles);
        assert.equal(await readFile(historicalPath, 'utf8'), 'preserve historical evidence');
    } finally { await rm(root, { recursive: true, force: true }); }
});
