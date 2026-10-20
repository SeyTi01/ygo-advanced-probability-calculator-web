# Yu-Gi-Oh! Advanced Probability Calculator

An exact opening-hand probability calculator for Yu-Gi-Oh! decks accounting for overlapping card roles and distinct physical cards for each positive requirement.

It can model complex combo conditions while correctly accounting for hands that satisfy several combos at once.

**[Try the stable calculator](https://ygo-calculator.pages.dev/)**

> **Development preview:** This README describes the current `dev` branch. You can try that version at **[dev.ygo-calculator.pages.dev](https://dev.ygo-calculator.pages.dev/)**.

![Yu-Gi-Oh! Advanced Probability Calculator example](YGOProbabilityCalculatorBlazor/Assets/probability_calculator_example.png)

## What it can model

- **Reusable and overlapping card roles and properties** - cards can belong to several custom roles and metadata properties at once, such as `VS Starter`, `Attribute: FIRE`, or `Level 5`.
- **Exact unions of overlapping combo routes** - calculate the probability of opening any valid combo without double-counting hands that satisfy several routes, with optional grouping for related combo families.
- **Distinct physical requirements within a combo** - if a route needs both a `Starter` and a `Fire` card, one card that belongs to both does not satisfy both requirements by itself.

Results use exact combinatorial counting for the configured model, with no Monte Carlo sampling. Mixed draw/search configurations use the search-access assumption described below.

## Example: Vanquish Soul / K9

The included example uses a Vanquish Soul / K9 deck and models nine underlying routes as eight combo definitions. `VS Starter + (Fire OR Dark)` combines two starter routes into one OR definition. The routes either reach the full Vanquish Soul setup directly or make Ripper + Saryuja as a bridge. Half boards are intentionally not counted.

The example uses three custom role categories:

- `VS Monster`
- `VS Starter`
- `K9 Starter`

Objective properties such as FIRE, DARK, and Level 5 come from card metadata rather than separate user categories. The example’s three search spells use maintained search effects; the fetched monsters contribute their own roles and properties.

Common properties are available in card, combo, and draw-filter selectors before a matching card is present. They use the same metadata identity as imported-card properties; specific values such as archetypes and monster types appear when relevant.

Cards can still satisfy several roles and properties at once. For example, Vanquish Soul Razen is both a `VS Monster` and a `VS Starter` and has the FIRE property, while Reinforcement of the Army can search a remaining Razen copy. Reinforcement retains only its actual Spell properties; the fetched Razen contributes its own roles and identity.

With all eight combo definitions active, the example has an **83.59%** probability of opening at least one modeled full-combo hand.

## How to use it

### Get started

1. **Import or enter your deck.**
2. **Create custom categories** for reusable roles and assign them to cards. Cards can have several categories; imported cards also expose objective properties automatically.
3. **Define combos** with **Add requirement**. Choose a card, category or property from one picker. Click a requirement row to edit its target or counts.
4. **Set counts.** **At least** defaults to one. Open **Maximum: Any** to set an upper bound, or leave it empty for **Any** (the final hand size). Set both counts to `0` to exclude matching cards.
5. **Calculate** to see each combo's probability and the chance of opening any active combo. Hands satisfying several combos count only once in the combined result.

Separate positive requirements need distinct physical copies: one card matching both `Starter` and `Fire` cannot fill both parts of `Starter + Fire`.

### Add OR alternatives and same-card AND conditions

- **OR:** Open a requirement row and choose **Add alternative (OR)**. Select another card, category or property and submit. An OR group needs only one alternative; each keeps its own bounds. Open any alternative to edit it or add another.
- **Same-card AND:** Open a category requirement or alternative and choose **Add property (AND)**. `(Orcust AND Monster) Min 2` needs two cards that each match both properties, using shared bounds. Separate requirements need distinct copies.

Use **Remove requirement** or **Remove alternative** inside the editor. Removing the next-to-last alternative promotes the remaining requirement without changing its bounds. In a same-card editor, change or remove individual properties, then save. Escape or Cancel restores the ordinary add draft. If edits are pending, extension buttons explicitly save those edits before adding an alternative or property.

### Configure draw effects

Expand a card to see its saved simulation. Maintained cards have a **Built-in simulation** defined by their own card-effect class. Choose **Customize simulation** (or **Configure simulation** on an ordinary card), edit the generic settings, and **Save**. **Cancel** discards the draft. **Restore built-in simulation** explicitly reapplies the current definition; matching its values manually still leaves a saved override **Custom**. Supported built-in models have a neutral check icon; partially implemented models show **Partial**. Empty cards without a maintained definition show **No built-in simulation**. **Model notes** appears only for card-specific limitations or saved-version information.

The custom editor offers **None**, **Draw 1**, **Draw 2**, **Draw 3**, or **Search 1**. An activating copy is consumed. With **Once per turn** on (the default for draws), later copies of that entry stay in hand; turn it off to let every copy activate, including newly drawn copies. Draws and searches share this per-entry activation flag. Separate custom entries have independent allowances, even when they refer to the same printed card.

Open the gear **Calculation settings** menu to choose how these settings apply:

- **Progressive (default):** Shows a completed Fast result immediately, then attempts physical Exact on the same input without a calculation time limit. Exact replaces the preliminary result if it completes. Cancellation, Storage limits, unsupported cases or errors keep the completed Fast result and its accuracy status. Fast uses bounded Work; the Exact upgrade has no cumulative Work cutoff.
- **Fast only:** Runs Fast once and retains its certified bounds or Uncertified status, without an automatic Exact upgrade.
- **Exact only:** Applies the same saved rules and policy, returning values only when physical Exact completes. Storage limits and unsupported cases return typed failures; there is no cumulative Work cutoff.
- **No effect simulation:** Resolve no draw or search effects; configured cards still occupy initial-hand slots.

**Draw activation priority** in the same settings menu defaults to **Encounter order**. Choose **Custom priority** to rank workspace entries, highest first, using the up/down buttons. The highest-ranked legal effect activates after the current draw batch, mill and mandatory removal finish. Newly drawn effects can then overtake earlier waiting effects; a blocked effect waits while another legal effect resolves. Copies share their entry's priority and existing once-per-turn allowance.

Priority is one fixed order for all combos and groups. It does not change the shuffle, deck order or cost/removal choices. Rankings are saved with sessions and share links. Renames, visual sorting and inactive entries retain their preferences; duplicated/new effect entries are appended to the list, and replacing/importing a deck clears the old ranking. Switching back to Encounter order ignores the saved ranking.

**Advanced → Banish from deck** sets a hidden top-deck cost before drawing. The built-in Pot of Desires simulation banishes 10 then draws 2, requiring at least 12 cards left in deck. Fast tracks the physical deck size. Exact also counts surviving search-target identities on supported count-state draw routes. Banished-card effects are not simulated.

Solar Recharge discards one card that is both a Lightsworn and a Monster, draws 2, then sends the top 2 deck cards to the GY. It requires at least 4 cards left in deck. Milling reduces deck capacity before subsequent draws. Exact counts surviving search targets on supported count-state draw routes; GY effects remain outside the model. Custom edits preserve the saved milling operation; choosing None removes the entire draw effect, and Restore built-in simulation restores the complete plan.

Requirements apply to the final hand. If the full draw amount is unavailable, the source stays in hand and the outcome is evaluated normally.

### Search from the Main Deck

Reinforcement of the Army searches one Level 1–4 Warrior monster using the configured deck’s objective metadata. Manual combo-role/property overrides do not change its eligibility. **"A Case for K9"** searches one K9 monster, once per turn. **Chaotic Elements** searches one Level 5+ monster that is both LIGHT/DARK and Pyro/Aqua, once per turn. Only its Main Deck search is modeled; GY retrieval, optional destruction and the GY effect are excluded. In the example it can fetch only Jokul or Lantern.

The expanded card lists current targets. Editing a search filter creates a hypothetical custom search over assigned categories/properties, including automatic user categories. Merely opening/saving an existing plan preserves its saved matching policy; Restore built-in restores verified eligibility.

All configured draws, costs and discards resolve before searches. Each optional search substitutes one held source with one eligible target. For each combo, the calculator asks whether any legal combination of search choices succeeds. Different result rows may use different choices; groups and the total count each random outcome once. Maxima, exclusions, distinct copies and source consumption still apply. Only originally held searchers act: a fetched effect stays in hand, without restarting draws or searches.

Availability is bounded by original target copies minus copies in the final pre-search hand, shared across all search choices. Every substitution consumes one remaining deck slot. This is exact for search-only hands under the single-pass policy. **When target survival is unproven after draws, Fast uses an optimistic identity-blind search-access approximation:** targets outside that final hand are assumed available even if previously drawn and removed, banished or milled. Hidden quantity and deck capacity still apply. It does not reconstruct physical target survival through draw histories. Its lower endpoint uses guaranteed surviving target counts and the option to decline searches under the same draw/removal policy. Each combo, group and total has its own interval; a zero-width row is exact.

**Exact only** uses the efficient physical count-state solver when all draw batches fit and no hand operations are needed. Ordered draws, hand costs and mandatory removals have a physical fallback that retains each original card identity and jointly counts hidden losses. Consumed or discarded copies cannot become search targets; competing searches share the actual surviving copies. The same fixed activation and removal policy applies. Interactive Exact has no Work cutoff. Large cases can still reach Storage limits; no partial or optimistic result is returned. Unsupported saved execution models remain separate input failures.

Search and draw operations on the same source remain unsupported. Simple draws retain the reduced count-state distribution. During hand costs/discards, search sources stay explicit, alongside target counts needed by the keep policy. Ordinary cards remain pooled only when their labels cannot change removal decisions or search availability; unobserved labels retain their conditional uniform-subset distribution. Search assignments compile to predicates over the final hand. A shared decision diagram merges equivalent predicates and counts their union with exact card-copy weights, without enumerating every source/target count vector. Exclusions, maxima and distinct requirements are preserved, and each hand counts once. No remaining-identity vector or search-history frontier is added. Storage limits remain unchanged; noninteractive finite Work policies remain supported.

Search retention assesses each source individually; it does not reserve targets for competing searchers or guarantee that a target survives later draws/removal. Final search counting enforces shared copy and capacity limits.

Session schema 6 saves search plans explicitly. New search snapshots explicitly save the shared once-per-turn flag. Older search snapshots remain unrestricted; the formerly ignored draw-only flag does not acquire a new meaning on load. Older sessions preserve their previous behavior, including manual search proxies, until explicitly edited/restored; loading does not automatically give an old Reinforcement entry a search effect. Earlier calculator versions reject schema 6. Use **Customize simulation → None** to disable a search and **Restore built-in simulation** to restore it.

### Customize costs and removals

With a draw amount set, **Before drawing** and **After drawing** can each remove one or two cards. **Eligible** accepts any card, a category/property, a same-card AND group, or OR alternatives.

- A pre-draw cost must be payable without using the activating copy. Otherwise the effect waits and may be reconsidered after another activation.
- A required post-draw removal can use newly drawn cards. If eligible material is missing after a legal draw, that branch fails and remains in the probability denominator.
- Removal follows a deterministic policy: keep held positive combo pieces and searches with plausible access to a positive piece first; then other usable searches and draw cards; then potentially usable cost-deferred draws; then other cards. Searches need an eligible target outside the current hand and positive deck capacity after any reserved draw. Equal tiers retain the canonical tie order. Changing active routes can change the resulting hands.

Individual cards can have hypothetical custom plans: set **Before drawing: None**, **After drawing: None**, or **Banish from deck: 0** to remove only that operation. Other cards keep their own costs. **Restore built-in simulation** restores the maintained plan. Accuracy is reported for the saved plan and fixed policy; this is **not optimal-play simulation**. A custom plan need not represent the printed card or unsupported interactions such as Maliss banish triggers; its result is not a guaranteed upper bound under maxima or exclusions. Draw activations remain mandatory under this policy. Discard triggers and general card effects are unsupported.

Recognized imports and database selections receive authoritative built-in definitions. Saved cards retain their exact plan, source, revision and limitations until explicitly changed or restored; older snapshots are labeled **Saved simulation**. An unavailable execution model is reported rather than replaced with generic draws. Choosing **Draw None** clears pre- and post-draw configuration. **Storage safety limit** is a runtime preference, not a session field. Schema 8 saves one explicit calculation mode. Old ON or omitted settings migrate to Fast; old OFF settings migrate to **No effect simulation**. Card plans remain unchanged.

A small pre-calculation notice flags potentially expensive draw configurations without blocking Calculate. An uncertain current Fast result offers **Recalculate using Exact**. This one-off request preserves the saved mode preference and uses the selected Storage limit and manual cancellation, without a Work cutoff.

Fast and No effect simulation allow 150 million bounded work units. Interactive Exact has no cumulative Work cutoff, can take arbitrarily long, and remains cancelable; completion is not guaranteed. Storage limits apply separately: larger mixed draw configurations can require an explicit **High** or **VeryHigh** storage selection. Work units are not seconds, and increasing work does not resolve a Storage limit.

Progressive uses one worker sequentially and shows Fast while Exact continues without a calculation time cutoff. Fast retains its 150M Interactive Work allowance. Exact starts with unlimited Work even if Fast exhausted its allowance; both stages retain the selected Storage tier. Fast only skips the upgrade; Exact only runs physical Exact directly. Startup retains a separate 30-second guard. Actual interruption of automatic work retains the 2.5-second worker restart delay; successful upgrades reuse the worker. Schema 11 saves the Fast execution strategy separately: previously saved Fast sessions select Progressive, while new Fast-only selections round-trip distinctly. Saved Exact and No effect simulation retain their meanings.

See the calculator's [Help page](https://dev.ygo-calculator.pages.dev/help) for detailed rules and built-in examples.

## How the calculation works

The calculator uses exact combinatorial counting rather than simulation. Each combo is reduced to constraints over the physical cards that can satisfy it. Overlapping categories are normalized while still ensuring that separate positive requirements need separate card copies.

To enforce distinct physical copies across overlapping requirements, the calculator applies Hall’s matching condition to the sets of cards eligible for each requirement.

Independently of user-authored OR groups, compatible alternatives can sometimes be factored into a simpler equivalent condition:

```text
(X AND A) OR (X AND B) = X AND (A OR B)
```

When several combos overlap, compatible constraints are merged, impossible intersections are discarded, and equivalent overlaps only need to be counted once.

Because one opening hand can satisfy several combos, their probabilities cannot simply be added. The calculator uses inclusion-exclusion to avoid double-counting:

```text
P(A OR B) = P(A) + P(B) - P(A AND B)
```

The same principle extends to larger sets of combos.

With draw-only effects, an exact count-state dynamic program produces weighted scenarios. Each scenario fixes retained duplicate effect cards and counts the remaining ordinary cards with the same Hall constraints. Scenario probabilities are combined as exact fractions before converting the final results for display.

Pre-draw costs and post-draw removals use a separate bounded count-state resolver that tracks hand composition, remaining multiplicities, encounter-ordered candidates with an optional activation priority, and used once-per-turn entries; equivalent ordinary entries and resolution states are merged. Exact integer weights count labeled draw prefixes and their unseen suffixes. Final hands are evaluated against the same compiled Hall constraints, and results are converted for display only after exact aggregation.

Cards that behave identically for the remaining conditions are grouped together, and exact combinatorial counts are used to determine how many opening hands succeed without checking every physical hand one by one.

```text
              matching opening hands
P(success) = ------------------------
              all possible opening hands
```

## Features

- Require categories, individual cards, or both within a combo.
- Set minimum counts and either fixed maximums or `Any` (the current hand size) for requirements.
- Calculate individual combo probabilities, grouped probabilities, and the union of all active combos.
- Organize related combos into named groups.
- Import decks from `.ydk` files or YDKe deck codes.
- See automatic card thumbnails in the deck list and a larger preview in an expanded imported-card editor. Images are retained on project hosting; unavailable artwork does not affect calculations or saved sessions.
- Save and load complete calculator sessions.
- Temporarily deactivate cards and combos while testing changes.
- Reorder cards, categories, combos, and combo groups.
- Color-coded categories.
- Light, dark, and system themes.

## Technical stack

- **Blazor WebAssembly**
- **.NET 10**
- **C# 14**
