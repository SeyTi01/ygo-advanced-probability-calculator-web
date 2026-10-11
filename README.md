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

Cards can still satisfy several roles and properties at once. For example, Vanquish Soul Razen is both a `VS Monster` and a `VS Starter` and has the FIRE property, while Reinforcement of the Army is assigned the VS Monster and VS Starter roles with a manual FIRE property.

With all eight combo definitions active, the example has an **83.61%** probability of opening at least one modeled full-combo hand.

## How to use it

1. **Import or enter your deck.**
2. **Create custom categories** for reusable roles. Imported cards expose objective card properties automatically.
3. **Assign categories to cards.** A card can belong to multiple categories.
4. **Define combos** using categories, specific cards, or both.
5. **Set minimum and maximum counts** for each requirement.
6. **Calculate** to see the probability of each combo and the combined probability of opening any active combo.

Click **Add OR**, then click a condition or whole OR group in the expanded expression. Enter the alternative with the type and range inputs and choose **Add alternative**. Cancel target selection or alternative authoring to return to your ordinary draft. Remove alternatives using their chip crosses. For example, `Razen + (Fire OR Dark)` requires Razen and either Fire or Dark; satisfying both alternatives still counts once. Each alternative has its own Min and fixed Max or Any. Separate positive requirements still need distinct physical copies. Multiple flat OR groups are supported, with one result per combo.

To add same-card constraints, choose **Add AND** and select a category requirement or category alternative in an OR group. Choose another category in the existing requirement row and submit. The new category inherits the target's shared Min and Max, which apply to the resulting conjunction. Each category appears as a removable chip: removing one of two collapses the requirement to the remaining category, while removing the last removes the requirement or alternative. OR branches keep independent ranges. For example, `(Orcust AND Monster) Min 2` needs two cards that each match both properties. Separate requirements joined by `+` still need distinct physical copies.

Each card entry can optionally use **Draw 1**, **Draw 2**, or **Draw 3**. **Once per turn** is on by default: the first encountered copy automatically draws that many cards and is consumed; later copies of that entry remain in the final hand. Turn it off to let every copy activate and be consumed, including copies drawn by another effect. Separate entries have independent allowances, even with the same name. **None** restores ordinary behavior without deactivating the card and preserves your toggle setting. The global **Draw effects** switch can temporarily ignore every configured draw effect, treating those cards as ordinary cards while leaving their individual settings intact. This supports alternative opening-hand analysis and can reduce calculation cost; turning it back on restores the saved card behavior. New imports and explicit database-card selections receive initial draw defaults only for the small curated set of real cards; other imports and selections, plus manual free-text cards, start with no effect. You can change or clear any preset.

Requirements are evaluated on the final hand, including retained duplicate effect cards. **Any** uses that complete final hand size. If the deck runs out during a requested draw, the effect draws every remaining card and the resulting hand is evaluated normally, without renormalizing the branch. With a draw amount configured, **After drawing** supports None, Remove 1, or Remove 2. **Eligible** can be Any card, one category/property, a same-card AND group, or an OR of categories and/or AND groups. New Dark World Dealings presets use Draw 1 / discard 1; Graceful Charity uses Draw 3 / discard 2, both without once-per-turn limits. Existing saves keep their settings. New Allure of Darkness defaults to Draw 2 / non-OPT / remove one DARK card; Radiant Typhoon Vision defaults to Draw 2 / OPT / remove one Quick-Play Spell OR Radiant Typhoon card. If eligible material for a required removal is unavailable, that branch fails without renormalizing the other outcomes. These defaults apply only to new imports and pristine database selections; old saved cards remain authoritative. Choosing Draw None clears the removal setting and filter.

Open the gear **Calculation settings** menu for **Draw simulation**. **Draw effects** enables Basic simulation: configured fixed draws and once-per-turn limits, ignoring all post-draw removal, filter, and failure settings. The nested **Advanced draw simulation** switch adds supported post-draw removals using the stored card settings and defaults off. These are two levels of draw simulation complexity; both require Draw effects to be on. Turning Draw effects off also turns Advanced off; re-enabling Draw effects returns to Basic simulation. Advanced mode and the separate **Storage safety limit** are runtime preferences, not saved/shared/recovery session fields; loading or importing a deck does not enable Advanced. The storage selector applies to every calculation path and remains available in Basic mode.

Open the gear **Calculation settings** menu for **Draw simulation**. **Draw effects** enables Basic simulation: configured fixed draws and once-per-turn limits, ignoring all post-draw removal, filter, and failure settings. The nested **Advanced draw simulation** switch adds supported post-draw removals using the stored card settings and defaults off. These are two levels of draw simulation complexity; both require Draw effects to be on. Turning Draw effects off also turns Advanced off; re-enabling Draw effects returns to Basic simulation. Advanced mode and the separate **Storage safety limit** are runtime preferences, not saved/shared/recovery session fields; loading or importing a deck does not enable Advanced. The storage selector applies to every calculation path and remains available in Basic mode.

In Advanced mode, each effect consumes its source, draws up to its configured batch size (stopping when the deck is empty), then performs its required removal before the next surviving FIFO candidate activates. The source has already left the hand; newly drawn cards can be removed, and removed candidates never activate. Eligibility is checked before applying the keep policy. The deterministic policy keeps cards matching positive requirements in any currently active route first, then draw cards that can still activate, then unrelated/non-usable cards. Maximum-only constraints do not protect cards. Stable membership and card-identity ties make deck-row order irrelevant. All combo, group, and total rows use the same resulting hands; changing active routes can change the policy. Random draws are counted exactly for this policy, which is **not optimal-play simulation**. Pre-draw costs ([#147](https://github.com/SeyTi01/ygo-advanced-probability-calculator-web/issues/147)) are not yet supported. Discard triggers, optional activation, searches, and general card effects also remain unsupported.

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

Post-draw removals use a separate bounded count-state resolver that tracks hand composition, remaining multiplicities, FIFO candidates, and used once-per-turn entries; equivalent ordinary entries and resolution states are merged. Exact integer weights count labeled draw prefixes and their unseen suffixes. Final hands are evaluated against the same compiled Hall constraints, and results are converted for display only after exact aggregation.

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
