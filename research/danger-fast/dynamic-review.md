# Independent review of dynamic anonymous-ordinary prototype

Reviewed `aggregate-dynamic.mjs` against the production `PostDrawPreparation`, `PostDrawOrderedResolver`, `PostDrawDangerResolver`, `PostDrawSearchKeepPolicy`, and `PhysicalSearchHandCounter`. This is a review of the JavaScript research candidate, not approval of a production C# integration. No source edits to the candidate were made by this reviewer.

## Conclusion at this checkpoint

The anonymous-pool idea has a defensible exact conditional-distribution invariant for the original heavy fixture. I found no concrete fixture-semantic contradiction in its draw/discard/removal/search transitions by source inspection. That statement is limited: it is not exhaustive oracle validation, not a proof of generic support, and not validation of a future port.

Two established delivery failures remain:

1. **The measured 8,192-state results exceed Standard Storage already.** The prototype reports a lower-bound retained payload of 285,809 cells for 40 cards and 310,671 for 60, versus the 262,144-cell limit. It also reaches 16,385 map entries before trimming, holds the current and next maps simultaneously, materializes a sorting array, keeps stringified keys and nested arrays, and uses recursive revelation temporaries. The reported cell lower bound observes only the post-trim next map. Its own reported number is enough to reject a Standard-compliant claim; actual memory is larger. Packing and honest counters must precede a claim that the target fits policy.
2. **Increasing beam capacity does not give nested intervals.** A concrete counterexample is below. Individual completed intervals can still be sound; narrowing width is weaker than monotone refinement of both endpoints.

There is no Work counter, safe budget interruption, generic input adapter, or worker/result integration yet. Node runtime is not evidence for published-AOT end-to-end latency.

## Exact anonymous-pool invariant

Use labeled physical copies as the underlying sample space: a uniform full deck permutation, together with independent uniform discard choices at each realized Danger activation. For this fixture, all ordinary draw effects have zero net settled-hand gain; every Danger activation reduces settled hand size by one. Thus at most `min(opening hand size, total Danger copies)` activations occur, and every random-discard hand size is at most the opening hand size. Padding unused random choices with a uniform slot in `1..LCM(1..H)` gives an equal-weight universe of size `N! * LCM(1..H)^min(H,D)`. This justifies the prototype's integer denominator only under these gain/activation assumptions.

Condition on the explicitly observed history. For ordinary labels still in `inventory`, all assignments of its remaining type multiplicities to three kinds of unobserved physical positions are equally likely:

- `unknown`: anonymous ordinary hand positions;
- `pool`: physical ordinary deck positions;
- the remaining positions: anonymous already-lost ordinary copies.

The third count is `sum(inventory) - unknown - pool`, which must remain nonnegative. Known ordinary hand positions and previously revealed/removed identities have been subtracted from `inventory` and are not in that exchangeable pool. The state also retains all explicit effect counts and physical FIFO tokens; their incoming probability mass is separate from this conditional distribution.

Why the operations preserve the invariant:

- Drawing an ordinary copy moves one unobserved physical position from deck to anonymous hand, with probability `pool / total physical deck`. It does **not** reveal the row label or reduce inventory.
- A uniformly random discard of an anonymous ordinary hand copy moves an unobserved hand position to anonymous loss. Inventory again stays unchanged. The probability `unknown / hand size` is independent of that unseen label.
- Discarding an already-known ordinary copy changes only known hand counts. It must not be returned to inventory.
- Revelation samples the hand's row counts with the multivariate hypergeometric probability `product(C(inventory_i, count_i)) / C(sum(inventory), unknown)`. Removing the revealed counts from inventory makes the rest exchangeable. Deterministic removals then act on the same fully known hand as the physical policy.
- At terminal search, after hand revelation, the ordinary physical deck is a uniformly chosen `pool`-subset of the remaining inventory. The prototype jointly enumerates Starter and Overlap deck counts; aggregating filler and Extender into `other` is valid here because neither is a search target and neither deck label affects the event after the draw phase.

The invariant is stronger than having matching marginal probabilities. It must hold after every observation: an identity-dependent removal, search-retention tier, canonical tie, or stopping decision cannot be taken while pretending its observed labels remain anonymous. Two histories may coalesce only when they induce the same conditional distribution and all continuation-observable state, not just the same hand-size/category totals. The current complete state includes the inventory vector, preventing the most obvious lost-depletion merge.

## Agreement with the original fixture policy

- `SearchKeepPolicy.Tier` deliberately compares held target counts with their **original** copy counts and physical remaining capacity, not known actual surviving targets. The prototype's `known[1] < ordinary[1] || known[3] < ordinary[3]`, evaluated after full revelation for Graceful removals, matches that intentionally coarse retention policy. Replacing it with actual survivor availability would change the model.
- Production `RemoveCard` first classifies relevant cards as tier 1, usable effects tier 2, deferred effects tier 3, other cards tier 4; the highest tier and last canonical row lose. The fixture has no hand costs, hand-size gates, activation limits, locks, first-only effects, or hidden deck operations. Both ordinary positive-role rows and Overlap are relevant. Graceful's full revelation therefore permits the same policy choice. Removing the latest FIFO token of a deterministically discarded effect matches production.
- Allure's shortcut is conditionally valid **for this fixture**: every ordinary DARK card is positively relevant (tier 1), every held queued Danger with a remaining deck card is usable (tier 2), and their canonical ties select Nessie, then Mothman, then Bigfoot. No ordinary DARK row can win that removal. This is not a general rule about Danger or DARK cards.
- Danger random selection enumerates each queued physical occurrence separately, retaining FIFO differences after deletion. Unqueued held copies, known ordinary copies, and anonymous ordinary copies contribute their physical multiplicities. Different effect rows are treated as different canonical identities; this agrees only because the fixture has exactly one entry per distinct Danger canonical identity and no ordinary copy sharing one of those identities.
- Terminal searches are identical, repeatable, costless optional one-card searches for Starter/Overlap. Enumerating every `starter + overlap <= held search count`, with shared physical capacities and the option to search zero times, reproduces the allowed choices. Search sources have no combo role, so their removal does not change the hardcoded predicates. Each row is an existence event over choices; total/group events OR the row booleans on each same physical outcome. The implementation does not sum overlapping combo probabilities.
- For the first combo, `s + o > 0`, `e + o > 0`, and `s + e + o >= 2` correctly require two distinct physical copies. The hand-wide maxima include overlap copies in both categories; the exclusion tests `s + o == 0`. Optional search does not force an excluded Starter into the hand.

## Concrete non-nested capacity counterexample

This deterministic tiny case was executed without a heavy benchmark:

```javascript
import { dynamic } from './research/danger-fast/aggregate-dynamic.mjs';
const input = { ordinary: [1, 1, 1, 1], effects: [1, 1, 0, 1, 1, 1], handSize: 2 };
const first = dynamic({ ...input, cap: 1 });
const second = dynamic({ ...input, cap: 2 });
console.log(first.bounds[1], second.bounds[1]);
```

For the Starter-and-Extender combo (row index 1):

| Capacity | Lower | Upper |
| --- | --- | --- |
| 1 | `4001/30240` | `30263/36288` |
| 2 | `25/189 = 4000/30240` | `3769/5040` |

The lower endpoint decreases by exactly `1/30240` when capacity increases. Different pruning/coalescing histories mean that the larger beam need not explore every contribution explored by the smaller beam. Within a single execution, accumulating resolved successes/failures with pending and residual mass can produce nested checkpoints. Across restarted runs, retain a refinement tree or intersect independently valid intervals, and charge all replay work. Do not claim the capacity parameter itself supplies monotone refinement.

## Preconditions and counterexamples for a generic port

The current script must remain fixture-scoped. It ignores the frozen custom-priority/direct-card challenge definitions, hardcodes six row predicates and fixed maxima five, fixes the ordinary and effect row ordering, and has no disabled/group-general handling. Passing altered multiplicities or hand six is not sufficient evidence of generic support.

Specific traps to test before port approval:

- Add a DARK ordinary card that appears only in a zero-maximum requirement. Production's `relevant` flag tests **positive minima only**; that ordinary card can be tier 4 and discarded before a usable Danger (tier 2). The current Allure shortcut would discard Danger instead if generalized blindly.
- Make Danger itself a positive direct-card requirement. Its tier becomes 1; ordinary DARK ties and canonical ordering may select an ordinary card. The same shortcut now needs a different proof or revelation.
- Add/reorder a direct-card or maximum-only selector. Canonical reduction order includes those selector bits even though positive relevance may not. Hardcoded canonical indices can change which ordinary row Graceful removes.
- Add two workspace entries/artwork aliases for one Danger canonical identity, or an ordinary/no-simulation copy of that identity. `target !== source` is no longer the printed same-identity test. Preserve canonical identity separately from entry allowances and physical FIFO occurrence.
- Add hand costs, minimum-hand gates, locks, activation limits or positive net-gain effects. These require additional state and can invalidate both the removal-tier simplification and the `H`-based denominator/horizon. Generic production `RandomScale` already accounts for positive gains.
- Add search targets with differing eligibility, source roles, costs, alternative modes, per-entry allowances or search ordering. The terminal `search` count and two target capacities are then insufficient; reuse an established physical terminal evaluator after revealing the relevant joint state.

A safe generic starting point is to keep every effect and every Danger-equal canonical identity explicit, retain row-level inventory descriptors in canonical order, and reveal the anonymous hand before any identity-sensitive policy observation. Optimize a particular observation away only after proving its choice invariant for every possible anonymous assignment. At terminal evaluation retain the joint hand/deck/loss law, not independently sampled marginals.

## Interruption, accounting and arithmetic review

Current pruning adds each evicted contribution's exact completion mass to residual; later re-arrivals of the same state represent different histories and can be retained without double counting. Full-layer `pending + residual + resolved + failures == denominator` assertions are useful evidence. They do not make interruption safe: `settle`, `reveal` and terminal enumeration mutate wins/residual during a parent's expansion, and the parent is not kept as an explicitly publishable pending enclosure. A budget exception midway cannot be caught to publish the current accumulators.

A port needs transactional per-parent refinement or explicit residual ownership for every not-yet-generated child, with budget reserved for final row construction. BigInteger endpoints are exact, but `lower_decimal`, `upper_decimal` and width conversion are diagnostic doubles only, not outward-rounded certified display values. The prototype's locale-dependent string tie-break should become an explicitly ordinal deterministic key comparator. Factorials stop at 80 and no input validation enforces safe ranges. None of these limitations is addressed by agreement with two frozen fractions.

## Verification status and next step

Source review and the cap-monotonicity counterexample above are complete. No independent physical-oracle sweep or Exact rerun was performed in this review. The lead should first port the invariant with full resource accounting and existing policy/terminal evaluators, then request a fresh integrated-code review and small exact containment tests before interpreting performance. This reviewer made no commits; lead owns local checkpoints under the owner's no-further-push direction.
