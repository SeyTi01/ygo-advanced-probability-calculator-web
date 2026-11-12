# Source and contract owners

Use current source to confirm these locations; do not freeze runtime/package versions in skills. The application is under `repo:YGOProbabilityCalculatorBlazor/`.

| Area | Source and conditional reading |
| --- | --- |
| App and UI | `Pages/Index.razor`, `Components/ProbabilityCalculator/`, `Models/`; use [UI contracts](../../ygo-ui/references/editor-contracts.md) for editor changes. |
| Neutral calculation engine | `Engine/`; `Services/CalculationEngineAdapter/` translates application data. Use [calculation contracts](calculation.md). |
| Calculation host | `Services/ProbabilityCalculator/`, `Services/BackgroundCalculation/`; worker/runtime guidance in `repo:scripts/background-calculation/README.md`. |
| Built-in effects | `Services/CardEffects/`; saved plans and their execution contracts are covered by [calculation](calculation.md) and [compatibility](compatibility.md). |
| Persistence/import | `Services/Session/`, `Services/Converter/`, `Services/DeckImport/`; use [compatibility](compatibility.md). |
| Localization/catalog | `repo:docs/localization.md`, `repo:infrastructure/card-catalog/README.md`; use [compatibility](compatibility.md). |
| Application tests | `repo:YGOProbabilityCalculatorBlazorTest/`; components use bUnit. |
| Neutral engine tests | `repo:CardProbability.EngineTests/`; isolation checks under `repo:scripts/engine-boundary/`. |
| Build/deployment | Project files, `repo:YGOProbabilityCalculatorBlazor/build.sh`, `repo:.github/workflows/`; use [infrastructure](infrastructure.md) only for changes there. |

General documentation is indexed in `repo:docs/README.md`. Technical contracts remain in those documents rather than copies in the plugin.
