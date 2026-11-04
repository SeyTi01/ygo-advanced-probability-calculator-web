import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { exclusionReason, eligibleCatalog, assertNoCatalogRegression } from './card-eligibility.mjs';
import { buildCatalog, MIN_FULL_CATALOG_CARDS, MIN_ELIGIBLE_CARDS } from './prepare-catalog.mjs';
import { retainEligibleNames, retainYamlYugiSnapshot, buildLocalizedNames } from './prepare-localized-names.mjs';

const card = (type, properties = {}) => ({ id: 1, name: 'Example', type, ...properties });

test('Main Deck Ritual, Pendulum and mixed subtypes survive while every Extra Deck classification wins', () => {
    for (const type of ['Ritual Monster', 'Ritual Effect Monster', 'Pendulum Effect Ritual Monster',
        'Pendulum Normal Monster', 'Pendulum Effect Monster', 'Pendulum Tuner Effect Monster',
        'Pendulum Flip Effect Monster', 'Flip Tuner Effect Monster', 'Spell Card', 'Trap Card']) {
        assert.equal(exclusionReason(card(type)), null, type);
    }
    for (const type of ['Fusion Monster', 'Synchro Tuner Monster', 'XYZ Monster', 'Link Monster',
        'Pendulum Effect Fusion Monster', 'Synchro Pendulum Effect Monster', 'XYZ Pendulum Effect Monster']) {
        assert.equal(exclusionReason(card(type)), 'extra-deck', type);
    }
    assert.equal(exclusionReason(card('Pendulum Effect Monster', { frameType: 'xyz_pendulum' })), 'extra-deck');
    assert.throws(() => exclusionReason(card('Future Monster')), /Unreviewed/);
});

test('Skills, Tokens and Speed Duel-only records are excluded without rejecting shared printings', () => {
    assert.equal(exclusionReason(card('Skill Card')), 'skill');
    assert.equal(exclusionReason(card('Token')), 'token');
    const speed = { misc_info: [{ formats: ['Speed Duel'] }] };
    assert.equal(exclusionReason(card('Effect Monster', speed)), 'speed-duel-only');
    for (const format of ['TCG', 'OCG']) {
        assert.equal(exclusionReason(card('Effect Monster', {
            misc_info: [{ formats: ['Speed Duel'] }, { formats: [format] }],
            card_sets: [{ set_name: 'Speed Duel promotional cards' }, { set_name: 'Magazine promotional card' }]
        })), null);
    }
});

test('reviewed special promos use exact identities, ordinary promotional releases stay eligible', () => {
    assert.equal(exclusionReason(card('Effect Monster', { id: 68811206, name: 'Tyler the Great Warrior' })), 'special-promotional-record');
    assert.equal(exclusionReason(card('Normal Monster', { id: 20240828, name: 'Decoy' })), 'special-promotional-record');
    assert.throws(() => exclusionReason(card('Effect Monster', { id: 68811206, name: 'Changed' })), /Changed reviewed/);
    assert.equal(exclusionReason(card('Effect Monster', { id: 44910027, name: 'Victory Dragon',
        card_sets: [{ set_name: 'Retro Pack 2' }, { set_name: 'Shonen Jump promotional card' }] })), null);
    assert.throws(() => exclusionReason(card('Normal Monster', {
        card_sets: [{ set_name: 'Yu-Gi-Oh! Championship Series 2030 prize card' }]
    })), /Review promotional/);
});

test('canonical removal guard permits policy exclusions but never an eligible disappearance', () => {
    const previous = { version: 2, cards: [card('Effect Monster'), card('Link Monster', { id: 2 })] };
    const next = eligibleCatalog(previous);
    assert.doesNotThrow(() => assertNoCatalogRegression(previous, next));
    assert.throws(() => assertNoCatalogRegression(previous, { cards: [] }), /regression: 1/);
    assert.doesNotThrow(() => assertNoCatalogRegression(previous, { cards: [] }, [1]));
});

test('full-response and eligible-pool floors independently reject partial catalogues', () => {
    assert.throws(() => buildCatalog({ data: Array.from({ length: 13999 }, (_, index) =>
        card('Effect Monster', { id: index + 1 })) }, MIN_FULL_CATALOG_CARDS), /incomplete/);
    assert.throws(() => buildCatalog({ data: Array.from({ length: 14000 }, (_, index) =>
        card('Token', { id: index + 1 })) }, MIN_FULL_CATALOG_CARDS, MIN_ELIGIBLE_CARDS), /Eligible catalog.*incomplete/);
});

test('translation retention follows canonical eligibility, preserves unique aliases and excludes ambiguous owners', () => {
    const catalog = { version: 2, cards: [
        card('Effect Monster', { artworkImageIds: [2, 3, 9] }),
        card('Link Monster', { id: 2, artworkImageIds: [9, 20] })
    ] };
    const snapshot = retainYamlYugiSnapshot(Buffer.from(JSON.stringify([1, 2, 3, 9, 20, 99].map(id =>
        ({ password: id, name: { en: 'Example', de: 'Beispiel' } })))), 'a'.repeat(40), '2026-11-01T00:00:00Z');
    const retained = retainEligibleNames(snapshot, catalog);
    assert.deepEqual(retained.cards.map(card => card.id), [1, 3]);
    const { asset, audit } = buildLocalizedNames(snapshot, catalog, { Cards: [] });
    assert.deepEqual(asset.names, [[1, 'Beispiel']]);
    assert.deepEqual(audit.aliasMatches, [{ sourceId: 3, canonicalId: 1 }]);
    assert.equal(audit.canonicalRecords, 1);
    assert.deepEqual(audit.missingIds, []);
});

test('all shipped canonical and retained translation records belong to the same eligible pool', async () => {
    const read = async path => JSON.parse(await readFile(new URL(path, import.meta.url), 'utf8'));
    const catalog = await read('../../YGOProbabilityCalculatorBlazor/wwwroot/data/card-catalog.v1.json');
    const snapshot = await read('./snapshots/names.de.yaml-yugi.2026-10-31.json');
    assert.deepEqual(eligibleCatalog(catalog), catalog);
    assert.deepEqual(retainEligibleNames(snapshot, catalog), snapshot);
    assert.ok(catalog.cards.some(card => card.type === 'Pendulum Effect Ritual Monster'));
});
