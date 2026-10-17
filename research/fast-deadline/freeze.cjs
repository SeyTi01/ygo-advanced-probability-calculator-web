const fs = require('node:fs');
const path = require('node:path');
const cp = require('node:child_process');
const crypto = require('node:crypto');
const root = path.resolve(__dirname, '../..');
const prior = JSON.parse(cp.execFileSync('git', ['-c', `safe.directory=${root.replaceAll('\\', '/')}`, 'show',
    '487b344766734874d7c70c2d8f95c7f567ea62bc:research/fast-refinement/inputs/corpus.json'], {cwd: root, maxBuffer: 5000000}));
const corpus = structuredClone(prior);
const vsk = prior.find(x => x.Name === 'vsk9-three-desires');
for (const hidden of [3, 4]) {
    const c = structuredClone(vsk);
    Object.assign(c, {Name: `vsk9-hidden-${hidden}`, Split: hidden === 3 ? 'design' : 'held-out', Kind: 'hypothetical-saved-override'});
    c.Input.Cards.find(x => x.Id === 'research-desires').PreDrawDeckRemovalCount = hidden;
    corpus.push(c);
}
for (const size of [40, 60]) for (const hand of [5, 6]) {
    if (size === 40 && hand === 5) continue;
    const c = structuredClone(vsk);
    Object.assign(c, {Name: `vsk9-${size}-hand-${hand}`, Split: size === 60 ? 'held-out' : 'design', Kind: 'maintained-equivalent-variant'});
    c.Input.HandSize = hand;
    for (let remaining = size - 40, row = 0; remaining > 0; row++) {
        const copies = Math.min(3, remaining);
        c.Input.Cards.push({Id: `extra-filler-${row}`, Name: null, Copies: copies, Categories: [], ManualMetadataCategoryKeys: []});
        remaining -= copies;
    }
    corpus.push(c);
}
const catalog = JSON.parse(fs.readFileSync(path.join(__dirname, 'inputs/catalog.json'), 'utf8'));
for (const fixture of catalog.filter(x => x.Name.endsWith('-Fast'))) {
    const grouped = fixture.Input.Cards.some(c => c.Copies > 3);
    corpus.push({Name: `catalog-${fixture.Name.slice(0, -5)}`, Split: 'held-out',
        Kind: grouped ? 'grouped-filler-diagnostic' : 'catalog-backed-synthetic', Input: fixture.Input});
}
for (const c of corpus) {
    if (c.Name !== 'work-limited') c.Input.WorkUnits = 150000000;
    c.Input.StorageTier = 0;
}
const output = JSON.stringify(corpus, null, 2) + '\n';
fs.writeFileSync(path.join(__dirname, 'inputs/corpus.json'), output);
const hashes = {};
for (const file of ['protocol.md', 'freeze.cjs', 'inputs/corpus.json', 'inputs/catalog.json']) {
    hashes[file] = crypto.createHash('sha256').update(fs.readFileSync(path.join(__dirname, file))).digest('hex');
}
fs.writeFileSync(path.join(__dirname, 'inputs/freeze.json'), JSON.stringify({Utc: new Date().toISOString(), Baseline: '63a4780fb3fa937907c1c8be4662550197179f27', hashes}, null, 2));
console.log(JSON.stringify({cases: corpus.length, hashes}));
