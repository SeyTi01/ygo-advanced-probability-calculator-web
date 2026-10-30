# Aggregate investigation: reveal ordinary identities only when observed

Status: mathematical candidate and isolated prototype, not production implementation. Lead owns commits; this investigation has made no commit of its own.

## Source observation

At baseline `bd75460`, `PostDrawDangerResolver.BranchDanger` branches separately on every ordinary row although the transition observes only physical multiplicity and whether its canonical identity equals the revealed Danger source. The ordinary row's combo categories do not affect this random choice. `RemoveCard` does observe categories, removal eligibility, relevance and canonical row order, so ordinary cards cannot remain pooled across an Allure/Graceful removal without further conditioning. Existing `DeferUnobservedIdentities` explicitly disables Danger and excludes all potentially removable material; it cannot simply be enabled unchanged.

## Candidate state and invariant

Keep every effect/search entry and FIFO effect token explicit. Pool only ordinary entries whose canonical identities differ from every possible Danger source identity. For each pooled row retain its **unobserved identity inventory**, an anonymous held count U, and an anonymous hidden-loss count H. Known held ordinary counts are separate. Unobserved inventory labels are uniformly assigned to anonymous held slots, physical deck ordinary slots, and hidden-loss slots, conditional on the observable history. Thus physical ordinary deck count equals inventory total minus U minus H.

- Anonymous ordinary draw: select the whole ordinary class with probability physical ordinary deck count / physical deck size, increment U, without revealing its category.
- Random Danger discard of an anonymous held ordinary token: probability U / hand size; decrement U and increment H. It always differs from the source identity under the partition precondition. Remove the explicit source then draw as usual.
- Before a policy operation that observes ordinary identities, reveal all U anonymous held labels with multivariate hypergeometric weights `product choose(inventory_i, count_i) / choose(inventory_total, U)`, subtract them from the inventory, and add them to known held counts. Run the existing policy using the original full combo list and canonical row order. This is conditioning, not changed policy.
- At terminal, reveal held ordinary composition and, where terminal searches need it, joint remaining-deck composition. Hidden losses stay represented by the complement. A terminal hand alone does not determine physical target availability.

Exchangeability proof sketch: condition on all previous observations and operations. Every bijection of unobserved ordinary labels over these anonymous positions preserves every prior observable choice and its weight; random Danger selection uses physical slots and the pooled identity-difference predicate only. Drawing/relabeling an anonymous slot and uniformly discarding it preserve that symmetry. Explicit hypergeometric revelation conditions on an orbit and leaves the unobserved complement uniformly assigned. By induction this implements the same joint probability law. This proof does not permit hiding effect identities, disabled copies sharing a source identity, search sources, FIFO tokens, per-entry allowances, or any ordinary observation by a policy predicate.

## Intended bounded use

A frontier can stop before a revelation, retain its full normalized mass as unresolved, or refine the hypergeometric partition atomically. This supplies a valid interval independently of a completed primary calculation. Such a no-revelation fallback is expected to be too wide when Allure/Graceful are common; measure that before claiming usefulness. Dynamic revelation and bounded continuation caching may improve it, but their practical costs are unmeasured.

## Executed experiment (2026-10-30)

Run `node research/danger-fast/aggregate-prototype.mjs`. Each completed case appends to `aggregate-results.jsonl`. The script uses reduced BigInt rationals and checks exact total mass one; no Monte Carlo, float endpoint arithmetic, external package, production counter or heavy Exact execution. Timings are exploratory Node desktop wall times, not browser/AOT performance claims. Python was unavailable (Windows Store alias failed; `py -0p` found no installed interpreter), so the experiment was ported to dependency-free Node.

Four pure-Danger inputs compare three row probabilities (including a zero-maximum exclusion and distinct positive requirements) exactly against independent enumeration of labeled deck permutations and uniform physical hand discards. All four agree. This is a check of the anonymous ordinary skeleton, **not** independent verification of the complete heavy mixed-effect model.

| Frozen heavy size | First Allure/Graceful censored mass | Expanded skeleton states | Peak frontier entries | Node seconds |
| --- | ---: | ---: | ---: | ---: |
| 40 | 36.0066973473% | 10,184 | 3,613 | 0.207 |
| 60 | 24.4590639746% | 10,184 | 3,613 | 0.241 |

The exact first-observer masses are `265832036869/738284976000` and `2397858465283/9803557764000`. This establishes that censoring at the first category-observing effect is far too wide for the initial 2-point goal even if all non-censored terminal rows were solved cheaply.

A second fixture-specific refinement safely executes Allure whenever its post-draw removal can choose a queued Danger. All pooled DARK ordinary rows are relevant tier1; the explicit queued Danger is tier2. The canonical same-signature ID ordering selects Nessie, then Mothman, then Bigfoot. No ordinary identity needs revelation in that case. If no such Danger remains, the prototype censors the complete parent mass; Graceful always censors. Both cases preserve mass one.

| Frozen heavy size | Refined censored mass | Expanded skeleton states | Peak frontier entries | Node seconds |
| --- | ---: | ---: | ---: | ---: |
| 40 | 23.9754297385% | 86,489 | 20,930 | 1.840 |
| 60 | 18.0781171476% | 86,489 | 20,930 | 2.373 |

Exact refined masses are `36874097342363/153799526200320` and `51520721965111199/284989424199480000`. Still too wide. State count alone does not establish Storage compliance: this prototype retains JSON keys, arrays and rationals without production cell accounting. It is not a production Fast implementation or a completed bounded result.

## Remaining proof/implementation work and strongest next step

Implement generic atomic hypergeometric revelation at the actual policy observation, retaining original canonical order and original full-combo relevance. The posterior inventory must distinguish known ordinary hands from unobserved hand/deck/hidden positions; treating hidden random discards as known depletion is incorrect. Conversely, revealing all anonymous cards at every Allure activation misses the proved cheap explicit-Danger removal case above.

After the first revelation, retain known held cards explicitly, pool future ordinary draws again, and memoize continuation values independent of incoming mass. Measure state growth before attempting broad production integration. Terminal searches need a joint conditional count over held and physically remaining ordinary inventory, not an optimistic target pool or independent survival probability. This dynamic revelation route remains a conjectured performance improvement, with no 150M Work or five-second AOT feasibility claim yet.

## Dynamic revelation prototype and evidence

Implemented `aggregate-dynamic.mjs` as a **fixture-scoped isolated experiment**, not general application code. It handles the frozen ordinary roles, three distinct base Danger identities, Allure, Graceful and one optional starter-search entry. Counts can vary for small-oracle checks. It hardcodes the fixture's canonical row ordering, relevance, DARK eligibility, result predicates and source behaviors. It does **not** implement arbitrary priorities, aliases, per-entry activation limits, draw locks/costs, mills, custom selectors, alternative combo requirements, disabled-entry contracts or session/wire result handling. A production port must derive those semantics from the request or reject the unsupported route truthfully; copying these constants into a general solver would be unsound.

The state is `[effectDeck, physicalOrdinaryDeck, explicitHeld, anonymousHeld, effectFIFO, drawsLeft, completionSourcePlusOne, unobservedOrdinaryInventory, knownOrdinaryHand]`. Ordinary vectors use filler/Starter/Extender/Overlap. Effect vectors use Bigfoot/Nessie/Mothman/Allure/Graceful/Search. Random anonymous losses are implicit: `sum(inventory) - physicalOrdinaryDeck - anonymousHeld`. Known held identities have already left inventory. Discarding a known card changes its held count, not the inventory. The independent oracle uses distinct physical card objects, complete labeled deck permutations, physical uniform hand discards and actual remaining-deck targets; it does not use the anonymous representation.

Each pending state carries a normalized **completion mass** in a fixed denominator `D = N! * L^A`, with `L = lcm(1..H)` and `A = min(H, initial Danger copies)`. Every supported ordinary effect is net hand-neutral and every Danger activation shrinks the hand by one. Thus hand size never exceeds H and at most A random discards occur. Imagine a uniformly random labeled deck permutation and A independent L-ary random digits: each physical choice from a k-card hand receives L/k digits, and unused digits are free suffixes. This gives the fixed normalized finite sample space. Anonymous revelation conditions that same space; every integer division is checked for zero remainder.

At a policy observation the full anonymous hand composition is revealed with exact multivariate hypergeometric mass, then the known policy runs. The proved Allure explicit-Danger shortcut above avoids unnecessary revelation. At terminal the unknown hand is revealed, and remaining physical Starter/Overlap target counts have joint multivariate hypergeometric weights over the remaining unobserved inventory. Optional search assignments are enumerated over shared target counts before the six Boolean rows are accumulated. No sum of overlapping combo probabilities is used.

The experimental frontier trims to a fixed target entry count after inserting at most twice that many entries; each discarded contribution adds its full normalized mass to residual. A discarded state's later contribution may be retained again: the two contributions refer to disjoint weighted histories, so this is permitted. Every completed layer asserts `pending + residual + terminal + failed = D`. On completion row endpoints are `[win_r/D, (win_r + residual)/D]`. No result is published on an exception. This prototype has **no production Work cutoff, budget finalization reserve, packed key, or production memory accounting**. Its bounded-entry heap/sort allocation is also not Standard compliant merely because target count fits; the parent/current and next maps coexist.

### Validation performed

`node research/danger-fast/aggregate-dynamic-oracle.mjs` passes five independent mixed-effect small cases exactly on all six rows, including same-name duplicates, different Danger identities, Allure failure, Graceful, deck exhaustion, optional searches, overlaps and zero-maximum exclusion. The same five cases at entry caps 1, 2 and 8 pass all-row containment by integer cross-products and repeatability. Exact terminal fractions and cap widths are appended to `aggregate-dynamic-oracle-results.jsonl`. These tests establish evidence for this fixture-scoped implementation; they do not test the absent general model features above.

### Heavy curves

Commands are `node research/danger-fast/aggregate-dynamic.mjs SIZE CAP`. Every completed case appends all six rational bounds, per-layer mass, counters and source SHA-256 to `aggregate-dynamic-results.jsonl`. The first three results were retroactively associated with the exact unchanged source hash before the source-hash output feature was added; later samples identify their executed source directly.

| Size | Target entries | Full interval width, every row | Expanded states | Node seconds | Retained cell lower estimate |
| --- | ---: | ---: | ---: | ---: | ---: |
| 40 | 2,048 | 6.60920057 pp | 23,065 | 1.366 | 71,198 |
| 40 | 8,192 | 2.98693901 pp | 85,426 | 3.240 | 285,809 |
| 60 | 8,192 | 0.47611871 pp | 85,280 | 3.053 | 310,671 |
| 40 | 16,384 | 1.71520676 pp | 162,386 | 5.748 | 582,355 |

The diagnostic 16,384 run reaches the initial width goal in Node, **but exceeds Standard Storage in its unpacked representation** and is not five-second AOT evidence. Peak inserted entries are `2*cap+1`. The cell estimate excludes keys, transient allocations, simultaneous maps and object overhead, so it is explicitly a lower estimate, not a safe aggregate memory limit. The 8,192 results also exceed 262,144 cells before such overhead. Per-row endpoints are retained in the JSONL; the uniform width here is actual residual treatment, not hidden row variation.

Production feasibility now has a credible hypothesis: pack the small count-state vectors, bound all retained structures honestly, cache normalized terminal continuation values independent of incoming probability mass, and charge actual operation/BigInteger work. Terminal conditional expansion currently accounts for about 1.6 million split evaluations at cap 8,192, so there is repeated work to eliminate. A nested refinement must preserve previously accepted bounds; independently rerunning beams with a larger cap is not a proof that each endpoint is monotone.

### Terminal formula/cache follow-up

For this fixture with H<=5 (fixed maxima inactive), the revealed-hand terminal events depend on only three deck probabilities. With inventory size M, physical ordinary deck K, Starter inventory S and Overlap inventory O:

```
a = P(deck Overlap >= 1) = 1 - C(M-O,K)/C(M,K)
b = P(deck Starter+Overlap >= 1) = 1 - C(M-S-O,K)/C(M,K)
c = P(deck Overlap >= 1 and deck Starter+Overlap >= 2)
  = a - O*C(M-S-O,K-1)/C(M,K)
```

For at least one held searcher, known hand counts s/e/o give distinct Starter+Extender probability: 1 if already true; otherwise b when o>0 or e>0; otherwise a when s>0; otherwise c if at least two searchers, else 0. Overlap row is 1 when o>0 else a. Exclusion is the constant `s+o==0 && e>0`. Positive group is 1 when o>0 or s>0&&e>0, else b when e>0, else a. Total is 1 when the exclusion holds, otherwise the positive-group value. This is a truth table over the same random deck assignment, not independent union arithmetic.

The analytic option exactly matches the enumerated terminal option and the independent small physical oracle. At cap 16,384 its heavy40 Node time was 5.494 seconds versus 5.748 for full target enumeration, with identical endpoints. A bounded 4,096-entry normalized terminal cache (keys: physical ordinary deck, held searchers, anonymous hand, inventory and known ordinary hand) initially slowed this to 6.433 seconds despite 715,350 hits: first retained keys monopolized storage and JSON key/normalization overhead mattered. A subsequent version clears optional terminal cache at each visible-depth layer. All versions and exact bounds remain in JSONL with source hashes; this is useful negative evidence, not justification for claiming caching solves the target. Parent asked to stop further JS tuning and focus on generic C# proof review.
