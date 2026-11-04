import assert from "node:assert/strict";
import test from "node:test";
import { buildCatalog, serializeCatalog } from "./prepare-catalog.mjs";

function card(id, name, type, properties = {}) {
    return { id, name, type, ...properties };
}

function sourceCards() {
    return [
        card(7, "Zeta Pendulum", "Pendulum Effect Monster", {
            frameType: "effect_pendulum", race: "Spellcaster", attribute: "DARK", level: 7, scale: 1,
            archetype: "Zeta", desc: "not retained", card_prices: [{ price: "9" }],
            card_images: [{ id: 70, image_url: "https://images.invalid/70.jpg" }]
        }),
        card(6, "World Legacy - World Ark", "Trap Card", { frameType: "trap", race: "Counter", card_images: [] }),
        card(5, "MST", "Spell Card", { frameType: "spell", race: "Quick-Play", card_images: [{ id: 50 }] }),
        card(4, "Blue Eyes", "XYZ Monster", {
            frameType: "xyz", race: "Dragon", attribute: "LIGHT", level: 8,
            card_images: [{ id: 41 }, { id: 40 }, { id: 41 }]
        }),
        card(3, "Ash Blossom & Joyous Spring", "Tuner Monster", {
            frameType: "effect", race: "Zombie", attribute: "FIRE", level: 3, archetype: "Floowandereeze",
            card_images: [{ id: 30 }]
        }),
        card(2, "Linkuriboh", "Link Monster", {
            frameType: "link", race: "Cyberse", attribute: "DARK", linkval: 1, card_images: [{ id: 20 }]
        }),
        card(1, "Twin Name", "Effect Monster", { frameType: "effect", card_images: [{ id: 10 }] }),
        card(8, "Twin Name", "Normal Monster", { frameType: "normal", card_images: [{ id: 80 }] })
    ];
}

test("projects objective metadata and artwork identities while excluding provider payload", () => {
    const catalog = buildCatalog({ data: sourceCards() });
    const cards = new Map(catalog.cards.map(entry => [entry.id, entry]));

    assert.equal(catalog.version, 2);
    assert.equal(cards.get(7).scale, 1);
    assert.equal(cards.has(4), false);
    assert.equal(cards.get(6).race, "Counter");
    assert.equal(cards.get(5).race, "Quick-Play");
    assert.equal(cards.has(2), false);
    assert.deepEqual(cards.get(7).artworkImageIds, [70]);
    assert.equal(cards.get(7).canonicalCardId, 7);
    assert.equal(cards.get(7).artworkMetadataKnown, true);

    const serialized = serializeCatalog(catalog).toString("utf8");
    for (const forbidden of ["desc", "not retained", "card_prices", "image_url", "images.invalid"]) {
        assert.equal(serialized.includes(forbidden), false);
    }
});

test("orders records and serializes deterministically", () => {
    const cards = sourceCards();
    const first = serializeCatalog(buildCatalog({ data: cards }));
    const second = serializeCatalog(buildCatalog({ data: [...cards].reverse() }));
    assert.deepEqual(first, second);

    const namesAndIds = JSON.parse(first).cards.map(entry => [entry.name, entry.id]);
    assert.deepEqual(namesAndIds.slice(0, 3), [
        ["Ash Blossom & Joyous Spring", 3], ["MST", 5], ["Twin Name", 1]
    ]);
    assert.deepEqual(namesAndIds.filter(([name]) => name === "Twin Name"), [["Twin Name", 1], ["Twin Name", 8]]);
});

test("rejects empty or malformed catalogs and records", () => {
    for (const response of [null, [], {}, { data: [] }, { data: "bad" }]) {
        assert.throws(() => buildCatalog(response));
    }

    const invalidCards = [
        card(0, "Bad ID", "Effect Monster"),
        card(1, " ", "Effect Monster"),
        card(1, "Bad Type", ""),
        card(1, "Bad Level", "Effect Monster", { level: true }),
        card(1, "Bad Images", "Effect Monster", { card_images: [{ id: "10" }] }),
        card(1, "Bad Image Record", "Effect Monster", { card_images: [null] }),
        null
    ];
    for (const invalidCard of invalidCards) {
        assert.throws(() => buildCatalog({ data: [invalidCard] }));
    }

    assert.throws(() => buildCatalog({
        data: [card(1, "One", "Spell Card"), card(1, "Again", "Trap Card")]
    }), /duplicate/i);
});

test("rejects partial provider responses when invoked as a full catalog", () => {
    assert.throws(() => buildCatalog({ data: [card(1, "Only One", "Effect Monster")] }, 1_000), /incomplete/i);
});

test("writes compact versioned JSON", () => {
    const output = serializeCatalog(buildCatalog({ data: sourceCards() }));
    assert.equal(output.at(-1), 10);
    assert.equal(output.includes(Buffer.from(": ")), false);
    assert.deepEqual(Object.keys(JSON.parse(output)), ["version", "cards"]);
});

test("objective names preserve permanent equivalents but mentions require exact quoted references", () => {
    const source = [
        card(24094653, "Polymerization", "Spell Card", { desc: "Fusion Summon 1 Fusion Monster." }),
        card(74335036, "Fusion Substitute", "Spell Card", { desc: '(This card\'s name is always treated as "Polymerization".)' }),
        card(1, "Conditional", "Effect Monster", { desc: 'This card\'s name becomes "Polymerization" while on the field.' }),
        card(2, "Shining Sarcophagus imposter", "Effect Monster", { desc: 'Shining Sarcophagus without quotes is not evidence.' }),
        card(3, "Reference", "Effect Monster", { desc: 'If you control “Shining Sarcophagus”: do something.' }),
        card(4, "Regulus reference", "Spell Card", { desc: 'Add 1 "Regulus, the Prince of Endymion".' }),
        card(5, "No wording", "Effect Monster")
    ];
    const result = new Map(buildCatalog({ data: source }).cards.map(entry => [entry.id, entry]));
    assert.ok(result.get(74335036).resolvedNames.includes("Polymerization"));
    assert.equal(result.get(1).resolvedNames?.includes("Polymerization") ?? false, false);
    assert.deepEqual(result.get(2).mentionedNames, []);
    assert.deepEqual(result.get(3).mentionedNames, ["Shining Sarcophagus"]);
    assert.deepEqual(result.get(4).mentionedNames, ["Regulus, the Prince of Endymion"]);
    assert.equal(result.get(5).mentionedNames, null);
});
