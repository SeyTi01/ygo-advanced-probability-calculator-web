# Native frozen export and replay

Apply [native setup](native-benchmark-setup.md) and the shared [frozen fixture contract](frozen-fixtures.md). The tool is `repo:YGOProbabilityCalculatorBlazorTest/Services/ProbabilityCalculator/PerformanceAuditBenchmark.cs`.

To generate fixtures, set `YGO_AUDIT_OUTPUT` to the frozen file on the baseline. Optionally set `YGO_AUDIT_CASE` to a name filter (`tiny` selects inexpensive controls). Export once:

```sh
dotnet test YGOProbabilityCalculatorBlazor.sln -c Release --no-build --no-restore --filter FullyQualifiedName=YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator.PerformanceAuditBenchmark.Export
```

For replay, set `YGO_AUDIT_INPUT` to that file and `YGO_AUDIT_OUTPUT` to a different output for each revision. `YGO_AUDIT_TRIALS` selects 1–30 warm samples (default 3). Run on baseline and candidate:

```sh
dotnet test YGOProbabilityCalculatorBlazor.sln -c Release --no-build --no-restore --filter FullyQualifiedName=YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator.PerformanceAuditBenchmark.Replay
```
