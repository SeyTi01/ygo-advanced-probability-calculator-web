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

    assert.equal(catalog.version, 1);
    assert.equal(cards.get(7).scale, 1);
    assert.equal(cards.get(4).level, 8);
    assert.equal(cards.get(6).race, "Counter");
    assert.equal(cards.get(5).race, "Quick-Play");
    assert.equal(cards.get(2).linkVal, 1);
    assert.deepEqual(cards.get(4).artworkImageIds, [41, 40]);
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
        ["Ash Blossom & Joyous Spring", 3], ["Blue Eyes", 4], ["Linkuriboh", 2]
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
