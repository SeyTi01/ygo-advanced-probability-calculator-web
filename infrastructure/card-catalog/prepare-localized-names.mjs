#!/usr/bin/env node
// Offline German display names. Canonical metadata and saved identities stay English.
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { dirname, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { gzipSync, brotliCompressSync } from 'node:zlib';
import { eligibleCatalog, exclusionReason } from './card-eligibility.mjs';

const sha256 = bytes => createHash('sha256').update(bytes).digest('hex');
const validName = name => typeof name === 'string' && name.trim().length > 0 && name.length <= 512;
const validLabel = name => validName(name) && !/[<>\x00-\x1f\x7f]|&(?:#\d+|#x[\da-f]+|[a-z]+);/i.test(name);
const numericOrder = (left, right) => left - right;
const validId = (id, assigned) => Number.isInteger(id) && id > 0 && id <= (assigned ? 2147483647 : 99999999);

function validateSnapshot(snapshot) {
    if (snapshot.version !== 1 || snapshot.locale !== 'de' || !Array.isArray(snapshot.cards)
        || snapshot.cards.length > 30000 || !/^[a-f0-9]{40}$/.test(snapshot.revision)
        || !/^[a-f0-9]{64}$/.test(snapshot.sourceSha256)) throw new Error('Unsupported name snapshot');
    const seen = new Set();
    for (const card of snapshot.cards) {
        if (card.id === null) continue;
        if (!validId(card.id, card.assigned === true) || seen.has(card.id)
            || (card.assigned !== undefined && card.assigned !== true)) throw new Error('Invalid or duplicate YAML Yugi identity');
        seen.add(card.id);
    }
}

export function retainYamlYugiSnapshot(bytes, revision, retrievedAt) {
    if (!/^[a-f0-9]{40}$/.test(revision)) throw new Error('Supply the verified YAML Yugi source commit');
    if (bytes.length > 160 * 1024 * 1024) throw new Error('Provider snapshot too large');
    const source = JSON.parse(bytes);
    if (!Array.isArray(source) || source.length === 0 || source.length > 30000) throw new Error('Invalid YAML Yugi aggregate');
    const seen = new Set();
    const cards = source.map(card => {
        const assigned = card.password == null && card.fake_password != null;
        const id = card.password ?? card.fake_password ?? null;
        if (id !== null && (!validId(id, assigned) || seen.has(id))) throw new Error('Invalid or duplicate YAML Yugi identity');
        if (id !== null) seen.add(id);
        return { id, ...(assigned ? { assigned: true } : {}), name: card.name?.de ?? null, name_en: card.name?.en ?? null };
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
    const cards = example.Cards.map(card => {
        const id = card.ExternalCardId;
        const verified = canonical.get(id)?.name === card.Name;
        return { id, canonical: card.Name, name: verified ? names.get(id) ?? null : null,
            reason: !verified ? 'unverified-canonical-identity' : names.has(id) ? null : 'missing-provider-name' };
    });
    return { entries: cards.length, matched: cards.filter(card => card.name !== null).length,
        unresolved: cards.filter(card => card.name === null), cards };
}

// Keep only identities belonging to the eligible canonical pool. Resolve against
// the full input first so an excluded canonical ID cannot become an artwork alias.
export function retainEligibleNames(snapshot, catalog) {
    validateSnapshot(snapshot);
    const canonical = new Map(catalog.cards.map(card => [card.id, card]));
    const owners = new Map();
    for (const card of catalog.cards) {
        for (const id of card.artworkImageIds ?? []) {
            const entries = owners.get(id) ?? new Set();
            entries.add(card.id);
            owners.set(id, entries);
        }
    }
    const cards = snapshot.cards.filter(card => {
        let match = canonical.get(card.id);
        if (!match && owners.get(card.id)?.size === 1) match = canonical.get([...owners.get(card.id)][0]);
        return match && exclusionReason(match) === null;
    });
    return { ...snapshot, cards };
}

export function buildLocalizedNames(snapshot, catalog, example) {
    validateSnapshot(snapshot);
    if (catalog.version !== 2 || !Array.isArray(catalog.cards)) {
        throw new Error('Unsupported name snapshot or canonical catalog');
    }
    const allCards = catalog.cards;
    const excludedIds = new Set(allCards.filter(card => exclusionReason(card) !== null).map(card => card.id));
    catalog = eligibleCatalog(catalog);
    const canonical = new Map();
    const artwork = new Map();
    for (const card of catalog.cards) {
        if (!validId(card.id, true) || canonical.has(card.id) || !validName(card.name)) throw new Error('Invalid canonical identity');
        canonical.set(card.id, card);
    }
    for (const card of allCards) {
        for (const id of card.artworkImageIds ?? []) {
            const owners = artwork.get(id) ?? new Set();
            owners.add(card.id);
            artwork.set(id, owners);
        }
    }
    const names = new Map();
    const rejectedNames = [];
    const unexpectedIds = [];
    const ambiguousIds = [];
    const aliasMatches = [];
    const assignedIds = [];
    const englishNameDifferences = [];
    for (const card of [...snapshot.cards].sort((left, right) => (left.id ?? 0) - (right.id ?? 0))) {
        if (card.id === null) continue;
        if (excludedIds.has(card.id)) continue;
        if (card.name === null) continue;
        if (!validLabel(card.name) || !validName(card.name_en)) {
            rejectedNames.push(card);
            continue;
        }
        let match = canonical.get(card.id);
        if (!match) {
            const owners = artwork.get(card.id);
            if (owners?.size === 1) {
                match = canonical.get([...owners][0]);
                if (!match) continue;
                aliasMatches.push({ sourceId: card.id, canonicalId: match.id });
            } else {
                if (owners?.size > 1) ambiguousIds.push({ id: card.id, candidates: [...owners].sort(numericOrder) });
                else unexpectedIds.push(card.id);
                continue;
            }
        }
        if (names.has(match.id) && names.get(match.id) !== card.name) throw new Error(`Conflicting German names for canonical ID ${match.id}`);
        names.set(match.id, card.name);
        if (card.assigned) assignedIds.push({ sourceId: card.id, canonicalId: match.id });
        if (card.name_en !== match.name) englishNameDifferences.push({ id: match.id, sourceId: card.id, provider: card.name_en, canonical: match.name });
    }
    const asset = { version: 1, locale: 'de', names: [...names].sort(([left], [right]) => left - right) };
    const byName = new Map();
    for (const [id, name] of asset.names) byName.set(name, [...(byName.get(name) ?? []), id]);
    const bytes = Buffer.from(JSON.stringify(asset) + '\n');
    const audit = {
        policy: 'Eligible Main Deck pool only; YAML Yugi exact canonical IDs first, then unique catalog artwork aliases; no name joins.',
        source: { url: snapshot.source, revision: snapshot.revision, sha256: snapshot.sourceSha256,
            retrievedAt: snapshot.retrievedAt, bytes: snapshot.sourceBytes,
            projectionSha256: sha256(Buffer.from(JSON.stringify(snapshot) + '\n')) },
        returnedRecords: snapshot.cards.length, canonicalRecords: canonical.size, matchedRecords: names.size,
        withoutId: snapshot.cards.filter(card => card.id === null).length,
        withoutGermanName: snapshot.cards.filter(card => card.name === null).length,
        missingIds: [...canonical.keys()].filter(id => !names.has(id)).sort(numericOrder),
        rejectedNames, unexpectedIds, ambiguousIds, aliasMatches, assignedIds, englishNameDifferences,
        unchangedNames: asset.names.filter(([id, name]) => canonical.get(id).name === name).map(([id]) => id),
        ambiguousNames: [...byName].filter(([, ids]) => ids.length > 1).map(([name, ids]) => ({ name, ids })),
        example: exampleNameCoverage(asset, catalog, example),
        sizes: { bytes: bytes.length, gzip: gzipSync(bytes).length, brotli: brotliCompressSync(bytes).length }
    };
    return { asset, audit };
}

export function assertNoNameRegression(previous, next, allowedRemovals = []) {
    if (!Array.isArray(allowedRemovals) || allowedRemovals.some(id => !validId(id, true))) throw new Error('Invalid reviewed removal IDs');
    const names = new Map(next.names);
    const removed = previous.names.filter(([id]) => !names.has(id)).map(([id]) => id);
    const unexpected = removed.filter(id => !allowedRemovals.includes(id));
    if (unexpected.length) throw new Error(`Localized name coverage regression: ${unexpected.join(', ')}`);
    return removed;
}

async function main(args) {
    const options = new Map();
    for (let index = 0; index < args.length; index += 2) {
        if (!['--provider', '--snapshot', '--catalog', '--output', '--audit', '--previous',
            '--revision', '--retrieved-at', '--example', '--allow-removals'].includes(args[index]) || !args[index + 1]) {
            throw new Error('Use --snapshot FILE --catalog FILE --example FILE --output FILE --audit FILE; optionally --provider FILE --revision SHA --retrieved-at ISO_TIMESTAMP');
        }
        options.set(args[index], args[index + 1]);
    }
    for (const option of ['--snapshot', '--catalog', '--example', '--output', '--audit',
        ...(options.has('--provider') ? ['--revision', '--retrieved-at'] : [])]) {
        if (!options.has(option)) throw new Error(`Missing ${option}`);
    }
    const sourceSnapshot = options.has('--provider')
        ? retainYamlYugiSnapshot(await readFile(options.get('--provider')), options.get('--revision'), options.get('--retrieved-at'))
        : JSON.parse(await readFile(options.get('--snapshot'), 'utf8'));
    const catalogBytes = await readFile(options.get('--catalog'));
    const catalog = JSON.parse(catalogBytes);
    const snapshot = retainEligibleNames(sourceSnapshot, catalog);
    const exampleBytes = await readFile(options.get('--example'));
    const { asset, audit } = buildLocalizedNames(snapshot, catalog, JSON.parse(exampleBytes));
    const allowedRemovals = options.has('--allow-removals') ? JSON.parse(await readFile(options.get('--allow-removals'), 'utf8')) : [];
    try {
        assertNoNameRegression(JSON.parse(await readFile(options.get('--previous') ?? options.get('--output'), 'utf8')), asset, allowedRemovals);
    } catch (error) {
        if (error.code !== 'ENOENT' || options.has('--provider') || options.has('--previous') || options.has('--allow-removals')) throw error;
    }
    audit.canonicalSha256 = sha256(catalogBytes);
    audit.exampleSha256 = sha256(exampleBytes);
    for (const [file, content] of [
        [options.get('--snapshot'), snapshot],
        [options.get('--output'), asset], [options.get('--audit'), audit]
    ]) {
        await mkdir(dirname(resolve(file)), { recursive: true });
        await writeFile(file, JSON.stringify(content) + '\n');
    }
    console.log(JSON.stringify({ returned: audit.returnedRecords, matched: audit.matchedRecords,
        missing: audit.missingIds.length, example: audit.example.matched, assetBytes: audit.sizes.bytes }));
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
    main(process.argv.slice(2)).catch(error => { console.error(error.message); process.exitCode = 1; });
}
