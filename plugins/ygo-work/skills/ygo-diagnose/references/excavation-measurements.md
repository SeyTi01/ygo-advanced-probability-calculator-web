# Excavation export

Apply [native setup](native-benchmark-setup.md) and the shared [frozen fixture contract](frozen-fixtures.md). Set `YGO_EXCAVATION_BENCHMARK` to the output JSON path. Optional controls are `YGO_EXCAVATION_TRIALS` (1–20 warm samples; default 3), `YGO_EXCAVATION_CASE`, `YGO_EXCAVATION_EXCLUDE`, and `YGO_EXCAVATION_MODE` (`Fast` or `Exact`).

```sh
dotnet test YGOProbabilityCalculatorBlazor.sln -c Release --no-build --no-restore --filter FullyQualifiedName=YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator.ExcavationBenchmark.Measure
```

The tool saves each fixture/mode's exact worker input and full native reference response. First/warm timings and allocations are printed; use [native replay](native-replay.md) for structured samples or [browser replay](browser-replay.md) for the same input in optimized WASM.

The routine browser correctness smoke uses a small independent excavation control; the 40-card measurement fixture is not a UI requirement. Historical acceptance results remain in `repo:scripts/background-calculation/README.md#historical-excavation-observations`.
