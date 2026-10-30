# Independent review of generic anonymous Danger prototype

Reviewer: aggregate investigation agent. Lead owns all commits. Review requested after local checkpoint `f57f4c8`; no publication authorized under the latest user instruction.

Status: reviewed first complete source of `PostDrawDangerAnonymous.cs` before its first build. Core probability arguments below check out under its applicability gate. This is not final production approval; resource-accounting observations were sent to the author, and independent native tests/Work/Storage/browser evidence remain pending.

## Checklist tied to the candidate proof

- Anonymous pool excludes effect/search identities and every canonical identity that can compare equal to a Danger source, including ordinary disabled copies/aliases.
- Inventory counts describe unobserved label assignment to unknown held + physical deck + hidden random losses. Known held ordinary counts are excluded from inventory; their discard does not subtract inventory again.
- Anonymous draw class probability uses physical ordinary deck count, not inventory total; hand revelation uses inventory total, not physical deck count.
- Every observable removal/cost/policy decision reveals necessary labels before the original deterministic policy evaluates them; no result-row-specific relevance replaces the original full-combo policy.
- FIFO remains per physical effect occurrence, including duplicate same-name sources; same-name discard removes only the selected copy and different-name discard removes the actual selected source token.
- Terminal physical capacity accounts jointly for hidden losses and unknown held identities; optional searches share actual remaining target copies.
- Prefix weights are normalized completion mass or explicitly multiplied by the correct suffix before becoming residual; no canceled partial expansion may remove its parent before all child mass is committed.
- Refinement/export uses exact integer or rational arithmetic, reserves finalization Work and preserves the certificate on limit/cancel paths.
- Cache keys preserve every future observation; cache values are continuation fractions independent of incoming mass. Optional cache eviction may lose speed, never probability mass.
- Generalized horizon accounts for net positive hand gains, source canonical aliases, costs, locks, non-Danger limits and hidden depletion, or the applicability gate explicitly excludes unsupported cases.

## Evidence available for comparison

Fixture-scoped Node dynamic implementation and independent physical oracle are in `aggregate-dynamic*.mjs`. Five small mixed cases match all six rows exactly, low entry caps contain those exact values, and all stored heavy samples enclose frozen references. Those tests do not prove a generic port with broader supported operations.

## Source review findings

1. **Interruption is mathematically safe despite non-atomic frontier expansion.** This implementation never subtracts a parent from an explicit remaining-mass counter. It defines unresolved mass globally as `D - resolved`; only fully classified terminal contributions and fully established failed-removal outcomes increment `resolved`. `Terminal` prepares every row contribution and completes all budget calls before mutating the accepted global sums. If a Work/Storage exception occurs during expansion, dictionary insertion, comparison, revelation, terminal-cache construction or per-row preparation, all uncommitted outcome mass stays within `D - resolved`. Previously committed terminal outcomes are disjoint from those pending outcomes. This supports enclosure even when a parent was partly expanded. It would not support *resuming that partially mutated frontier* without extra protocol; the current method returns and discards its transient state.

2. **Revelation uses the right population.** `Reveal` samples unknown hand composition from the full pooled unobserved inventory, subtracts observed identities from inventory and adds them to known hand. Physical ordinary deck remains a separate scalar. Anonymous random discards shrink only the unknown hand; known ordinary random discards shrink only known hand. This matches the exchangeability invariant and does not turn unobserved losses into observed depletion.

3. **Canonical comparison and FIFO are retained.** Pooling excludes every ordinary canonical identity that equals any active Danger source identity; those ordinary same-name copies remain explicit. The resolver copies the established physical occurrence branching, adjacent-run aggregation, selected-source queue adjustment and same-identity/no-draw case. Effect entries remain separate after physical coalescing. The applicability gate excludes bounded-search plans, so omitting a separate ordered search FIFO is consistent with existing optional-search evaluation.

4. **The one-removal observation shortcut is justified under the gate.** Every eligible pooled row must be relevant (tier1). Every eligible explicit search source is rejected by the shortcut. A held eligible explicit nonrelevant nonsearch row has tier2/3/4, so it dominates every unknown eligible card. Costs and hand-size gates are excluded, preventing those policy evaluations from observing the unknown count indirectly. Draw locks may change the explicit row's tier but leave it above tier1. The full original combo list still determines relevance and canonical order.

5. **Terminal capacity envelopes are sound even with maxima.** For pooled inventory `v_i`, total hidden loss H and physical ordinary deck P, every actual deck assignment satisfies `max(0,v_i-H) <= available_i <= min(v_i,P)`. Optional search success is monotone in target capacities: a witness using fewer available copies remains an allowed identical witness when capacities grow. This expands allowed search witnesses, not the held hand or activation policy. Therefore maxima and exclusions do not invalidate this particular monotonicity. When the low/high Boolean masks agree per combo, each combo event is constant over the whole conditional deck allocation; union/group rows follow exactly. Otherwise the code enumerates joint physical deck allocations, preserving target correlations.

6. **Terminal cache equivalence is adequate for the current gate.** Known held counts for all rows, pooled inventories, unknown count and physical ordinary capacity remain in the key. Nonpooled deck identities irrelevant to all search targets are zeroed; target copies above total held-source allowance are capped. No terminal witness can consume above that allowance, and there is no subsequent draw. Per-entry search limits are static input data; actual allowances derive from the included held source counts. Search-fetched effects remain inert. Cached continuation mass uses `inventory!` independently of incoming mass; sequential held/deck hypergeometric denominators divide that factorial. Multiplying by incoming mass and checking exact division avoids reusing another history's weight.

7. **Nested budget refinement is monotone.** For a fixed capacity and deterministic traversal, increasing the Work allowance extends the same accepted-terminal prefix. Every new accepted outcome adds success mass in `[0,newlyResolvedMass]` per row, so lower endpoints increase and upper endpoints decrease. This does not assert monotonicity between different frontier capacities or other search-order changes.

## Corrections/limits communicated to author

- Target-predicate preprocessing currently charges a row/source count while evaluating `Search.Matches`; selector evaluation complexity and category memberships may require the same fuller cost convention used by the existing physical counter constructor.
- `EnumeratePool` checks its own scratch/recursive payload, but the enclosing `SettleAnonymous` frame and callback stack do not include that live conditioning payload in `PeakConcurrentCells`. Combined retained-memory reporting should include overlapping conditioning arrays/frames, not only the active resolver payload. Per-structure checks and aggregate retained reporting are distinct obligations.
- The result accumulator commit loop is safe for the explicit Work/Storage exceptions because it has no budget calls. Out-of-memory or unrelated runtime exceptions must remain failures; do not broaden the catch and label them certified completion without reviewing atomicity.
- Preprocessing can still exhaust before the guarded refinement loop; that is a normal failed request until a separately valid initial enclosure/result protocol is implemented. Do not mislabel every exception as a bounded result.
- This source is isolated research, not yet wired through Fast/Progressive, wire contracts, pins, display/export or cancellation ownership. Those integration obligations remain outstanding.

### Accounting corrections rechecked

The author added the existing selector/category-complexity charge before target predicate compilation. The author also added request-local live `conditioningCells`/`conditioningFrames`, incremented before recursive pool enumeration and decremented in `finally`, with active resolver/terminal payload passed into the combined stack check and included in aggregate telemetry. These address the two initial accounting observations. Runtime counter measurements and production integration review remain necessary; this source-level recheck does not claim tests have run.
