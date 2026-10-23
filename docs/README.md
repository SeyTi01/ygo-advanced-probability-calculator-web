# Documentation

The [project overview](../README.md) introduces the calculator and the bundled example. Choose a guide by what you need:

| Guide | Reader and purpose |
| --- | --- |
| [Help / usage guide](https://dev.ygo-calculator.pages.dev/help) ([source](../YGOProbabilityCalculatorBlazor/Pages/Help.razor)) | Enter a deck, edit requirements and effects, read results, and save/share work. |
| [Calculation model](calculation-model.md) | Understand which saved rules execute and what gameplay is excluded. |
| [Probability engine](probability-engine.md) | Follow physical-copy counting, overlaps, draws and searches, with a small enumerable example. |
| [Accuracy and performance](accuracy-and-performance.md) | Interpret Exact/Bounded/Uncertified results and distinguish Work, Storage and worker lifecycle limits. |

For a first visit, use Help, then read the model before treating a simulated effect as the printed card's complete behavior. For engine work, read the model, engine explanation and accuracy reference in that order.

Developer references: [AGENTS.md](../AGENTS.md), [worker/runtime implementation and measurements](../scripts/background-calculation/README.md), [session recovery and share-link transport](../YGOProbabilityCalculatorBlazor/Services/Session/RECOVERY.md), and [restricted Linux verification](../scripts/codex/RESTRICTED-LINUX.md). Historical measurements in runtime notes describe their stated fixtures and policies; they are not completion guarantees for current decks.
