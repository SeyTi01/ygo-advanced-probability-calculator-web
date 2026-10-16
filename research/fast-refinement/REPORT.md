# NO-GO: broadly useful Fast certificate refinement

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

The proof uses the existing guarded count-state exchangeability conditions: all batches fit and no identity-dependent hand operations occur. Relevant sources: [count-state applicability](https://github.com/SeyTi01/ygo-advanced-probability-calculator-web/blob/63a4780fb3fa937907c1c8be4662550197179f27/YGOProbabilityCalculatorBlazor/Services/ProbabilityCalculator/DrawEffectResolver.cs#L132), [physical joint hand/loss weighting](https://github.com/SeyTi01/ygo-advanced-probability-calculator-web/blob/63a4780fb3fa937907c1c8be4662550197179f27/YGOProbabilityCalculatorBlazor/Services/ProbabilityCalculator/PhysicalSearchCalculation.cs#L6), [shared physical counting](https://github.com/SeyTi01/ygo-advanced-probability-calculator-web/blob/63a4780fb3fa937907c1c8be4662550197179f27/YGOProbabilityCalculatorBlazor/Services/ProbabilityCalculator/PhysicalSearchCalculation.cs#L67), [certificate passes](https://github.com/SeyTi01/ygo-advanced-probability-calculator-web/blob/63a4780fb3fa937907c1c8be4662550197179f27/YGOProbabilityCalculatorBlazor/Services/ProbabilityCalculator/SearchAvailabilityCertification.cs#L75), [search assignment compiler](https://github.com/SeyTi01/ygo-advanced-probability-calculator-web/blob/63a4780fb3fa937907c1c8be4662550197179f27/YGOProbabilityCalculatorBlazor/Services/ProbabilityCalculator/SearchAccessPlan.cs#L77).

Actual reuse is code/predicates and a shared decision diagram/weighted memo across all output rows. The prototype rebuilds the physical draw distribution because Fast's coarser terminal map merges observed-source counts. It does **not** reuse that completed map or existing lower-pass count work. Work is charged for the duplicate resolver, eligibility, compilation, counting, sorting, exact aggregation and retained checkpoints. Runtime complexity remains the sum of completed physical-region costs plus one attempted unfinished region, with bounded resolver/diagram/memo storage. Each completed checkpoint validates all rows and diagnostic storage before publication. Child allowances are carved from remaining request Work with 4096 reserved; attempted child Storage peaks are reported separately. A failed child never converts a completed Fast result into failure.

This yields useful incremental bounds, but too late economically. On all five improved representative cases, even the **first >=50% total-width reduction** has already spent **more cumulative Work (baseline + refinement) than a standalone completed Exact request**:

| Case | Refinement Work at first half-width | Cumulative Work | Standalone Exact Work | Native checkpoint / Exact ms | WASM checkpoint / Exact ms |
| --- | --- | --- | --- | --- | --- |
| redundant-two | 158302 | 345914 | 341332 | 5.768 / 4.797 | 75.500 / 65.800 |
| overlap-or | 246600 | 573694 | 525415 | 9.788 / 8.204 | 122.300 / 101.400 |
| multiple-draw-sources | 373141 | 894419 | 799069 | 17.354 / 12.488 | 186.800 / 147.900 |
| maxima-exclusion | 208519 | 372940 | 277501 | 6.054 / 4.214 | 76.500 / 54.100 |
| direct-plus-category | 195575 | 474616 | 411146 | 9.278 / 6.255 | 98.700 / 79.300 |

The larger-budget wins are effectively an Exact pass appended after Fast. At 100k, representative regions are unfinished or the completed mass proves no tighter total/group interval. For VS/K9, 1m completes only the no-Desires region (66.244939% mass), which the baseline already bounds more effectively. The frozen workload exposes this redundancy directly. The artificial 32-search and 30-search/2-blank cases reach Exact even at 100k, but are excluded from investment success; standalone Exact remains cheaper there too.

**Recommendation:** keep this mechanism out of production; do not start another sequence of all-copies-mandatory special cases. Existing Exact mode is preferable on the five cheap count-state workloads. The current ordered/deferred limitations remain unresolved. Better sharing or stronger conditional sufficient proofs could change the economics, but that is conjecture and was not prototyped. No next production task is recommended because the GO gates failed.

## Frozen corpus and reproducibility

[Precommitted protocol](https://github.com/SeyTi01/ygo-advanced-probability-calculator-web/blob/codex/research-fast-refinement-no-go/research/fast-refinement/protocol.md), [single mechanism and proof](https://github.com/SeyTi01/ygo-advanced-probability-calculator-web/blob/codex/research-fast-refinement-no-go/research/fast-refinement/design.md), [commands and measurement details](https://github.com/SeyTi01/ygo-advanced-probability-calculator-web/blob/codex/research-fast-refinement-no-go/research/fast-refinement/README.md), [frozen wire corpus](https://github.com/SeyTi01/ygo-advanced-probability-calculator-web/blob/codex/research-fast-refinement-no-go/research/fast-refinement/inputs/corpus.json), [all input/session SHA-256 hashes](https://github.com/SeyTi01/ygo-advanced-probability-calculator-web/blob/codex/research-fast-refinement-no-go/research/fast-refinement/inputs/sha256.json).

Corpus SHA-256: **352ad6060b3370c10697a6fc506dfead2f20e2053e78775df4e0be79676e09d2**. All inputs use **150,000,000 Work / Standard Storage (32,768 entries, 262,144 cells)** except the explicit 1000-Work control. Hands are five cards. Both refinement caps are additional maxima within that same request allowance. Exact changes only the calculation mode. Every representative deck has 40 cards and <=3 copies per entry. Synthetic searches are hypothetical custom plans, not claims about printed legal decklists.

The exact user session was not supplied. **vsk9-three-desires is a maintained equivalent**, built from the bundled current example by replacing its three Fuwalos with three Desires and clearing the old monster categories. Its no-Desires control is the unmodified maintained session. The existing certificate helper's one-card banish variant was not misrepresented as three Desires. Ordered/deferred cases derive from existing FastCertificateBenchmark fixtures, splitting 28 irrelevant pooled fillers into <=3-copy identities before freezing; this changes computational representation and is disclosed rather than equated with old timing references. No prior timing is used as a reference.

| Input file (inputs/) | Split / classification | SHA-256 |
| --- | --- | --- |
| [vsk9-three-desires.input.json](https://github.com/SeyTi01/ygo-advanced-probability-calculator-web/blob/codex/research-fast-refinement-no-go/research/fast-refinement/inputs/vsk9-three-desires.input.json) | design / maintained-equivalent | 9b3148b46ab47bf2bd97d30791e007a180d60c67f25509dd1f5915cf2ebd4b66 |
| [redundant-two.input.json](https://github.com/SeyTi01/ygo-advanced-probability-calculator-web/blob/codex/research-fast-refinement-no-go/research/fast-refinement/inputs/redundant-two.input.json) | design / synthetic-normal | 5783d11a079b28435658a144a967b05fe747b4eb71c27cb41701f841c90609b6 |
| [overlap-or.input.json](https://github.com/SeyTi01/ygo-advanced-probability-calculator-web/blob/codex/research-fast-refinement-no-go/research/fast-refinement/inputs/overlap-or.input.json) | design / synthetic-normal | e62e1c9dc6432d3a1b711916bfc5add10ae397fa5af66a6b4991de86c3c6cf9e |
| [multiple-draw-sources.input.json](https://github.com/SeyTi01/ygo-advanced-probability-calculator-web/blob/codex/research-fast-refinement-no-go/research/fast-refinement/inputs/multiple-draw-sources.input.json) | design / synthetic-normal | 71fa7ff807ee2a5e37b8db81312a167a8d6173be6549f6e0fb43014bd28917ef |
| [maxima-exclusion.input.json](https://github.com/SeyTi01/ygo-advanced-probability-calculator-web/blob/codex/research-fast-refinement-no-go/research/fast-refinement/inputs/maxima-exclusion.input.json) | held-out / synthetic-normal | d55ccc0e967d560d3ddb07301eb461feb54642d1381443735b930bc8a27aa45a |
| [direct-plus-category.input.json](https://github.com/SeyTi01/ygo-advanced-probability-calculator-web/blob/codex/research-fast-refinement-no-go/research/fast-refinement/inputs/direct-plus-category.input.json) | held-out / synthetic-normal | d9af2e5ddfbcbaace3246d050f8e9774deab84a9ef39f78a33e2f64645b93a8b |
| [ordered-target-loss-40.input.json](https://github.com/SeyTi01/ygo-advanced-probability-calculator-web/blob/codex/research-fast-refinement-no-go/research/fast-refinement/inputs/ordered-target-loss-40.input.json) | held-out / maintained-synthetic | 93b6d2125660c155b969f14f4a82f52d5a1b18c4be47f3c30f0cf4a53e52d60d |
| [deferred-target-loss-40.input.json](https://github.com/SeyTi01/ygo-advanced-probability-calculator-web/blob/codex/research-fast-refinement-no-go/research/fast-refinement/inputs/deferred-target-loss-40.input.json) | held-out / maintained-synthetic | b21846b802c43f059a9e68ca954420bf77709b1d488327ee84558c5fa29ebe2b |
| [vsk9-no-desires.input.json](https://github.com/SeyTi01/ygo-advanced-probability-calculator-web/blob/codex/research-fast-refinement-no-go/research/fast-refinement/inputs/vsk9-no-desires.input.json) | control / maintained | f4152bdf56f2ffa35dc115c5c68632f3458fe3b6f5c1bb62c55c4e5d514b397e |
| [no-search-draw.input.json](https://github.com/SeyTi01/ygo-advanced-probability-calculator-web/blob/codex/research-fast-refinement-no-go/research/fast-refinement/inputs/no-search-draw.input.json) | control / maintained-synthetic | 4b9ca46049817befc9032d8d7a9d869025ca016ff07d0012112b3f911e756d58 |
| [artificial-32-search.input.json](https://github.com/SeyTi01/ygo-advanced-probability-calculator-web/blob/codex/research-fast-refinement-no-go/research/fast-refinement/inputs/artificial-32-search.input.json) | diagnostic / artificial | 7f417fcc53323718255613fedc16b5ba3346f913e08dc00aadd639a3084e94df |
| [artificial-30-search.input.json](https://github.com/SeyTi01/ygo-advanced-probability-calculator-web/blob/codex/research-fast-refinement-no-go/research/fast-refinement/inputs/artificial-30-search.input.json) | diagnostic / artificial | fd7a5d73e9b2096ceb49b9ebeb8d82198da9d71870bc6eee73ca8851c3b708a9 |
| [opening-control.input.json](https://github.com/SeyTi01/ygo-advanced-probability-calculator-web/blob/codex/research-fast-refinement-no-go/research/fast-refinement/inputs/opening-control.input.json) | control / maintained | f46eaa0d66ce82a73cf12fa1909c1d87e76798bfc8fed2daa2305bdb294d9a74 |
| [work-limited.input.json](https://github.com/SeyTi01/ygo-advanced-probability-calculator-web/blob/codex/research-fast-refinement-no-go/research/fast-refinement/inputs/work-limited.input.json) | control / maintained-equivalent | f67dcebdf604eec91490ff032816a321d5ed3de903a34881418705609c34e0a3 |

Session counterparts use the same stem with .session.json; their exact hashes are in the manifest. The four held-out cases were structurally reserved before candidate design; they are a coverage split, not a statistical estimate of the population of player decks. All baseline outputs were visible during diagnosis; held-out candidate results were read only after fixing the mechanism, so this is not a fully blinded study. Bounded-search hand-cost/mode routes were inspected in current routing, but are not a separately timed corpus class; the main corpus exercises ordered draw/removal and deferred paths. This limitation cannot rescue the observed runtime failure and is not used to claim broader coverage.

## Native and optimized WASM measurements

Windows 11 build 26200, AMD Ryzen 7 5700X (8 cores/16 logical processors), approximately 32 GiB RAM; .NET SDK **10.0.401**, runtime **10.0.12**. Browser **Edge 155.0.4283.60**. Release publish used SDK-scoped wasm-tools, emcc and wasm-opt **-O2**, **RunAOTCompilation=false**. Local localhost worker execution only. Native used **DOTNET_TieredCompilation=0**. Native, browser, test and publish jobs were run serially. Five sides (primary-only diagnostic, full Fast, +100k, +1m, physical Exact), two warmups, ten seeded randomized/interleaved warmed samples. p50/p90 are nearest-rank empirical quantiles. JSON materialization/serialization/transport and native pre-call GC are excluded consistently; primary + baseline certificate/public result + optional endpoints/checkpoints are included. No UI integration exists, so no button-to-paint claim.

Warm suite first observations are retained separately; later fixtures share warmed runtimes. Separate cold files use fresh native processes and fresh WASM module workers per input/mode, with browser bootstrap recorded separately. Cold observations are singletons, not p50/p90 distributions. Raw evidence: [output/native.json](https://github.com/SeyTi01/ygo-advanced-probability-calculator-web/blob/codex/research-fast-refinement-no-go/research/fast-refinement/output/native.json), [output/wasm.json](https://github.com/SeyTi01/ygo-advanced-probability-calculator-web/blob/codex/research-fast-refinement-no-go/research/fast-refinement/output/wasm.json), [output/cold-native.json](https://github.com/SeyTi01/ygo-advanced-probability-calculator-web/blob/codex/research-fast-refinement-no-go/research/fast-refinement/output/cold-native.json), [output/cold-wasm.json](https://github.com/SeyTi01/ygo-advanced-probability-calculator-web/blob/codex/research-fast-refinement-no-go/research/fast-refinement/output/cold-wasm.json). Full phase timing, managed allocation, observed managed heap, working set/WASM capacity and resource counters: [output/resources.csv](https://github.com/SeyTi01/ygo-advanced-probability-calculator-web/blob/codex/research-fast-refinement-no-go/research/fast-refinement/output/resources.csv). Allocations are cumulative current-thread allocations; heap snapshots and linear-memory capacity are **not peak live memory**. Checked entries/cells bound individual structures, not total process RAM.

All successful baseline public results/fractions/routes are identical under both refinement caps. Every native/WASM row, checkpoint and successful Exact reference agrees. Exact never runs the optional certificate. Work-limited Exact requests supply **no reference probability**, not zero. Ineligible controls retain fractions/Work/storage; small timing noise and extra diagnostic allocations remain in the tables.

<details><summary>Complete warm runtime, allocation and resource comparison (all cases/modes)</summary>

Native/WASM time cells are p50/p90 milliseconds. Allocation cells are native/WASM p50 MiB. Work is total request Work; refinement Storage is separate from baseline and is the attempted peak, including failed checks. Exact and primary-only rows report their own public-conversion overhead rather than attributing it to a Fast certificate.

| Case | Side | Completion / refinement | Native ms | WASM ms | Allocated MiB N/W | Work N/W | Baseline entries/cells N | Child entries/cells N |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| vsk9-three-desires | fast | Success | 43.663/47.040 | 445.000/479.600 | 20.622/15.102 | 2077776/2077776 | 15142/60568 | 0/0 |
| vsk9-three-desires | low | Success / WorkLimit | 45.712/49.267 | 443.600/485.400 | 21.160/15.509 | 2177737/2177737 | 15142/60568 | 135/3996 |
| vsk9-three-desires | high | Success / WorkLimit | 64.116/76.942 | 619.600/642.800 | 30.635/22.737 | 3077772/3077772 | 15142/60568 | 18532/90538 |
| vsk9-three-desires | exact | Success | 121.833/177.550 | 1465.900/1500.400 | 39.292/30.473 | 7150686/7150686 | 32768/160925 | 0/0 |
| vsk9-three-desires | primary | Success | 26.088/29.650 | 268.900/285.400 | 14.427/10.539 | 1245452/1245452 | 15142/60568 | 0/0 |
| redundant-two | fast | Success | 3.595/3.947 | 44.700/50.400 | 2.598/1.833 | 187612/187612 | 827/3308 | 0/0 |
| redundant-two | low | Success / WorkLimit | 4.765/5.166 | 64.200/78.300 | 3.749/2.694 | 287612/287612 | 827/3308 | 1541/6164 |
| redundant-two | high | Success / Completed | 8.347/9.002 | 113.000/131.500 | 5.211/3.797 | 532726/532726 | 827/3308 | 1541/6164 |
| redundant-two | exact | Success | 4.797/6.008 | 65.800/121.400 | 2.603/1.955 | 341332/341332 | 1541/6164 | 0/0 |
| redundant-two | primary | Success | 2.116/2.498 | 31.300/40.500 | 1.945/1.372 | 122115/122115 | 827/3308 | 0/0 |
| overlap-or | fast | Success | 6.716/6.943 | 76.200/80.500 | 4.144/2.931 | 327094/327094 | 1287/5148 | 0/0 |
| overlap-or | low | Success / WorkLimit | 7.450/8.194 | 92.200/98.400 | 5.191/3.710 | 427073/427073 | 1287/5148 | 2431/9724 |
| overlap-or | high | Success / Completed | 14.402/16.670 | 177.300/200.900 | 8.351/6.103 | 856273/856273 | 1287/5148 | 2444/9776 |
| overlap-or | exact | Success | 8.204/9.053 | 101.400/111.900 | 4.197/3.164 | 525415/525415 | 2444/9776 | 0/0 |
| overlap-or | primary | Success | 3.912/4.519 | 51.000/52.400 | 3.119/2.207 | 208089/208089 | 1287/5148 | 0/0 |
| multiple-draw-sources | fast | Success | 10.295/11.551 | 117.500/127.400 | 5.773/4.062 | 521278/521278 | 941/3764 | 0/0 |
| multiple-draw-sources | low | Success / WorkLimit | 11.994/15.459 | 131.200/139.700 | 6.612/4.677 | 621263/621263 | 941/3764 | 1922/7688 |
| multiple-draw-sources | high | Success / Completed | 24.271/38.025 | 274.000/301.300 | 12.630/9.224 | 1329114/1329114 | 941/3764 | 1947/7788 |
| multiple-draw-sources | exact | Success | 12.488/17.136 | 147.900/169.600 | 6.824/5.136 | 799069/799069 | 1947/7788 | 0/0 |
| multiple-draw-sources | primary | Success | 6.414/7.643 | 75.600/81.000 | 4.547/3.199 | 311826/311826 | 941/3764 | 0/0 |
| maxima-exclusion | fast | Success | 2.878/3.057 | 35.800/41.300 | 1.734/1.232 | 164421/164421 | 475/1900 | 0/0 |
| maxima-exclusion | low | Success / WorkLimit | 4.444/4.885 | 56.900/62.800 | 2.469/1.785 | 264418/264418 | 475/1900 | 844/3376 |
| maxima-exclusion | high | Success / Completed | 7.282/8.295 | 91.400/96.200 | 3.361/2.461 | 445442/445442 | 475/1900 | 844/3376 |
| maxima-exclusion | exact | Success | 4.214/4.674 | 54.100/59.200 | 1.617/1.221 | 277501/277501 | 844/3376 | 0/0 |
| maxima-exclusion | primary | Success | 1.963/2.225 | 23.000/26.900 | 1.264/0.899 | 99134/99134 | 475/1900 | 0/0 |
| direct-plus-category | fast | Success | 5.588/6.238 | 60.100/72.200 | 3.270/2.324 | 279041/279041 | 1109/4436 | 0/0 |
| direct-plus-category | low | Success / WorkLimit | 6.844/8.888 | 79.400/81.800 | 4.286/3.078 | 379038/379038 | 1109/4436 | 2139/8556 |
| direct-plus-category | high | Success / Completed | 12.799/14.612 | 141.000/155.100 | 6.955/5.109 | 693987/693987 | 1109/4436 | 2290/9160 |
| direct-plus-category | exact | Success | 6.255/7.863 | 79.300/90.100 | 3.674/2.775 | 411146/411146 | 2290/9160 | 0/0 |
| direct-plus-category | primary | Success | 3.607/4.452 | 40.600/43.200 | 2.464/1.753 | 173521/173521 | 1109/4436 | 0/0 |
| ordered-target-loss-40 | fast | Success | 5.077/5.630 | 84.100/109.100 | 5.542/4.187 | 351050/351050 | 166/3126 | 0/0 |
| ordered-target-loss-40 | low | Success / Ineligible | 4.883/5.592 | 84.800/99.500 | 5.542/4.188 | 351050/351050 | 166/3126 | 0/0 |
| ordered-target-loss-40 | high | Success / Ineligible | 4.914/6.136 | 85.700/104.900 | 5.542/4.188 | 351050/351050 | 166/3126 | 0/0 |
| ordered-target-loss-40 | exact | WorkLimit | 2346.979/2386.933 | 22487.800/22656.700 | 690.249/586.132 | 149999999/149999999 | 41/784 | 0/0 |
| ordered-target-loss-40 | primary | Success | 2.991/3.402 | 57.900/64.000 | 3.123/2.416 | 294610/294610 | 166/3126 | 0/0 |
| deferred-target-loss-40 | fast | Success | 4.698/5.700 | 68.700/71.200 | 4.137/3.064 | 233230/233230 | 331/1624 | 0/0 |
| deferred-target-loss-40 | low | Success / Ineligible | 4.527/5.147 | 66.200/74.500 | 4.137/3.064 | 233230/233230 | 331/1624 | 0/0 |
| deferred-target-loss-40 | high | Success / Ineligible | 4.329/5.599 | 67.800/75.700 | 4.137/3.064 | 233230/233230 | 331/1624 | 0/0 |
| deferred-target-loss-40 | exact | WorkLimit | 2435.688/2511.622 | 22665.800/23029.200 | 693.653/588.699 | 149999998/149999998 | 41/784 | 0/0 |
| deferred-target-loss-40 | primary | Success | 3.276/3.811 | 44.200/56.000 | 2.740/2.037 | 178998/178998 | 331/1624 | 0/0 |
| vsk9-no-desires | fast | Success | 7.489/8.364 | 81.200/89.200 | 4.537/3.343 | 382637/382637 | 14769/59076 | 0/0 |
| vsk9-no-desires | low | Success / Ineligible | 7.561/8.652 | 80.900/85.100 | 4.537/3.343 | 382637/382637 | 14769/59076 | 0/0 |
| vsk9-no-desires | high | Success / Ineligible | 7.617/9.551 | 84.500/87.900 | 4.537/3.343 | 382637/382637 | 14769/59076 | 0/0 |
| vsk9-no-desires | exact | Success | 7.743/8.595 | 80.900/89.500 | 4.537/3.343 | 382637/382637 | 14769/59076 | 0/0 |
| vsk9-no-desires | primary | Success | 7.060/7.869 | 78.800/85.400 | 4.534/3.342 | 382637/382637 | 14769/59076 | 0/0 |
| no-search-draw | fast | Success | 31.486/35.212 | 274.000/284.300 | 11.896/9.037 | 2164440/2164440 | 231/3143 | 0/0 |
| no-search-draw | low | Success / Ineligible | 32.471/33.522 | 262.400/270.800 | 11.896/9.037 | 2164440/2164440 | 231/3143 | 0/0 |
| no-search-draw | high | Success / Ineligible | 31.979/36.708 | 266.500/278.500 | 11.896/9.037 | 2164440/2164440 | 231/3143 | 0/0 |
| no-search-draw | exact | Success | 30.334/33.707 | 263.900/268.900 | 11.896/9.037 | 2164440/2164440 | 231/3143 | 0/0 |
| no-search-draw | primary | Success | 32.018/34.527 | 262.200/281.300 | 11.894/9.036 | 2164440/2164440 | 231/3143 | 0/0 |
| artificial-32-search | fast | Success | 0.225/0.294 | 4.300/4.900 | 0.186/0.134 | 11401/11401 | 27/215 | 0/0 |
| artificial-32-search | low | Success / Completed | 0.622/0.790 | 8.100/10.300 | 0.358/0.263 | 25740/25740 | 27/215 | 55/348 |
| artificial-32-search | high | Success / Completed | 0.648/0.817 | 8.200/9.600 | 0.358/0.263 | 25740/25740 | 27/215 | 55/348 |
| artificial-32-search | exact | Success | 0.274/0.329 | 4.700/4.900 | 0.170/0.126 | 12910/12910 | 55/348 | 0/0 |
| artificial-32-search | primary | Success | 0.136/0.164 | 2.600/2.900 | 0.118/0.084 | 5291/5291 | 27/215 | 0/0 |
| artificial-30-search | fast | Success | 0.250/0.304 | 4.400/5.500 | 0.189/0.136 | 12243/12243 | 27/215 | 0/0 |
| artificial-30-search | low | Success / Completed | 0.770/0.914 | 10.500/11.500 | 0.372/0.274 | 31298/31298 | 27/215 | 81/376 |
| artificial-30-search | high | Success / Completed | 0.649/0.856 | 9.800/10.900 | 0.372/0.274 | 31298/31298 | 27/215 | 81/376 |
| artificial-30-search | exact | Success | 0.327/0.489 | 5.400/6.000 | 0.178/0.133 | 16992/16992 | 81/376 | 0/0 |
| artificial-30-search | primary | Success | 0.155/0.183 | 3.000/3.900 | 0.119/0.085 | 5775/5775 | 27/215 | 0/0 |
| opening-control | fast | Success | 10.221/11.693 | 90.800/92.800 | 3.477/2.591 | 640369/640369 | 169/2086 | 0/0 |
| opening-control | low | Success / Ineligible | 10.495/11.105 | 85.800/88.600 | 3.477/2.591 | 640369/640369 | 169/2086 | 0/0 |
| opening-control | high | Success / Ineligible | 10.811/11.366 | 86.900/100.300 | 3.477/2.591 | 640369/640369 | 169/2086 | 0/0 |
| opening-control | exact | Success | 10.861/12.262 | 86.200/99.500 | 3.477/2.591 | 640369/640369 | 169/2086 | 0/0 |
| opening-control | primary | Success | 10.848/11.735 | 86.700/97.700 | 3.474/2.590 | 640369/640369 | 169/2086 | 0/0 |
| work-limited | fast | WorkLimit | 0.019/0.029 | 0.100/0.100 | 0.002/0.001 | 0/0 | 25/700 | 0/0 |
| work-limited | low | WorkLimit | 0.016/0.020 | 0.100/0.100 | 0.002/0.001 | 0/0 | 25/700 | 0/0 |
| work-limited | high | WorkLimit | 0.019/0.025 | 0.000/0.200 | 0.002/0.001 | 0/0 | 25/700 | 0/0 |
| work-limited | exact | WorkLimit | 0.017/0.028 | 0.100/0.100 | 0.002/0.001 | 0/0 | 25/700 | 0/0 |
| work-limited | primary | WorkLimit | 0.016/0.023 | 0.100/0.100 | 0.002/0.001 | 0/0 | 25/700 | 0/0 |

WASM-specific storage counters, per-side p90 allocations and all observed heap/working-set/capacity values are retained in resources.csv, without treating them as managed peaks. A displayed 0.000 ms means below browser timer resolution. Certificate-phase counters include public result assembly/capability checks, rather than an isolated certificate-kernel measurement.

</details>

Cold VS/K9 first engine observations (singletons; not warmed quantiles):

| Mode | Fresh native process ms | Fresh WASM worker ms | WASM bootstrap ms |
| --- | --- | --- | --- |
| fast | 233.913 | 705.000 | 238.300 |
| low | 302.854 | 663.800 | 119.300 |
| high | 283.730 | 900.200 | 114.900 |
| exact | 267.655 | 1678.000 | 118.300 |
| primary | 166.381 | 494.500 | 123.400 |

[Every cold observation and bootstrap time](https://github.com/SeyTi01/ygo-advanced-probability-calculator-web/blob/codex/research-fast-refinement-no-go/research/fast-refinement/output/cold.csv); [toolchain, hardware and measurement settings](https://github.com/SeyTi01/ygo-advanced-probability-calculator-web/blob/codex/research-fast-refinement-no-go/research/fast-refinement/output/environment.json).

At 1m the five improved native cases allocate approximately 1.94–2.19x baseline bytes. VS/K9 allocates 21,623,720 -> 32,123,336 native bytes despite unchanged endpoints; its child spends 999,996 Work and reaches 18,532 checked entries / 90,538 cells. Ordered/deferred inputs add only the declined-probe diagnostic allocation (408 native bytes), no charged refinement Work. Exact on those two cases repeatedly reaches the unchanged 150m Work limit. No baseline-success case becomes a failure.

## Row-wise intervals and Exact references

Percentages below are display conversions of retained exact rational endpoints. Raw fractions are authoritative. [output/rows.csv](https://github.com/SeyTi01/ygo-advanced-probability-calculator-web/blob/codex/research-fast-refinement-no-go/research/fast-refinement/output/rows.csv) includes every row's absolute/relative reduction and lower/upper distance from Exact; [output/checkpoints.csv](https://github.com/SeyTi01/ygo-advanced-probability-calculator-web/blob/codex/research-fast-refinement-no-go/research/fast-refinement/output/checkpoints.csv) records when bounds became available. Total/group results are genuine Boolean unions.

<details><summary>Every completed output row: baseline, both budgets, Exact and width reductions</summary>

| Case / row | Old L–U % | Physical Exact % or limit | 100k L–U % | 1m L–U % | 100k width reduction pp (%) | 1m width reduction pp (%) |
| --- | --- | --- | --- | --- | --- | --- |
| vsk9-three-desires / total 0: Any active combo | 85.914392–90.085640 | 89.931311 | 85.914392–90.085640 | 85.914392–90.085640 | 0.000000 (0.00%) | 0.000000 (0.00%) |
| vsk9-three-desires / combo 1: VS Starter + (Fire OR Dark) | 42.902249–46.574552 | 46.271005 | 42.902249–46.574552 | 42.902249–46.574552 | 0.000000 (0.00%) | 0.000000 (0.00%) |
| vsk9-three-desires / combo 2: Razen + Sue | 11.058053–12.539850 | 12.413560 | 11.058053–12.539850 | 11.058053–12.539850 | 0.000000 (0.00%) | 0.000000 (0.00%) |
| vsk9-three-desires / combo 3: Mad Love + Fire | 27.206990–27.851090 | 27.796195 | 27.206990–27.851090 | 27.206990–27.851090 | 0.000000 (0.00%) | 0.000000 (0.00%) |
| vsk9-three-desires / combo 4: Sue + Fire + Dark | 22.039901–23.617838 | 23.543848 | 22.039901–23.617838 | 22.039901–23.617838 | 0.000000 (0.00%) | 0.000000 (0.00%) |
| vsk9-three-desires / combo 5: Sue + Start + VS Monster | 3.530139–3.594215 | 3.588754 | 3.530139–3.594215 | 3.530139–3.594215 | 0.000000 (0.00%) | 0.000000 (0.00%) |
| vsk9-three-desires / combo 6: K9 Starter + Lv. 5 | 46.560632–55.196361 | 54.872934 | 46.560632–55.196361 | 46.560632–55.196361 | 0.000000 (0.00%) | 0.000000 (0.00%) |
| vsk9-three-desires / combo 7: Noroi + Stake | 3.438560–4.623085 | 4.264138 | 3.438560–4.623085 | 3.438560–4.623085 | 0.000000 (0.00%) | 0.000000 (0.00%) |
| vsk9-three-desires / combo 8: Izuna + Sue + 2 x Lv. 5 | 4.407498–5.959787 | 5.925807 | 4.407498–5.959787 | 4.407498–5.959787 | 0.000000 (0.00%) | 0.000000 (0.00%) |
| vsk9-three-desires / group 9: Full VS | 66.482323–69.284522 | 69.085063 | 66.482323–69.284522 | 66.482323–69.284522 | 0.000000 (0.00%) | 0.000000 (0.00%) |
| vsk9-three-desires / group 10: Full K9 | 48.284752–56.908174 | 56.562807 | 48.284752–56.908174 | 48.284752–56.908174 | 0.000000 (0.00%) | 0.000000 (0.00%) |
| redundant-two / total 0: Any active combo | 51.940763–63.280389 | 63.208644 | 51.940763–63.280389 | 63.208644–63.208644 | 0.000000 (0.00%) | 11.339626 (100.00%) |
| redundant-two / combo 1: Two A | 39.020254–50.286243 | 50.047131 | 39.020254–50.286243 | 50.047131–50.047131 | 0.000000 (0.00%) | 11.265989 (100.00%) |
| redundant-two / combo 2: Two B | 39.020254–50.286243 | 49.859023 | 39.020254–50.286243 | 49.859023–49.859023 | 0.000000 (0.00%) | 11.265989 (100.00%) |
| redundant-two / group 3: Routes | 51.940763–63.280389 | 63.208644 | 51.940763–63.280389 | 63.208644–63.208644 | 0.000000 (0.00%) | 11.339626 (100.00%) |
| overlap-or / total 0: Any active combo | 56.951190–66.875180 | 66.866648 | 56.951190–66.875180 | 66.866648–66.866648 | 0.000000 (0.00%) | 9.923990 (100.00%) |
| overlap-or / combo 1: Distinct overlapping roles | 53.446790–64.353019 | 64.277144 | 53.446790–64.353019 | 64.277144–64.277144 | 0.000000 (0.00%) | 10.906229 (100.00%) |
| overlap-or / combo 2: OR route | 23.269005–32.029663 | 31.104268 | 23.269005–32.029663 | 31.104268–31.104268 | 0.000000 (0.00%) | 8.760658 (100.00%) |
| overlap-or / group 3: Routes | 56.951190–66.875180 | 66.866648 | 56.951190–66.875180 | 66.866648–66.866648 | 0.000000 (0.00%) | 9.923990 (100.00%) |
| multiple-draw-sources / total 0: Any active combo | 56.880381–67.619547 | 67.577741 | 56.880381–67.619547 | 67.577741–67.577741 | 0.000000 (0.00%) | 10.739166 (100.00%) |
| multiple-draw-sources / combo 1: Two A | 40.012024–52.175889 | 51.892741 | 40.012024–52.175889 | 51.892741–51.892741 | 0.000000 (0.00%) | 12.163865 (100.00%) |
| multiple-draw-sources / combo 2: A plus B | 55.087014–66.355039 | 66.261600 | 55.087014–66.355039 | 66.261600–66.261600 | 0.000000 (0.00%) | 11.268025 (100.00%) |
| multiple-draw-sources / group 3: Routes | 56.880381–67.619547 | 67.577741 | 56.880381–67.619547 | 67.577741–67.577741 | 0.000000 (0.00%) | 10.739166 (100.00%) |
| maxima-exclusion / total 0: Any active combo | 63.349247–73.682885 | 73.450391 | 63.349247–73.682885 | 73.450391–73.450391 | 0.000000 (0.00%) | 10.333638 (100.00%) |
| maxima-exclusion / combo 1: Bounded A | 35.957798–41.246997 | 41.130665 | 35.957798–41.246997 | 41.130665–41.130665 | 0.000000 (0.00%) | 5.289199 (100.00%) |
| maxima-exclusion / combo 2: Bounded B | 38.859069–50.125058 | 49.697838 | 38.859069–50.125058 | 49.697838–49.697838 | 0.000000 (0.00%) | 11.265989 (100.00%) |
| maxima-exclusion / group 3: Routes | 63.349247–73.682885 | 73.450391 | 63.349247–73.682885 | 73.450391–73.450391 | 0.000000 (0.00%) | 10.333638 (100.00%) |
| direct-plus-category / total 0: Any active combo | 37.624608–49.610719 | 49.308336 | 37.624608–49.610719 | 49.308336–49.308336 | 0.000000 (0.00%) | 11.986111 (100.00%) |
| direct-plus-category / combo 1: Direct plus category | 36.427409–48.376189 | 48.041980 | 36.427409–48.376189 | 48.041980–48.041980 | 0.000000 (0.00%) | 11.948779 (100.00%) |
| direct-plus-category / combo 2: B route | 16.551716–24.303200 | 24.026906 | 16.551716–24.303200 | 24.026906–24.026906 | 0.000000 (0.00%) | 7.751485 (100.00%) |
| direct-plus-category / group 3: Routes | 37.624608–49.610719 | 49.308336 | 37.624608–49.610719 | 49.308336–49.308336 | 0.000000 (0.00%) | 11.986111 (100.00%) |
| ordered-target-loss-40 / total 0: Any active combo | 46.607336–47.789575 | WorkLimit | 46.607336–47.789575 | 46.607336–47.789575 | 0.000000 (0.00%) | 0.000000 (0.00%) |
| ordered-target-loss-40 / combo 1: One or two targets | 46.590141–47.772380 | WorkLimit | 46.590141–47.772380 | 46.590141–47.772380 | 0.000000 (0.00%) | 0.000000 (0.00%) |
| ordered-target-loss-40 / combo 2: Distinct target and other | 14.550146–14.928677 | WorkLimit | 14.550146–14.928677 | 14.550146–14.928677 | 0.000000 (0.00%) | 0.000000 (0.00%) |
| ordered-target-loss-40 / group 3: Routes | 46.607336–47.789575 | WorkLimit | 46.607336–47.789575 | 46.607336–47.789575 | 0.000000 (0.00%) | 0.000000 (0.00%) |
| deferred-target-loss-40 / total 0: Any active combo | 47.226689–47.886400 | WorkLimit | 47.226689–47.886400 | 47.226689–47.886400 | 0.000000 (0.00%) | 0.000000 (0.00%) |
| deferred-target-loss-40 / combo 1: One or two targets | 47.208631–47.868342 | WorkLimit | 47.208631–47.868342 | 47.208631–47.868342 | 0.000000 (0.00%) | 0.000000 (0.00%) |
| deferred-target-loss-40 / combo 2: Distinct target and other | 14.755649–14.979254 | WorkLimit | 14.755649–14.979254 | 14.755649–14.979254 | 0.000000 (0.00%) | 0.000000 (0.00%) |
| deferred-target-loss-40 / group 3: Routes | 47.226689–47.886400 | WorkLimit | 47.226689–47.886400 | 47.226689–47.886400 | 0.000000 (0.00%) | 0.000000 (0.00%) |
| vsk9-no-desires / total 0: Total | 83.590625–83.590625 | 83.590625 | 83.590625–83.590625 | 83.590625–83.590625 | 0.000000 (0.00%) | 0.000000 (0.00%) |
| vsk9-no-desires / combo 1: VS Starter + (Fire OR Dark) | 41.260289–41.260289 | 41.260289 | 41.260289–41.260289 | 41.260289–41.260289 | 0.000000 (0.00%) | 0.000000 (0.00%) |
| vsk9-no-desires / combo 2: Razen + Sue | 9.797753–9.797753 | 9.797753 | 9.797753–9.797753 | 9.797753–9.797753 | 0.000000 (0.00%) | 0.000000 (0.00%) |
| vsk9-no-desires / combo 3: Mad Love + Fire | 22.869023–22.869023 | 22.869023 | 22.869023–22.869023 | 22.869023–22.869023 | 0.000000 (0.00%) | 0.000000 (0.00%) |
| vsk9-no-desires / combo 4: Sue + Fire + Dark | 17.905405–17.905405 | 17.905405 | 17.905405–17.905405 | 17.905405–17.905405 | 0.000000 (0.00%) | 0.000000 (0.00%) |
| vsk9-no-desires / combo 5: Sue + Start + VS Monster | 2.499362–2.499362 | 2.499362 | 2.499362–2.499362 | 2.499362–2.499362 | 0.000000 (0.00%) | 0.000000 (0.00%) |
| vsk9-no-desires / combo 6: K9 Starter + Lv. 5 | 47.715529–47.715529 | 47.715529 | 47.715529–47.715529 | 47.715529–47.715529 | 0.000000 (0.00%) | 0.000000 (0.00%) |
| vsk9-no-desires / combo 7: Noroi + Stake | 3.547981–3.547981 | 3.547981 | 3.547981–3.547981 | 3.547981–3.547981 | 0.000000 (0.00%) | 0.000000 (0.00%) |
| vsk9-no-desires / combo 8: Izuna + Sue + 2 x Lv. 5 | 3.357254–3.357254 | 3.357254 | 3.357254–3.357254 | 3.357254–3.357254 | 0.000000 (0.00%) | 0.000000 (0.00%) |
| vsk9-no-desires / group 9: Full VS | 61.234666–61.234666 | 61.234666 | 61.234666–61.234666 | 61.234666–61.234666 | 0.000000 (0.00%) | 0.000000 (0.00%) |
| vsk9-no-desires / group 10: Full K9 | 49.107610–49.107610 | 49.107610 | 49.107610–49.107610 | 49.107610–49.107610 | 0.000000 (0.00%) | 0.000000 (0.00%) |
| no-search-draw / total 0: Total | 69.956006–69.956006 | 69.956006 | 69.956006–69.956006 | 69.956006–69.956006 | 0.000000 (0.00%) | 0.000000 (0.00%) |
| no-search-draw / combo 1: VS Starter + (Fire OR Dark) | 32.857416–32.857416 | 32.857416 | 32.857416–32.857416 | 32.857416–32.857416 | 0.000000 (0.00%) | 0.000000 (0.00%) |
| no-search-draw / combo 2: Razen + Sue | 7.533061–7.533061 | 7.533061 | 7.533061–7.533061 | 7.533061–7.533061 | 0.000000 (0.00%) | 0.000000 (0.00%) |
| no-search-draw / combo 3: Mad Love + Fire | 22.981019–22.981019 | 22.981019 | 22.981019–22.981019 | 22.981019–22.981019 | 0.000000 (0.00%) | 0.000000 (0.00%) |
| no-search-draw / combo 4: Sue + Fire + Dark | 15.919528–15.919528 | 15.919528 | 15.919528–15.919528 | 15.919528–15.919528 | 0.000000 (0.00%) | 0.000000 (0.00%) |
| no-search-draw / combo 5: Sue + Start + VS Monster | 2.745905–2.745905 | 2.745905 | 2.745905–2.745905 | 2.745905–2.745905 | 0.000000 (0.00%) | 0.000000 (0.00%) |
| no-search-draw / combo 6: K9 Starter + Lv. 5 | 27.672466–27.672466 | 27.672466 | 27.672466–27.672466 | 27.672466–27.672466 | 0.000000 (0.00%) | 0.000000 (0.00%) |
| no-search-draw / combo 7: Noroi + Stake | 1.433873–1.433873 | 1.433873 | 1.433873–1.433873 | 1.433873–1.433873 | 0.000000 (0.00%) | 0.000000 (0.00%) |
| no-search-draw / combo 8: Izuna + Sue + 2 x Lv. 5 | 1.922108–1.922108 | 1.922108 | 1.922108–1.922108 | 1.922108–1.922108 | 0.000000 (0.00%) | 0.000000 (0.00%) |
| no-search-draw / group 9: Full VS | 54.974876–54.974876 | 54.974876 | 54.974876–54.974876 | 54.974876–54.974876 | 0.000000 (0.00%) | 0.000000 (0.00%) |
| no-search-draw / group 10: Full K9 | 28.856760–28.856760 | 28.856760 | 28.856760–28.856760 | 28.856760–28.856760 | 0.000000 (0.00%) | 0.000000 (0.00%) |
| artificial-32-search / total 0: Any active combo | 66.245322–73.245318 | 73.179709 | 73.179709–73.179709 | 73.179709–73.179709 | 6.999996 (100.00%) | 6.999996 (100.00%) |
| artificial-32-search / combo 1: Combo | 66.245322–73.245318 | 73.179709 | 73.179709–73.179709 | 73.179709–73.179709 | 6.999996 (100.00%) | 6.999996 (100.00%) |
| artificial-30-search / total 0: Any active combo | 49.335954–73.245318 | 55.838147 | 55.838147–55.838147 | 55.838147–55.838147 | 23.909364 (100.00%) | 23.909364 (100.00%) |
| artificial-30-search / combo 1: Combo | 49.335954–73.245318 | 55.838147 | 55.838147–55.838147 | 55.838147–55.838147 | 23.909364 (100.00%) | 23.909364 (100.00%) |
| opening-control / total 0: Total | 66.600558–66.600558 | 66.600558 | 66.600558–66.600558 | 66.600558–66.600558 | 0.000000 (0.00%) | 0.000000 (0.00%) |
| opening-control / combo 1: VS Starter + (Fire OR Dark) | 31.026371–31.026371 | 31.026371 | 31.026371–31.026371 | 31.026371–31.026371 | 0.000000 (0.00%) | 0.000000 (0.00%) |
| opening-control / combo 2: Razen + Sue | 6.808580–6.808580 | 6.808580 | 6.808580–6.808580 | 6.808580–6.808580 | 0.000000 (0.00%) | 0.000000 (0.00%) |
| opening-control / combo 3: Mad Love + Fire | 21.198830–21.198830 | 21.198830 | 21.198830–21.198830 | 21.198830–21.198830 | 0.000000 (0.00%) | 0.000000 (0.00%) |
| opening-control / combo 4: Sue + Fire + Dark | 14.045422–14.045422 | 14.045422 | 14.045422–14.045422 | 14.045422–14.045422 | 0.000000 (0.00%) | 0.000000 (0.00%) |
| opening-control / combo 5: Sue + Start + VS Monster | 2.362585–2.362585 | 2.362585 | 2.362585–2.362585 | 2.362585–2.362585 | 0.000000 (0.00%) | 0.000000 (0.00%) |
| opening-control / combo 6: K9 Starter + Lv. 5 | 25.753638–25.753638 | 25.753638 | 25.753638–25.753638 | 25.753638–25.753638 | 0.000000 (0.00%) | 0.000000 (0.00%) |
| opening-control / combo 7: Noroi + Stake | 1.282051–1.282051 | 1.282051 | 1.282051–1.282051 | 1.282051–1.282051 | 0.000000 (0.00%) | 0.000000 (0.00%) |
| opening-control / combo 8: Izuna + Sue + 2 x Lv. 5 | 1.476274–1.476274 | 1.476274 | 1.476274–1.476274 | 1.476274–1.476274 | 0.000000 (0.00%) | 0.000000 (0.00%) |
| opening-control / group 9: Full VS | 51.765480–51.765480 | 51.765480 | 51.765480–51.765480 | 51.765480–51.765480 | 0.000000 (0.00%) | 0.000000 (0.00%) |
| opening-control / group 10: Full K9 | 26.759249–26.759249 | 26.759249 | 26.759249–26.759249 | 26.759249–26.759249 | 0.000000 (0.00%) | 0.000000 (0.00%) |

The Work=1000 case has no completed rows on any side. Already-exact controls show zero-width fractions from their result contract; they do not acquire a new certificate or refinement result.

</details>

## Validation, limitations and stopping point

Targeted Release checks: **240 passed**, including seven disposable research tests that compare every completed checkpoint/final interval at five caps (1, 100, 1000, 100k, 1m) against the independent labeled-copy SearchCertificateOracle, and existing TargetSurvivalCertificateTest / PhysicalSearchTest / SearchAvailabilityCertificateTest coverage. The independent oracle retains actual physical suffix identities and does not call the production compiler/weighted survivor counter. Large-deck Exact references reuse the production physical solver, so they are regression references, not an independent implementation; the small oracle supplies independent correctness checks. The artificial 32-search reference is additionally covered by the existing independent chronological opening/banish/draw partition test.

Full Release suite with the external research include: **2,528 passed, 0 failed**, 1m58s. The measurement build has one CA1416 warning because the disposable native test calls the browser-annotated JS-export probe; the same probe is intentionally used through reflection by the native runner. No production diagnostic was suppressed. The app publish succeeds with normal optimized non-AOT linking. Frozen input/session hashes and the absence of production diffs were rechecked. This investigation stopped well within the six-hour ceiling after the runtime gate failed; the remaining work completed paired evidence and publication rather than another design iteration.

Offline verification made **42888 assertions** over the frozen baseline and warmed/cold records: deterministic primary output, unchanged public contracts, rational nesting at every checkpoint, physical Exact enclosure where completed, caps, and native/WASM output agreement. No proof-failure status occurred. Low-budget interrupted regions retain the last complete interval. Cancellation and forced child-Storage/diagnostic-allocation fault injection were **not** exhaustively tested; this research result does not certify production robustness. No integration is recommended in any event.

Commands: restore, Release build with the external targets, targeted dotnet test -c Release --no-build --filter ..., local optimized publish with RunAOTCompilation=false, native/Playwright replay, node research/fast-refinement/analyze.cjs, and publication privacy/diff checks. Exact invocations and environment prerequisites are in the linked README. .NET info/restore/tests, successful Release build/publish, browser launch and Git writes used scoped approved Windows execution. Restricted build exited 1 without diagnostics; approved build succeeded. Restricted Edge closed during launch; approved replay succeeded. The first native calibration's incorrect mode discriminator was discarded and repaired before baseline diagnosis/prototype design; no results from it enter these tables.

Stop reason: repeatable runtime failure plus no improvement on the primary costly workload, after one mechanism. No additional special-case formulas, expanded limits, or second prototype were pursued. Frozen evidence remains on the research branch for audit only.
