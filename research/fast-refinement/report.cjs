const fs=require('node:fs');
const path=require('node:path');
const {resource,bounds,cases,coldRows}=require('./analyze.cjs');
const load=n=>JSON.parse(fs.readFileSync(path.join(__dirname,n),'utf8'));
const corpus=load('inputs/corpus.json'),hashes=load('inputs/sha256.json'),native=load('output/native.json');
const wasm=load('output/wasm.json');
const validation=load('output/validation.json');
const base='https://github.com/SeyTi01/ygo-advanced-probability-calculator-web/blob/63a4780fb3fa937907c1c8be4662550197179f27/';
const branch='https://github.com/SeyTi01/ygo-advanced-probability-calculator-web/blob/codex/research-fast-refinement-no-go/research/fast-refinement/';
const link=(p,label=p)=>`[${label}](${branch+p})`;
const fmt=(n,d=3)=>typeof n==='number'?n.toFixed(d):String(n);
const pct=p=>Number(p.Numerator)/Number(p.Denominator)*100;
const q=(a,k)=>a.map(x=>x[k]).sort((a,b)=>a-b)[4];
const timing=r=>`${fmt(r.P50Ms)}/${fmt(r.P90Ms)}`;
const table=(headers,rows)=>'| '+headers.join(' | ')+' |\n| '+headers.map(()=> '---').join(' | ')+' |\n'+rows.map(r=>'| '+r.join(' | ')+' |').join('\n')+'\n';
const rep=corpus.filter(x=>['design','held-out'].includes(x.Split));
let text=`# NO-GO: broadly useful Fast certificate refinement

**Decision: do not integrate this region-refinement mechanism.** At 100,000 additional Work it improves **0/6** meaningfully uncertain representative cases. At 1,000,000 it closes the intervals in **5/6**, but all five are slower than existing physical Exact and fail the <=25% full-Fast p50 overhead criterion in both native Release and optimized non-AOT WASM. The primary VS/K9 three-Desires workload gains **no endpoint improvement** at either budget. This is a negative investment decision from one tested reusable technique, not a mathematical impossibility claim about all future techniques.

Research-only evidence branch, **not for merge**. No production source, routing, schema, public contract, UI, dependency, CI or deployment changes; no implementation PR. Baseline is freshly fetched **63a4780fb3fa937907c1c8be4662550197179f27**, merged #224. The assigned workspace was clean and matched dev; dev remained at that SHA on the publication refresh. Protocol/corpus were frozen before baseline, design fixed after baseline, and the held-out inputs were not used to tune it. One prototype, no redesign or focused algorithm correction.

## Gates

| Gate | 100k budget | 1m budget |
| --- | --- | --- |
| Breadth, >=half of meaningfully uncertain representative cases | FAIL: 0/6 | PASS for precision alone: 5/6, including multi-route groups/totals |
| >=50% width reduction on a majority | FAIL: 0/6 | PASS: 5/6, all reaching zero width |
| <=25% full Fast p50 overhead on substantial improvements | No substantial improvement | FAIL: 0/5; Exact is faster on all five |
| Practical advantage on costly motivating cases | FAIL: VS/K9 unchanged | FAIL: VS/K9 unchanged; costly ordered/deferred controls ineligible |
| Proof/safety | Checked intervals valid; unfinished work retains bounds | Checked intervals valid; unchanged primary/Exact; no GO-grade cancellation/storage-fault certification claimed |
| Maintainability | Shared mechanism, no card formulas | Shared mechanism, but duplicates Exact work and offers no economic advantage |

Eligibility: **6/8** representative inputs; **8/14** of the complete corpus including artificial diagnostics. Two ordered/deferred representative cases decline immediately; three already-exact/no-search/opening controls decline; one Work-limited control cannot finish baseline. Six representative total/group intervals meet the predeclared >=2 pp threshold. The two ordered/deferred cases have nonzero but smaller widths (reported, excluded from the improvement denominator by the frozen threshold). Design subset: 3/4 improved at 1m; held-out subset: **2/2** meaningfully uncertain improved, with **2/4** held-out inputs ineligible. No initially zero-width row/case is counted as an improvement.

## Diagnosis and decision

The VS/K9 equivalent has total **[85.914392%, 90.085640%]**, physical Exact **89.931311%**. Of its **4.171249 pp** width, **4.016920 pp (96.30%)** is lower-side pessimism and **0.154329 pp** upper-side optimism. This attribution uses Exact, not the displayed midpoint. All six count-state representative totals are dominated by the lower gap; redundant physical targets often survive, while worst-case loss certificates cannot prove it. The per-row table separates both gaps, including groups and individual routes. It does not attribute all remaining dependence to union combination alone: no separate conditional-union ablation was performed.

The technique counts disjoint conditional draw-terminal regions in descending probability mass using existing physical predicates. For completed regions, let **S = sum P(region) P(success | region)** and **M = sum P(region)**. For each individual, group and total event, **S <= P(success) <= S + 1 - M**. Intersect that interval with the completed old interval. Unknown mass remains unknown; a half-computed region contributes nothing. Boolean OR combines search witnesses/combos before counting, so overlapping routes are never added as disjoint probabilities. The same deterministic draw/removal policy and labeled-copy sample space apply. IDs, competing searches, redundant targets, maxima and alternative requirements remain in the production compiler/counter.

The proof uses the existing guarded count-state exchangeability conditions: all batches fit and no identity-dependent hand operations occur. Relevant sources: [count-state applicability](${base}YGOProbabilityCalculatorBlazor/Services/ProbabilityCalculator/DrawEffectResolver.cs#L132), [physical joint hand/loss weighting](${base}YGOProbabilityCalculatorBlazor/Services/ProbabilityCalculator/PhysicalSearchCalculation.cs#L6), [shared physical counting](${base}YGOProbabilityCalculatorBlazor/Services/ProbabilityCalculator/PhysicalSearchCalculation.cs#L67), [certificate passes](${base}YGOProbabilityCalculatorBlazor/Services/ProbabilityCalculator/SearchAvailabilityCertification.cs#L75), [search assignment compiler](${base}YGOProbabilityCalculatorBlazor/Services/ProbabilityCalculator/SearchAccessPlan.cs#L77).

Actual reuse is code/predicates and a shared decision diagram/weighted memo across all output rows. The prototype rebuilds the physical draw distribution because Fast's coarser terminal map merges observed-source counts. It does **not** reuse that completed map or existing lower-pass count work. Work is charged for the duplicate resolver, eligibility, compilation, counting, sorting, exact aggregation and retained checkpoints. Runtime complexity remains the sum of completed physical-region costs plus one attempted unfinished region, with bounded resolver/diagram/memo storage. Each completed checkpoint validates all rows and diagnostic storage before publication. Child allowances are carved from remaining request Work with 4096 reserved; attempted child Storage peaks are reported separately. A failed child never converts a completed Fast result into failure.

This yields useful incremental bounds, but too late economically. On all five improved representative cases, even the **first >=50% total-width reduction** has already spent **more cumulative Work (baseline + refinement) than a standalone completed Exact request**:

`;
const incremental=[];
for(const fixture of rep){
 const n=native.find(x=>x.Name===fixture.Name), w=wasm.output.find(x=>x.Name===fixture.Name);
 const old=n.Samples.fast[0].Result?.SearchAvailability?.Total;
 if(!old)continue;
 const width=pct(old.Upper)-pct(old.Lower);
 const points=n.Samples.high[0].Refinement?.Checkpoints??[];
 const index=points.findIndex(p=>pct(p.Upper[0])-pct(p.Lower[0])<=width*.5+1e-10);
 if(index<0)continue;
 const p=points[index];
 const medianAt=s=>s.map(x=>x.PrimaryMilliseconds+x.CertificateMilliseconds+x.Refinement.Checkpoints[index].Milliseconds).sort((a,b)=>a-b)[4];
 incremental.push([fixture.Name,p.Work,n.Samples.fast[0].Work+p.Work,n.Samples.exact[0].Work,
  `${fmt(medianAt(n.Samples.high))} / ${fmt(q(n.Samples.exact,'Milliseconds'))}`,
  `${fmt(medianAt(w.Samples.high))} / ${fmt(q(w.Samples.exact,'Milliseconds'))}`]);
}
text+=table(['Case','Refinement Work at first half-width','Cumulative Work','Standalone Exact Work','Native checkpoint / Exact ms','WASM checkpoint / Exact ms'],incremental);
text+=`
The larger-budget wins are effectively an Exact pass appended after Fast. At 100k, representative regions are unfinished or the completed mass proves no tighter total/group interval. For VS/K9, 1m completes only the no-Desires region (66.244939% mass), which the baseline already bounds more effectively. The frozen workload exposes this redundancy directly. The artificial 32-search and 30-search/2-blank cases reach Exact even at 100k, but are excluded from investment success; standalone Exact remains cheaper there too.

**Recommendation:** keep this mechanism out of production; do not start another sequence of all-copies-mandatory special cases. Existing Exact mode is preferable on the five cheap count-state workloads. The current ordered/deferred limitations remain unresolved. Better sharing or stronger conditional sufficient proofs could change the economics, but that is conjecture and was not prototyped. No next production task is recommended because the GO gates failed.

## Frozen corpus and reproducibility

${link('protocol.md','Precommitted protocol')}, ${link('design.md','single mechanism and proof')}, ${link('README.md','commands and measurement details')}, ${link('inputs/corpus.json','frozen wire corpus')}, ${link('inputs/sha256.json','all input/session SHA-256 hashes')}.

Corpus SHA-256: **352ad6060b3370c10697a6fc506dfead2f20e2053e78775df4e0be79676e09d2**. All inputs use **150,000,000 Work / Standard Storage (32,768 entries, 262,144 cells)** except the explicit 1000-Work control. Hands are five cards. Both refinement caps are additional maxima within that same request allowance. Exact changes only the calculation mode. Every representative deck has 40 cards and <=3 copies per entry. Synthetic searches are hypothetical custom plans, not claims about printed legal decklists.

The exact user session was not supplied. **vsk9-three-desires is a maintained equivalent**, built from the bundled current example by replacing its three Fuwalos with three Desires and clearing the old monster categories. Its no-Desires control is the unmodified maintained session. The existing certificate helper's one-card banish variant was not misrepresented as three Desires. Ordered/deferred cases derive from existing FastCertificateBenchmark fixtures, splitting 28 irrelevant pooled fillers into <=3-copy identities before freezing; this changes computational representation and is disclosed rather than equated with old timing references. No prior timing is used as a reference.

`;
text+=table(['Input file (inputs/)','Split / classification','SHA-256'],corpus.map(f=>[link('inputs/'+f.Name+'.input.json',f.Name+'.input.json'),`${f.Split} / ${f.Kind}`,hashes.find(h=>h.File===f.Name+'.input.json').Hash.toLowerCase()]));
text+=`
Session counterparts use the same stem with .session.json; their exact hashes are in the manifest. The four held-out cases were structurally reserved before candidate design; they are a coverage split, not a statistical estimate of the population of player decks. All baseline outputs were visible during diagnosis; held-out candidate results were read only after fixing the mechanism, so this is not a fully blinded study. Bounded-search hand-cost/mode routes were inspected in current routing, but are not a separately timed corpus class; the main corpus exercises ordered draw/removal and deferred paths. This limitation cannot rescue the observed runtime failure and is not used to claim broader coverage.

## Native and optimized WASM measurements

Windows 11 build 26200, AMD Ryzen 7 5700X (8 cores/16 logical processors), approximately 32 GiB RAM; .NET SDK **10.0.401**, runtime **10.0.12**. Browser **Edge ${wasm.Browser}**. Release publish used SDK-scoped wasm-tools, emcc and wasm-opt **-O2**, **RunAOTCompilation=false**. Local localhost worker execution only. Native used **DOTNET_TieredCompilation=0**. Native, browser, test and publish jobs were run serially. Five sides (primary-only diagnostic, full Fast, +100k, +1m, physical Exact), two warmups, ten seeded randomized/interleaved warmed samples. p50/p90 are nearest-rank empirical quantiles. JSON materialization/serialization/transport and native pre-call GC are excluded consistently; primary + baseline certificate/public result + optional endpoints/checkpoints are included. No UI integration exists, so no button-to-paint claim.

Warm suite first observations are retained separately; later fixtures share warmed runtimes. Separate cold files use fresh native processes and fresh WASM module workers per input/mode, with browser bootstrap recorded separately. Cold observations are singletons, not p50/p90 distributions. Raw evidence: ${link('output/native.json')}, ${link('output/wasm.json')}, ${link('output/cold-native.json')}, ${link('output/cold-wasm.json')}. Full phase timing, managed allocation, observed managed heap, working set/WASM capacity and resource counters: ${link('output/resources.csv')}. Allocations are cumulative current-thread allocations; heap snapshots and linear-memory capacity are **not peak live memory**. Checked entries/cells bound individual structures, not total process RAM.

All successful baseline public results/fractions/routes are identical under both refinement caps. Every native/WASM row, checkpoint and successful Exact reference agrees. Exact never runs the optional certificate. Work-limited Exact requests supply **no reference probability**, not zero. Ineligible controls retain fractions/Work/storage; small timing noise and extra diagnostic allocations remain in the tables.

<details><summary>Complete warm runtime, allocation and resource comparison (all cases/modes)</summary>

Native/WASM time cells are p50/p90 milliseconds. Allocation cells are native/WASM p50 MiB. Work is total request Work; refinement Storage is separate from baseline and is the attempted peak, including failed checks. Exact and primary-only rows report their own public-conversion overhead rather than attributing it to a Fast certificate.

`;
const perf=[];
for(const f of corpus)for(const side of ['fast','low','high','exact','primary']){
 const n=resource.find(x=>x.Platform==='native'&&x.Case===f.Name&&x.Side===side);
 const w=resource.find(x=>x.Platform==='wasm'&&x.Case===f.Name&&x.Side===side);
 perf.push([f.Name,side,n.Outcome+(n.Refinement?' / '+n.Refinement:''),timing(n),timing(w),`${fmt(n.P50Allocated/1048576)}/${fmt(w.P50Allocated/1048576)}`,`${n.Work}/${w.Work}`,
  `${n.BaseEntries}/${n.BaseCells}`,`${n.RefinementEntries}/${n.RefinementCells}`]);
}
text+=table(['Case','Side','Completion / refinement','Native ms','WASM ms','Allocated MiB N/W','Work N/W','Baseline entries/cells N','Child entries/cells N'],perf);
text+='\nWASM-specific storage counters, per-side p90 allocations and all observed heap/working-set/capacity values are retained in resources.csv, without treating them as managed peaks. A displayed 0.000 ms means below browser timer resolution. Certificate-phase counters include public result assembly/capability checks, rather than an isolated certificate-kernel measurement.\n\n</details>\n\n';
text+='Cold VS/K9 first engine observations (singletons; not warmed quantiles):\n\n';
text+=table(['Mode','Fresh native process ms','Fresh WASM worker ms','WASM bootstrap ms'],['fast','low','high','exact','primary'].map(side=>{
 const n=coldRows.find(x=>x.Platform==='native'&&x.Case==='vsk9-three-desires'&&x.Side===side);
 const w=coldRows.find(x=>x.Platform==='wasm'&&x.Case==='vsk9-three-desires'&&x.Side===side);
 return [side,fmt(n?.Milliseconds),fmt(w?.Milliseconds),fmt(w?.BootstrapMilliseconds)];
}));
text+='\n'+link('output/cold.csv','Every cold observation and bootstrap time')+'; '+link('output/environment.json','toolchain, hardware and measurement settings')+'.\n\n';
text+=`At 1m the five improved native cases allocate approximately 1.94–2.19x baseline bytes. VS/K9 allocates 21,623,720 -> 32,123,336 native bytes despite unchanged endpoints; its child spends 999,996 Work and reaches 18,532 checked entries / 90,538 cells. Ordered/deferred inputs add only the declined-probe diagnostic allocation (408 native bytes), no charged refinement Work. Exact on those two cases repeatedly reaches the unchanged 150m Work limit. No baseline-success case becomes a failure.

## Row-wise intervals and Exact references

Percentages below are display conversions of retained exact rational endpoints. Raw fractions are authoritative. ${link('output/rows.csv')} includes every row's absolute/relative reduction and lower/upper distance from Exact; ${link('output/checkpoints.csv')} records when bounds became available. Total/group results are genuine Boolean unions.

<details><summary>Every completed output row: baseline, both budgets, Exact and width reductions</summary>

`;
const rowTable=[];
for(const r of bounds.filter(x=>x.Platform==='native'&&x.Budget==='high')){
 const low=bounds.find(x=>x.Platform==='native'&&x.Case===r.Case&&x.Row===r.Row&&x.Budget==='low');
 rowTable.push([`${r.Case} / ${r.Type} ${r.Row}: ${r.Label}`,`${fmt(r.Lower,6)}–${fmt(r.Upper,6)}`,fmt(r.Exact,6),
  `${fmt(low.NewLower,6)}–${fmt(low.NewUpper,6)}`,`${fmt(r.NewLower,6)}–${fmt(r.NewUpper,6)}`,
  `${fmt(low.ReductionPP,6)} (${fmt(low.ReductionPercent,2)}%)`,`${fmt(r.ReductionPP,6)} (${fmt(r.ReductionPercent,2)}%)`]);
}
text+=table(['Case / row','Old L–U %','Physical Exact % or limit','100k L–U %','1m L–U %','100k width reduction pp (%)','1m width reduction pp (%)'],rowTable);
text+=`
The Work=1000 case has no completed rows on any side. Already-exact controls show zero-width fractions from their result contract; they do not acquire a new certificate or refinement result.

</details>

## Validation, limitations and stopping point

Targeted Release checks: **240 passed**, including seven disposable research tests that compare every completed checkpoint/final interval at five caps (1, 100, 1000, 100k, 1m) against the independent labeled-copy SearchCertificateOracle, and existing TargetSurvivalCertificateTest / PhysicalSearchTest / SearchAvailabilityCertificateTest coverage. The independent oracle retains actual physical suffix identities and does not call the production compiler/weighted survivor counter. Large-deck Exact references reuse the production physical solver, so they are regression references, not an independent implementation; the small oracle supplies independent correctness checks. The artificial 32-search reference is additionally covered by the existing independent chronological opening/banish/draw partition test.

Full Release suite with the external research include: **2,528 passed, 0 failed**, 1m58s. The measurement build has one CA1416 warning because the disposable native test calls the browser-annotated JS-export probe; the same probe is intentionally used through reflection by the native runner. No production diagnostic was suppressed. The app publish succeeds with normal optimized non-AOT linking. Frozen input/session hashes and the absence of production diffs were rechecked. This investigation stopped well within the six-hour ceiling after the runtime gate failed; the remaining work completed paired evidence and publication rather than another design iteration.

Offline verification made **${validation.Assertions} assertions** over the frozen baseline and warmed/cold records: deterministic primary output, unchanged public contracts, rational nesting at every checkpoint, physical Exact enclosure where completed, caps, and native/WASM output agreement. No proof-failure status occurred. Low-budget interrupted regions retain the last complete interval. Cancellation and forced child-Storage/diagnostic-allocation fault injection were **not** exhaustively tested; this research result does not certify production robustness. No integration is recommended in any event.

Commands: restore, Release build with the external targets, targeted dotnet test -c Release --no-build --filter ..., local optimized publish with RunAOTCompilation=false, native/Playwright replay, node research/fast-refinement/analyze.cjs, and publication privacy/diff checks. Exact invocations and environment prerequisites are in the linked README. .NET info/restore/tests, successful Release build/publish, browser launch and Git writes used scoped approved Windows execution. Restricted build exited 1 without diagnostics; approved build succeeded. Restricted Edge closed during launch; approved replay succeeded. The first native calibration's incorrect mode discriminator was discarded and repaired before baseline diagnosis/prototype design; no results from it enter these tables.

Stop reason: repeatable runtime failure plus no improvement on the primary costly workload, after one mechanism. No additional special-case formulas, expanded limits, or second prototype were pursued. Frozen evidence remains on the research branch for audit only.
`;
fs.writeFileSync(path.join(__dirname,'REPORT.md'),text);
console.log('Report bytes:',Buffer.byteLength(text));
