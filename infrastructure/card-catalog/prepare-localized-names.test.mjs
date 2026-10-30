import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile, mkdtemp, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { buildLocalizedNames, retainNameSnapshot, retainYamlYugiSnapshot, compareYamlYugi,
    exampleNameCoverage, assertNoNameRegression } from './prepare-localized-names.mjs';

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
    for (const cards of [[entry, entry], [{ ...entry, id: -1 }], [{ ...entry, name: '' }],
        [{ ...entry, name: '<b>Äther</b>' }], [{ ...entry, name: 'Äther<br>' }],
        [{ ...entry, name: 'Äther &amp; mehr' }], [{ ...entry, name: 'Äther\n' }], [{ ...entry, name_en: null }]]) {
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
    const audit = await read('./snapshots/names.de.2026-10-28.audit.json');
    const { canonicalSha256, sourceSha256, ...generated } = audit;
    assert.deepEqual(result.audit, generated);
    assert.equal(result.audit.matchedRecords + result.audit.missingIds.length, canonical.cards.length);
    assert.deepEqual(result.audit.unexpectedIds, []);
    assert.deepEqual(result.audit.ambiguousNames, []);
    const example = await read('../../YGOProbabilityCalculatorBlazor/wwwroot/sample-data/example_session_state.json');
    const coverage = exampleNameCoverage(shipped, canonical, example);
    assert.equal(coverage.entries, example.Cards.length);
    for (const entry of coverage.cards) {
        assert.equal(entry.name, new Map(shipped.names).get(entry.id) ?? null);
    }
    assert.equal(coverage.matched + coverage.unresolved.length, coverage.entries);
});

const revision = 'a'.repeat(40);
const retrievedAt = '2026-10-29T00:00:00Z';
const yamlSnapshot = cards => retainYamlYugiSnapshot(Buffer.from(JSON.stringify(cards)), revision, retrievedAt);
const example = { Cards: [
    { ExternalCardId: 1, Name: 'First' }, { ExternalCardId: 2, Name: 'Second' },
    { ExternalCardId: 3, Name: 'Third' }, { ExternalCardId: 1, Name: 'My label' }
] };

test('additional-source comparison keeps primary names and joins only exact passcodes', () => {
    const primary = snapshot([{ id: 1, name: 'Beibehalten', name_en: 'First' }]);
    const records = [
        { password: 1, name: { en: 'First', de: 'Abweichung' }, text: { de: 'Do not retain' } },
        { password: 2, name: { en: 'Second', de: 'Second' } },
        { password: 3, name: { en: 'Third', de: 'Beibehalten' } },
        { password: 8, name: { en: 'Third', de: 'Falsches Bild' } },
        { password: 99, name: { en: 'First', de: 'Unbekannt' } },
        { konami_id: 1, name: { en: 'First', de: 'Keine Passnummer' } },
        { password: 7, name: { en: 'No German' } }
    ];
    const candidate = yamlSnapshot(records);
    assert.deepEqual(Object.keys(candidate.cards[0]), ['id', 'name', 'name_en']);
    assert.equal(candidate.revision, revision);
    assert.match(candidate.sourceSha256, /^[a-f0-9]{64}$/);
    assert.equal(JSON.stringify(candidate).includes('Do not retain'), false);
    const audit = compareYamlYugi(primary, candidate, catalog, example);
    assert.deepEqual(audit.disagreements, [{ id: 1, retained: 'Beibehalten', candidate: 'Abweichung', decision: 'retain-ygoprodeck' }]);
    assert.deepEqual(audit.addedIds, [2, 3]);
    assert.deepEqual(audit.candidate.unexpectedIds, [8, 99]);
    assert.deepEqual(audit.candidate.artworkOnlyIds, [{ id: 8, candidates: [3] }]);
    assert.equal(audit.candidate.equalToEnglish, 1);
    assert.equal(audit.hypothetical.matched, 3);
    assert.deepEqual(audit.hypothetical.duplicateNames, [{ name: 'Beibehalten', ids: [1, 3] }]);
    assert.equal(audit.withoutPasscode, 1);
    assert.equal(audit.withoutGermanName, 1);
    assert.equal(audit.example.before.matched, 1);
    assert.equal(audit.example.hypothetical.matched, 3);
    assert.equal(audit.example.hypothetical.cards[0].name, 'Beibehalten');
    assert.equal(audit.example.hypothetical.cards[3].reason, 'unverified-canonical-identity');
    const reordered = yamlSnapshot([...records].reverse());
    assert.deepEqual(reordered.cards, candidate.cards);
    const reorderedAudit = compareYamlYugi(primary, { ...candidate, cards: reordered.cards }, catalog, example);
    assert.deepEqual(reorderedAudit, audit);
    assert.equal('asset' in audit, false, 'research audit must not publish an additional runtime asset');
});

test('comparison rejects malformed/duplicate passcodes even if one record lacks a German name', () => {
    const card = { password: 1, name: { en: 'First', de: 'Erste' } };
    for (const password of [-1, 0, 1.5, '1', 100000000]) {
        assert.throws(() => yamlSnapshot([{ ...card, password }]), /passcode/);
    }
    assert.throws(() => yamlSnapshot([card, { password: 1, name: { en: 'First' } }]), /duplicate/);
    assert.throws(() => retainYamlYugiSnapshot(Buffer.from('[]'), 'master', retrievedAt), /commit/);
    const audit = compareYamlYugi(snapshot([{ id: 1, name: 'Erste', name_en: 'First' }]),
        yamlSnapshot([{ password: 2, name: { en: 'Second', de: '<ruby>Zweite</ruby>' } }]), catalog, example);
    assert.equal(audit.rejectedNames.length, 1);
    assert.equal(audit.hypothetical.matched, 1);
    assert.equal(audit.example.hypothetical.cards[1].reason, 'missing-provider-name');
});

test('a refresh cannot silently lose previously shipped canonical names', () => {
    const asset = names => ({ version: 1, locale: 'de', names });
    assert.throws(() => assertNoNameRegression(asset([[1, 'Erste'], [2, 'Zweite']]), asset([[1, 'Erste']])), /2/);
    assert.doesNotThrow(() => assertNoNameRegression(asset([[1, 'Erste']]), asset([[1, 'Erste'], [2, 'Zweite']])));
});

test('audit-only CLI cannot replace the shipped name asset', async () => {
    const directory = await mkdtemp(join(tmpdir(), 'name-comparison-'));
    try {
        const raw = join(directory, 'source.json');
        const primary = join(directory, 'primary.json');
        const canonical = join(directory, 'catalog.json');
        const session = join(directory, 'example.json');
        const audit = join(directory, 'audit.json');
        const output = join(directory, 'names.json');
        await writeFile(raw, JSON.stringify([{ password: 2, name: { en: 'Second', de: 'Zweite' } }]));
        await writeFile(primary, JSON.stringify(snapshot([{ id: 1, name: 'Erste', name_en: 'First' }])));
        await writeFile(canonical, JSON.stringify(catalog));
        await writeFile(session, JSON.stringify(example));
        await writeFile(output, 'existing bytes');
        const args = [fileURLToPath(new URL('./prepare-localized-names.mjs', import.meta.url)),
            '--snapshot', primary, '--catalog', canonical, '--audit', audit,
            '--compare-yaml-yugi', raw, '--revision', revision, '--retrieved-at', retrievedAt, '--example', session];
        execFileSync(process.execPath, args);
        const firstAudit = await readFile(audit, 'utf8');
        execFileSync(process.execPath, args);
        assert.equal(await readFile(audit, 'utf8'), firstAudit, 'same input bytes reproduce the complete audit');
        assert.equal(JSON.parse(await readFile(audit, 'utf8')).example.hypothetical.matched, 2);
        assert.throws(() => execFileSync(process.execPath, [...args, '--output', output], { stdio: 'pipe' }));
        assert.equal(await readFile(output, 'utf8'), 'existing bytes');
        const provider = join(directory, 'provider.json');
        const baseline = join(directory, 'previous.json');
        await writeFile(provider, JSON.stringify({ data: [{ id: 2, name: 'Zweite', name_en: 'Second' }] }));
        await writeFile(baseline, JSON.stringify({ version: 1, locale: 'de', names: [[1, 'Erste']] }));
        const refresh = [args[0], '--snapshot', primary, '--catalog', canonical,
            '--provider', provider, '--output', join(directory, 'new-names.json'), '--audit', audit];
        assert.throws(() => execFileSync(process.execPath, refresh, { stdio: 'pipe' }), error => /ENOENT/.test(error.stderr),
            'a provider refresh requires a shipped baseline before writing');
        assert.throws(() => execFileSync(process.execPath, [...refresh, '--previous', baseline], { stdio: 'pipe' }),
            error => /coverage regression: 1/.test(error.stderr));
        assert.equal(JSON.parse(await readFile(primary, 'utf8')).cards[0].id, 1, 'failed refresh preserves the old snapshot');
    } finally {
        await rm(directory, { recursive: true, force: true });
    }
});
