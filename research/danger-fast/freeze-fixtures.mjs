// Run after MeasureDanger with YGO_DANGER_BENCHMARK_EXPORT_ONLY=1.
// No probability calculation or benchmark is executed by this script.
import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
import assert from 'node:assert/strict';

const root = new URL('./', import.meta.url);
const exported = JSON.parse(readFileSync(new URL('exported-inputs.json', root), 'utf8'));
const canonical = value => Array.isArray(value) ? value.map(canonical)
  : value !== null && typeof value === 'object'
    ? Object.fromEntries(Object.keys(value).sort().map(key => [key, canonical(value[key])])) : value;
const hash = value => createHash('sha256').update(JSON.stringify(canonical(value))).digest('hex');
let cheapSamples = [];
if (['control', 'single', 'mixed'].every(shape => existsSync(new URL(`${shape}-evidence.json`, root)))) {
  for (const shape of ['control', 'single', 'mixed']) {
    const measurements = JSON.parse(readFileSync(new URL(`${shape}-evidence.json`, root), 'utf8'));
    const journal = readFileSync(new URL(`${shape}-evidence.json.samples.jsonl`, root), 'utf8').trim().split('\n').map(JSON.parse);
    assert.equal(measurements.length, 4);
    assert.ok(journal.length === 8 || journal.length === 16, 'Expected four sample pairs, optionally with wire pairs');
    for (const measurement of measurements) {
      const events = journal.filter(sample => sample.Name === measurement.Name && sample.Trial === 1);
      assert.deepEqual(events.map(event => event.Phase), ['Started', 'Completed']);
      const completed = events[1];
      assert.equal(completed.Outcome, 'Success');
      assert.deepEqual(completed.Result, measurement.Response.Result);
      const wireCompleted = journal.find(sample => sample.Name === measurement.Name && sample.Phase === 'WireCompleted');
      if (wireCompleted) {
        assert.deepEqual(wireCompleted.Response, measurement.Response);
      }
      const contract = measurement.Response.Result.Contract;
      const fractions = contract.Fractions;
      cheapSamples.push({ Name: measurement.Name, InputSha256: hash(measurement.Input),
        Milliseconds: completed.Milliseconds, Work: completed.Work, PeakEntries: completed.PeakEntries,
        PeakCells: completed.PeakCells, AllocatedBytes: completed.AllocatedBytes,
        ExactReference: [fractions.Total, ...fractions.Combos, ...fractions.Groups].map(value => `${value.Numerator}/${value.Denominator}`),
        Accuracy: contract.Accuracy, Proof: contract.Proof, SolverRoute: contract.SolverRoute });
    }
  }
  writeFileSync(new URL('cheap-samples.json', root), JSON.stringify({ Configuration: 'Debug .NET 10.0.401 Windows x64; single trial, serial; includes JIT on first call. Not browser latency.', Samples: cheapSamples }, null, 2) + '\n');
} else if (existsSync(new URL('cheap-samples.json', root))) {
  cheapSamples = JSON.parse(readFileSync(new URL('cheap-samples.json', root), 'utf8')).Samples;
}
const references = {
  40: [
    '19155694438132844204083/22830578417398752000000',
    '111099644933409857/165035354964480000',
    '159817660221030078031/304407712231983360000',
    '34400725571562104809/203844450155346000000',
    '660816141258725116781/913223136695950080000',
    '34400725571562104809/203844450155346000000'
  ],
  60: [
    '1452870058184223003498319/2218129686429392736000000',
    '4935584606343002860291/12779347987659052800000',
    '1130830528179953545339391/3105381561001149830400000',
    '38681233721651543104931/188695060130278201500000',
    '10584126355376122068727271/21737670927008048812800000',
    '38681233721651543104931/188695060130278201500000'
  ]
};
const cases = exported.filter(item => item.Mode === 'Fast').map(item => {
  const exact = structuredClone(exported.find(other => other.Name === item.Name.replace(/fast$/, 'exact')).Input);
  exact.Options.Mode = item.Input.Options.Mode;
  assert.deepEqual(exact, item.Input, 'Fast/Exact fixture inputs must differ only by mode');
  return { Name: item.Name.replace(/-fast$/, ''), Kind: 'baseline', Input: item.Input,
    ExactReference: item.Shape === 'heavy' ? references[item.DeckSize] : null,
    ReferenceProvenance: item.Shape === 'heavy'
      ? 'Historical full physical frontier, diagnostic larger storage; retained by baseline MeasureDanger. Not an independent proof.'
      : 'Pending cheap reference execution; no probability inferred.' };
});
function challenge(name, base, description, mutate) {
  const input = structuredClone(cases.find(item => item.Name === base).Input);
  mutate(input);
  cases.push({ Name: name, Kind: 'challenge', Base: base, Change: description,
    Input: input, ExactReference: null, ReferenceProvenance: 'Frozen before tuning; not calculated.' });
}
challenge('challenge-heavy-40-hand6', 'danger-heavy-40', 'Hand size 6; original fixed maxima remain 5.', input => {
  input.HandSize = 6;
});
challenge('challenge-heavy-60-priority', 'danger-heavy-60', 'Custom priority: Graceful, Allure, Mothman, Nessie, Bigfoot, search.', input => {
  input.Options.DrawActivationPriority = { Mode: 1, CardIds: ['79571449', '1475311', '52350806', '70711847', '43316238', 'search'] };
});
challenge('challenge-heavy-40-multiplicity', 'danger-heavy-40', 'Bigfoot 1, Nessie 2, Mothman 3; add 2 filler to preserve deck size.', input => {
  for (const [id, count] of [['43316238', 1], ['70711847', 2], ['52350806', 3]]) {
    input.Cards.find(card => card.Id === id).Copies = count;
  }
  input.Cards.find(card => card.Id === 'filler').Copies += 2;
});
challenge('challenge-heavy-60-direct', 'danger-heavy-60', 'First combo additionally requires a direct overlap copy, distinct from existing positive slots; existing exclusion retained.', input => {
  input.Combos[0].Cards.push({ CardId: 'overlap', MinCount: 1, MaxCount: 1, MaximumMode: 0 });
});
challenge('challenge-mixed-40-direct-exclusion', 'danger-mixed-40', 'First combo direct overlap 1; exclusion combo additionally excludes direct starter entry; custom priority Nessie then Bigfoot then Upstart/search.', input => {
  input.Combos[0].Cards.push({ CardId: 'overlap', MinCount: 1, MaxCount: 5, MaximumMode: 0 });
  input.Combos[2].Cards.push({ CardId: 'starter', MinCount: 0, MaxCount: 0, MaximumMode: 0 });
  input.Options.DrawActivationPriority = { Mode: 1, CardIds: ['70711847', '43316238', '70368879', 'search'] };
});
for (const fixture of cases) {
  assert.equal(fixture.Input.WorkUnits, 150000000);
  assert.equal(fixture.Input.StorageTier, 0);
  fixture.InputSha256 = hash(fixture.Input);
  const cheap = cheapSamples.find(sample => sample.Name === `${fixture.Name}-fast`);
  if (cheap) {
    assert.equal(cheap.InputSha256, fixture.InputSha256);
    assert.deepEqual(cheap.ExactReference, cheapSamples.find(sample => sample.Name === `${fixture.Name}-exact`).ExactReference);
    fixture.ExactReference = cheap.ExactReference;
    fixture.ReferenceProvenance = 'Unchanged reference solver, Debug .NET 10.0.401, Fast/Exact native and wire parity checked. Not an independent oracle.';
  }
  fixture.ReferenceSha256 = fixture.ExactReference === null ? null : hash(fixture.ExactReference);
}
const data = { SourceCommit: 'bd75460e4bb3d9f990d8bc04bd1fcd915bcabc71',
  HashConvention: 'SHA-256 of UTF-8 JSON, recursively ordinal-key-sorted objects, original array order, no whitespace or trailing newline.',
  RowOrder: ['Any active combo', 'Starter and extender', 'Overlap', 'Extender without starter', 'Positive routes', 'Exclusion route'],
  Cases: cases };
writeFileSync(new URL('frozen-fixtures.json', root), JSON.stringify(data) + '\n');
writeFileSync(new URL('fixture-manifest.json', root), JSON.stringify({ SourceCommit: data.SourceCommit,
  HashConvention: data.HashConvention, Cases: cases.map(({ Name, Kind, InputSha256, ReferenceSha256 }) => ({ Name, Kind, InputSha256, ReferenceSha256 })) }, null, 2) + '\n');
console.log(`Frozen ${cases.length} inputs, including both historical heavy reference vectors; no calculations executed.`);
