# Yu-Gi-Oh! Advanced Probability Calculator

Calculate opening-hand combo probabilities for Yu-Gi-Oh! decks with overlapping card roles, distinct physical requirements, and configured draw/search effects. The engine uses combinatorial counting, not Monte Carlo sampling. Each result reports its accuracy for the configured model.

**[Try the stable calculator](https://ygo-calculator.pages.dev/)** · **[Development preview](https://dev.ygo-calculator.pages.dev/)** · **[How to use it](https://dev.ygo-calculator.pages.dev/help)**

This README describes the current `dev` branch; the stable deployment may differ.

![Calculator showing the 40-card Vanquish Soul / K9 example with eight combos and an Exact 85.92% total](YGOProbabilityCalculatorBlazor/Assets/probability_calculator_example.png)

## What it models

- **Overlapping roles and properties.** A card can be a custom Starter, a FIRE monster, and a Level 5 card at once. Imports supply objective card metadata; custom categories describe your own roles.
- **Distinct physical copies.** Separate positive requirements such as `Starter + Fire` need different copies. Same-card `AND` combines properties on each matching card; `OR` offers alternative routes.
- **Overlapping combos and groups.** Individual rows may overlap. Group totals and **Any active combo** count each successful random outcome once.
- **Configured effects.** Supported built-in and custom draw/search plans resolve under a fixed policy, including supported costs, removals, hidden deck losses and activation priority. This models the saved plan, not full gameplay or optimal play.

## Start with a deck

1. Import a `.ydk` file or YDKe code, or enter cards manually. Set the opening hand size.
2. Assign reusable categories if needed. Imported metadata properties are available alongside custom roles.
3. Use **Add requirement** to define combos. Set **At least** and optionally **Maximum: Any**. A `0–0` requirement excludes matching cards. Numeric properties support inclusive comparisons and ranges in one term.
4. Add **OR** alternatives or same-card **AND** properties where appropriate. Separate positive requirements still consume distinct copies.
5. Open **Calculation settings**, choose a mode, and **Calculate**. Read the accuracy shown with each row before comparing results.

Expand a card to inspect or customize its simulation. **Save** accepts edits; **Cancel** preserves the saved plan. **Restore built-in simulation** explicitly applies the current maintained plan. Saved sessions keep their effect settings until changed; compatible built-in descriptions can refresh without changing those settings.

## Example: Vanquish Soul / K9

The bundled session has **24 card entries representing 40 copies**, a **five-card hand**, **eight combo definitions**, and the **Full VS** and **Full K9** groups. It has no user-defined categories: requirements use card identities and objective metadata. `VS Starter + (Fire OR Dark)` combines alternatives in one definition.

Its search cards use their configured search effects; fetched monsters contribute their own identities and properties. Reinforcement of the Army uses one Level 1–4 Warrior predicate, and the supplied Reinforcement and Chaotic Elements plans match their current maintained definitions.

With all eight combos active and **Exact** selected, **Any active combo is 85.92% (565375/658008)**. Open the [Help page](https://dev.ygo-calculator.pages.dev/help) and choose **Try It with Example Data** to inspect the complete setup.

## Modes and accuracy

**Progressive** first presents a completed Fast result, then attempts physical Exact on the same input if needed. **Fast only** stops after Fast; it may report Exact, certified bounds, or Uncertified. **Exact only** publishes values only after physical Exact completes. **No effect simulation** evaluates the dealt hand while ignoring all configured effects.

Interactive Exact and Progressive's Exact stage have no calculation deadline or cumulative Work cutoff. They remain cancelable and subject to Storage limits, unsupported cases and failures; unlimited Work does not guarantee completion. An interrupted Progressive upgrade keeps the accepted Fast result. Bounded percentages use a certified midpoint and outward-rounded ± radius; the retained primary fraction is a separate modeled quantity.

See [accuracy and performance](docs/accuracy-and-performance.md) for the precise contracts and [the calculation model](docs/calculation-model.md) for supported operations and exclusions.

## Workspace features

- Save/load sessions, copy share links, and recover accepted workspace edits from local browser storage.
- Temporarily deactivate cards and combos; organize combos into named groups.
- Reorder cards, categories, combos, groups and effect priority with arrows or a direct **Move to position** destination.
- Pin and copy results; compare only after checking input context, stale/preliminary state and accuracy.
- Card thumbnails, category colors, and light, dark or system themes. Missing artwork does not affect calculations.

## Documentation and development

Start with the [documentation index](docs/README.md). It links the user guide, supported model, worked probability explanation, and runtime accuracy/resource reference.

The app uses **Blazor WebAssembly, .NET 10 and C# 14**. With a .NET 10 SDK:

```sh
dotnet restore YGOProbabilityCalculatorBlazor.sln
dotnet build YGOProbabilityCalculatorBlazor.sln --no-restore
dotnet test YGOProbabilityCalculatorBlazor.sln --no-build
dotnet run --project YGOProbabilityCalculatorBlazor/YGOProbabilityCalculatorBlazor.csproj
```

Install SDK-scoped `wasm-tools` for optimized Release publishing. See [repository instructions](AGENTS.md), [restricted Linux verification](scripts/codex/RESTRICTED-LINUX.md), and [publication privacy checks](scripts/codex/PRIVACY.md) before contributing. [Worker/runtime notes](scripts/background-calculation/README.md) contain implementation and historical benchmark evidence.

Card artwork is supplied by [YGOPRODeck](https://ygoprodeck.com/api-guide/) and retained on project hosting. The software license does not cover third-party artwork.
