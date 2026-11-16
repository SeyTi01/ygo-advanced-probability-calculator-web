# Documentation

The [project overview](../README.md) introduces the calculator and the bundled example. Choose a guide by what you need:

| Guide | Reader and purpose |
| --- | --- |
| [Help / usage guide](https://dev.ygo-calculator.pages.dev/help) ([source](../YGOProbabilityCalculatorBlazor/Pages/Help.razor)) | Enter a deck, edit requirements and effects, read results, and save/share work. |
| [Calculation model](calculation-model.md) | Understand which saved rules execute and what gameplay is excluded. |
| [Probability engine](probability-engine.md) | Follow physical-copy counting, overlaps, draws and searches, with a small enumerable example. |
| [Accuracy and performance](accuracy-and-performance.md) | Interpret Exact/Bounded/Uncertified results and distinguish Work, Storage and worker lifecycle limits. |
| [Generic calculation API](generic-calculation-api.md) | Call the public neutral engine API, inspect terminal outcomes, and understand its operation model. |
| [Engine dependency maintenance](../scripts/engine-boundary/README.md#adopt-or-upgrade-the-package) | Adopt a published engine version while preserving calculator contracts and verifying the public package boundary. |
| [Localization contributions](localization.md) | Add presentation resources while preserving canonical identities, saved sessions and effect compatibility. |

For a first visit, use Help, then read the model before treating a simulated effect as the printed card's complete behavior.

Developer references: [build and test setup](../README.md#documentation-and-development), [worker/runtime implementation and measurements](../scripts/background-calculation/README.md), [session recovery and share-link transport](../YGOProbabilityCalculatorBlazor/Services/Session/RECOVERY.md), and [publication guard](../scripts/codex/PRIVACY.md). Historical measurements in runtime notes describe their stated fixtures and policies; they are not completion guarantees for current decks.
