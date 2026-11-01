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
