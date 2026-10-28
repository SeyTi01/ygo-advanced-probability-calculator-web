import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { buildLocalizedNames, retainNameSnapshot } from './prepare-localized-names.mjs';

const catalog = { version: 2, cards: [
    { id: 1, name: 'First', artworkImageIds: [1, 9] },
    { id: 2, name: 'Second', artworkImageIds: [2, 9] },
    { id: 3, name: 'Third', artworkImageIds: [3, 8] }
] };
const snapshot = cards => ({ version: 1, locale: 'de', cards });

test('ID-only joins retain distinct same-name cards and report missing/artwork/unknown IDs', () => {
    const source = snapshot([
        { id: 99, name: 'First', name_en: 'First' },
        { id: 9, name: 'Mehrdeutiges Bild', name_en: 'First' },
        { id: 8, name: 'Anderes Bild', name_en: 'Third' },
        { id: 2, name: 'Weißer Drache', name_en: 'Second' },
        { id: 1, name: 'Weißer Drache', name_en: 'FIRST' }
    ]);
    const result = buildLocalizedNames(source, catalog);
    assert.deepEqual(result.asset.names, [[1, 'Weißer Drache'], [2, 'Weißer Drache']]);
    assert.deepEqual(result.audit.missingIds, [3]);
    assert.deepEqual(result.audit.unexpectedIds, [8, 9, 99]);
    assert.deepEqual(result.audit.artworkOnlyIds, [{ id: 8, candidates: [3] }, { id: 9, candidates: [1, 2] }]);
    assert.deepEqual(result.audit.ambiguousNames, [{ name: 'Weißer Drache', ids: [1, 2] }]);
    assert.deepEqual(result.audit.englishNameDifferences, [{ id: 1, provider: 'FIRST', canonical: 'First' }]);
    assert.deepEqual(buildLocalizedNames(snapshot([...source.cards].reverse()), catalog), result);
});

test('malformed or duplicate identities cannot publish ambiguous labels', () => {
    const entry = { id: 1, name: 'Äther', name_en: 'First' };
    for (const cards of [[entry, entry], [{ ...entry, id: -1 }], [{ ...entry, name: '' }], [{ ...entry, name_en: null }]]) {
        assert.throws(() => buildLocalizedNames(snapshot(cards), catalog));
    }
});

test('retained source projects only original name/identity fields and hashes original bytes', () => {
    const bytes = Buffer.from(JSON.stringify({ data: [{ id: 1, name: 'Äther', name_en: 'First',
        desc: 'Do not retain', card_images: [{ id: 9, image_url: 'private.invalid' }], card_prices: [{}] }] }));
    const retained = retainNameSnapshot(bytes, '2026-10-28T00:00:00Z');
    assert.deepEqual(retained.cards, [{ id: 1, name: 'Äther', name_en: 'First' }]);
    assert.equal(retained.sourceBytes, bytes.length);
    assert.match(retained.sourceSha256, /^[a-f0-9]{64}$/);
});

test('shipped German names reproduce offline from retained provider names and canonical catalog', async () => {
    const read = async relative => JSON.parse(await readFile(new URL(relative, import.meta.url), 'utf8'));
    const retained = await read('./snapshots/names.de.2026-10-28.json');
    const canonical = await read('../../YGOProbabilityCalculatorBlazor/wwwroot/data/card-catalog.v1.json');
    const shipped = await read('../../YGOProbabilityCalculatorBlazor/wwwroot/data/card-names.de.v1.json');
    const result = buildLocalizedNames(retained, canonical);
    assert.deepEqual(result.asset, shipped);
    assert.equal(result.audit.returnedRecords, 11769);
    assert.equal(result.audit.matchedRecords, 11769);
    assert.equal(result.audit.missingIds.length, 2838);
    assert.deepEqual(result.audit.unexpectedIds, []);
    assert.deepEqual(result.audit.ambiguousNames, []);
});
