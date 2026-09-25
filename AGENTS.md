# Repository instructions

Use these instructions when changing this repository. Read [README.md](README.md) for the user-facing description; keep issue work focused on the requested behavior.

## Project map

- The .NET 9 Blazor WebAssembly app is in `YGOProbabilityCalculatorBlazor/`; `YGOProbabilityCalculatorBlazor/Pages/Index.razor` renders the main calculator.
- `YGOProbabilityCalculatorBlazor/Services/ProbabilityCalculator/ProbabilityCalculatorService.cs` implements the probability calculation.
- `YGOProbabilityCalculatorBlazor/Models/` contains cards, categories, combo constraints, and session state.
- `YGOProbabilityCalculatorBlazor/Components/ProbabilityCalculator/` contains the calculator and its card, category, and combo editors. `YGOProbabilityCalculatorBlazor/Components/ProbabilityCalculator/EditorKeys.cs` supports stable editor identity.
- Import and persistence code lives in `YGOProbabilityCalculatorBlazor/Services/DeckImport/`, `YGOProbabilityCalculatorBlazor/Services/Session/`, and `YGOProbabilityCalculatorBlazor/Services/Converter/`.
- The test project is `YGOProbabilityCalculatorBlazorTest/`. It contains bUnit editor tests and focused service tests.

## Local development and verification

The solution's projects target `.NET 9` (`net9.0`); the README identifies C# 13. Install the .NET 9 SDK. Run commands from the repository root:

```sh
dotnet restore YGOProbabilityCalculatorBlazor.sln
dotnet build YGOProbabilityCalculatorBlazor.sln --no-restore
dotnet test YGOProbabilityCalculatorBlazor.sln --no-build
```

For focused regression runs:

```sh
dotnet test YGOProbabilityCalculatorBlazor.sln --filter "FullyQualifiedName~YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator"
dotnet test YGOProbabilityCalculatorBlazor.sln --filter "FullyQualifiedName~CalculatorEditorTest"
```

Run the app locally with `dotnet run --project YGOProbabilityCalculatorBlazor/YGOProbabilityCalculatorBlazor.csproj`. Coverage collection is available through the test project's `coverlet.collector` package:

```sh
dotnet test YGOProbabilityCalculatorBlazor.sln --collect:"XPlat Code Coverage"
```

There is no GitHub Actions CI workflow. Run relevant tests locally and report the exact commands and outcomes in the pull request. Only if the local environment reports MSBuild parallel-node or reuse errors, retry the affected command with `-m:1` and report that workaround; serial builds are not a general requirement.

## Probability correctness

- A card may belong to multiple categories. A drawn copy counts once toward every category assigned to that card.
- Each combo requires all of its category constraints; success means at least one combo matches. Preserve inclusive minimum and maximum bounds and count overlapping combos only once.
- A `0/0` constraint is valid. Do not clamp, discard, or silently change valid constraints.
- When changing calculation behavior, add targeted cases to `YGOProbabilityCalculatorBlazorTest/Services/ProbabilityCalculator/`. Prefer the existing `YGOProbabilityCalculatorBlazorTest/Services/ProbabilityCalculator/SmallDeckOracleTest.cs`, which enumerates physical hands and evaluates constraints independently of the production aggregation algorithm.
- Keep oracle expectations independent of the implementation under test. Verify expected values with exhaustive enumeration or another actual calculation tool; never select numbers by intuition or memory. Cover overlapping memberships/combos and boundary constraints when relevant.

## Editor state and compatibility

- Preserve each card and combo editor's draft and active state through edits, list removal/reordering, session loading, and deck import. The list editors use `EditorKeys<T>` and Blazor `@key` to carry UI identity across model replacement; preserve this behavior.
- Do not overwrite an in-progress draft when a parent value changes. In particular, hand-size changes may update untouched defaults but must not alter user-entered or saved constraints.
- Add bUnit interaction tests in `YGOProbabilityCalculatorBlazorTest/Components/CalculatorEditorTest.cs` for editor behavior changes.
- Preserve existing saved-session JSON and `.ydk` import behavior unless the issue explicitly changes it. For persistence or import changes, add focused coverage under `YGOProbabilityCalculatorBlazorTest/Services/Session/`, `YGOProbabilityCalculatorBlazorTest/Services/Converter/`, or `YGOProbabilityCalculatorBlazorTest/Services/DeckImport/` as appropriate.

## Contribution workflow and boundaries

- Start from an up-to-date `dev` branch. Read the issue, relevant dependencies, applicable agent instructions, and recent code before editing.
- Before the first push, review local commits. If an unpushed commit merely corrects or refines an earlier unpushed commit for the same logical change, amend or squash them into one coherent commit. Keep independently meaningful changes in separate commits, grouped by purpose rather than by pull request or file.
  Never amend, rebase, or squash already-pushed commits or force-push for cosmetic cleanup. If you cannot confidently establish that a commit is unpushed, preserve it.
- Keep each change and pull request focused; add the most relevant regression tests and run the solution-level checks for substantial changes.
- Create a feature branch and open a pull request targeting `dev`. Never push directly to protected branches or merge a pull request.
- In the pull request, report commands and pass/fail results, manual checks, and any limits on verification. Put verification results in the pull request discussion, not in an extra report file.
- Do not add GitHub Actions, change Cloudflare deployment or branch policies, upgrade frameworks/dependencies, perform broad refactors, or add unrelated documentation unless explicitly requested. Do not intentionally trigger deployments or modify releases without authorization.
- Do not claim that the hosted site represents a particular branch unless you have verified it.
