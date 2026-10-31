# Yu-Gi-Oh! Advanced Probability Calculator

Calculate opening-hand combo probabilities for Yu-Gi-Oh! decks with overlapping card roles, distinct physical requirements, and configured draw/search effects. The engine uses combinatorial counting, not Monte Carlo sampling. Each result reports its accuracy for the configured model.

**[Try the stable calculator](https://ygo-calculator.pages.dev/)** · **[Development preview](https://dev.ygo-calculator.pages.dev/)** · **[How to use it](https://dev.ygo-calculator.pages.dev/help)**

This README is updated before each release and may not reflect the latest development changes.

![Dark full-page calculator showing a 40-card Vanquish Soul deck, eight combos, and a settled Progressive result of 85.92%.](YGOProbabilityCalculatorBlazor/Assets/probability_calculator_example.png)

## What it models

- **Effect simulation.** Simulate supported draw and search effects, including conditional effects.
- **Distinct physical copies.** Separate positive requirements such as `Warrior + Fire` need different copies. A single copy of Vanquish Soul Razen cannot fulfill both.
- **Overlapping properties and combos.** A card can match several properties, and a hand can satisfy several combos. Group totals and **Any combo** count each successful outcome only once.

## Modes and accuracy

The model resolves all draw effects before search effects. It then checks whether any legal combination of search targets can satisfy each combo, without committing each search effect to a particular target. Interleaving searches with draws would invalidate that approach and require substantially more expensive calculations. The fixed order can differ from optimal play, even when the result is exact for the model.

Random banishes and mills can remove search targets from the deck. Fast can sometimes account for these losses statistically, or prove they cannot affect the result, without tracking the identity of every remaining card. Most practical calculations produce an exact result; others may be bounded by a certified range or be uncertified, with no proven error range.

When Fast is not exact, the default mode attempts an exact calculation. Some combinations of draw and search effects make this substantially more expensive. You can also choose Fast only, Exact only, or calculate without effect simulation.

See [accuracy and performance](docs/accuracy-and-performance.md) for the precise contracts and [the calculation model](docs/calculation-model.md) for supported operations and exclusions. For the mathematics behind counting, including inclusion-exclusion and Hall-style matching, see [the probability engine](docs/probability-engine.md).

## Start with a deck

1. Import a `.ydk` file or YDKe code, or enter cards manually. Set the opening hand size.
2. Assign reusable categories if needed. Imported metadata properties are available alongside custom roles.
3. Define combos using card identities, categories, and properties. Combine alternatives or properties as needed.
4. Calculate.

## Example: Vanquish Soul / K9

The bundled session shows how to define Vanquish Soul and K9 combos using card identities and properties, including alternatives such as `VS Starter + (Fire OR Dark)`. With all combos active, the exact chance of reaching any combo is **85.92%**. Open the [Help page](https://dev.ygo-calculator.pages.dev/help) and choose **Try It with Example Data** to explore the setup.

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
