// Independent artifact check: endpoints/reference containment, never a solver.
import fs from 'node:fs';
import crypto from 'node:crypto';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
const directory = path.dirname(fileURLToPath(import.meta.url));
const frozen = JSON.parse(fs.readFileSync(path.join(directory, 'frozen-fixtures.json'), 'utf8'));
const canonical = value => Array.isArray(value) ? value.map(canonical)
  : value && typeof value === 'object' ? Object.fromEntries(Object.keys(value).sort().map(key => [key, canonical(value[key])])) : value;
const hash = value => crypto.createHash('sha256').update(JSON.stringify(canonical(value))).digest('hex');
const cases = new Map(frozen.Cases.map(fixture => {
  if (hash(fixture.Input) !== fixture.InputSha256) throw Error(`Changed frozen input: ${fixture.Name}`);
  return [fixture.Name, fixture];
}));
const challengePath = path.join(directory, 'cheap-challenge-reference.json');
if (fs.existsSync(challengePath)) {
  const challenge = JSON.parse(fs.readFileSync(challengePath, 'utf8'));
  const fixture = cases.get(challenge.Name);
  if (!fixture || fixture.InputSha256 !== challenge.FrozenInputSha256) throw Error('Challenge identity mismatch');
  const fractions = challenge.Response.Result?.Contract?.Fractions;
  if (fractions) fixture.ExactReference = [fractions.Total, ...fractions.Combos, ...fractions.Groups]
    .map(value => `${value.Numerator}/${value.Denominator}`);
}
const summaries = [];
for (const filename of ['beam-results.jsonl', 'continuation-results.jsonl', 'anonymous-results.jsonl']) {
  const location = path.join(directory, filename);
  if (!fs.existsSync(location)) continue;
  const data = fs.readFileSync(location, 'utf8');
  const lines = data.split(/\r?\n/);
  if (!data.endsWith('\n')) lines.pop(); // interrupted append: only complete records
  let samples = 0, references = 0, rows = 0;
  for (const line of lines.filter(Boolean)) {
    const sample = JSON.parse(line);
    if (sample.Event && sample.Event !== 'Sample') continue;
    const fixture = cases.get(sample.Name);
    if (!fixture) throw Error(`Unknown measured input ${sample.Name}`);
    const denominator = BigInt(sample.Denominator);
    if (denominator <= 0n || sample.Rows.length !== 1 + fixture.Input.Combos.length + fixture.Input.Groups.length) throw Error('Malformed result rows');
    for (const [index, row] of sample.Rows.entries()) {
      const lower = BigInt(row.Lower), upper = BigInt(row.Upper);
      if (lower < 0n || lower > upper || upper > denominator) throw Error(`Invalid interval ${filename}/${sample.Name}/${index}`);
      if (fixture.ExactReference) {
        const [n, d] = fixture.ExactReference[index].split('/').map(BigInt);
        if (lower * d > n * denominator || upper * d < n * denominator) throw Error(`Reference escaped ${filename}/${sample.Name}/${index}`);
      }
      rows++;
    }
    samples++;
    if (fixture.ExactReference) references++;
  }
  summaries.push({ file: filename, samples, referenceCheckedSamples: references, rows });
}
console.log(JSON.stringify({ frozenInputsVerified: cases.size, evidence: summaries }, null, 2));
