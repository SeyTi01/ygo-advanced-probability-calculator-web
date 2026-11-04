// Offline bundle generation. All validation completes before any tracked file changes.
import { readFile, writeFile, mkdir, rename, rm } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { resolve, dirname } from 'node:path';
import { pathToFileURL } from 'node:url';
import { execFileSync } from 'node:child_process';
import { buildCatalog, serializeCatalog, MIN_FULL_CATALOG_CARDS, MIN_ELIGIBLE_CARDS } from './prepare-catalog.mjs';
import { assertNoCatalogRegression, exclusionReason } from './card-eligibility.mjs';
import { auditMembership, membershipPolicy } from './archetype-membership.mjs';
import { retainYamlYugiSnapshot, retainEligibleNames, buildLocalizedNames, assertNoNameRegression } from './prepare-localized-names.mjs';
import { cardNameAssetPaths, cardNameLocales } from './locale-capabilities.mjs';

const json = value => JSON.stringify(value) + '\n';
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
export const dataRoot = 'YGOProbabilityCalculatorBlazor/wwwroot/data';
export const auditRoot = 'infrastructure/card-catalog/snapshots/refresh';

export function refreshPaths(locales) {
    const nameLocales = locales.filter(locale => locale.cardNames?.asset);
    return [`${dataRoot}/card-catalog.v1.json`,
        ...nameLocales.flatMap(locale => [`YGOProbabilityCalculatorBlazor/wwwroot/${locale.cardNames.asset}`, `${auditRoot}/names.${locale.id}.json`]),
        `${auditRoot}/audit.json`];
}

export async function assertCurrentRefreshWorkflow(env = process.env, read = readFile,
    git = args => execFileSync('git', args, { encoding: 'utf8' })) {
    if (env.GITHUB_ACTIONS !== 'true' || env.GITHUB_WORKFLOW !== 'Monthly card data refresh') return;
    const path = '.github/workflows/card-data-refresh.yml';
    const current = await read(path, 'utf8');
    const executing = git(['show', `${env.GITHUB_SHA}:${path}`]);
    if (current.replace(/\r\n/g, '\n') !== executing.replace(/\r\n/g, '\n')) {
        throw new Error('Refresh workflow changed since this run started. Start a new workflow_dispatch on dev instead of rerunning an old run.');
    }
}

// Providers sometimes move the primary ID to another artwork or replace a beta ID.
// Retain an established identity only through a unique numeric link and unchanged name/type.
export function stabilizeCanonicalIds(provider, previousCatalog) {
    if (!Array.isArray(provider?.data)) throw new Error('Invalid canonical source');
    const currentIds = new Set(provider.data.map(card => card.id));
    const missing = previousCatalog.cards.filter(card => !currentIds.has(card.id));
    const remaps = new Map();
    const identityChanges = [];
    for (const old of missing) {
        const owners = provider.data.filter(card => card.card_images?.some(image => image.id === old.id)
            || card.misc_info?.some(info => info.beta_id === old.id));
        if (owners.length > 1) throw new Error(`Ambiguous canonical identity owner: ${old.id}`);
        if (owners.length === 1) {
            const source = owners[0];
            if (source.name !== old.name || source.type !== old.type || remaps.has(source.id)) throw new Error(`Changed canonical identity: ${old.id}`);
            remaps.set(source.id, old.id);
            const evidence = source.card_images?.some(image => image.id === old.id) ? 'unique-artwork-id' : 'unique-beta-id';
            identityChanges.push({ canonicalId: old.id, providerId: source.id, name: old.name, evidence });
        }
    }
    return { provider: { ...provider, data: provider.data.map(card => remaps.has(card.id) ? { ...card, id: remaps.get(card.id) } : card) }, identityChanges };
}

export function candidateAudit(provider, series, previous) {
    if (!Array.isArray(provider) || provider.some(item => typeof item?.archetype_name !== 'string' || !item.archetype_name.trim())
        || !series || Array.isArray(series) || typeof series !== 'object') throw new Error('Invalid candidate vocabulary');
    const names = [...new Set([...provider.map(item => item.archetype_name), ...Object.keys(series)])].sort();
    if (names.some(name => name.length > 512 || /[\x00-\x1f]/.test(name))) throw new Error('Invalid candidate name');
    const reviewed = new Set(membershipPolicy.candidates.map(item => item.name));
    const excluded = new Set(membershipPolicy.excluded);
    return { names, added: names.filter(name => !(previous ?? []).includes(name)),
        removed: (previous ?? []).filter(name => !names.includes(name)),
        reviewRequired: names.filter(name => !reviewed.has(name) && !excluded.has(name)),
        evidence: names.map(name => ({ name, provider: provider.some(item => item.archetype_name === name), yamlYugi: Object.hasOwn(series, name) })) };
}

export function buildRefresh({ provider, yamlBytes, manifest, series, vocabulary, previousCatalog, previousNames, previousCandidates, example, minimumCards = MIN_FULL_CATALOG_CARDS, minimumEligible = MIN_ELIGIBLE_CARDS }) {
    const stabilized = stabilizeCanonicalIds(provider, previousCatalog);
    provider = stabilized.provider;
    const catalog = buildCatalog(provider, minimumCards, minimumEligible);
    assertNoCatalogRegression(previousCatalog, catalog);
    const names = {};
    const snapshots = {};
    const localizedNames = {};
    const nameChanges = {};
    for (const [locale, previous] of Object.entries(previousNames)) {
        if (previous.locale !== locale) throw new Error(`Mismatched coverage baseline locale: ${locale}`);
        const snapshot = retainEligibleNames(retainYamlYugiSnapshot(yamlBytes, manifest.revision, manifest.retrievedAt, locale), catalog);
        snapshot.source = `https://github.com/DawnbrandBots/yaml-yugi/tree/${manifest.revision}/data/cards`;
        const { asset, audit } = buildLocalizedNames(snapshot, catalog, example);
        try { assertNoNameRegression(previous, asset); }
        catch (error) { throw new Error(`${locale}: ${error.message}`); }
        const previousRatio = previous.names.length / previousCatalog.cards.length;
        if (asset.names.length / catalog.cards.length < previousRatio - 0.005) throw new Error(`${locale}: coverage ratio regression`);
        const oldNames = new Map(previous.names);
        nameChanges[locale] = asset.names.filter(([id, name]) => oldNames.get(id) !== name)
            .map(([id, name]) => ({ id, before: oldNames.get(id) ?? null, after: name }));
        names[locale] = asset;
        snapshots[locale] = snapshot;
        localizedNames[locale] = audit;
    }
    const candidates = candidateAudit(vocabulary, series, previousCandidates);
    if (minimumCards > 1 && (vocabulary.length < 300 || Object.keys(series).length < 300)) throw new Error('Incomplete candidate vocabulary');
    const old = new Map(previousCatalog.cards.map(card => [card.id, card]));
    const changes = catalog.cards.filter(card => json(old.get(card.id)) !== json(card)).map(card => ({ id: card.id, name: card.name,
        before: old.get(card.id) ?? null, after: card }));
    const newUnknown = catalog.cards.filter(card => card.resolvedArchetypes === null && old.get(card.id)?.resolvedArchetypes !== null);
    if (newUnknown.length > 25) throw new Error('Unexpected unresolved membership regression');
    const membership = auditMembership(provider.data.filter(card => exclusionReason(card) === null));
    return { catalog, names, snapshots, audit: { version: 2, changes, nameChanges, identityChanges: stabilized.identityChanges,
        candidates, membership, localizedNames, source: manifest } };
}

export async function readRefreshSources(input) {
    const manifest = JSON.parse(await readFile(resolve(input, 'sources.json'), 'utf8'));
    if (manifest.version !== 1 || !Array.isArray(manifest.sources)) throw new Error('Invalid source manifest');
    for (const required of ['cardinfo.json', 'archetypes.json', 'yaml-cards.json', 'series.json', 'yaml-yugi.tar.gz']) {
        const entry = manifest.sources.find(source => source.file === required);
        if (!entry) throw new Error('Missing source hash: ' + required);
        const bytes = await readFile(resolve(input, required));
        if (bytes.length !== entry.bytes || hash(bytes) !== entry.sha256) throw new Error('Source hash mismatch: ' + required);
    }
    return manifest;
}

export async function refresh(input, root = '.', minimumCards, minimumEligible) {
    const readJson = async path => JSON.parse(await readFile(path, 'utf8'));
    const at = path => resolve(root, path);
    const manifest = await readRefreshSources(input);
    let previousAudit;
    try { previousAudit = await readJson(at(`${auditRoot}/audit.json`)); }
    catch (error) { if (error.code !== 'ENOENT') throw error; }
    const exampleBytes = await readFile(at('YGOProbabilityCalculatorBlazor/wwwroot/sample-data/example_session_state.json'), 'utf8');
    const example = JSON.parse(exampleBytes);
    const locales = await readJson(at('YGOProbabilityCalculatorBlazor/wwwroot/localization/locales.json'));
    const configuredNameLocales = locales.filter(locale => locale.cardNames?.asset);
    const registryMatches = configuredNameLocales.length === cardNameLocales.length
        && configuredNameLocales.every(locale => cardNameAssetPaths.get(locale.id)
            === `YGOProbabilityCalculatorBlazor/wwwroot/${locale.cardNames.asset}`);
    if (!registryMatches) throw new Error('Card-name locale registry is inconsistent');
    const previousNames = {};
    for (const { id: locale } of configuredNameLocales) {
        previousNames[locale] = await readJson(at(cardNameAssetPaths.get(locale)));
    }
    const result = buildRefresh({ provider: await readJson(resolve(input, 'cardinfo.json')),
        yamlBytes: await readFile(resolve(input, 'yaml-cards.json')), manifest,
        series: await readJson(resolve(input, 'series.json')), vocabulary: await readJson(resolve(input, 'archetypes.json')),
        previousCatalog: await readJson(at(`${dataRoot}/card-catalog.v1.json`)),
        previousNames,
        previousCandidates: previousAudit?.candidates.names,
        example,
        minimumCards, minimumEligible });
    result.audit.canonicalSha256 = hash(serializeCatalog(result.catalog));
    result.audit.exampleSha256 = hash(exampleBytes.replace(/\r\n/g, '\n'));
    // Compare useful state, excluding retrieval dates, raw upstream noise and relative diffs.
    const fingerprint = hash(json({ catalog: result.catalog, names: result.names,
        localizedNames: Object.fromEntries(Object.entries(result.audit.localizedNames).map(([locale, { source, ...audit }]) => [locale, audit])),
        exampleSha256: result.audit.exampleSha256,
        candidates: result.audit.candidates.names, membership: result.audit.membership }));
    if (previousAudit?.fingerprint === fingerprint) {
        // A fingerprint describes intended output, not what an earlier workflow committed.
        // Preserve original provenance on a no-op, but repair stale/missing bundle files.
        const expected = [[`${dataRoot}/card-catalog.v1.json`, serializeCatalog(result.catalog)],
            ...Object.entries(result.names).map(([locale, names]) => [cardNameAssetPaths.get(locale), json(names)])];
        let complete = true;
        for (const [path, bytes] of expected) {
            try { complete &&= (await readFile(at(path), 'utf8')).replace(/\r\n/g, '\n') === bytes.toString('utf8'); }
            catch (error) { if (error.code !== 'ENOENT') throw error; complete = false; }
        }
        for (const locale of Object.keys(result.names)) {
            try {
                const bytes = (await readFile(at(`${auditRoot}/names.${locale}.json`), 'utf8')).replace(/\r\n/g, '\n');
                complete &&= hash(bytes) === previousAudit.localizedNames?.[locale]?.source.projectionSha256;
            } catch (error) { if (error.code !== 'ENOENT') throw error; complete = false; }
        }
        if (complete) return false;
    }
    result.audit.fingerprint = fingerprint;
    const localeSummary = Object.entries(result.audit.localizedNames).map(([locale, audit]) =>
        `${locale}: labels: ${audit.matchedRecords}; missing: ${audit.missingIds.length}; added/changed: ${result.audit.nameChanges[locale].length}.`).join('\n');
    const summary = `Monthly card-data refresh\n\nCards: ${result.catalog.cards.length}; added: ${result.audit.changes.filter(change => !change.before).length}; changed: ${result.audit.changes.filter(change => change.before).length}.\n${localeSummary}\nArchetype candidates requiring review: ${result.audit.candidates.reviewRequired.length}; unresolved declarations: ${result.audit.membership.unresolved.length}.\n\nReview infrastructure/card-catalog/snapshots/refresh/audit.json for identities, per-locale name changes, missing labels, source and normalization evidence, membership changes, conflicts, overlaps, candidates and example coverage. Policy files are never generated.\n`;
    const files = [[`${dataRoot}/card-catalog.v1.json`, serializeCatalog(result.catalog)],
        ...Object.entries(result.names).flatMap(([locale, names]) => [[cardNameAssetPaths.get(locale), json(names)],
            [`${auditRoot}/names.${locale}.json`, json(result.snapshots[locale])]]),
        [`${auditRoot}/audit.json`, json(result.audit)]];
    // Stage the complete validated bundle. Failure cannot publish a partial bundle: the workflow commits only on success.
    try {
        for (const [path, bytes] of files) { await mkdir(dirname(at(path)), { recursive: true }); await writeFile(at(path + '.refresh-tmp'), bytes); }
        for (const [path] of files) await rename(at(path + '.refresh-tmp'), at(path));
    } finally { for (const [path] of files) await rm(at(path + '.refresh-tmp'), { force: true }); }
    await writeFile(resolve(input, 'pr-body.md'), summary);
    return true;
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
    if (process.argv.length !== 3) throw new Error('Usage: node refresh-card-data.mjs SOURCE_DIRECTORY');
    assertCurrentRefreshWorkflow().then(() => refresh(resolve(process.argv[2]))).then(changed => console.log(changed ? 'Refresh generated' : 'No meaningful changes'))
        .catch(error => { console.error(error.message); process.exitCode = 1; });
}
