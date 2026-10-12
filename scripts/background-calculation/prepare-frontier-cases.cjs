// Run after prepare-audit-cases.cjs. Expectations must be frozen on the baseline.
// These are role-aggregated models, not tournament deck lists: 21 engine cards
// (9 Orcust monsters, 6 World Legacy monsters, 3 Orcust spells, 3 other extenders),
// 2 Return (OPT), 2 Allure (non-OPT), and 15 unrelated cards in the 40-card model.
// All 15 engine monsters are modeled as DARK. The 60-card variants add 20 blanks.
const fs = require('node:fs');
const assert = require('node:assert/strict');
const [source, destination] = process.argv.slice(2);
const audit = JSON.parse(fs.readFileSync(source, 'utf8'));
const names = ['example-baseline', 'example-advanced-no-conditions',
    'filtered-1475311-basic', 'filtered-1475311-advanced', 'filtered-20508881-advanced',
    'pre-draw-38120068-advanced', 'pre-draw-26845680-advanced',
    'realistic-three-graceful', 'trade-in-hand6', 'return-hand6-deck60',
    'post-mixed-enabled', 'mixed-hand6', 'mixed-hand7-tier0', 'mixed-hand7-tier1',
    'mixed-hand7-tier2', 'post-pathological-enabled', 'or-product-16', 'restrictive-22-hand5'];
const cases = names.map(name => {
    const fixture = audit.find(value => value.Name === name);
    assert(fixture, name);
    return { Name: name, Input: fixture.Input };
});
const mixed = structuredClone(audit.find(value => value.Name === 'pre-draw-26845680-advanced').Input);
const allure = structuredClone(audit.find(value => value.Name === 'filtered-1475311-advanced').Input.Cards[0]);
const dark = audit.find(value => value.Name === 'filtered-1475311-advanced').Input.Cards[1].Categories[1];
mixed.Cards[0].Copies = 2;
mixed.Cards[0].Id = 'return';
mixed.Cards.find(card => card.Id === 'z').Copies = 15;
for (const card of mixed.Cards.filter(card => ['a', 'b', 'c'].includes(card.Id))) card.Categories.push(dark);
allure.Id = 'allure';
allure.Copies = 2;
mixed.Cards.push(allure);
for (const [size, hand] of [[40, 5], [40, 6], [60, 5], [60, 6]]) {
    const input = structuredClone(mixed);
    input.HandSize = hand;
    input.Cards.find(card => card.Id === 'z').Copies += size - 40;
    assert.equal(input.Cards.reduce((sum, card) => sum + card.Copies, 0), size);
    cases.push({ Name: `return-allure-${size}-${hand}`, Input: input });
}
const basic = structuredClone(mixed);
basic.Options.ConditionalEffectsEnabled = false;
// Snapshot.Capture strips conditional configuration in Basic mode.
for (const card of basic.Cards) {
    card.PreDrawRemovalCount = 0;
    card.PostDrawDiscardCount = 0;
    card.PreDrawSelector = { Kind: 'Any', Categories: [] };
    card.PostDrawSelector = { Kind: 'Any', Categories: [] };
}
cases.push({ Name: 'return-allure-40-5-basic', Input: basic });
const routes = structuredClone(mixed);
// The older same-card fixture uses a different monster metadata identity.
// Use the actual preset model's category in the added AND requirements.
const monster = mixed.Cards.find(card => card.Id === 'a').Categories.find(category => category.MetadataKey === 'kind:monster');
const and = JSON.parse(JSON.stringify(audit.find(value => value.Name === 'or-and').Input.Combos[0]),
    (key, value) => value === 'metadata:card-type:monster' ? monster.Identity
        : value?.MetadataKey === 'card-type:monster' ? monster : value);
and.Name = 'Same-card OR routes';
and.GroupId = 'g';
routes.Combos.push(and);
cases.push({ Name: 'return-allure-40-5-or-and', Input: routes });
const copies = structuredClone(mixed);
copies.Cards.find(card => card.Id === 'return').Copies = 3;
copies.Cards.find(card => card.Id === 'allure').Copies = 1;
cases.push({ Name: 'return-allure-40-5-copies', Input: copies });
fs.writeFileSync(destination, JSON.stringify(cases));
console.log(`${cases.length} frontier inputs; freeze exact results before candidate replay`);
