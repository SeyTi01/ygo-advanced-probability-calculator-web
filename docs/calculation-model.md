# Calculation model

[Documentation index](README.md) · [Usage guide](https://dev.ygo-calculator.pages.dev/help) · [Accuracy and resources](accuracy-and-performance.md)

## Inputs and success

A calculation captures the accepted active deck entries, copy counts, opening hand size, active combos/groups, category membership, saved effects and activation priority. Editor drafts are not applied until saved. Cards may match several properties, but each physical copy fills at most one positive requirement within a chosen combo route. Different combos are evaluated independently.

Requirements have inclusive minimum/maximum counts. `Any` follows the final hand size; a fixed zero remains zero. Maxima count every matching card in the hand, even when a different copy fills a positive requirement. `0–0` excludes matching cards. Same-card AND intersects properties on each card. OR chooses a requirement alternative; separate positive requirements need distinct copies. Repeated references to the same selector or card ID intersect their bounds; use a higher minimum to request multiple copies. Numeric Level, Rank and Link rating predicates use inclusive comparisons/ranges; a missing or inapplicable numeric value does not satisfy a numeric predicate. A range inside one predicate does not request two cards.

Individual combos report success under these rules. Each group is the union of its active combos; the total is the union of all active combos. A random outcome satisfying several alternatives, combos or search assignments contributes once to the corresponding union.

## Saved effects and provenance

Only configured, simulation-enabled effects on active, positive-copy entries execute. Switching a card's simulation off preserves its physical copies, saved plan and priority preference. Recognized imports or explicit database selections can receive maintained built-ins; an unrecognized/manual card starts ordinary. A built-in is a supported abstraction with its own model notes, not a general card-text interpreter. Partial support means that specific printed behavior is omitted.

Customizing and saving creates a custom plan. Manually restoring equivalent values does not change its provenance to built-in. Restore built-in explicitly applies the current definition. Loading does not silently replace historical plans. Current descriptive text may be used for a built-in whose canonical identity, semantic revision, execution model, support status and compiled plan match the maintained definition; its serialized profile stays unchanged. Differing saved or custom plans retain their own notes. Unsupported saved execution models fail rather than becoming generic draws.

## One activation order for draws and searches

Opening-hand sources enter one draw/search queue in physical encounter order. Newly drawn sources join that queue; search-fetched sources remain ineligible for effect activation. Encounter order is the default. Custom priority ranks workspace card-entry IDs, highest first, while preserving encounter order for ties and unranked entries. Renaming or sorting the deck does not alter authored ranks. New effect entries append deterministically, and temporarily inactive, effectless or simulation-disabled entries retain their preference until the entry is removed.

After each complete activation, the highest-priority legal queued source is considered. An activation finishes its hand costs, source consumption, draw batch or search transfer, hidden losses and mandatory removal before another source acts. Newly drawn higher-priority effects can overtake waiting ones. An effect lacking eligible cost material is deferred and retried after another activation; a pass without activation ends resolution. A deck-size-blocked batch cannot become legal later because deck capacity only decreases.

This chronological policy also applies in encounter-order sessions: a held search can resolve before a later draw. Search retrieval depletes the physical deck immediately, changing subsequent draw probabilities. Older saved draw-only priority lists retain their authored order and append search entries.

## Fixed draw activation and hand removal

Configured draw effects activate automatically when legal. The source is consumed; an entry's once-per-turn allowance is shared by its copies, while separate entries have independent allowances. Non-OPT newly drawn copies can activate. The full draw batch, pre-draw banish and post-draw mill must fit in the remaining deck; hand costs and the saved activation restrictions must also be satisfied before activation. Otherwise the source remains in hand and the allowance/cost is not consumed. Such an outcome remains in the probability denominator.

Supported hand costs remove one or two eligible cards before drawing or searching; supported mandatory removals occur afterward. The activating source cannot pay its own hand cost or be removed again. Newly drawn or fetched cards can be removed. Eligibility supports properties, same-card AND and OR. Failure of a mandatory removal is a failed branch, without renormalizing other outcomes.

The deterministic keep policy protects held positive combo pieces and searches with plausible access to them, then other usable searches/draw cards, potentially usable cost-deferred draws, and other cards. Stable requirement membership and card identity break ties. Maximum-only constraints do not protect cards. The policy uses all active routes, so changing active combos can change removal choices. It is shared by individual, group and total rows; it does not optimize separately for each row. Visual deck sorting does not change these tie-breaks. Search protection is local: it neither reserves competing targets nor promises survival through later operations.

Hidden top-deck banish and milling reduce deck capacity. Completed physical Exact also accounts for surviving target identities, including ordered draws, costs and removals. Banished-card and GY effects/recovery are not inferred. Fast may prove the same outcome exact, or require a certificate; do not infer accuracy merely from the presence of Desires or a draw effect.

## Best causal search decisions

At a queued search opportunity, the model chooses a legal target and mode, or declines, to maximize the probability of the requested result. Declining leaves the source in hand and retires that opportunity; it cannot wait for a later draw and reconsider. Activating consumes the source and transfers one eligible remaining Main Deck copy to hand. Hand costs, mandatory removals, per-entry and per-mode activation allowances, minimum-hand requirements and actual target copies constrain the choice. A target already held is not a remaining deck copy. Fetched effects remain ineligible to activate even when a later draw occurs.

Choices use current information. The model treats the remaining unordered Main Deck contents as known at a search opportunity, including the consequences of earlier hidden banishment or milling. Future draw order remains unknown. Random draws and hidden losses contribute their physical probabilities; a search decision takes the best continuation probability available with that current information. Search alternatives are decisions, not extra random outcomes, and do not add denominator mass.

Each combo, each group's union, and the total union is a separate objective with its own best causal search decisions. A group maximizes the chance of completing any member under one policy for that objective. It is not the union of outcomes obtained by independently optimizing each member. The same physical outcome contributes at most once to a union. When no random effects remain, these choices reduce to asking whether a legal search sequence can satisfy the objective. Activation priority and the deterministic hand-removal policy remain fixed for all rows.

Built-in search eligibility uses objective metadata where configured; manual role overrides do not rewrite that policy. Custom filters describe the saved hypothetical plan. Search plus draw operations on the same source are unsupported. Optimized counting and [certificates](accuracy-and-performance.md#accuracy-and-the-numbers-you-see) are used only where they preserve this chronological policy; other cases use the causal resolver.

## Boundaries

The model does not simulate a duel, opponent interaction, summon/effect legality in general, field/Extra Deck play, arbitrary GY or banish triggers, recursive searched effects, optional draw activation, or optimization of activation order or hand removal. A specific built-in may have additional assumptions listed in Model notes. No effect simulation ignores every configured draw/search operation and counts only the dealt hand, preserving saved plans and priority.

Exact means exact for these inputs and rules. A hypothetical custom plan need not bound real gameplay, especially with maxima or exclusions. More Storage or unlimited Work cannot repair an unsupported model.

Implementation anchors: [effect registry](../YGOProbabilityCalculatorBlazor/Services/CardEffects/CardEffectRegistry.cs), [draw resolution](../YGOProbabilityCalculatorBlazor/Services/ProbabilityCalculator/DrawResolution/), [search counting](../YGOProbabilityCalculatorBlazor/Services/ProbabilityCalculator/Search/). Regression anchors: [three modes](../YGOProbabilityCalculatorBlazorTest/Services/ProbabilityCalculator/ThreeCalculationModesTest.cs), [physical searches](../YGOProbabilityCalculatorBlazorTest/Services/ProbabilityCalculator/PhysicalSearchTest.cs), and [small-deck oracle](../YGOProbabilityCalculatorBlazorTest/Services/ProbabilityCalculator/SmallDeckOracle.cs).
