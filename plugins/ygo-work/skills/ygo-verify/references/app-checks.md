# Application checks

Run from the repository root after [environment preparation](environment.md). Select the affected runtime:

- .NET: `dotnet restore YGOProbabilityCalculatorBlazor.sln`, then `dotnet build YGOProbabilityCalculatorBlazor.sln --no-restore` and `dotnet test YGOProbabilityCalculatorBlazor.sln --no-build`.
- JavaScript: `node --test`; pass specific test paths for a focused run.

For focused .NET tests, add `--filter "FullyQualifiedName~YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator"` or `--filter "FullyQualifiedName~YGOProbabilityCalculatorBlazorTest.Components.CalculatorEditor"`; prefer a narrower existing filter when appropriate. Coverage is opt-in with `--collect:"XPlat Code Coverage"`.

The solution runs calculator application and integration tests against the published engine dependency. Neutral `CardProbability.EngineTests` run in the library repository. For engine dependency or adapter-boundary changes, also use [package-boundary verification](engine-boundary.md).
