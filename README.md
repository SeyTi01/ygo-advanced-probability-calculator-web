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

Results use combinatorial counting and never fall back on Monte Carlo simulation or approximation.

## Example: Vanquish Soul / K9

The included example uses a Vanquish Soul / K9 deck and models nine underlying routes as eight combo definitions. `VS Starter + (Fire OR Dark)` combines two starter routes into one OR definition. The routes either reach the full Vanquish Soul setup directly or make Ripper + Saryuja as a bridge. Half boards are intentionally not counted.

The example uses three custom role categories:

- `VS Monster`
- `VS Starter`
- `K9 Starter`

Objective properties such as FIRE, DARK, and Level 5 come from card metadata rather than separate user categories. Where a spell functionally represents one of those properties for combo modeling, the example uses a manual metadata assignment.

Common properties are available in card, combo, and draw-filter selectors before a matching card is present. They use the same metadata identity as imported-card properties; specific values such as archetypes and monster types appear when relevant.

Cards can still satisfy several roles and properties at once. For example, Vanquish Soul Razen is both a `VS Monster` and a `VS Starter` and has the FIRE property, while Reinforcement of the Army is assigned the VS Monster and VS Starter roles with a manual FIRE property.

With all eight combo definitions active, the example has an **83.61%** probability of opening at least one modeled full-combo hand.

## How to use it

### Get started

1. **Import or enter your deck.**
2. **Create custom categories** for reusable roles and assign them to cards. Cards can have several categories; imported cards also expose objective properties automatically.
3. **Define combos** using categories, specific cards, or both.
4. **Set minimum and maximum counts.** Leave Max empty for **Any** (the final hand size), or enter a fixed maximum. Set both to `0` to exclude matching cards.
5. **Calculate** to see each combo's probability and the chance of opening any active combo. Hands satisfying several combos count only once in the combined result.

Separate positive requirements need distinct physical copies: one card matching both `Starter` and `Fire` cannot fill both parts of `Starter + Fire`.

### Add OR alternatives and same-card AND conditions

- **OR:** Click **Add OR**, select a condition or OR group in the expanded expression, then enter another condition and choose **Add alternative**. `Razen + (Fire OR Dark)` needs Razen and either Fire or Dark. Each alternative has its own Min/Max; satisfying both still counts once.
- **AND:** Click **Add AND**, select a category requirement or category alternative, then choose another category and submit. `(Orcust AND Monster) Min 2` needs two cards that each match both properties, using shared Min/Max bounds.

Use chip crosses to remove conditions.

### Configure draw effects

Expand a card entry to choose **None**, **Draw 1**, **Draw 2**, or **Draw 3**. An activating copy is consumed. With **Once per turn** on (the default), later copies of that entry stay in hand; turn it off to let every copy activate, including newly drawn copies. Separate entries have independent allowances.

Open the gear **Calculation settings** menu to choose how these settings apply:

- **Draw effects off:** Treat all cards as ordinary cards while preserving their configured effects.
- **Basic simulation:** Enable **Draw effects** for fixed draws and once-per-turn limits. Pre-draw costs and post-draw removals are ignored.
- **Advanced draw simulation:** Also enable the nested switch to apply those costs, removals, and eligibility filters. It defaults off and can require more calculation resources.

Requirements apply to the final hand. If the full draw amount is unavailable, the source stays in hand and the outcome is evaluated normally.

### Use Advanced costs and removals

With a draw amount set, **Before drawing** and **After drawing** can each remove one or two cards. **Eligible** accepts any card, a category/property, a same-card AND group, or OR alternatives.

- A pre-draw cost must be payable without using the activating copy. Otherwise the effect waits and may be reconsidered after another activation.
- A required post-draw removal can use newly drawn cards. If eligible material is missing after a legal draw, that branch fails and remains in the probability denominator.
- Removal follows a deterministic policy: keep cards matching positive requirements in active routes first, then usable draw cards, then other cards. Changing active routes can change the resulting hands.

Results are exact for this policy, **not optimal-play simulation**. Optional activation, searches, discard triggers, and general card effects are unsupported.

Some new imports and database selections receive editable presets; saved cards keep their chosen settings. Choosing **Draw None** clears pre- and post-draw configuration. Advanced mode and **Storage safety limit** are runtime preferences, not session fields. Turning Draw effects off resets Advanced; re-enabling returns to Basic.

See the calculator's [Help page](https://dev.ygo-calculator.pages.dev/help) for detailed rules and preset examples.

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

Pre-draw costs and post-draw removals use a separate bounded count-state resolver that tracks hand composition, remaining multiplicities, FIFO candidates, and used once-per-turn entries; equivalent ordinary entries and resolution states are merged. Exact integer weights count labeled draw prefixes and their unseen suffixes. Final hands are evaluated against the same compiled Hall constraints, and results are converted for display only after exact aggregation.

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
