# Independent mathematical review and research leads

Status: research, 2026-10-30. No production implementation reviewed yet. Lead owns commits; this handoff has no separate agent commit.

## Source and paper inspected

Inspected Danger resolver/streaming, ordered resolver, physical search preparation, terminal accumulator, result contracts, and `MeasureDanger` at baseline `bd75460e4bb3d9f990d8bc04bd1fcd915bcabc71`.

Read sections 2 and 4, including Algorithms 2–4, of [Dubray, Schaus and Nijssen, CP 2024](https://drops.dagstuhl.de/storage/00lipics/lipics-vol307-cp2024/LIPIcs.CP.2024.10/LIPIcs.CP.2024.10.pdf). The solver uses projected weighted Boolean counting, normalized discrete distributions, Horn propagation and independent-component decomposition. It maintains recognized success/failure weight, explores high-weight choices through limited discrepancy search, and intersects successive valid bounds. Its fixed distribution weights and independent component products require a justified encoding; they cannot be applied directly to dependent draws/discards. The high-weight heuristic is not a convergence guarantee for this workload. The relevant transferable idea is to preserve both success and failure certificates, with an explicit residual. Inspect the implementation rather than copying pseudocode literally: the printed outer loop initializes `ub` to zero although the subsequent minimum update requires an initial upper bound of one, and the cache discrepancy comparison also deserves checking. This paper establishes no browser feasibility result for Danger.

## Exact mass invariant in the current encoding

Let N be original copies, S the initial random-discard scale, r the unseen physical suffix length, and w the current prefix weight. The probability represented by that prefix is `w * r! / (N! * S)`, not `w / (N! * S)`. A draw partition with c_j remaining copies of row j preserves it because `sum(c_j) = r` and `sum(w*c_j*(r-1)!) = w*r!`. A random-discard partition preserves it because the queue-aware branch multiplicities sum to the physical hand size h and the children receive `w * multiplicity / h` at the same r. Hidden-loss deferred pools require retaining the current solver's conditional allocation semantics; never replace r with effective deck size without proving the denominator change.

For any disjoint unresolved prefixes, accumulate rowwise resolved-success and resolved-failure mass. The interval is `[success, 1-failure]`; failure includes semantically failed branches such as an unavailable mandatory postdraw removal. This avoids pretending that omitted continuation mass vanished. Coalescing equal continuation states adds incoming weights; memoized continuation probabilities must exclude incoming weights. If an expansion may fail midway, retain its parent until all children and terminal contributions have been committed, or track a provably disjoint unprocessed child residual.

## Counterexamples / invariants a new approximation must survive

- A hand satisfying a minimum can cease to satisfy it after a random discard. A currently violated maximum or zero exclusion can become satisfied after a discard. Neither current success nor current failure alone is an absorbing certificate.
- Ignoring activations can increase or decrease a row's success: an activation can draw a needed extender, discard the only extender, or remove the sole forbidden starter. It supplies neither a general lower nor upper bound.
- Expanding the available hand by one forbidden card changes an exclusion from success to failure. A monotone superset argument must evaluate maxima universally, and terminal searches remain optional choices over each actual outcome.
- Same categories/name labels do not establish state equivalence. Canonical Danger identity determines the no-draw branch; per-entry allowances and effect/search FIFO tokens affect subsequent transitions. Nonadjacent equal draw tokens cannot generally be merged because discarding different occurrences changes the FIFO word.
- Search existence is per concrete outcome. A lower witness must be legal for that outcome; an abstract existential witness that chooses a different hidden allocation without conditioning proves no lower bound.
- Union rows must use the same coupled terminal outcomes. Summing individual lower probabilities is unsafe; `max(lower_i)` and `min(1,sum(upper_i))` are safe but may be loose.
- A result with an interval but no completed primary fraction needs an explicit contract. Current `SearchAvailability` naming, `CalculationFractions`, display/export/pin/wire validators assume distinctions that must be audited together.

## Heavy-fixture event algebra (not a production specialization)

For final hand counts S=starter, E=extender, O=overlap, the three events are: distinct S+E; O>=1; and S=0,E>=1. Their union is exactly E>=1. The positive group is exactly S>=1,E>=1, since O covers the only failure of distinct assignment. The exclusion group duplicates its sole member. This identity follows from the predicates and can support an independent oracle or generic predicate simplification; it must not be hardcoded from fixture labels or exact fractions. It does not justify early success while draws/removals remain pending.

## Conservative visible-draw horizon experiment

`review_horizon.py` is a small exact-rational independent count-state experiment. It charges every encountered Danger copy one optimistic draw credit, every Allure copy two, and Graceful three; all other cards have zero credit. Start with five available draws. Ignore all random/deterministic discards, costs and locks. Continue while a credit is available. Every actual visible draw can be coupled to the same deck permutation: it consumes one credit, and every actual extra draw is charged to a distinct encountered source. Thus the optimistic depth dominates actual depth on these fixtures. The capped variant permits at most four Danger credits: ordinary effects have net hand gain zero; every Danger activation shrinks the settled hand by one, and at hand size one its only possible discard is itself, yielding no draw. The initial hand is five, so at most four Danger draws occur. This cap is specific to verified no-positive-net-gain effects and the fixed base-only policy.

The count-state tuple is encountered counts of (Danger,Allure,Graceful,ordinary). Conditional next-row probabilities are remaining copies divided by remaining deck size. Exact `Fraction` accumulation keeps the probability of reaching each depth. Result JSON retains rational endpoints and state counts. These are **upper bounds on the unresolved mass after a depth cutoff**, not measured solver residuals or achieved row intervals.

| Optimistic tail | 40 cards, capped | 60 cards, capped |
| --- | ---: | ---: |
| Reach depth 10 | 21.62464% | 8.03899% |
| Reach depth 12 | 8.92771% | 2.12554% |
| Reach depth 13 | 5.21813% | 0.94852% |
| Reach depth 14 | 2.91327% | 0.39489% |
| Reach depth 15 | 1.04410% | 0.11142% |
| Reach depth 16 | 0.57935% | 0.03933% |

The optimistic process uses at most 43 states in these runs. A depth-only cutoff would need resolving all histories through depth 14 for 40 cards (through 12 for 60) before this envelope alone reaches 2% width. No runtime claim follows. The real mass may be much lower because discarded effects and same-identity no-draw outcomes are ignored. This is a cheap feasibility/cutoff diagnostic, not yet the selected method.

Reproduce with a usable Python 3: `python research/danger-fast/review_horizon.py`. The system `python` app execution alias was unavailable in this environment; the bundled runtime Python succeeded. Output: `research/danger-fast/review-horizon-results.json`. No heavy Exact rerun, .NET tests, or browser benchmark was performed by this reviewer.

## Recommended next experiment

Instrument a separate bounded prefix prototype to measure actual normalized unresolved mass by visible depth and Danger activation count, using atomic parent refinement. Retain analytic failure certificates before enumerating deep tails. If width stays high at 150M honestly accounted Work, pivot to continuation-value reuse or aggregate ordinary-state abstractions; do not market the conservative horizon bounds as useful Fast delivery. Fresh independent review must compare the selected integrated code against this invariant and test queue/identity/cost/search counterexamples.

## Fresh review of the isolated beam prototype

Reviewed `PostDrawDangerBeam.cs` and `DangerBeamExperiment.cs` after implementation. The complete terminal counter runs before any success/resolved accumulator mutation; all Work calls precede that commit. Coalesced histories have disjoint incoming masses, and discarded frontier entries remain in denominator-minus-resolved mass. Aborting the entire traversal on interruption therefore preserves the intended enclosure. Advancing resolved mass before all row-success additions would break this argument, but the reviewed ordering avoids that defect.

The scope is narrower than general supported requests: hidden losses and bounded searches are excluded; packing assumes byte-sized vectors; the harness sets every input entry active. Work/storage finalization and ranking estimates are provisional. These shortcomings prevent promoting it as the production route. No independent small oracle enclosure tests had run at this review point. The lead subsequently reported widths too large for the performance goal; that outcome motivated the continuation prototype below.

## Conditional continuation prototype (implemented, awaiting lead validation)

Files: `PostDrawDangerContinuation.cs` and `DangerContinuationExperiment.cs`, opt-in only via `Research.targets`; normal production source/route is untouched. Lead owns compilation, serial measurements and commits. No separate agent commit.

State is the complete existing ordered resolver vector: remaining per-row deck counts, held counts, allowances, activation locks, current batch, completion source and FIFO tokens. The guard excludes hidden losses/bounded searches and nonactive or non-byte-packable decks. `r` is recoverable from the sum of remaining counts, so it need not be duplicated in the cache key.

For a nonterminal state, draw one physical copy and run the unchanged `SettleDanger`. Start each row branch with weight `S*c_j`, where S is the verified global random-discard scale. Every emitted child has normalized conditional probability `w/(r*S)`. Children with identical state coalesce by **adding only their local edge weight**. A completed expansion may omit mandatory failed-removal branches, which are failures for every row; its emitted child weight is at most r*S. If expansion is interrupted, no such mass is classified and the state keeps `[0,1]`.

Conditional intervals use exact unsigned integers at `T=2^48`. A terminal produces `[0,0]` or `[T,T]` per row after complete physical search. For visited children with edge weights w_i and bounds [l_i,u_i], and still-unvisited emitted mass m, set `L=floor(sum(w_i*l_i)/(r*S))` and `U=ceil((sum(w_i*u_i)+m*T)/(r*S))`. All numerators/rounding use `BigInteger`; the returned endpoints are rational L/T and U/T, not floating estimates. This proves enclosure by induction on decreasing r. Rounding incurs at most one grid unit per aggregation direction; complete traversal is numerically bounded rather than necessarily exact. It does not fabricate a completed primary fraction.

The cache contains conditional intervals, never incoming history mass. LRU eviction discards only reusable computation. Child order is descending conditional mass with an insertion-order tie break. All children are generated before sorting. Arithmetic for combining a child and finalizing its parent is charged before descending, so a Work interruption can return the completed child refinement and unwind without another Work call. Cache insertion failure retains the already computed interval. The reported `FinalizationWork` is an included subset of spent Work, unlike the initial beam's separately estimated finalization. Export/wire/browser overhead remains outside this isolated harness and has not been certified.

Provisional implementation limits: bounded cache and recursive child dictionaries can still exhaust Standard Storage; a partially expanded state then remains unknown. Sorting/cache/object-memory accounting needs fresh inspection. The harness still uses only the frozen active fixtures and references, not a general oracle suite. No production-ready or five-second claim is made.

Initial lead-run untruncated traversal at capacity 4000 and 140M Work produced approximately 99.6284pp / 94.0793pp widths for 40/60 cards; about 95M Work was arithmetic precharge. These runs preceded the terminal-cache deduplication edit. They are **valid but uncompetitive**, because depth-first exploration spends most budget fully resolving a tiny deep subtree before the broad opening-hand distribution. This is a counterexample to assuming conditional caching alone fixes mass coverage.

At the lead's request, the prototype now accepts a fixed visible-draw horizon (default 8, harness `YGO_DANGER_HORIZON`). After terminal handling, a state at that depth with further draws pending returns `[0,1]`. The horizon is fixed for the entire invocation/cache; no incorrect reuse across horizons occurs. Completed horizon-limited traversal can cover shallow terminal mass before deep tails. Increasing the horizon in a fresh interrupted run need not improve bounds under a fixed Work budget; the intended experiment compares measured width/cost, not a claim of monotonic cross-run refinement. Next: lead builds, runs horizons 6/8/10 serially and adds independent small-oracle checks; samples persist to `continuation-results.jsonl`.
