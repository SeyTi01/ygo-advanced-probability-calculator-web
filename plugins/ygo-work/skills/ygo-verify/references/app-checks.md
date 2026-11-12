# Application checks

Run from the repository root after [environment preparation](environment.md). Select the affected runtime:

- .NET: `dotnet restore YGOProbabilityCalculatorBlazor.sln`, then `dotnet build YGOProbabilityCalculatorBlazor.sln --no-restore` and `dotnet test YGOProbabilityCalculatorBlazor.sln --no-build`.
- JavaScript: `node --test`; pass specific test paths for a focused run.

For focused .NET tests, add `--filter "FullyQualifiedName~YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator"` or `--filter "FullyQualifiedName~YGOProbabilityCalculatorBlazorTest.Components.CalculatorEditor"`; prefer a narrower existing filter when appropriate. Coverage is opt-in with `--collect:"XPlat Code Coverage"`.

Solution tests include `CardProbability.EngineTests`. For engine-boundary changes, also use [the isolation proof](engine-boundary.md).
