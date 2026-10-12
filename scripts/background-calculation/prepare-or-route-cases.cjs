// Run after prepare-audit-cases.cjs. Freeze Replay's Trial=0 records on the baseline.
// node prepare-or-route-cases.cjs <audit-matrix.json> <output.json>
const fs = require('node:fs');
const [source, output] = process.argv.slice(2);
const matrix = JSON.parse(fs.readFileSync(source));
const names = ['or-product-4', 'or-product-8', 'or-product-16', 'or-and',
    'example-baseline', 'example-fuwalos-draw3-once', 'restrictive-21-hand5', 'restrictive-22-hand5'];
const cases = matrix.filter(value => names.includes(value.Name)).map(value => ({ Name: value.Name, Input: value.Input }));
function add(name, sourceName, edit) {
    const input = structuredClone(cases.find(value => value.Name === sourceName).Input);
    edit(input);
    cases.push({ Name: name, Input: input });
}
add('or-permuted-8', 'or-product-8', input => {
    input.Cards.reverse();
    input.Combos[0].AlternativeGroups.forEach((group, index) => {
        if (index % 2) group.Alternatives.reverse();
    });
});
add('or-direct-bounds-8', 'or-product-8', input => {
    input.Combos[0].AlternativeGroups.forEach(group => {
        group.Alternatives = [
            { Kind: 'Card', Card: { CardId: 'ab', MinCount: 2, MaxCount: 3, MaximumMode: 0 } },
            { Kind: 'Card', Card: { CardId: 'bd', MinCount: 0, MaxCount: 0, MaximumMode: 0 } }
        ];
    });
});
add('or-non-equivalent-8', 'or-product-8', input => {
    input.Combos[0].AlternativeGroups.forEach((group, index) => {
        group.Alternatives[0] = { Kind: 'Card', Card: { CardId: `distinct-${index}`, MinCount: 1, MaxCount: 0, MaximumMode: 1 } };
    });
});
add('or-non-equivalent-16', 'or-product-16', input => {
    input.Combos[0].AlternativeGroups.forEach((group, index) => {
        group.Alternatives[0] = { Kind: 'Card', Card: { CardId: `distinct-${index}`, MinCount: 1, MaxCount: 0, MaximumMode: 1 } };
    });
});
for (const count of [8, 16]) {
    for (const advanced of [false, true]) {
        add(`or-${advanced ? 'advanced' : 'basic'}-${count}`, `or-product-${count}`, input => {
            input.Cards.find(card => card.Id === 'f').Copies -= 1;
            input.Cards.push({ Id: 'effect', Copies: 1, Categories: [], ManualMetadataCategoryKeys: [],
                DrawCount: 2, DrawOncePerTurn: true, PreDrawRemovalCount: advanced ? 1 : 0,
                PostDrawDiscardCount: advanced ? 1 : 0 });
            input.Options = { ConditionalEffectsEnabled: advanced };
        });
    }
}
add('or-work-limit', 'or-product-8', input => { input.WorkUnits = 1; });
fs.writeFileSync(output, JSON.stringify(cases));
console.log(`${cases.length} OR-route comparison inputs`);
