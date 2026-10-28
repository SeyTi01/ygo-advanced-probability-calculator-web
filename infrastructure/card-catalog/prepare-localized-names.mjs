#!/usr/bin/env node
// Offline by default. Retain only provider identity/name fields, never card text or URLs.
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { dirname, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

export const GERMAN_NAMES_URL = 'https://db.ygoprodeck.com/api/v7/cardinfo.php?language=de';
const sha256 = bytes => createHash('sha256').update(bytes).digest('hex');
const validName = name => typeof name === 'string' && name.trim().length > 0 && name.length <= 512;

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
        || snapshot.cards.length === 0 || snapshot.cards.length > 30000
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
            || seen.has(card.id) || !validName(card.name) || !validName(card.name_en)) {
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
        if (!['--provider', '--snapshot', '--catalog', '--output', '--audit'].includes(args[index]) || !args[index + 1]) {
            throw new Error('Use --snapshot FILE --catalog FILE --output FILE --audit FILE; optionally --provider FILE to retain a new name-only snapshot.');
        }
        options.set(args[index], args[index + 1]);
    }
    for (const option of ['--snapshot', '--catalog', '--output', '--audit']) {
        if (!options.has(option)) throw new Error(`Missing ${option}`);
    }
    const catalogBytes = await readFile(options.get('--catalog'));
    let snapshot;
    if (options.has('--provider')) {
        const providerBytes = await readFile(options.get('--provider'));
        if (providerBytes.length > 100 * 1024 * 1024) throw new Error('Provider snapshot too large');
        snapshot = retainNameSnapshot(providerBytes, new Date().toISOString());
    } else {
        snapshot = JSON.parse(await readFile(options.get('--snapshot'), 'utf8'));
    }
    const { asset, audit } = buildLocalizedNames(snapshot, JSON.parse(catalogBytes));
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
