const fs=require('node:fs');
const path=require('node:path');
const root=__dirname;
const read=file=>JSON.parse(fs.readFileSync(path.join(root,'output',file)));
const baseline=[...read('baseline-summary.json').summary,...read('supplemental-summary.json').summary];
const candidate=read('production-summary.json');
const explicit=read('explicit-summary.json');
const smoke=read('smoke-final.json');
const band=read('production.json').runs.filter(r=>r.name==='band-extra-draw-2'&&r.iteration>=0);
const bandUpgrades=band.filter(r=>r.response.Result.Contract.AutomaticExact===0).length;
const sec=value=>value==null?'—':(value/1000).toFixed(2);
const pair=(a,b)=>`${sec(a)} / ${sec(b)} s`;
const labels=[['vsk9-hidden-3','VS/K9 hidden-loss 3'],['vsk9-three-desires','VS/K9, three Desires'],['band-extra-draw-2','Two extra draw sources (synthetic)'],['ordered-target-loss-40','Ordered target loss'],['deferred-target-loss-40','Deferred target loss'],['catalog-ordered-219-eleven','Slow initial Fast'],['catalog-branded-distinct','Already Exact, distinct fillers']];
const lines=labels.map(([name,label])=>{
 const fast=baseline.find(r=>r.name===name&&r.mode==='fast');
 const exact=baseline.find(r=>r.name===name&&r.mode==='exact');
 const after=candidate.summary.find(r=>r.name===name);
 const outcome=after.outcomes.map(x=>({0:'Exact upgrade',1:'retained bound after deadline',6:'initial Fast completed; no upgrade','already-exact':'already Exact'})[x]??x).join(', ');
 return `| ${label} | ${pair(fast.dispatchP50,fast.dispatchP90)} | ${exact.warmN?pair(exact.dispatchP50,exact.dispatchP90):'Work limit; see single-run evidence'} | ${pair(after.p50,after.p90)} | ${outcome} |`;
});
const capacity=Math.max(...candidate.summary.map(r=>r.maxCapacity));
const lateness=candidate.summary.flatMap(r=>r.timeouts).map(r=>r.latenessMs);
const evidence=process.argv[2]||'RESEARCH_COMMIT_URL';
const text=`Fast now retains its completed normal result and tries physical Exact when the remaining opportunity permits it. Affordable Exact wins even when it takes several seconds longer. An unfinished upgrade is terminated and the completed Fast response is returned with its original fractions and certificate.

Initial Fast is **never automatically timed out**: it keeps the existing Work/Storage limits and manual cancellation, including when it needs more than seven seconds. Proven Exact returns immediately. Otherwise, an upgrade gets at most five seconds and must finish within seven seconds of dispatching the initial pass to a ready worker. If initial Fast has already used that window, return it without another pass. Explicit Exact and Opening Hand keep their existing behavior.

One worker runs the stages sequentially. Exact receives only unused Work from the original allowance (150M in the UI), with the original Storage tier and unchanged input/activation policy. Successful physical counting keeps requested/executed Fast and Accuracy=Exact. Deadline, Work, Storage, Unsupported, and recoverable failures retain the completed fallback. Cancellation and replacement still reject stale completions. Immutable accuracy notes follow pins and copied results; the existing collapsed disclosure and midpoint ± presentation remain intact.

The seven-second opportunity is the one focused correction to the original six-second design: six seconds rejected a 4.6-second Exact calculation after about 1.4–1.6 seconds of Fast. It still prevents two four-second stages becoming an eight-second wait. A slow initial pass can leave insufficient time for an otherwise affordable standalone Exact calculation; this is an explicit tradeoff, not a prediction guarantee.

Optimized non-AOT Edge ${smoke.browser}, .NET 10.0.401 / runtime 10.0.12, Standard Storage. Worker dispatch-to-response warm p50 / p90 below (instrumented unchanged references versus the shipped owner/wire) exclude two warmups and cold worker starts. Ten recorded attempts per successful case; actual warm counts are in the summaries. Frozen 37-case primary corpus plus six predeclared supplemental cases. VS/K9 is a maintained equivalent, not the user's exact saved session. Artificial grouped-copy controls are correctness evidence, not speedup claims.

| Case | Baseline Fast | Standalone physical Exact | New Fast | Outcome |
|---|---:|---:|---:|---|
${lines.join('\n')}

The 4.6-second reference case upgraded in ${bandUpgrades}/${band.length} recorded attempts (10/12 including warmups). Two attempts reached the five-second watchdog; one response arrived at about 5.006 seconds and correctly lost the deadline race. This runtime variation is disclosed, not treated as a failed calculation or hidden by dropping samples.

Ordered/deferred standalone Exact hit the original Work limit after 22.85 / 22.35 seconds in single observations. New Fast intentionally spends about five extra seconds seeking Exact before returning their unchanged bounds. The slower initial Fast case still completes around twelve seconds; already-Exact distinct-filler controls take around nine seconds. A separate mixed filtered case still ends at its original Work limit around fifteen seconds, and the storage-limited reference still returns its typed Storage failure. The already-Exact distinct-filler p90 increased from 9.51 to 11.61 seconds in this matrix, despite identical charged Work and no second solver; this timing regression is disclosed rather than attributed to a proven cause. These small host samples do not establish a universal speedup. There is no guarantee of a completed bounded result for every input.

Browser UI smoke measured ${sec(smoke.cases[0].run.uiMs)} seconds button-to-result for the cold VS/K9 upgrade and ${sec(smoke.cases[1].run.uiMs)} seconds for the ordered fallback. Three genuinely synchronous infinite-loop upgrades were terminated; page heartbeats continued and all worker contexts closed. A further pair of blocked-startup cancellations verified that the prior context closed before replacement. Observed upgrade termination-to-context-close was ${smoke.blocked.shutdownMs.map(x=>x.toFixed(1)).join(', ')} ms. Immediate retries include the 2.5-second cleanup delay plus startup (about 8.7 seconds total in the blocked smoke). The maximum observed watchdog lateness in the matrix was ${Math.max(...lateness).toFixed(1)} ms. Worker close is not a portable CPU-quiescence acknowledgement; browser suspension/main-thread stalls can delay timers. Startup retains its separate 30-second timeout.

Worker WASM backing capacity observed after candidate completions reached ${(capacity/1048576).toFixed(1)} MiB. Reference allocation/managed-heap/Work/Storage counters are published per case. The larger successful supplement charges 28,968,356 combined Work and allocates about 143 MiB cumulatively across both solvers; cumulative allocation is not live memory. End-request heap and buffer capacity are not peak memory. A terminated synchronous upgrade cannot return its final counters; existing per-structure Storage caps and the remaining Work allowance still apply. No parallel heavy worker, raised cap, or retained result cache was introduced.

Validation:

- Approved Windows setup/restore and SDK checks; Release build passed with zero warnings/errors. Restricted build first stopped in csc with MSB5021; the approved retry passed, without a serial-build workaround.
- \`dotnet test YGOProbabilityCalculatorBlazor.sln -c Release --no-build\`: **2,542 passed**. Includes independent physical-card oracle rows, shared allowance, mode contracts, owner cancellation/replacement, settings, pins and copy. One obsolete help-text assertion was corrected before the passing full rerun.
- \`node --test scripts/background-calculation/*.test.mjs\`: **52 passed**, including controllable deadlines, response/timer races, typed/transport failures, an Uncertified fallback, long initial Fast, manual cancellation, startup cancellation and restart cleanup.
- \`dotnet publish YGOProbabilityCalculatorBlazor/YGOProbabilityCalculatorBlazor.csproj -c Release --no-restore -p:RunAOTCompilation=false -o research/fast-deadline/bin/candidate\`: optimized normal publish passed; the candidate assembly was checked for absence of the external benchmark probe.
- \`node scripts/background-calculation/fast-deadline-smoke.cjs <candidate-wwwroot> <smoke.json> <output.json>\`: rendered UI, manual/edit/session cancellation, explicit Exact beyond seven seconds, pin/copy, blocked-worker termination and recovery passed. Desktop 1280px and mobile 390px screenshots were inspected; no horizontal overflow.
- Frozen reference and production scripts, native diagnostics, and exact-row comparisons passed: ${candidate.checks} automatic-path checks and ${explicit.checks} explicit-path checks; all 86 native mode runs matched browser references and charged Work. Every completed upgrade/fallback and individual/group/total row matched its unchanged reference. Input/Storage forwarding and cumulative Work were checked.
- \`git diff --check\` and publication privacy guard passed. Benchmarks, builds and long test runs were kept sequential. Transient Windows checkpoint locks were recovered by preserving completed samples and recording a fresh browser segment; superseded policy/helper data is labeled and excluded from acceptance.

[Reproducible corpus, raw evidence, protocol history, scripts and screenshots](${evidence}). Benchmark files live on the separate research branch and do not enter this production diff. The performance matrix predates only the final startup-cancellation marker fix; the ready-worker path is unchanged, and the final publish, explicit-mode parity, 52 JS tests and browser smoke (including blocked startup replacement) were rerun. No session schema, dependency, workflow, deployment policy, or gameplay semantics changed. This PR is intentionally unmerged.
`;
fs.writeFileSync(path.join(root,'bin','pr-body.md'),text);
console.log(text);
