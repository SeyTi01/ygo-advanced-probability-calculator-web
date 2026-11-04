# Calculation model

[Documentation index](README.md) · [Usage guide](https://dev.ygo-calculator.pages.dev/help) · [Accuracy and resources](accuracy-and-performance.md)

## Inputs and success

A calculation captures the accepted active deck entries, copy counts, opening hand size, active combos/groups, category membership, saved effects and activation priority. Editor drafts are not applied until saved. Cards may match several properties, but each physical copy fills at most one positive requirement within a chosen combo route. Different combos are evaluated independently.

Requirements have inclusive minimum/maximum counts. `Any` follows the final hand size; a fixed zero remains zero. Maxima count every matching card in the hand, even when a different copy fills a positive requirement. `0–0` excludes matching cards. Same-card AND intersects properties on each card. OR chooses a requirement alternative; separate positive requirements need distinct copies. Repeated references to the same selector or card ID intersect their bounds; use a higher minimum to request multiple copies. Numeric Level, Rank and Link rating predicates use inclusive comparisons/ranges; a missing or inapplicable numeric value does not satisfy a numeric predicate. A range inside one predicate does not request two cards.

Individual combos report success under these rules. Each group is the union of its active combos; the total is the union of all active combos. A random outcome satisfying several alternatives, combos or search assignments contributes once to the corresponding union.

## Saved effects and provenance

Only configured effects execute. Recognized imports or explicit database selections can receive maintained built-ins; an unrecognized/manual card starts ordinary. A built-in is a supported abstraction with its own model notes, not a general card-text interpreter. Partial support means that specific printed behavior is omitted.

Customizing and saving creates a custom plan. Manually restoring equivalent values does not change its provenance to built-in. Restore built-in explicitly applies the current definition. Loading does not silently replace historical plans. Current descriptive text may be used for a built-in whose canonical identity, semantic revision, execution model, support status and compiled plan match the maintained definition; its serialized profile stays unchanged. Differing saved or custom plans retain their own notes. Unsupported saved execution models fail rather than becoming generic draws.

## Draw resolution is a fixed policy

Configured draw effects activate automatically when legal. The source is consumed; an entry's once-per-turn allowance is shared by its copies, while separate entries have independent allowances. Non-OPT newly drawn copies can activate. The full draw batch, pre-draw banish and post-draw mill must fit in the remaining deck; hand costs and the saved activation restrictions must also be satisfied before activation. Otherwise the source remains in hand and the allowance/cost is not consumed. Such an outcome remains in the probability denominator.

Encounter order is the default activation order. Custom priority ranks workspace entries separately within **Draw effects** and **Search effects**, highest first, while preserving physical encounter order for ties. Cross-phase rank positions have no execution meaning. A selected activation finishes its draw batch, mill and mandatory removal before selecting another effect. Newly drawn higher-priority effects can overtake waiting ones. An effect lacking eligible cost material is deferred and retried after another activation; a pass without activation ends resolution. A deck-size-blocked batch cannot become legal later because deck capacity only decreases.

Supported hand costs remove one or two eligible cards before drawing; supported mandatory removals occur afterward. The activating source cannot pay its own hand cost or be removed again. Newly drawn cards can be removed. Eligibility supports properties, same-card AND and OR. Failure of a mandatory removal is a failed branch, without renormalizing other outcomes.

The deterministic keep policy protects held positive combo pieces and searches with plausible access to them, then other usable searches/draw cards, potentially usable cost-deferred draws, and other cards. Stable requirement membership and card identity break ties. Maximum-only constraints do not protect cards. The policy uses all active routes, so changing active combos can change removal choices. It is shared by individual, group and total rows; it does not optimize separately for each row. Visual deck sorting does not change these tie-breaks. Search protection is local: it neither reserves competing targets nor promises survival through later operations.

Hidden top-deck banish and milling reduce deck capacity. Completed physical Exact also accounts for surviving target identities, including ordered draws, costs and removals. Banished-card and GY effects/recovery are not inferred. Fast may prove the same outcome exact, or require a certificate; do not infer accuracy merely from the presence of Desires or a draw effect.

## One excavation after draws

After all draw effects, at most one held excavation source activates before ordinary searches. The eligible source with the highest excavation count wins; equal counts use stable entry identity, independent of visual deck order. Selection happens before revealing cards. Activation consumes the source even if no revealed target helps a combo; the model does not decline it, switch to another source, or excavate again after seeing the pool.

Pot of Duality excavates three cards. Pot of Prosperity always excavates six and assumes six Extra Deck cards are available. The full excavation must fit in the remaining Main Deck. Prosperity is unavailable if a card effect already drew cards on that branch; eligible draws are not skipped to reserve a Prosperity activation. If the higher-count source is ineligible, a lower-count eligible source may activate. If none is eligible, the hand proceeds directly to searches.

Exactly one revealed physical copy replaces the source. The calculator checks possible additions jointly with ordinary search assignments, preserving distinct-copy requirements, hand-wide maxima and overlapping combo/group unions. Unselected revealed cards remain available to ordinary searches. Duality shuffles them back and Prosperity puts them on the bottom; the distinction cannot affect the modeled continuation because there are no later draws or excavations. Field/summoning and damage restrictions remain outside the opening-hand model.

An excavated effect does not activate. Only ordinary search opportunities already held before the excavation addition may act afterward. Multiple sequential excavations are deliberately unsupported: leaving their choices undecided would let an earlier selection use knowledge of a later reveal. This restriction is part of the model, not uncertainty covered by a Fast certificate.

## Optional searches after excavation

All configured draws and their associated costs and removals, followed by the optional presence of one excavation activation, resolve before searches. Sources held after draws, including sources drawn earlier, may activate; excavation does not create new search opportunities. Each eligible search source may be declined or consumed to transfer one eligible remaining Main Deck copy to hand. Sources share actual copy and deck-capacity limits. A target already held is not a remaining deck copy. Fetched effects do not activate or start another draw/search pass; supported search-phase removals can still remove them. Supported search plans with their own hand costs, removals, mode allowances or minimum-hand requirements resolve those rules during the search phase; Fast and Exact use physical counting for these plans.

At each search step the highest-priority currently legal source acts or is declined. Costs, remaining targets, minimum hand size and per-entry/mode allowances are checked again after each activation. Declining retires that physical opportunity; temporarily unavailable sources may become legal after another search. The completed draw phase stays closed.

For each combo the model asks whether any legal combination of optional search choices under this fixed source order succeeds. Different rows may use different choices, while unions count an outcome once. This search-choice existence test does not turn draw activation or discard selection into optimal play. Simple substitutions without hand costs, removals or minimum-hand restrictions commute: any feasible assignment can be performed in priority order, so the existing count-state, deferred and survivor projections remain valid. Order-sensitive searches use physical terminal-hand counting with joint hidden losses.

Built-in search eligibility uses objective metadata where configured; manual role overrides do not rewrite that policy. Custom filters describe the saved hypothetical plan.

Search plus draw operations on the same source are unsupported. Search-only counting is exact under the single-pass policy. When draw histories leave target survival unproven, Fast's primary may optimistically treat targets outside the final pre-search hand as available, despite earlier consumption, discards, banishment or milling. Shared capacity/copy constraints still apply. [Certificates and physical Exact](accuracy-and-performance.md#accuracy-and-the-numbers-you-see) distinguish this from a proven physical result.

## Boundaries

The model does not simulate a duel, opponent interaction, summon/effect legality in general, field/Extra Deck play, arbitrary GY or banish triggers, recursive fetched effects, optional draw/excavation activation, multiple excavation activations, or optimal sequencing/removal. A specific built-in may have additional assumptions listed in Model notes. No effect simulation ignores every configured draw, excavation and search operation and counts only the dealt hand.

Exact means exact for these inputs and rules. A hypothetical custom plan need not bound real gameplay, especially with maxima or exclusions. More Storage or unlimited Work cannot repair an unsupported model.

Implementation anchors: [effect registry](../YGOProbabilityCalculatorBlazor/Services/CardEffects/CardEffectRegistry.cs), [draw resolution](../YGOProbabilityCalculatorBlazor/Services/ProbabilityCalculator/DrawResolution/), [search counting](../YGOProbabilityCalculatorBlazor/Services/ProbabilityCalculator/Search/). Regression anchors: [three modes](../YGOProbabilityCalculatorBlazorTest/Services/ProbabilityCalculator/ThreeCalculationModesTest.cs), [physical searches](../YGOProbabilityCalculatorBlazorTest/Services/ProbabilityCalculator/PhysicalSearchTest.cs), and [small-deck oracle](../YGOProbabilityCalculatorBlazorTest/Services/ProbabilityCalculator/SmallDeckOracle.cs).
