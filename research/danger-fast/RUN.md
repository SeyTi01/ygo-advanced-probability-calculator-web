# Reproduce the local research checkpoint

Read RESUME.md first. No research source is compiled by a normal project build. Production remains the exact reference implementation; all approximation methods are experimental. Use repository-required approved execution for restore/test and read the normal AGENTS instructions.

From the repository root in PowerShell, after the normal restore:

```powershell
$dangerResearch = (Resolve-Path research/danger-fast).Path
dotnet build YGOProbabilityCalculatorBlazor.sln --no-restore "-p:CustomAfterMicrosoftCommonTargets=$dangerResearch/Research.targets"
dotnet test YGOProbabilityCalculatorBlazor.sln --no-build --filter 'FullyQualifiedName~DangerBoundsOracleExperiment'

$env:YGO_DANGER_RESEARCH = $dangerResearch
dotnet test YGOProbabilityCalculatorBlazor.sln --no-build --filter 'FullyQualifiedName~DangerChallengeReference.CaptureCheapChallenge'
$env:YGO_DANGER_CASE = 'danger-heavy'
$env:YGO_DANGER_WORK = '149000000'
$env:YGO_DANGER_CAPACITY = '2048'
$env:YGO_DANGER_BATCHES = '0'
$env:YGO_DANGER_RETAINED_STRATUM = '1'
dotnet test YGOProbabilityCalculatorBlazor.sln --no-build --filter 'FullyQualifiedName~DangerAnonymousExperiment.Measure'
node research/danger-fast/verify-evidence.mjs
node research/danger-fast/summarize-evidence.mjs
```

An empty case filter runs all 13 frozen inputs; none of these measurements runs heavy Exact. The cheap-reference test is restricted to the frozen mixed/direct/exclusion challenge and uses a finite diagnostic request. To reproduce earlier methods, set retained-stratum to0; whole batches can be tested separately with batches1, but combining both is deliberately unsupported. Beam and continuation measurements have their own explicit test classes. Larger Work values in historical journals are diagnostic cost-gap measurements and never production-policy compliance.

Samples append before reference assertions. Preserve complete newline-terminated JSONL records after interruption; a Started record without Sample/Failed means unknown completion. Do not delete existing journals or overwrite frozen inputs to tune a result. verify-evidence independently checks frozen input hashes, every stored interval and available exact references with integer cross-products.

After experiments, restore the normal build without the extra import:

```powershell
dotnet build YGOProbabilityCalculatorBlazor.sln --no-restore
```

Do not build while an existing test process is using its output DLLs. Run timing jobs serially. The owner subsequently authorized publishing the result with the six unpushed checkpoints squashed into one research commit.
