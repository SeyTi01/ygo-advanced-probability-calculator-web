# Independent proof attack: two-stratum anonymous ordinary cards

Status: proposal and first two-stratum implementation reviewed. No mathematical blocker found under the explicit applicability gate. Native independent tests and performance checks remain the lead's responsibility; this source review does not claim they ran. Agent owns this note only. Lead owns local commits; no pushes authorized by the latest instruction.

## Proposed invariant

Retain explicit effect/search rows and canonical Danger-equivalent ordinary copies exactly. Let I be the row-count inventory of never-observed ordinary labels, U its currently held anonymous slots and P its physical deck slots; its remaining slots are hidden random losses. Separately let K be the exact row-count inventory of an ordinary hand bag observed at the last relevant policy boundary, and V the number of its physical copies still held. The remaining K slots are random hand losses that can never return to the deck. K and I refer to disjoint physical copies even when their row/category labels overlap.

Conditional on the complete abstract state, the V surviving K slots are a uniform V-subset of K; assignments of I labels to U held, P deck and hidden-loss positions are uniform. These two conditional label assignments are independent. This is stronger than merely stating correct marginals and must be maintained by each transition.

## Why the independence claim can hold

At each re-anonymization, the actual surviving ordinary hand composition has just been revealed, and K is set to that exact bag with V=sum(K). Thus the K assignment is initially degenerate. All remaining never-observed labels have the usual exchangeability conditional on their exact I inventory. Until the next observation, the only permitted K mutation is uniformly random Danger deletion: selecting the K stratum occurs with probability V/total hand, independent of all K/I labels; then the deleted physical slot is uniform in the K survivors. This preserves uniformity of the remaining K subset and its independence from I. A random I-hand deletion similarly depends only on U/total hand and preserves the I assignment law. Drawing an I-ordinary copy chooses the physical ordinary class using P/physical deck size; it does not observe any K label or I identity. Explicit-effect draws and source selection observe neither stratum's labels under the applicability gate.

At the next policy observation, sample K survivors by multivariate hypergeometric weights `prod C(K_i,k_i)/C(sumK,V)`, and I held labels by `prod C(I_i,u_i)/C(sumI,U)`. Their **joint** weight is the product because of the invariant above. Subtract only the I observations from I; K observations were already excluded from I. Execute the unchanged deterministic policy using the resulting full actual hand, original full-combo relevance and canonical row order. Replace K with the actual surviving ordinary hand and V=sumK. Every unheld old-K copy stays a permanent hand loss; it must never be returned to I or to P.

## Counterexamples / invalid shortcuts to reject

- Returning `K_i - sampledHeld_i` to I would make previously discarded physical cards drawable/searchable again.
- Treating K as another draw pool would confuse hand survivors with physical deck availability.
- Pooling an ordinary disabled/alias copy with a canonical identity equal to a Danger source would change same-name no-draw outcomes into different-name draw outcomes.
- Selecting a K stratum with probability sumK/hand instead of V/hand overweights already lost copies.
- Revealing only I before Allure/Graceful would let hidden K category/row counts change the fixed removal choice or failure outcome.
- Evaluating combo probabilities independently for the two strata and combining them by multiplication/summation would lose cross-stratum distinct-copy, maximum and union semantics. Independence concerns label allocation only; event evaluation must use the combined hand for each joint assignment.
- Caching solely by I/U/P and explicit hand counts would alias different K/V distributions. K's entire relevant inventory and V are future-observable and must be retained in the key.
- Keeping the former terminal unit `factorial(sumI)` is generally insufficient after introducing K uncertainty. A safe conditional counting unit is `factorial(sumK) * factorial(sumI)` (or a proved common multiple). Successive I-hand/deck allocation denominators divide I!, and K-hand denominators divide K!. The global physical sample denominator remains valid because K/I are disjoint physical subsets and K!I! divides N!.

## Observer and cache obligations

Existing one-discard skip can generalize only if it treats **both U and V** as unknown ordinary hand. Its proof remains valid when every eligible pooled row is tier1, an eligible held explicit nonrelevant nonsearch row dominates with tier2/3/4, and costs/minimum-hand-size predicates cannot observe unknown categories/counts. Danger discard denominator must include explicit held + U + V. All other policy observations must reveal both strata before evaluation.

At terminal, sample the K hand and I hand jointly, then sample physical deck targets only from I's remaining inventory at capacity P. Optional search choices share these actual target copies. K hidden losses do not affect the I physical-capacity envelope except through already-excluded counts. Terminal continuation cache values must remain normalized fractions independent of incoming prefix weight; keys include K/V as well as I/U/P, all fixed held rows, and capped explicit target capacities. Any cache compression needs a future-observation proof.

## Next review step

Inspect the actual two-stratum transition, re-anonymization placement, observer skip, conditional unit and terminal key. Then compare native results against the independent physical oracle with forced repeated observations, K losses between observers, mixed K/I random discards, same-canonical ordinary copies, exclusions, and very small Work/Storage cutoffs.

## Actual implementation review

Reviewed the version of `PostDrawDangerAnonymous.cs` introducing opt-in `retainedStratum` and packed U/P/V metadata, with whole-batch plus retained-stratum combination explicitly rejected.

- K uses pooled rows in the existing hand vector; V is `retainedKnown`. Physical deck draws never inspect or decrement K. Danger hand-size calculation adds U+V and only nonpooled explicit hand rows. The ordinary known-row branch loop skips K rows; the additional K branch uses V/hand, decrements V alone, and leaves K unchanged. These are the required stratum transitions.
- `RevealBoth` first samples V from K using `EnumeratePool` at `layout.HandStart`; it replaces K counts with the sampled surviving hand without modifying I. It then invokes the existing U-from-I revelation, which subtracts only sampled I labels and adds them to the now-known actual ordinary hand. Thus the old K's lost copies never return to I or the deck.
- Re-entering settlement after revelation sets V to the exact observed combined hand size. `knownExact` permits resetting V after the actual policy removes cards. When K remains unobserved and the proved explicit-dominance shortcut executes, the selected removal is necessarily nonpooled, so keeping K/V unchanged is correct. An unobserved K does not reach an arbitrary removal.
- Existing `SelectCandidate` and `Activate` cannot observe phantom K inventory counts under the gate: pooled rows are non-effects/non-searches; pre-draw costs and minimum-hand-size gates are excluded. Timing, priorities and allowances depend only on explicit effect state. The one-removal shortcut remains valid because any eligible K/I row is tier1 and an explicit eligible row with tier2/3/4 dominates. If that condition is absent, both strata are revealed.
- Terminal key retains K in the pooled hand slots, I in pooled deck/inventory slots, and all U/P/V metadata. The terminal unit is I!*K!, as required. `RevealBoth` builds the combined actual hand before the generic event/search evaluator runs. Physical deck allocation still uses only remaining I and P, so K's hand losses cannot become targets.
- Fixed-budget refinement remains a deterministic prefix. Endpoint monotonicity follows from fully resolved mass commits as in `native-anonymous-review.md`; it does not rely on a guessed independence of combo events.

## Related bucket and batch checks

The approximate priority bins use mass exponent plus top four significant bits only to choose refinements/evictions. FIFO ties and packed-state equality retain deterministic traversal. Removing/updating a node repairs its map and linked-list membership; stale minimum/maximum descriptors are scanned before use. Rejecting a larger weight within the same coarse bucket affects quality, not enclosure, because its mass stays unresolved. The globally normalized mass of a disjoint frontier state cannot exceed D, so descriptor count based on D's bit length covers all valid weights.

Separately reviewed the whole-batch coefficient: a count vector has probability `prod C(c_i,k_i)/C(R,d)` and each distinct candidate FIFO word has share `prod(candidate k_i!)/E!`. This equals the implementation's `prod C * prod(candidate k_i!) * d!/E! / (R)_d`. Candidate eligibility is fixed until batch completion; noncandidate interleavings are unobservable under the simple-search gate. Combining this quotient with two strata requires threading K/V through unchanged; the current explicit rejection avoids assuming that integration is already implemented.

## Remaining tests / no approval beyond scope

Force at least two category-observing effects with a Danger deletion from K between them, plus later new ordinary draws into U, so both strata are genuinely uncertain at the second observer. Include V=0 with nonempty old K, K/I containing the same category labels, aliases excluded from pooling, optional terminal searches requiring depleted targets, maxima/exclusions and interruption during the second conditional enumeration. Oracle equality and useful width/Work/Storage measurements remain necessary before any production claim.
