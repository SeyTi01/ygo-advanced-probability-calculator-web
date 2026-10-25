# Accuracy and performance

[Documentation index](README.md) · [Calculation model](calculation-model.md) · [Probability engine](probability-engine.md)

## Modes and execution strategies

| UI choice | Execution and accepted result |
| --- | --- |
| Progressive (default) | Complete Fast first. If already proven Exact, finish; otherwise present it as Preliminary and continue physical Exact on the same captured input. A validated completed upgrade replaces it. |
| Fast only | Complete Fast once; no automatic physical upgrade. It may be Exact, Bounded or Uncertified. |
| Exact only | Physical counting under the saved activation/removal policy and best causal search decisions; publish values only when the required physical proof completes. |
| No effect simulation | Ignore configured effects on owned calculation copies and count the dealt hand. Saved effects remain unchanged. |

`OpeningHand` is the internal name for No effect simulation. Progressive and Fast only share the Fast model but have different saved execution strategies. A one-off **Recalculate using Exact** does not change the selected preference.

An interrupted Progressive upgrade retains the accepted Fast result and its accuracy, rather than fabricating a partial Exact result. Cancellation, unsupported cases, Storage exhaustion and execution/transport failures can stop the upgrade. A failed Exact-only request supplies no new values. An input edit can leave a previous result visible with a stale label; that result describes its captured inputs, not the edited workspace. Pinned results preserve their captured context as well.

## Accuracy and the numbers you see

| Status | Meaning |
| --- | --- |
| Exact | The model's physical probability is proven, including Fast routes/certificates that prove exactness. Shown with ±0%. This is not a proof that the model includes every printed card effect. |
| Bounded | Certified lower and upper endpoints enclose the physical-model probability. Each total, group and combo has its own interval. |
| Uncertified | A completed primary modeled result exists, but no usable physical certificate was established. Treat it as the configured approximation, without an asserted physical error bound. |

Fast does not always approximate. Search-only cases and other proven routes can be exact; uncertain survival after draws can require bounds on routes proven equivalent to the selected activation policy. General chronological draw/search resolution computes the same causal result as Exact under Fast's finite budget: it returns Exact if completed, or a calculation limit without new probabilities if it cannot complete. It does not substitute a different effect order or invent an Uncertified primary.

On routes supporting an approximate primary, certification runs after that primary completes. If no usable interval completes, a certification limit or error leaves the primary Uncertified. If a later refinement fails, the last completed certified interval is retained. Exhausting Work before primary completion is a calculation failure. Progressive needs an accepted Fast result before an Exact upgrade can begin.

Keep three quantities separate:

1. **Primary rational:** the retained result of the selected counting model. On an optimistic Fast search-access path it can exceed the certificate's upper endpoint. It is not the certified midpoint.
2. **Certified interval:** rational lower/upper bounds established by physical survival analysis on a route proven compatible with the selected chronological policy. The lower construction uses guaranteed surviving targets and the ability to decline searches under that policy; upper analysis also constrains physical availability.
3. **Displayed percentage ± radius:** for Bounded rows, presentation uses the interval midpoint at display precision and rounds the radius outward to cover both endpoints around that displayed center. This can be wider than the exact half-width. The ± is an absolute change on the 0%–100% probability scale, not a relative percentage or a statistical confidence interval. No point inside the interval is claimed more likely. An interval collapsing to a point is exact; tiny nonzero uncertainty must not be presented as ±0%.

Hover or keyboard-focus a result value to inspect its primary fraction. Accuracy details and copied results retain the status and interval context. Do not interpret the primary fraction alone as a certified physical answer, and do not add overlapping combo percentages. The [ten-hand example](probability-engine.md#worked-example-ten-equally-likely-hands) explains the union.

Search decisions are optimized separately for each combo, group union and total union using current information, with future draw order unknown. A union row is its own optimization objective, rather than a union of independently optimized combo outcomes. All rows use the same fixed activation and discard policy. See [the search decision model](calculation-model.md#best-causal-search-decisions) for the information available at each choice.

## Work and Storage are independent

Work is deterministic weighted accounting for engine operations, not wall-clock seconds. Ordinary UI Fast and No effect simulation use a request-local **150,000,000-unit** finite allowance. Native callers and bounded wire requests without an explicit allowance retain the **10,000,000-unit** default. Explicit finite policies remain supported.

**Interactive Exact and Progressive's physical Exact stage use unlimited Work**, even when the Fast stage has spent its remaining allowance. Neither has a calculation deadline. Unlimited Work removes that cutoff; it does not guarantee completion or a practical running time.

Storage independently limits retained entries, cells and integer payload. Choose the runtime **Storage safety limit** tier consciously; it is not saved in a session. A larger tier can permit a larger state space but can increase browser memory pressure. More Work cannot resolve a Storage limit. Exact does not publish an optimistic fallback as Exact when storage or support prevents completion. The engine also has a 30-combo compatibility ceiling; that ceiling is not a claim that every smaller case completes.

Current per-structure limits are:

| Storage tier | Entries | 32-bit cells |
| --- | ---: | ---: |
| Standard | 32,768 | 262,144 |
| High | 65,536 | 524,288 |
| VeryHigh | 131,072 | 1,048,576 |

These checks include retained integer payload and relevant state structures; they are not a total browser-memory ceiling. Several live maps, temporary state and object overhead can coexist. The [storage policy](../YGOProbabilityCalculatorBlazor/Services/Interface/CalculationStoragePolicy.cs) defines the tiers and [WorkBudget](../YGOProbabilityCalculatorBlazor/Services/ProbabilityCalculator/Resources/WorkBudget.cs) applies accounting.

Mixed draws, ordered costs/removals, overlapping selectors, competing searches and hidden target losses can greatly increase state growth. No universal completion or elapsed-time promise follows from the deck size, combo count, chosen mode or highest Storage tier. A pre-calculation notice is guidance, not a feasibility proof. Use cancellation and compare simpler models deliberately; changing a cost or filter changes the modeled question.

## Worker lifecycle and compatibility APIs

Current UI Progressive uses `runProgressive` and `finish` on one staged worker lease. The first response is validated before acceptance and the terminal response is validated before the lease is released. A slow initial Fast stage does not consume an automatic Exact deadline because this path has none. Worker startup has a separate **30-second guard**. After actual interruption of automatic work, the shared **2.5-second restart delay** still applies. These lifecycle guards are not calculation time budgets.

The older standalone `CalculateAsync` / `run(json, true)` compatibility path retains a **seven-second opportunity**, **five-second upgrade maximum**, and remaining finite Work. This historical API contract is not the current Progressive UI contract. Native synchronous calls do not acquire browser deadlines. [Runtime notes](../scripts/background-calculation/README.md#automatic-exact-opportunity-in-fast) contain compatibility and measurement details.

## Evidence and implementation

[CalculationSnapshot](../YGOProbabilityCalculatorBlazor/Services/BackgroundCalculation/CalculationSnapshot.cs) chooses finite versus unlimited execution; [BackgroundCalculator](../YGOProbabilityCalculatorBlazor/Services/BackgroundCalculation/BackgroundCalculator.cs) owns validated staged delivery; [background-calculation.js](../YGOProbabilityCalculatorBlazor/wwwroot/js/background-calculation.js) separates current and legacy worker timing. [ProbabilityPresentation](../YGOProbabilityCalculatorBlazor/Components/ProbabilityCalculator/ProbabilityPresentation.cs) implements fraction, midpoint and outward-radius display.

Regression references: [unlimited Exact completion](../YGOProbabilityCalculatorBlazorTest/Services/ProbabilityCalculator/UnlimitedExactCompletionTest.cs), [target-survival certificates](../YGOProbabilityCalculatorBlazorTest/Services/ProbabilityCalculator/TargetSurvivalCertificateTest.cs), and [wire execution policy](../YGOProbabilityCalculatorBlazorTest/Services/BackgroundCalculation/UnlimitedExactWireTest.cs). Runtime benchmarks are fixture-specific evidence, not product deadlines or guarantees.
