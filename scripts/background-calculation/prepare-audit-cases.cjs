// Combine existing opt-in exports, adding explicitly synthetic boundary inputs.
// node prepare-audit-cases.cjs <export-directory> <output.json>
const fs = require('node:fs');
const path = require('node:path');
const [directory, output] = process.argv.slice(2);
const files = ['draw', 'post-draw', 'filtered-post-draw', 'pre-draw', 'realistic-graceful', 'same-card'];
const cases = files.flatMap(name => JSON.parse(fs.readFileSync(path.join(directory, `${name}-worker-cases.json`))))
    .filter(value => !value.Name.startsWith('small-'));
const copy = (name, source, edit) => {
    const input = structuredClone(cases.find(value => value.Name === source).Input);
    edit(input);
    cases.push({ Name: name, Input: input });
};
copy('example-advanced-no-conditions', 'example-baseline', input => { input.Options = {}; });
copy('trade-in-hand6', 'pre-draw-38120068-advanced', input => { input.HandSize = 6; });
copy('return-hand6-deck60', 'pre-draw-26845680-advanced', input => {
    input.HandSize = 6; input.Cards.find(card => card.Id === 'z').Copies += 20;
});
copy('mixed-hand6', 'post-mixed-enabled', input => { input.HandSize = 6; });
for (const tier of [0, 1, 2]) {
    copy(`mixed-hand7-tier${tier}`, 'post-mixed-enabled', input => { input.HandSize = 7; input.StorageTier = tier; });
}
copy('exhaustion-full-batch', 'three-copies-draw2', input => {
    input.Cards.forEach(card => { card.Copies = 1; }); input.HandSize = 3;
});
copy('pre-and-post-mixed', 'pre-draw-38120068-advanced', input => {
    input.Cards.find(card => card.Id === 'z').Copies -= 3;
    input.Cards.push({ ...structuredClone(input.Cards[0]), Id: 'post', Name: 'Filtered post-draw',
        DrawCount: 2, PostDrawDiscardCount: 1, PreDrawRemovalCount: 0, PreDrawSelector: { Kind: 'Any', Categories: [] } });
});
for (const [roles, hand] of [[20, 5], [21, 5], [22, 5], [15, 15], [16, 15]]) {
    const categories = Array.from({ length: roles }, (_, i) => ({ Name: `Role${i}`, Source: 0, MetadataKey: null }));
    const input = { Cards: categories.map((category, i) => ({ Id: `role${i}`, Copies: 2, Categories: [category], ManualMetadataCategoryKeys: [] }))
        .concat([{ Id: 'blank', Copies: 60 - roles * 2, Categories: [], ManualMetadataCategoryKeys: [] }]),
        Combos: categories.map(category => ({ Name: category.Name, Categories: [{ BaseCategory: category, MinCount: 1, MaxCount: 1, MaximumMode: 'Fixed' }], Cards: [] })),
        Groups: [], HandSize: hand, WorkUnits: 50000000 };
    cases.push({ Name: `restrictive-${roles}-hand${hand}`, Input: input });
}
for (const count of [4, 8, 16]) {
    copy(`or-product-${count}`, 'or-and', input => {
        input.Combos[0].AlternativeGroups = Array.from({ length: count }, () => structuredClone(input.Combos[0].AlternativeGroups[0]));
    });
}
copy('ordinary-zero-direct-overlap', 'atomic', input => {
    input.Combos.push({ Name: 'No Orcust plus direct monster', Categories: [{ ...input.Combos[0].Categories[0], MinCount: 0, MaxCount: 0 }],
        Cards: [{ CardId: 'bd', MinCount: 1, MaxCount: 0, MaximumMode: 1 }] });
});
fs.writeFileSync(output, JSON.stringify(cases));
console.log(`${cases.length} frozen inputs`);
