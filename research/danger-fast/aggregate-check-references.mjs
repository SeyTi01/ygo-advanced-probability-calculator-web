// Validation only: the algorithm never reads these frozen references.
import fs from 'node:fs';
import { fileURLToPath } from 'node:url';
const read = name => fs.readFileSync(fileURLToPath(new URL(name, import.meta.url)), 'utf8');
const fixtures = JSON.parse(read('frozen-fixtures.json'));
const samples = read('aggregate-dynamic-results.jsonl').trim().split(/\r?\n/).map(JSON.parse);
function le(left, right) {
  const [a, b] = left.split('/').map(BigInt), [c, d] = right.split('/').map(BigInt);
  return a * d <= c * b;
}
for (const sample of samples) {
  const fixture = fixtures.Cases.find(value => value.Name === `danger-heavy-${sample.size}` && value.Kind === 'baseline');
  if (!fixture || sample.bounds.length !== fixture.ExactReference.length) throw Error('Missing reference');
  sample.bounds.forEach((bound, row) => {
    if (!le(bound.lower, fixture.ExactReference[row]) || !le(fixture.ExactReference[row], bound.upper)) throw Error(`Missed reference size=${sample.size} cap=${sample.cap} row=${row}`);
  });
}
console.log(`All ${samples.length} samples enclose all six frozen fractions by integer cross-products.`);
