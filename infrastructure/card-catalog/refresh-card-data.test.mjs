import assert from 'node:assert/strict';
import test from 'node:test';
import { buildRefresh, candidateAudit, refresh, dataRoot, auditRoot, stabilizeCanonicalIds } from './refresh-card-data.mjs';
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
        previousCatalog: buildCatalog({ data: cards }), previousNames: { version: 1, locale: 'de', names: [] },
        example: { Cards: [] }, minimumCards: 1, minimumEligible: 1 };
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
    assert.throws(() => buildRefresh({ ...input, previousNames: { names: [[99, 'Lost']] } }), /regression/);
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
    input.previousNames.names = [[1, 'Alter Name']];
    const result = buildRefresh(input);
    const retained = result.catalog.cards.find(item => item.id === 1);
    assert.equal(retained.canonicalCardId, 1);
    assert.deepEqual(retained.artworkImageIds, [10]);
    assert.ok(!result.catalog.cards.some(item => item.id === 10));
    assert.deepEqual(result.names.names, [[1, 'Neuer Name']]);
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

test('offline replay is a no-op despite retrieval time changes; corrupted inputs preserve accepted files', async () => {
    const root = await mkdtemp(join(tmpdir(), 'card-refresh-'));
    try {
        const input = fixture();
        const sources = join(root, 'sources');
        await mkdir(sources);
        const put = async (path, value) => { await mkdir(dirname(path), { recursive: true }); await writeFile(path, typeof value === 'string' ? value : JSON.stringify(value)); };
        await put(join(root, dataRoot, 'card-catalog.v1.json'), input.previousCatalog);
        await put(join(root, dataRoot, 'card-names.de.v1.json'), input.previousNames);
        await put(join(root, 'YGOProbabilityCalculatorBlazor/wwwroot/sample-data/example_session_state.json'), input.example);
        const entries = [];
        for (const [file, value] of [['cardinfo.json', input.provider], ['archetypes.json', input.vocabulary], ['yaml-cards.json', JSON.parse(input.yamlBytes)], ['series.json', input.series], ['yaml-yugi.tar.gz', 'fixture']]) {
            await put(join(sources, file), value);
            const bytes = await readFile(join(sources, file));
            entries.push({ file, bytes: bytes.length, sha256: createHash('sha256').update(bytes).digest('hex') });
        }
        const manifest = { ...input.manifest, version: 1, sources: entries };
        await put(join(sources, 'sources.json'), manifest);
        assert.equal(await refresh(sources, root, 1, 1), true);
        const accepted = await readFile(join(root, auditRoot, 'audit.json'), 'utf8');
        await put(join(sources, 'sources.json'), { ...manifest, retrievedAt: '2026-12-01T00:00:00Z' });
        assert.equal(await refresh(sources, root, 1, 1), false);
        await put(join(sources, 'cardinfo.json'), '{}');
        await assert.rejects(refresh(sources, root, 1, 1), /hash mismatch/);
        assert.equal(await readFile(join(root, auditRoot, 'audit.json'), 'utf8'), accepted);
    } finally { await rm(root, { recursive: true, force: true }); }
});
