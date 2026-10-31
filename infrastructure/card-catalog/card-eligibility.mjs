// One canonical eligibility policy also defines the translation-maintenance pool.
import { readFileSync } from 'node:fs';

const policy = JSON.parse(readFileSync(new URL('./eligibility-policy.json', import.meta.url), 'utf8'));
const mainDeckTypes = new Set([
    'Spell Card', 'Trap Card', 'Normal Monster', 'Effect Monster', 'Flip Effect Monster',
    'Union Effect Monster', 'Tuner Monster', 'Gemini Monster', 'Normal Tuner Monster',
    'Spirit Monster', 'Ritual Effect Monster', 'Ritual Monster', 'Toon Monster',
    'Pendulum Effect Monster', 'Pendulum Effect Ritual Monster', 'Pendulum Normal Monster',
    'Pendulum Tuner Effect Monster', 'Pendulum Flip Effect Monster', 'Flip Tuner Effect Monster'
]);

export function exclusionReason(card) {
    if (!card || typeof card.type !== 'string') throw new Error('Missing eligibility type');
    const special = policy.excluded.find(entry => entry.id === card.id);
    if (special) {
        if (special.name !== card.name || special.type !== card.type) throw new Error(`Changed reviewed eligibility record ${card.id}`);
        return special.reason;
    }
    // Extra Deck classifications take precedence over Pendulum/Effect/Ritual.
    if (/\b(fusion|synchro|xyz|link)\b/i.test(card.type)
        || /^(fusion|synchro|xyz|link)(_|$)/i.test(card.frameType ?? '')) return 'extra-deck';
    if (card.type === 'Token' || card.frameType === 'token') return 'token';
    if (card.type === 'Skill Card' || card.frameType === 'skill') return 'skill';
    const formats = card.misc_info?.flatMap(info => info.formats ?? []) ?? [];
    if (formats.includes('Speed Duel') && !formats.some(format => format === 'TCG' || format === 'OCG')) return 'speed-duel-only';
    if (!mainDeckTypes.has(card.type)) throw new Error(`Unreviewed eligibility type: ${card.type}`);
    if (card.card_sets?.length && card.card_sets.every(set => /championship.*prize card|make-a-wish/i.test(set.set_name))) {
        throw new Error(`Review promotional eligibility for ${card.id}: ${card.name}`);
    }
    return null;
}

export function eligibleCatalog(catalog) {
    return { ...catalog, cards: catalog.cards.filter(card => exclusionReason(card) === null) };
}

export function assertNoCatalogRegression(previous, next, allowedRemovals = []) {
    const ids = new Set(next.cards.map(card => card.id));
    const unexpected = previous.cards.filter(card => !ids.has(card.id)
        && exclusionReason(card) === null && !allowedRemovals.includes(card.id));
    if (unexpected.length) throw new Error(`Canonical coverage regression: ${unexpected.map(card => card.id).join(', ')}`);
}
