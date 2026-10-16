import assert from "node:assert/strict";
import test from "node:test";
import { createResolver, resolveMembership, auditMembership } from "./archetype-membership.mjs";
import { buildCatalog, projectCard, serializeCatalog } from "./prepare-catalog.mjs";

const resolve = (name, desc = "Ordinary effect.", archetype) => resolveMembership({ id: 1, name, desc, archetype });

test("association and mentioned effect targets do not confer membership", () => {
    for (const name of ["Solar Recharge", "Charge of the Light Brigade"]) {
        assert.deepEqual(resolve(name, 'Discard 1 "Lightsworn" monster. Draw 2 cards.', "Lightsworn").archetypes, []);
    }
    assert.deepEqual(resolve("Raiden, Hand of the Lightsworn").archetypes, ["Lightsworn"]);
    assert.deepEqual(resolve("Ryko, Twilightsworn Fighter").archetypes, ["Lightsworn"]);
});

test("permanent declarations work after flavor and material text, and with multiple memberships", () => {
    assert.deepEqual(resolve("Summoned Skull", 'A fiend with dark powers.\n(This card is always treated as an "Archfiend" card.)').archetypes, ["Archfiend"]);
    assert.deepEqual(resolve("Unknown", '(This monster is also always treated as a “Dogmatika” and “Despia” monster.)').archetypes, ["Despia", "Dogmatika"]);
    assert.deepEqual(resolve("Unknown", '(This card is always treated as an "Artmage", "DoomZ", and "Elfnote" card.)').archetypes, ["Artmage", "DoomZ", "Elfnote"]);
    assert.deepEqual(resolve("Unknown", '(This card is always treated as a card "Fur Hire".)').archetypes, ["Fur Hire"]);
    assert.deepEqual(resolve("Unknown", "(This card is always treated as an 'Archfiend' card.)").archetypes, ["Archfiend"]);
    assert.deepEqual(resolve("Double Iris Magician", '[ Monster Effect ]\n(This card is always treated as a "Pendulum Dragon" card.)').archetypes, ["Magician", "Pendulum", "Pendulum Dragon"]);
});

test("exclusions outrank printed names and inclusions", () => {
    assert.deepEqual(resolve("Hardened Armed Dragon", '(This card is not treated as an "Armed Dragon" card.)').archetypes, []);
    assert.deepEqual(resolve("Violet Crystal", '(This card is never treated as a "Crystal" card.) (This card is always treated as a "Crystal" card.)').archetypes, []);
    assert.deepEqual(resolve("Diffusion Wave-Motion", '(This card is not treated as a "Fusion" card.)').archetypes, []);
});

test("name substitution replaces printed-name membership, additional names retain it", () => {
    assert.deepEqual(resolve("A Legendary Ocean", '(This card\'s name is always treated as "Umi".)').archetypes, ["Umi"]);
    assert.deepEqual(resolve("Fusion Substitute", '(This card is always treated as "Polymerization".)').archetypes, ["Polymerization"]);
    assert.deepEqual(resolve("Cyber Harpie Lady", '(This card\'s name is always treated as "Harpie Lady".)').archetypes, ["Harpie"]);
    assert.deepEqual(resolve("Harpie", '(This card is also always treated as "Umi".)').archetypes, ["Harpie", "Umi"]);
});

const reviewedCards = [
    { id: 13857930, name: "Neo-Spacian Twinkle Moss", type: "Fusion Monster",
        desc: 'This card\'s name is also treated as "Neo-Spacian Glow Moss".' },
    { id: 78734254, name: "Neo-Spacian Marine Dolphin", type: "Fusion Monster",
        desc: 'This card\'s name is also treated as "Neo-Spacian Aqua Dolphin".' },
    { id: 34103656, name: "Lemuria, the Forgotten City", type: "Spell Card",
        desc: 'This card\'s name is treated as "Umi".' }
];

test("reviewed older name clauses add or replace classification without broadening unknown wording", () => {
    for (const card of reviewedCards) {
        const result = resolveMembership(card);
        assert.deepEqual(result.unresolved, []);
        assert.deepEqual(result.archetypes, [card.id === 34103656 ? "Umi" : "Neo-Spacian"]);
        // Exact-name candidates make add-vs-replace observable even where both
        // Neo-Spacian names happen to confer the same real archetype.
        const treatedName = card.desc.match(/"([^"]+)"/)[1];
        const classifyNames = createResolver({ version: 1, candidates:
            [card.name, treatedName].map(name => ({ name, match: "exact" })) });
        assert.deepEqual(classifyNames(card).archetypes,
            (card.id === 34103656 ? [treatedName] : [card.name, treatedName]).sort());
        for (const changed of [{ ...card, id: 1 }, { ...card, name: "Different card" },
            { ...card, type: "Skill Card" }, { ...card, desc: card.desc.replace(/\.$/, " while face-up.") }]) {
            assert.equal(resolveMembership(changed).declarations.length, 0);
        }
    }
    assert.deepEqual(resolveMembership({ ...reviewedCards[2],
        desc: 'This card\'s name is treated as "Umi". This card is not treated as an "Umi" card.' }).archetypes, []);
    assert.equal(resolve("Unknown", 'This card\'s name is treated as "Umi".').unresolved.length, 1);
});

test("Skill identity remains explicitly out of scope and catalog projection preserves original identity", () => {
    const skill = { id: 300201007, name: "It's a Toon World!", type: "Skill Card",
        desc: '[At the start of the Duel, place this card in your center Spell & Trap Zone and flip it over.]\nThis card is treated as “Toon World”.' };
    assert.deepEqual(resolveMembership(skill).archetypes, []);
    assert.equal(auditMembership([skill]).unresolved[0].reason, "out-of-scope-skill-identity-clause");
    assert.equal(projectCard(skill).resolvedArchetypes, null);
    // Even an explicit "always" does not turn Skill setup into Main Deck identity.
    assert.equal(projectCard({ ...skill, desc: '(This card is always treated as "Toon World".)' }).resolvedArchetypes, null);
    assert.deepEqual(resolveMembership({ ...skill, name: "Mythic Depths",
        desc: 'This card\'s name becomes "Umi" while on the field.' }).unresolved, []);
    assert.equal(resolveMembership({ ...skill, name: "Tomb of the Pharaoh",
        desc: 'This card\'s name is always treated as "Necrovalley".' }).unresolved[0].reason,
        "out-of-scope-skill-identity-clause");
    for (const card of reviewedCards) {
        const projected = projectCard({ ...card, card_images: [{ id: card.id }, { id: card.id + 1 }] });
        assert.equal(projected.id, card.id);
        assert.equal(projected.canonicalCardId, card.id);
        assert.equal(projected.name, card.name);
        assert.deepEqual(projected.artworkImageIds, [card.id, card.id + 1]);
        assert.deepEqual(Object.keys(projected).sort(),
            ["id", "canonicalCardId", "name", "type", "artworkImageIds", "artworkMetadataKnown", "resolvedArchetypes", "resolvedNames", "mentionedNames"].sort());
    }
    assert.throws(() => createResolver(undefined, [{ id: 1, operation: "guess" }]), /reviewed name clauses/);
});

test("zone conditions, activatable changes and mentions are never permanent aliases", () => {
    for (const desc of [
        'This card\'s name becomes "Umi" while on the field or in the GY.',
        'While face-up, this card is treated as "Umi".',
        'This card\'s name is treated as "Umi" in your hand.',
        'You can discard 1 card; this card\'s name becomes "Umi".',
        'You can target 1 "Umi" card; add it to your hand.'
    ]) assert.deepEqual(resolve("Harpie Perfumer", desc).archetypes, ["Harpie"]);
});

test("malformed or unknown permanent clauses surface uncertainty without claiming membership", () => {
    for (const desc of [
        'This card is always treated as a "Lightsworn" card while face-up.',
        'If you control a monster, this card is always treated as a "Lightsworn" card.',
        'This card is always treated as a "Unreviewed Family" card.',
        'This card is always treated as a "Lightsworn" card',
        'This card is always considered a "Lightsworn" card.',
        'This card\'s name is treated as "Umi" at all times.',
        'This card\'s name is also treated as "Neo-Spacian Aqua Dolphin".',
        'This card isn\'t treated as a "Lightsworn" card.',
        '(This card\'s name is always treated as "Umi".) (This card\'s name is always treated as "Harpie Lady".)',
        '', null
    ]) {
        const result = resolve("Lightsworn Example", desc);
        assert.deepEqual(result.archetypes, [], desc);
        assert.ok(result.unresolved.length, desc);
    }
    const report = auditMembership([{ id: 22, name: "Unknown", desc: 'This card is always treated as a "New Family" card.' }]);
    assert.equal(report.unresolved[0].id, 22);
    assert.match(report.unresolved[0].excerpt, /New Family/);
});

test("reviewed boundaries preserve punctuation, nested names and established spellings", () => {
    for (const [name, expected] of [
        ["Orcustrated Return", ["Orcust"]], ["World Legacy - World Wand", ["World Legacy"]],
        ["Radiant Typhoon Vision", ["Radiant Typhoon"]], ["blue‑eyes white dragon", ["Blue-Eyes"]],
        ["Gravekeeper’s Spy", ["Gravekeeper's"]], ["Hi-Speedroid Cork Shooter", ["Roid", "Speedroid"]],
        ["HEROIC Champion", ["Heroic"]], ["Hero Kid", []], ["Elemental HERO Neos", ["Elemental HERO", "HERO", "Neos"]],
        ["Umibozu", []], ["Thunder Kid", []], ["Revolution", []], ["Dark World Dealings", ["Dark World"]]
    ]) assert.deepEqual(resolve(name).archetypes, expected, name);
    assert.deepEqual(resolve("LIGHTSWORN Example").archetypes, ["Lightsworn"]);
});

test("catalog ships resolved data only and repeats byte identically", () => {
    const cards = [
        { id: 1, name: "Solar Recharge", type: "Spell Card", archetype: "Lightsworn", desc: 'Discard 1 "Lightsworn" monster.' },
        { id: 2, name: "Summoned Skull", type: "Normal Monster", desc: '(This card is always treated as an "Archfiend" card.)' }
    ];
    const first = serializeCatalog(buildCatalog({ data: cards }));
    assert.deepEqual(first, serializeCatalog(buildCatalog({ data: [...cards].reverse() })));
    assert.equal(first.toString().includes('"desc"'), false);
    assert.equal(first.toString().includes('"archetype"'), false);
    assert.deepEqual(JSON.parse(first).cards[0].resolvedArchetypes, []);
    assert.throws(() => resolveMembership({ id: 1, name: "Valid", desc: 42 }), /invalid/);
    assert.throws(() => createResolver({ version: 1, candidates: [{ name: "A" }, { name: "a" }] }), /duplicate/);
});
