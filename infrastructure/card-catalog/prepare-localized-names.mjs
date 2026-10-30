#!/usr/bin/env node
// Offline by default. Retain only provider identity/name fields, never card text or URLs.
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { dirname, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { gzipSync, brotliCompressSync } from 'node:zlib';

export const GERMAN_NAMES_URL = 'https://db.ygoprodeck.com/api/v7/cardinfo.php?language=de';
const sha256 = bytes => createHash('sha256').update(bytes).digest('hex');
const validName = name => typeof name === 'string' && name.trim().length > 0 && name.length <= 512;
const validLabel = name => validName(name) && !/[<>\x00-\x1f\x7f]|&(?:#\d+|#x[\da-f]+|[a-z]+);/i.test(name);
const numericOrder = (left, right) => left - right;

// Research only: this projection does not establish permission to redistribute the data.
export function retainYamlYugiSnapshot(bytes, revision, retrievedAt) {
    if (!/^[a-f0-9]{40}$/.test(revision)) throw new Error('Supply the verified YAML Yugi source commit');
    const source = JSON.parse(bytes);
    if (!Array.isArray(source) || source.length === 0 || source.length > 30000) {
        throw new Error('Invalid YAML Yugi aggregate');
    }
    const seen = new Set();
    const cards = source.map(card => {
        const id = card.password;
        if (id != null && (!Number.isInteger(id) || id <= 0 || id > 99999999 || seen.has(id))) {
            throw new Error('Invalid or duplicate YAML Yugi passcode');
        }
        if (id != null) seen.add(id);
        return { id: id ?? null, name: card.name?.de ?? null, name_en: card.name?.en ?? null };
    });
    cards.sort((left, right) => {
        const order = (left.id ?? 0) - (right.id ?? 0);
        if (order) return order;
        const leftText = JSON.stringify(left);
        const rightText = JSON.stringify(right);
        return leftText < rightText ? -1 : leftText > rightText ? 1 : 0;
    });
    return { version: 1, locale: 'de', source: 'https://dawnbrandbots.github.io/yaml-yugi/cards.json',
        revision, retrievedAt, sourceBytes: bytes.length, sourceSha256: sha256(bytes), cards };
}

export function exampleNameCoverage(asset, catalog, example) {
    const canonical = new Map(catalog.cards.map(card => [card.id, card]));
    const names = new Map(asset.names);
    const entries = example.Cards.map(card => {
        const id = card.ExternalCardId;
        const match = canonical.get(id);
        const verified = match?.name === card.Name;
        return { id, canonical: card.Name, name: verified ? names.get(id) ?? null : null,
            reason: !verified ? 'unverified-canonical-identity' : names.has(id) ? null : 'missing-provider-name' };
    });
    return { entries: entries.length, matched: entries.filter(card => card.name !== null).length,
        unresolved: entries.filter(card => card.name === null), cards: entries };
}

const assetSize = asset => {
    const bytes = Buffer.from(JSON.stringify(asset) + '\n');
    return { bytes: bytes.length, gzip: gzipSync(bytes).length, brotli: brotliCompressSync(bytes).length };
};

// Deliberately returns an audit, never an asset: additional-source rights remain unresolved.
export function compareYamlYugi(primary, candidate, catalog, example) {
    const before = buildLocalizedNames(primary, catalog);
    const rejectedNames = candidate.cards.filter(card => card.id !== null && card.name !== null
        && (!validLabel(card.name) || !validName(card.name_en)));
    const usable = candidate.cards.filter(card => card.id !== null && card.name !== null
        && validLabel(card.name) && validName(card.name_en));
    const additional = buildLocalizedNames({ version: 1, locale: 'de', cards: usable }, catalog);
    const original = new Map(before.asset.names);
    const merged = new Map(additional.asset.names);
    for (const [id, name] of original) merged.set(id, name);
    const combined = { version: 1, locale: 'de', names: [...merged].sort(([left], [right]) => left - right) };
    const disagreements = additional.asset.names.filter(([id, name]) => original.has(id) && original.get(id) !== name)
        .map(([id, name]) => ({ id, retained: original.get(id), candidate: name, decision: 'retain-ygoprodeck' }));
    const summarize = result => ({ matched: result.audit.matchedRecords,
        differingFromEnglish: result.audit.matchedRecords - result.audit.unchangedNames.length,
        equalToEnglish: result.audit.unchangedNames.length, missingIds: result.audit.missingIds,
        unexpectedIds: result.audit.unexpectedIds, artworkOnlyIds: result.audit.artworkOnlyIds,
        duplicateNames: result.audit.ambiguousNames, englishNameDifferences: result.audit.englishNameDifferences });
    const canonical = new Map(catalog.cards.map(card => [card.id, card.name]));
    const unchanged = combined.names.filter(([id, name]) => canonical.get(id) === name).map(([id]) => id);
    const byName = new Map();
    for (const [id, name] of combined.names) byName.set(name, [...(byName.get(name) ?? []), id]);
    return {
        publication: 'research-only; additional-source redistribution permission unresolved',
        policy: 'Exact passcodes only; retain every YGOPRODeck name; fill missing IDs only; no artwork/name joins.',
        source: { url: candidate.source, revision: candidate.revision, sha256: candidate.sourceSha256,
            retrievedAt: candidate.retrievedAt, bytes: candidate.sourceBytes,
            projectionSha256: sha256(Buffer.from(JSON.stringify(candidate) + '\n')) },
        returnedRecords: candidate.cards.length,
        withoutPasscode: candidate.cards.filter(card => card.id === null).length,
        withoutGermanName: candidate.cards.filter(card => card.name === null).length,
        rejectedNames, duplicatePasscodes: [],
        before: summarize(before), candidate: summarize(additional),
        addedIds: combined.names.filter(([id]) => !original.has(id)).map(([id]) => id),
        disagreements,
        hypothetical: { matched: merged.size, differingFromEnglish: merged.size - unchanged.length,
            equalToEnglish: unchanged.length,
            missingIds: [...canonical.keys()].filter(id => !merged.has(id)).sort(numericOrder),
            duplicateNames: [...byName].filter(([, ids]) => ids.length > 1).map(([name, ids]) => ({ name, ids })) },
        example: { before: exampleNameCoverage(before.asset, catalog, example),
            candidate: exampleNameCoverage(additional.asset, catalog, example),
            hypothetical: exampleNameCoverage(combined, catalog, example) },
        sizes: { before: assetSize(before.asset), hypothetical: assetSize(combined) }
    };
}

export function assertNoNameRegression(previous, next) {
    const names = new Map(next.names);
    const removed = previous.names.filter(([id]) => !names.has(id)).map(([id]) => id);
    if (removed.length) throw new Error(`Localized name coverage regression: ${removed.join(', ')}`);
}

export function retainNameSnapshot(bytes, retrievedAt) {
    const source = JSON.parse(bytes);
    if (!Array.isArray(source.data) || source.data.length === 0 || source.data.length > 30000) {
        throw new Error('Invalid provider response');
    }
    const cards = source.data.map(card => ({ id: card.id, name: card.name, name_en: card.name_en }));
    return {
        version: 1, locale: 'de', source: GERMAN_NAMES_URL, retrievedAt,
        sourceBytes: bytes.length, sourceSha256: sha256(bytes),
        cards: cards.sort((left, right) => left.id - right.id)
    };
}

export function buildLocalizedNames(snapshot, catalog) {
    if (snapshot.version !== 1 || snapshot.locale !== 'de' || !Array.isArray(snapshot.cards)
        || snapshot.cards.length > 30000
        || catalog.version !== 2 || !Array.isArray(catalog.cards)) {
        throw new Error('Unsupported name snapshot or canonical catalog');
    }
    const canonical = new Map();
    const artwork = new Map();
    for (const card of catalog.cards) {
        if (!Number.isInteger(card.id) || card.id <= 0 || canonical.has(card.id) || !validName(card.name)) {
            throw new Error('Invalid canonical identity');
        }
        canonical.set(card.id, card);
        for (const id of card.artworkImageIds ?? []) {
            artwork.set(id, [...(artwork.get(id) ?? []), card.id]);
        }
    }
    const seen = new Set();
    const names = [];
    const unexpectedIds = [];
    const artworkOnlyIds = [];
    const englishNameDifferences = [];
    const unchangedNames = [];
    const byName = new Map();
    for (const card of [...snapshot.cards].sort((left, right) => left.id - right.id)) {
        if (!Number.isInteger(card.id) || card.id <= 0 || card.id > 2147483647
            || seen.has(card.id) || !validLabel(card.name) || !validName(card.name_en)) {
            throw new Error('Invalid or duplicate provider identity/name');
        }
        seen.add(card.id);
        const match = canonical.get(card.id);
        if (!match) {
            unexpectedIds.push(card.id);
            if (artwork.has(card.id)) artworkOnlyIds.push({ id: card.id, candidates: artwork.get(card.id) });
            continue;
        }
        names.push([card.id, card.name]);
        byName.set(card.name, [...(byName.get(card.name) ?? []), card.id]);
        if (card.name_en !== match.name) {
            englishNameDifferences.push({ id: card.id, provider: card.name_en, canonical: match.name });
        }
        if (card.name === match.name) unchangedNames.push(card.id);
    }
    const matched = new Set(names.map(([id]) => id));
    const audit = {
        returnedRecords: snapshot.cards.length, canonicalRecords: canonical.size, matchedRecords: names.length,
        missingIds: [...canonical.keys()].filter(id => !matched.has(id)).sort((left, right) => left - right),
        unexpectedIds, artworkOnlyIds, englishNameDifferences, unchangedNames,
        ambiguousNames: [...byName].filter(([, ids]) => ids.length > 1).map(([name, ids]) => ({ name, ids }))
    };
    return { asset: { version: 1, locale: 'de', names }, audit };
}

async function main(args) {
    const options = new Map();
    for (let index = 0; index < args.length; index += 2) {
        if (!['--provider', '--snapshot', '--catalog', '--output', '--audit', '--previous',
            '--compare-yaml-yugi', '--revision', '--retrieved-at', '--example'].includes(args[index]) || !args[index + 1]) {
            throw new Error('Use --snapshot FILE --catalog FILE --output FILE --audit FILE; optionally --provider FILE to retain a new name-only snapshot.');
        }
        options.set(args[index], args[index + 1]);
    }
    const comparison = options.has('--compare-yaml-yugi');
    for (const option of ['--snapshot', '--catalog', '--audit',
        ...(comparison ? ['--revision', '--retrieved-at', '--example'] : ['--output'])]) {
        if (!options.has(option)) throw new Error(`Missing ${option}`);
    }
    const catalogBytes = await readFile(options.get('--catalog'));
    const catalog = JSON.parse(catalogBytes);
    if (comparison) {
        if (options.has('--provider') || options.has('--output') || options.has('--previous')) {
            throw new Error('Comparison is audit-only; do not supply --provider, --output or --previous');
        }
        const bytes = await readFile(options.get('--compare-yaml-yugi'));
        const candidate = retainYamlYugiSnapshot(bytes, options.get('--revision'), options.get('--retrieved-at'));
        const primary = JSON.parse(await readFile(options.get('--snapshot'), 'utf8'));
        const example = JSON.parse(await readFile(options.get('--example'), 'utf8'));
        const audit = compareYamlYugi(primary, candidate, catalog, example);
        audit.canonicalSha256 = sha256(catalogBytes);
        audit.primarySourceSha256 = primary.sourceSha256;
        audit.exampleSha256 = sha256(await readFile(options.get('--example')));
        await mkdir(dirname(resolve(options.get('--audit'))), { recursive: true });
        await writeFile(options.get('--audit'), JSON.stringify(audit) + '\n');
        console.log(JSON.stringify({ before: audit.before.matched, hypothetical: audit.hypothetical.matched,
            added: audit.addedIds.length, disagreements: audit.disagreements.length,
            exampleBefore: audit.example.before.matched, exampleHypothetical: audit.example.hypothetical.matched,
            publication: audit.publication }));
        return;
    }
    let snapshot;
    if (options.has('--provider')) {
        const providerBytes = await readFile(options.get('--provider'));
        if (providerBytes.length > 100 * 1024 * 1024) throw new Error('Provider snapshot too large');
        snapshot = retainNameSnapshot(providerBytes, new Date().toISOString());
    } else {
        snapshot = JSON.parse(await readFile(options.get('--snapshot'), 'utf8'));
    }
    const { asset, audit } = buildLocalizedNames(snapshot, catalog);
    // A refresh must compare against the previously shipped asset, even when writing a new path.
    const previous = options.get('--previous') ?? options.get('--output');
    try {
        assertNoNameRegression(JSON.parse(await readFile(previous, 'utf8')), asset);
    } catch (error) {
        if (error.code !== 'ENOENT' || options.has('--provider') || options.has('--previous')) throw error;
    }
    audit.canonicalSha256 = sha256(catalogBytes);
    audit.sourceSha256 = snapshot.sourceSha256;
    for (const [file, content] of [
        ...(options.has('--provider') ? [[options.get('--snapshot'), snapshot]] : []),
        [options.get('--output'), asset], [options.get('--audit'), audit]
    ]) {
        await mkdir(dirname(resolve(file)), { recursive: true });
        await writeFile(file, JSON.stringify(content) + '\n');
    }
    console.log(JSON.stringify({ returned: audit.returnedRecords, matched: audit.matchedRecords,
        missing: audit.missingIds.length, unexpected: audit.unexpectedIds.length,
        assetBytes: Buffer.byteLength(JSON.stringify(asset) + '\n') }));
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
    main(process.argv.slice(2)).catch(error => { console.error(error.message); process.exitCode = 1; });
}
