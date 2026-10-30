# Danger Fast research checkpoint

## Current handoff — objective remains unachieved

Research is preserved on `codex/issue-215-danger-base-effects`; no experiment is left running. **No approximation is enabled in production.** The public solver, result/wire/UI contracts, limits, dependencies and deployment policies are unchanged. The only normal-build source change is interruption-safe opt-in benchmark journaling/export. Normal Debug build was restored successfully after experiments.

Best evaluated native family: generic anonymous ordinary-card conditioning with a separate retained-hand stratum, deterministic bounded priority buckets and conditional terminal caching. At149M Work/cap2048/Standard Storage, original heavy40/60 full widths are **6.75020191pp / 2.13809160pp** for every row. Cheap control/single/mixed cases converge exactly. The frozen hand-size-six challenge remains **18.90001932pp** wide. Therefore the initial2pp goal and production end-to-end browser target have **not** been achieved; no release-ready claim follows from native runtime.

Read `latest-native-results.md` for every row's outward-rounded endpoints, widths, Work, timing and memory evidence; raw exact integers and source/input/assembly hashes remain in JSONL. Read `RUN.md` for executable reproduction, `anonymous-native-design.md` / `two-stratum-review.md` for the maintained proof and independent implementation review, and `integration-map.md` for still-unimplemented public-result work.

Verification:48 focused physical-oracle cases passed (multiple budgets/caps, repeats, nested bounds, conditional interruption, samecanonical ordinary copies, maxima/direct requirements, two simultaneous strata, empty retained stratum and repeated observers). Full regular suite3136 passed and Node182 passed with approved execution. Final restricted normal build passed zero warnings/errors. `verify-evidence.mjs` verifies13 frozen inputs and318 stored native row intervals, including all available reference containment checks. Four heavy challenges have no independent full exact reference; the cheap direct/exclusion challenge was computed by the unchanged physical Exact route under a finite150M diagnostic cap. No heavy Exact rerun, new AOT publish, browser integration or original issue295 replay was performed.

Next concrete research step: prototype **fixed-point lower subprobability masses** (e.g.2^60) to reduce repeated large-integer arithmetic/storage, while maintaining separate per-row certified success and failure sums and retaining all rounding loss as unresolved. This is a conjectured cost improvement, not implemented or proven feasible. Reuse the frozen inputs/oracles and compare at unchanged Fast/Standard limits. Further algorithmic reduction is needed before production integration is justified; do not simply raise limits or describe wide intervals as target success. The latest completed prior milestone is `52ca458`; subsequent code/evidence and this handoff are saved together in the final local checkpoint identified by Git HEAD and the chat reply.

Publication update: the owner subsequently requested publishing the result and squashing the six unpushed research checkpoints. They are consolidated into one commit atop the already-published documentation checkpoint `0f57160011f4f4f0d8b3a01e678d07b379ad0d95`. Earlier local checkpoint hashes below remain historical references preserved in a local backup branch.

## Objective and contract

Produce deterministic, mathematically enclosing Fast intervals for the existing fixed-policy, base-only Danger model, with Exact preserved. Initial experimental goals: <=2 percentage-point full width for total/principal groups and about five seconds published stripped-AOT worker latency on frozen heavy 40/60 inputs. These are research goals, not achieved results or release thresholds. No sampling, changed model, raised budgets, or fabricated primary fraction.

## Recoverable baseline and ownership (2026-10-30)

- PR #317 is open/draft; issue #215 retains the base-only boundary.
- Starting remote feature SHA: `bd75460e4bb3d9f990d8bc04bd1fcd915bcabc71`.
- Starting dev/workspace SHA: `a2afd969cdf5479b8d653bd3a1ab0104b439c487`; dev is an ancestor of feature.
- Local backup ref: `codex/backup-danger-fast-start` at the feature baseline.
- Active branch: `codex/issue-215-danger-base-effects`, in the assigned workspace; lead owns integration and Git operations. No extra worktree.
- Baseline remote SHA was `bd75460e4bb3d9f990d8bc04bd1fcd915bcabc71`; last verified pushed SHA is `0f57160011f4f4f0d8b3a01e678d07b379ad0d95`. Guarded local Git fetch/push and GitHub connector read access were verified. `gh` is unavailable; not required.
- Existing publication policy validates resolved author/committer. Do not reconfigure identity or bypass hooks.

## Established facts / verification

- Workspace was clean before checkout; no unrelated edits.
- Read repository README, AGENTS, privacy policy, live PR metadata/discussion and issue #215.
- `dotnet --info` (approved execution): SDK 10.0.401, installed wasm-tools workload; `dotnet --list-sdks` (restricted) confirms .NET 10.
- Approved restore, restricted Debug build, export-only fixture capture, and cheap control/single/mixed native+wire checks passed. See fixture-notes.md for exact commands and values. No new AOT/browser run.
- Historical heavy Work/runtime values in PR are not new measurements.

## Research status

User override after the first checkpoint: **do not push checkpoints; local checkpoints are sufficient**. No further checkpoint pushes. Initial documentation commit `0f57160011f4f4f0d8b3a01e678d07b379ad0d95` was already pushed and its remote SHA verified before that instruction. Local-only work must be identified honestly; no remote durability claim for subsequent research.

No production method selected. Frozen 13 inputs and reference hashes are in frozen-fixtures.json / fixture-manifest.json; no heavy Exact rerun. The benchmark harness now journals every sample and atomically replaces each case aggregate. No production solver source changed.

- `PostDrawDangerBeam.cs`, `DangerBeamExperiment.cs`, `Research.targets`: isolated opt-in full-state beam. Include with `-p:CustomAfterMicrosoftCommonTargets=<repo>/research/danger-fast/Research.targets`; normal builds omit it. Existing exact transitions, physical terminal search, and atomic terminal mass commits give `[success, success + denominator - resolved]`. Priority eviction leaves mass unresolved. Independent code review found proof sound within explicitly limited scope, but production Work/storage accounting and finalization reserve are not finished. Hence Work below is provisional research instrumentation, not compliance certification.
- Debug beam at 140M Work / 8000 retained states: heavy40 width 22.30957594pp, 1269.59ms; heavy60 width 11.53198582pp, 941.66ms. All six frozen references enclosed by integer cross-products. Full per-row endpoints, allocation, retained payload, counters and five challenges are appended per sample in beam-results.jsonl. Hand6 width 83.12pp is especially poor. These timings exclude preparation/worker transport and are not AOT/browser latency.
- Beam is **valid but too wide**, not successful Fast delivery. A first compile failed due to a missing Interface using; corrected opt-in Debug build passed without warnings/errors. Approved opt-in Measure test passed.
- `aggregate.md`, `aggregate-prototype.mjs`, `aggregate-results.jsonl`: exact anonymous ordinary-card skeleton with independent small labeled-card checks. Stopping at first identity-observing removal leaves 36.01% / 24.46% heavy mass unresolved; refined Allure handling still 23.98% / 18.08%. Dynamic revelation remains a conjectured next step, not proven implemented general solution.
- `proof-review.md`, `review_horizon.py`, `review-horizon-results.json`: primary paper assumptions, counterexamples, normalized prefix invariant and conservative draw-horizon envelope. The horizon alone does not establish practical runtime.

Agent handoffs are persistent files; lead owns their local commits. No agent has committed independently. The next local milestone includes these findings and isolated code, not an enabled production approximation.

## Next concrete step

Measure a small beam capacity curve, independently test small oracle containment, and investigate conditional continuation caching with outward-rounded fixed-point endpoints. Aggregate investigator is extending dynamic revelation. Keep production disabled until quality, accounting, contract and browser validation actually pass. Checkpoint locally before expensive work and at most 10–15 minutes apart; no checkpoint pushes.

## Second local checkpoint findings

Last completed local checkpoint: `d7089d1` (frozen fixtures, harness, first prototypes). Subsequent files below are being checkpointed locally; remote remains `0f57160`.

- Beam capacity curve at 140M provisional Work, capacities 1000/4000/8000/14000: heavy40 widths 35.68/19.30/22.31/35.88pp; heavy60 13.11/6.82/11.53/18.51pp. Cap1000 exhausted its retained frontier at about50M Work; larger caps stopped at Work. No credible target success; stop tuning this representation.
- Six beam physical-oracle tests passed, each checking two priorities, three capacities, three Work budgets, deterministic repeats, all rows and nested-Work tightening. Covers mixed/same identities, optional search, growth, lock and mandatory failure. First attempted fixture construction incorrectly supplied ordinary DrawCount to Danger; constructor rejected all six before calculation, then fixed and all passed.
- Conditional continuation cache prototype is isolated in `PostDrawDangerContinuation.cs`. Integer endpoints at2^48 use outward rounding and mass-independent cache entries. Untruncated DFS was nearly useless (99.63/94.08pp); horizons6/8/10 at140M still too wide (40:75.55/94.86/98.82pp;60:54.28/78.92/89.79pp). First two journal samples precede removal of redundant terminal caching and lack Horizon; later six use the present horizon variant. No production claim, stop tuning this traversal.
- Anonymous dynamic JS prototype now has five independent full mixed-effect physical oracles and integer all-row containment/repeatability checks at tiny caps. `aggregate-check-references.mjs` checks heavy intervals. At cap8192:40 width2.98694pp,60 width0.476119pp. At cap16384:40 width1.71520676pp. **Unpacked state payload exceeds Standard Storage**, and no production Work accounting exists, so these are research feasibility results only. Actual measured timings/counts/source hashes are in aggregate-dynamic-results.jsonl; detailed proof/scope in aggregate.md and independent dynamic-review.md.
- Increasing beam capacity is not nested refinement: reviewer found cap1 lower4001/30240 vs cap2 lower4000/30240 for a small row. Within a fixed traversal, increasing completed terminal mass tightens bounds; do not claim monotonicity across independent differently-pruned runs.

Active next step: generic packed C# anonymous-state port in isolated research files, reusing existing actual removal/selection/terminal search policies; terminal conditional cache and safe optional-search capacity envelopes. Need small physical-oracle review, then honest Work/Storage/time measurement before considering any result-contract or production integration. The production solver, wire and UI are unchanged.

## Native anonymous checkpoint

Last preceding local checkpoint: `f57f4c8`. Generic native source/proof is in `PostDrawDangerAnonymous.cs` / `anonymous-native-design.md`; independent source review in `native-anonymous-review.md` found no mathematical blocker within the explicit applicability gate. Includes checked integer conditioning, actual removal/search policies, terminal continuation cache and safe capacity envelopes. No production selector calls this method.

- First measured native source SHA256 `F65B91097909FFBAD751FDA465461F9DEF21BBCD50FEC5F49FDE37D88A8936BE`: at140M Work/cap16384, heavy40 width28.19161197pp, Work139999987, expanded19033,1405.26ms; heavy60 width15.25159478pp, Work139999996, expanded12583,940.62ms. Per-row exact endpoints/source/input/assembly hashes and before-assertion records in `anonymous-results.jsonl`. All frozen references enclosed. These results are **too wide**, despite the JS feasibility result; do not conflate the two implementations or their accounting.
- Terminal cache is effective: only709 terminal splits in each case, with22432/16790 hits. Priority tree comparisons and repeated normalized integer transitions are suspected Work bottlenecks; profiling/alternative priority representation still needed. Standard per-structure payload held (40peak230297cells;60peak262144cells). Aggregate estimates500848/572568cells; allocations298MB/205MB, not live heap.
- Native oracle suite:22 cases passed, including generic anonymous samecanonical ordinary copies, duplicate sources, maximum-only constraints, direct Danger requirements, growth/lock/search/failure. The additional Work-1 interruption assertion failed because all mass could finish before the last incidental charged operation. This was a test assumption failure; corrected to locate the exact mass-completion budget threshold and test one below. Corrected assertion not rerun yet.
- Full regular .NET suite3136 passed (approved,3m43s), Node182 passed (approved). Details and restricted Node fixture failure / overlapping build copy-lock errors are recorded in verification.json. No new AOT/browser run or production contract integration.

Next experiment: reduce priority-management Work using deterministic bounded mass-magnitude buckets (approximate expansion priorities do not change enclosure), while retaining actual Work accounting and storage limits. Preserve this source/evidence before changing it. If practical width is still not achieved, report the concrete gap rather than enabling a wide result as success. `integration-map.md` records the required bounded-only protocol/UI work, deliberately not implemented before a useful solver exists.

## Priority-bucket follow-up

Checkpoint preceding this refinement: `d7652ba`. Native priority tree replaced by deterministic exponent/top-bit buckets; process larger aggregate masses first and release completed current states. All23 native research-oracle tests pass, including corrected interrupted terminal commit. At140M Work/Standard, heavy40 width16.70628788pp,1540.10ms,25477 expansions; heavy60 width8.04304007pp,1048.61ms,23090 expansions. Still too wide. Priority cost is now about12M Work; Scale arithmetic31M/45M, other transition/policy/accounting dominates the rest. Actual intervals/hashes/counters appended before assertions in anonymous-results.jsonl.

Next concrete attempt: quotient complete multi-card draw batches by explicit count vectors and FIFO effect words, removing unobservable ordinary interleavings. Proposed branch mass multiplier `(d!/E!) * choose(P,u) * product choose(deck_i,k_i) * product(candidate_i factorial(k_i)) / fallingFactorial(R,d)` per distinct eligible-effect word. No activation/removal occurs during the batch; ordinary labels remain anonymous. Scope excludes ordered terminal search, so non-effect encounter order is unobservable. This is not implemented/verified at this checkpoint. A wider **diagnostic** Work curve may establish remaining cost gap, but must never be reported as satisfying the unchanged150M Fast policy.

## Batch and capacity experiments

Whole-batch variant is now implemented in `PostDrawDangerAnonymousBatch.cs`, explicitly opt-in with `YGO_DANGER_BATCHES=1`. All34 physical-oracle tests passed including both variants and interruption. Heavy140M widths regressed to36.31/19.17pp; retain as a negative experiment, defaultfalse. Larger batch expansion delays useful mass classification and performs substantial within-batch work. A conservative batch-stack payload estimate was then increased; no probability changes. One build was canceled with MSB5021, ordinary retry passed; no environment change.

Nonbatch native diagnostic350M widths9.49/4.077pp,700M2.36738/0.66328pp. These violate Fast's150M policy and are cost-gap evidence only. Source/hashes/endpoints retained per sample.

At140M, nonbatch smaller-capacity sweep:

| Capacity | Heavy40 width pp | Heavy60 width pp | Completion |
| ---: | ---: | ---: | --- |
| 1024 | 9.29386807 | 2.45887129 | retained frontier exhausted at132.74M/137.00M Work |
| 2048 | 7.92461167 | 2.61312412 | Work stopped |
| 4096 | 10.78807429 | 6.87555597 | Work stopped |
| 8192 | 16.59656914 | 7.93548493 | Work stopped |

Every row has the reported common full width, including individual and exclusion rows; all frozen exact row fractions enclosed. No useful-quality release claim or production integration.

Current next substantive hypothesis: after a policy observer, re-anonymize the retained known ordinary hand as a **separate stratum** K with actual held subset size V. Random Danger discards then decrement V without branching by ordinary category. Newly drawn anonymous cards remain U in the separate I/P deck inventory. Reveal both strata at the next observer/terminal, never merge K with deck inventory. This may remove the explicit known-card random-discard branching left in the first abstraction. Implementation/review is in progress; independent proof and native oracle required. Fixed-point lower subprobability masses are another documented, unimplemented arithmetic alternative.
