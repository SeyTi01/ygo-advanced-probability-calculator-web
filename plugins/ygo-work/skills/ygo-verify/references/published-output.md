# Optimized published output

Use a clean Release publish matching the revision being checked. When one is unavailable, use [environment preparation](environment.md), install SDK-scoped `wasm-tools` for the publishing SDK, and publish from the repository root:

```sh
dotnet publish YGOProbabilityCalculatorBlazor/YGOProbabilityCalculatorBlazor.csproj -c Release
```

Preserve production AOT/stripped-IL settings from current project/build configuration; omit temporary measurement probes. Debug output is not equivalent evidence. Resolve `<published-wwwroot>` from the actual target framework/output directory.
