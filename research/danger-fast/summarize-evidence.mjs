import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
const directory = path.dirname(fileURLToPath(import.meta.url));
const fixtures = JSON.parse(fs.readFileSync(path.join(directory, 'frozen-fixtures.json'), 'utf8')).Cases;
const samples = fs.readFileSync(path.join(directory, 'anonymous-results.jsonl'), 'utf8').trim().split(/\r?\n/).map(JSON.parse)
  .filter(row => row.Event === 'Sample' && row.RetainedStratum && !row.WholeBatches && row.Capacity === 2048 && row.WorkLimit === 149000000);
const latest = new Map(samples.map(sample => [sample.Name, sample]));
const decimal = value => `${value / 1000000n}.${(value % 1000000n).toString().padStart(6, '0')}`;
const percent = (value, denominator, upper) => decimal((value * 100000000n + (upper ? denominator - 1n : 0n)) / denominator);
let text = '# Native research results — not production acceptance\n\n';
text += 'Variant: two separate anonymous ordinary-card strata, capacity 2048, 149M research Work, unchanged Standard Storage, Debug native. Timing includes solver preparation but excludes worker transport/input capture; samples are single serial observations, not cold/warm browser measurements. Every displayed lower endpoint is rounded down, upper and full width up to six decimal percentage digits. Exact integer endpoints remain in anonymous-results.jsonl.\n\n';
text += '**The practical target is not achieved.** Neither original heavy input reaches the initial 2pp width goal; the hand-size-six challenge remains much wider. No production route, public result contract or AOT/browser claim is enabled. Frozen heavy references are regression checks, not an independent mathematical proof.\n\n';
text += '| Input | Work | Native ms | Peak cells / structure | Aggregate cells estimate | Allocated bytes |\n| --- | ---: | ---: | ---: | ---: | ---: |\n';
for (const fixture of fixtures) {
  const sample = latest.get(fixture.Name);
  if (!sample) continue;
  text += `| ${fixture.Name} | ${sample.Work} | ${sample.Milliseconds.toFixed(2)} | ${sample.PeakCells} | ${sample.PeakConcurrentCells} | ${sample.AllocatedBytes} |\n`;
}
text += '\nAllocation totals are not live retained heap; payload estimates exclude object overhead. The first control sample includes native startup/JIT effects. No universal deadline follows from these timings.\n';
for (const fixture of fixtures) {
  const sample = latest.get(fixture.Name);
  if (!sample) continue;
  const labels = ['Any active combo', ...fixture.Input.Combos.map(row => row.Name), ...fixture.Input.Groups.map(row => row.Name)];
  const denominator = BigInt(sample.Denominator);
  text += `\n## ${fixture.Name}\n\nSource SHA256: \`${sample.SourceSha256}\`. Stop: ${sample.Stop}.\n\n`;
  text += '| Row | Lower % | Upper % | Full width pp |\n| --- | ---: | ---: | ---: |\n';
  sample.Rows.forEach((row, index) => {
    const lower = BigInt(row.Lower), upper = BigInt(row.Upper);
    text += `| ${labels[index]} | ${percent(lower, denominator, false)} | ${percent(upper, denominator, true)} | ${percent(upper - lower, denominator, true)} |\n`;
  });
}
fs.writeFileSync(path.join(directory, 'latest-native-results.md'), text);
console.log(`Wrote ${latest.size} case tables.`);
