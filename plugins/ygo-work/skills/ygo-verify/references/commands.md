# Verification entry points

Run commands from the repository root through the selected [environment](environment.md). Discover installed tools and relevant project configuration before substantial work; do not hard-code a host's paths or SDK patch version.

## Application checks

```sh
dotnet restore YGOProbabilityCalculatorBlazor.sln
dotnet build YGOProbabilityCalculatorBlazor.sln --no-restore
dotnet test YGOProbabilityCalculatorBlazor.sln --no-build
node --test
```

Focused .NET runs add `--filter "FullyQualifiedName~YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator"` or `--filter "FullyQualifiedName~YGOProbabilityCalculatorBlazorTest.Components.CalculatorEditor"` to `dotnet test YGOProbabilityCalculatorBlazor.sln`. Choose a more precise existing filter where appropriate. Coverage is opt-in with `--collect:"XPlat Code Coverage"`.

Solution tests include the neutral `CardProbability.EngineTests` project. For engine-boundary changes, additionally follow `repo:scripts/engine-boundary/README.md` for the isolated public-consumer proof. Inspect `repo:.github/workflows/tests.yml` for current CI coverage; CI results do not replace relevant local checks.

## Browser and publishing

```sh
dotnet run --project YGOProbabilityCalculatorBlazor/YGOProbabilityCalculatorBlazor.csproj
```

For optimized publishing, install SDK-scoped `wasm-tools` for the exact publishing SDK, following current build/CI configuration. Preserve production AOT and stripped-IL settings; Debug is not equivalent evidence. Use `repo:scripts/background-calculation/README.md` for the existing published smoke entry point, Playwright/browser setup and focused lifecycle checks. That guide also owns the frozen regression's input requirements; do not fabricate a replacement fixture.

Use `repo:scripts/verify-published-seo.sh` for published HTML risks. Visual checks belong to [UI work](../../ygo-ui/SKILL.md). Performance measurements use [measurement guidance](../../ygo-diagnose/references/measurements.md).

## Plugin changes

```sh
node plugins/ygo-work/scripts/package.mjs --check
node --test plugins/ygo-work/scripts/package.test.mjs
node plugins/ygo-work/scripts/package.mjs /absolute/writable/path/ygo-work.tar.gz
```

The packager validates source and verifies the final archive. Keep generated archives outside the plugin directory and out of commits. Test realistic task routing as well as syntax, including a cloud publication path, initialized Windows, a limited release request and the installed-package path. Publish this canonical source through Plugin Creator; never hand-edit an installed cache as a separate source of truth.
