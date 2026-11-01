import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile, readdir, mkdtemp, writeFile, rm } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { buildLocalizedNames, retainYamlYugiSnapshot, exampleNameCoverage, assertNoNameRegression } from './prepare-localized-names.mjs';

const revision = 'a'.repeat(40);
const retrievedAt = '2026-10-31T00:00:00Z';
const snapshot = cards => retainYamlYugiSnapshot(Buffer.from(JSON.stringify(cards)), revision, retrievedAt);
const record = (password, en, de) => ({ password, name: { en, de } });
const catalog = { version: 2, cards: [
    { id: 1, type: 'Effect Monster', name: 'First', artworkImageIds: [1, 2, 9] },
    { id: 2, type: 'Spell Card', name: 'Second', artworkImageIds: [2, 9] },
    { id: 3, type: 'Trap Card', name: 'Third', artworkImageIds: [3, 8, 8] }
] };
const example = { Cards: [
    { ExternalCardId: 1, Name: 'First' }, { ExternalCardId: 2, Name: 'Second' },
    { ExternalCardId: 3, Name: 'Third' }, { ExternalCardId: 1, Name: 'My label' }
] };

test('canonical IDs win over aliases; unique artwork IDs join, ambiguous IDs and translated names do not', () => {
    const raw = [record(1, 'FIRST', 'Weißer Drache'), record(2, 'Second', 'Weißer Drache'),
        record(8, 'Third', 'Dritte'), record(9, 'First', 'Mehrdeutig'), record(99, 'First', 'Unbekannt')];
    const result = buildLocalizedNames(snapshot(raw), catalog, example);
    assert.deepEqual(result.asset.names, [[1, 'Weißer Drache'], [2, 'Weißer Drache'], [3, 'Dritte']]);
    assert.deepEqual(result.audit.aliasMatches, [{ sourceId: 8, canonicalId: 3 }]);
    assert.deepEqual(result.audit.ambiguousIds, [{ id: 9, candidates: [1, 2] }]);
    assert.deepEqual(result.audit.unexpectedIds, [99]);
    assert.deepEqual(result.audit.ambiguousNames, [{ name: 'Weißer Drache', ids: [1, 2] }]);
    assert.deepEqual(result.audit.englishNameDifferences, [{ id: 1, sourceId: 1, provider: 'FIRST', canonical: 'First' }]);
    const reordered = snapshot(raw);
    reordered.cards.reverse();
    assert.deepEqual(buildLocalizedNames(reordered, catalog, example).asset, result.asset);
    assert.equal(result.audit.example.cards[3].reason, 'unverified-canonical-identity');
});

test('source-assigned IDs preserve passwordless canonical cards without guessing by name', () => {
    const cards = [record(null, 'Slifer', 'Slifer der Himmelsdrache')];
    cards[0].fake_password = 10000020;
    const retained = snapshot(cards);
    assert.deepEqual(retained.cards[0], { id: 10000020, assigned: true, name: 'Slifer der Himmelsdrache', name_en: 'Slifer' });
    const canonical = { version: 2, cards: [{ id: 10000020, type: 'Effect Monster', name: 'Slifer' }] };
    const result = buildLocalizedNames(retained, canonical, { Cards: [] });
    assert.deepEqual(result.asset.names, [[10000020, 'Slifer der Himmelsdrache']]);
    assert.deepEqual(result.audit.assignedIds, [{ sourceId: 10000020, canonicalId: 10000020 }]);
});

test('duplicate and malformed identities fail even when the duplicate has no German name', () => {
    const card = record(1, 'First', 'Erste');
    for (const id of [-1, 0, 1.5, '1', 100000000]) assert.throws(() => snapshot([{ ...card, password: id }]), /identity/);
    assert.throws(() => snapshot([card, record(1, 'First', null)]), /duplicate/);
    assert.throws(() => snapshot([{ ...record(null, 'First', 'Erste'), fake_password: 2147483648 }]), /identity/);
    const retained = snapshot([card]);
    retained.cards.push({ id: 1, name: null, name_en: 'First' });
    assert.throws(() => buildLocalizedNames(retained, catalog, example), /duplicate/);
    assert.throws(() => retainYamlYugiSnapshot(Buffer.from('[]'), 'master', retrievedAt), /commit/);
    assert.throws(() => buildLocalizedNames(snapshot([record(3, 'Third', 'Dritte'), record(8, 'Third', 'Andere')]), catalog, example), /Conflicting/);
    assert.deepEqual(buildLocalizedNames(snapshot([record(3, 'Third', 'Dritte'), record(8, 'Third', 'Dritte')]), catalog, example).asset.names, [[3, 'Dritte']]);
});

test('projection omits unrelated data and audits invalid or missing labels without substituting English', () => {
    const raw = [{ ...record(1, 'First', 'Erste'), effect: 'Do not retain', image: 'https://unused.invalid' },
        record(2, 'Second', null), record(3, 'Third', '<ruby>Dritte</ruby>'), record(null, 'No ID', 'Ohne ID')];
    const retained = snapshot(raw);
    assert.deepEqual(Object.keys(retained.cards.find(card => card.id === 1)), ['id', 'name', 'name_en']);
    assert.equal(JSON.stringify(retained).includes('Do not retain'), false);
    assert.match(retained.sourceSha256, /^[a-f0-9]{64}$/);
    const result = buildLocalizedNames(retained, catalog, example);
    assert.deepEqual(result.asset.names, [[1, 'Erste']]);
    assert.equal(result.audit.withoutId, 1);
    assert.equal(result.audit.withoutGermanName, 1);
    assert.equal(result.audit.rejectedNames.length, 1);
    assert.deepEqual(result.audit.missingIds, [2, 3]);
    for (const name of ['', 'Dritte\n', 'Dritte &amp; mehr', 'x'.repeat(513)]) {
        assert.deepEqual(buildLocalizedNames(snapshot([record(1, 'First', name)]), catalog, example).asset.names, []);
    }
});

test('each supported locale projects only its selected names and preserves canonical identity rules', () => {
    const first = { password: 1, name: { en: 'First', de: 'Erste', fr: 'Première', it: 'Prima', pt: 'Primeira', ja: '一番' } };
    const third = { password: 8, name: { en: 'Third', de: 'Dritte', fr: 'Troisième', it: 'Terza', pt: 'Terceira', ja: '三番' } };
    for (const locale of ['de', 'fr', 'it', 'pt', 'ja']) {
        const bytes = Buffer.from(JSON.stringify([first, third, { password: 2, name: { en: 'Second' } }]));
        const retained = retainYamlYugiSnapshot(bytes, revision, retrievedAt, locale);
        const result = buildLocalizedNames(retained, catalog, example);
        assert.equal(retained.locale, locale);
        assert.equal(result.asset.locale, locale);
        assert.deepEqual(result.asset.names, [[1, first.name[locale]], [3, third.name[locale]]]);
        assert.deepEqual(result.audit.aliasMatches, [{ sourceId: 8, canonicalId: 3 }]);
        assert.deepEqual(result.audit.missingIds, [2]);
        assert.equal(retained.cards[0].name, first.name[locale]);
        assert.equal(JSON.stringify(retained.cards).includes('Troisième'), locale === 'fr');
    }
    for (const locale of ['en', 'es', 'ja-JP', 'pt-BR', '', null, {}, '__proto__']) {
        assert.throws(() => retainYamlYugiSnapshot(Buffer.from(JSON.stringify([first])), revision, retrievedAt, locale), /locale/);
        assert.throws(() => buildLocalizedNames({ ...snapshot([record(1, 'First', 'Erste')]), locale }, catalog, example), /snapshot/);
    }
});

test('Japanese ruby supplies plain base labels while retained source and normalization evidence remain reproducible', () => {
    const raw = '<ruby>強<rt>ごう</rt></ruby><ruby>欲<rt>よく</rt></ruby>で<ruby>貪<rt>どん</rt></ruby><ruby>欲<rt>よく</rt></ruby>な<ruby>壺<rt>つぼ</rt></ruby>';
    const source = [{ password: 1, name: { en: 'First', ja: raw } },
        { password: 8, name: { en: 'Third', ja: '<ruby>ＶＳ<rt>ヴァンキッシュ・ソウル</rt></ruby> ラゼン' } }];
    const retained = retainYamlYugiSnapshot(Buffer.from(JSON.stringify(source)), revision, retrievedAt, 'ja');
    assert.equal(retained.cards[0].name, raw);
    const result = buildLocalizedNames(retained, catalog, example);
    assert.deepEqual(result.asset.names, [[1, '強欲で貪欲な壺'], [3, 'ＶＳ ラゼン']]);
    assert.deepEqual(result.audit.normalizedNames, [
        { sourceId: 1, canonicalId: 1, sourceName: raw, name: '強欲で貪欲な壺' },
        { sourceId: 8, canonicalId: 3, sourceName: source[1].name.ja, name: 'ＶＳ ラゼン' }
    ]);
    assert.deepEqual(buildLocalizedNames(JSON.parse(JSON.stringify(retained)), catalog, example), result);
    for (const name of ['<b>基</b>', '<ruby>基</ruby>', '<ruby>基<rt>き</ruby>', '<ruby>基<rt>き</rt>',
        '<ruby onclick="x">基<rt>き</rt></ruby>', '<ruby>基<rt><b>き</b></rt></ruby>',
        '<ruby><ruby>基<rt>き</rt></ruby><rt>き</rt></ruby>', '<ruby>基<rt>き\n</rt></ruby>',
        '<ruby>基<rt>き</rt></ruby><script>x</script>', '<ruby>基<rt>き</rt></ruby>&amp;', 'Ｍ∀ＬＩＣＥ＜Ｐ＞<P>']) {
        const input = retainYamlYugiSnapshot(Buffer.from(JSON.stringify([{ password: 1, name: { en: 'First', ja: name } }])), revision, retrievedAt, 'ja');
        const rejected = buildLocalizedNames(input, catalog, example);
        assert.deepEqual(rejected.asset.names, [], name);
        assert.equal(rejected.audit.rejectedNames.length, 1, name);
        assert.deepEqual(rejected.audit.normalizedNames, [], name);
    }
});

test('shipped names reproduce from the sole retained source and cover every actual example entry', async () => {
    const read = async path => JSON.parse(await readFile(new URL(path, import.meta.url), 'utf8'));
    const retained = await read('./snapshots/names.de.yaml-yugi.2026-10-31.json');
    const canonical = await read('../../YGOProbabilityCalculatorBlazor/wwwroot/data/card-catalog.v1.json');
    const session = await read('../../YGOProbabilityCalculatorBlazor/wwwroot/sample-data/example_session_state.json');
    const shipped = await read('../../YGOProbabilityCalculatorBlazor/wwwroot/data/card-names.de.v1.json');
    const savedAudit = await read('./snapshots/names.de.yaml-yugi.2026-10-31.audit.json');
    const { canonicalSha256, exampleSha256, ...audit } = savedAudit;
    const result = buildLocalizedNames(retained, canonical, session);
    assert.deepEqual(result.asset, shipped);
    assert.deepEqual(result.audit, audit);
    assert.equal(result.audit.matchedRecords + result.audit.missingIds.length, canonical.cards.length);
    const coverage = exampleNameCoverage(shipped, canonical, session);
    assert.equal(coverage.matched, session.Cards.length);
    assert.deepEqual(coverage.unresolved, []);
    assert.equal(new Map(shipped.names).get(83764719), 'Wiedergeburt');
    assert.equal(new Map(shipped.names).get(10000020), 'Slifer der Himmelsdrache');
    assert.equal(new Map(shipped.names).has(58074572), false, 'missing source translation uses English fallback');
});

test('registered follow-on locale assets and audits reproduce from retained dated snapshots', async () => {
    const read = async path => readFile(new URL(path, import.meta.url));
    const locales = JSON.parse(await read('../../YGOProbabilityCalculatorBlazor/wwwroot/localization/locales.json'));
    const [canonicalBytes, exampleBytes, snapshots] = await Promise.all([
        read('../../YGOProbabilityCalculatorBlazor/wwwroot/data/card-catalog.v1.json'),
        read('../../YGOProbabilityCalculatorBlazor/wwwroot/sample-data/example_session_state.json'),
        readdir(new URL('./snapshots/', import.meta.url))
    ]);
    const canonical = JSON.parse(canonicalBytes);
    const session = JSON.parse(exampleBytes);
    const hash = bytes => createHash('sha256').update(bytes.toString('utf8').replace(/\r\n/g, '\n')).digest('hex');
    for (const { id: locale } of locales.filter(locale => !['en', 'de'].includes(locale.id))) {
        const matches = snapshots.filter(name => new RegExp(`^names\\.${locale}\\.yaml-yugi\\.\\d{4}-\\d{2}-\\d{2}\\.json$`).test(name));
        assert.equal(matches.length, 1, `${locale}: one retained dated snapshot`);
        const retained = JSON.parse(await read(`./snapshots/${matches[0]}`));
        const shipped = JSON.parse(await read(`../../YGOProbabilityCalculatorBlazor/wwwroot/data/card-names.${locale}.v1.json`));
        const { canonicalSha256, exampleSha256, ...audit } = JSON.parse(await read(`./snapshots/${matches[0].replace('.json', '.audit.json')}`));
        const result = buildLocalizedNames(retained, canonical, session);
        assert.equal(retained.locale, locale);
        assert.deepEqual(result.asset, shipped, locale);
        assert.deepEqual(result.audit, audit, locale);
        assert.equal(canonicalSha256, hash(canonicalBytes), locale);
        assert.equal(exampleSha256, hash(exampleBytes), locale);
        assert.equal(result.audit.matchedRecords + result.audit.missingIds.length, canonical.cards.length, locale);
        assert.equal(result.audit.example.matched, session.Cards.length, locale);
    }
});

test('coverage changes require explicit reviewed removal IDs', () => {
    const asset = names => ({ version: 1, locale: 'de', names });
    const before = asset([[1, 'Erste'], [2, 'Zweite']]);
    const after = asset([[2, 'Zweite']]);
    assert.throws(() => assertNoNameRegression(before, after), /regression: 1/);
    assert.throws(() => assertNoNameRegression(before, after, [2]), /regression: 1/);
    assert.deepEqual(assertNoNameRegression(before, after, [1]), [1]);
    assert.throws(() => assertNoNameRegression(before, after, ['1']), /reviewed/);
});

test('CLI refresh validates before writing and the retained snapshot regenerates identical output offline', async () => {
    const directory = await mkdtemp(join(tmpdir(), 'german-names-'));
    try {
        const paths = Object.fromEntries(['provider', 'snapshot', 'catalog', 'example', 'output', 'audit', 'allowed']
            .map(key => [key, join(directory, key + '.json')]));
        const canonicalText = JSON.stringify(catalog, null, 2) + '\n';
        const exampleText = JSON.stringify(example, null, 2) + '\n';
        await writeFile(paths.catalog, canonicalText);
        await writeFile(paths.example, exampleText);
        await writeFile(paths.provider, JSON.stringify([record(2, 'Second', 'Zweite')]));
        await writeFile(paths.snapshot, 'preserve snapshot');
        await writeFile(paths.output, JSON.stringify({ version: 1, locale: 'de', names: [[1, 'Erste']] }));
        const args = [fileURLToPath(new URL('./prepare-localized-names.mjs', import.meta.url)),
            '--snapshot', paths.snapshot, '--catalog', paths.catalog, '--example', paths.example,
            '--output', paths.output, '--audit', paths.audit];
        const refresh = [...args, '--provider', paths.provider, '--revision', revision, '--retrieved-at', retrievedAt];
        assert.throws(() => execFileSync(process.execPath, refresh, { stdio: 'pipe' }), error => /regression: 1/.test(error.stderr));
        assert.equal(await readFile(paths.snapshot, 'utf8'), 'preserve snapshot');
        await writeFile(paths.allowed, '[1]');
        execFileSync(process.execPath, [...refresh, '--allow-removals', paths.allowed]);
        const firstAsset = await readFile(paths.output, 'utf8');
        const firstAudit = await readFile(paths.audit, 'utf8');
        assert.deepEqual(JSON.parse(firstAsset).names, [[2, 'Zweite']]);
        execFileSync(process.execPath, args);
        assert.equal(await readFile(paths.output, 'utf8'), firstAsset);
        assert.equal(await readFile(paths.audit, 'utf8'), firstAudit);
        await writeFile(paths.catalog, canonicalText.replace(/\n/g, '\r\n'));
        await writeFile(paths.example, exampleText.replace(/\n/g, '\r\n'));
        execFileSync(process.execPath, args);
        assert.equal(await readFile(paths.output, 'utf8'), firstAsset);
        assert.equal(await readFile(paths.audit, 'utf8'), firstAudit, 'CRLF and LF input files reproduce identical audits');
        assert.throws(() => execFileSync(process.execPath, [...args, '--previous', join(directory, 'missing.json')], { stdio: 'pipe' }),
            error => /ENOENT/.test(error.stderr));
    } finally {
        await rm(directory, { recursive: true, force: true });
    }
});

test('CLI initial locale generation requires a new output and rejects mismatched replay or coverage locales', async () => {
    const directory = await mkdtemp(join(tmpdir(), 'localized-names-'));
    try {
        const paths = Object.fromEntries(['provider', 'snapshot', 'catalog', 'example', 'output', 'audit']
            .map(key => [key, join(directory, key + '.json')]));
        await writeFile(paths.catalog, JSON.stringify(catalog));
        await writeFile(paths.example, JSON.stringify(example));
        await writeFile(paths.provider, JSON.stringify([{ password: 1, name: { en: 'First', fr: 'Première', ja: '一番' } }]));
        const args = [fileURLToPath(new URL('./prepare-localized-names.mjs', import.meta.url)),
            '--snapshot', paths.snapshot, '--catalog', paths.catalog, '--example', paths.example,
            '--output', paths.output, '--audit', paths.audit];
        const refresh = [...args, '--provider', paths.provider, '--revision', revision, '--retrieved-at', retrievedAt, '--locale', 'fr'];
        assert.throws(() => execFileSync(process.execPath, refresh, { stdio: 'pipe' }), error => /ENOENT/.test(error.stderr));
        execFileSync(process.execPath, [...refresh, '--initial']);
        const initial = await readFile(paths.output, 'utf8');
        assert.deepEqual(JSON.parse(initial), { version: 1, locale: 'fr', names: [[1, 'Première']] });
        assert.throws(() => execFileSync(process.execPath, [...refresh, '--initial'], { stdio: 'pipe' }), error => /new output/.test(error.stderr));
        assert.throws(() => execFileSync(process.execPath, [...refresh, '--locale', 'ja'], { stdio: 'pipe' }), error => /baseline locale/.test(error.stderr));
        assert.throws(() => execFileSync(process.execPath, [...args, '--locale', 'it'], { stdio: 'pipe' }), error => /Snapshot locale/.test(error.stderr));
        assert.throws(() => execFileSync(process.execPath, [...refresh, '--initial', '--previous', paths.output], { stdio: 'pipe' }), error => /baseline/.test(error.stderr));
        assert.equal(await readFile(paths.output, 'utf8'), initial);
        execFileSync(process.execPath, args);
        assert.equal(await readFile(paths.output, 'utf8'), initial);
    } finally {
        await rm(directory, { recursive: true, force: true });
    }
});
