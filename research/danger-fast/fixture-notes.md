# Frozen inputs and interruption-safe measurements

## Established evidence

Source baseline: `bd75460e4bb3d9f990d8bc04bd1fcd915bcabc71`, `CardEffectProfileBenchmark.MeasureDanger`. The fixture construction and its six heavy fractions are unchanged. Export-only mode captures snapshots and never invokes a solver, including for rows whose name ends in `exact`.

`frozen-fixtures.json` holds eight baseline Fast worker inputs (four shapes at 40/60 cards), all six heavy reference fractions for each size, six newly checked cheap reference vectors, and five challenge inputs frozen before tuning. `fixture-manifest.json` lists input/reference SHA-256 values. Hashes use recursively ordinal-key-sorted object keys, original array order, UTF-8 compact JSON without a trailing newline. They identify semantic JSON, not the particular file whitespace or .NET encoder escaping.

The heavy 40/60 input hashes are respectively `a9bb949c8f2c7f4672516b877a14fb0d6bd574079cb7748b72461bce6c3985cc` and `129d4a6fb930ea1aa40da2c2158fd5af5cf16939b5c455563a8ca770930f5781`. Their six-row reference-vector hashes are `3d2d4e40d8c6bc4dc8020c0b61db2d6c24797708dd6667fc584667e9296c239f` and `6655abac2616f0db4060fa276c600883fc7a1c4d1830ab97f53e55e2ac202090`. No heavy Exact calculation was run to obtain them. Historical full-frontier fractions are regression references, not independent mathematical proof.

Row order is total, three combos, then two groups. This includes the `0–0` exclusion row and the identical single-combo exclusion group. All snapshots retain Standard Storage (`32,768` entries / `262,144` cells per structure), Fast `150,000,000` Work, the original base-only profiles, and the fixed activation policy.

Challenges: heavy 40 hand six (fixed maxima stay five); heavy 60 custom priority; heavy 40 altered Danger multiplicities with deck size preserved; heavy 60 an added direct-card positive requirement; mixed 40 added direct-card and exclusion requirements plus custom priority. They have no invented expected values. The challenge transformations are explicit in `freeze-fixtures.mjs`; challenge solver/wire execution remains to be checked.

## Verification completed by fixture worker

- Read README/AGENTS. `dotnet --info` approved execution: SDK 10.0.401, Windows x64, wasm-tools installed. `dotnet --list-sdks` restricted execution confirmed .NET 10.
- `dotnet restore YGOProbabilityCalculatorBlazor.sln` approved execution passed.
- `dotnet build YGOProbabilityCalculatorBlazor.sln --no-restore` restricted execution passed, zero warnings/errors.
- `MeasureDanger` export-only passed, 16 snapshots, 195 ms test time. Fast/Exact snapshots compare equal after changing only `Options.Mode`.
- Three serial single-trial `MeasureDanger` executions filtered to `control`, `single`, and `mixed` passed (approved execution, Debug). Twelve native samples and twelve corresponding wire executions returned matching complete result/contract JSON. Six Fast/Exact reference vectors match. All sample journal records parsed and matched aggregate results.
- `cheap-samples.json` preserves fractions, proof/route, Work, per-structure peaks, native elapsed time, and thread allocation counts. Timings include JIT on the first call of each process and are **not** published-AOT/browser performance evidence. Samples were serial; no timing overlap with builds.
- Work remained 2,838 (controls), 69,355/78,749 (single 40/60), and 2,436,630/2,695,466 (mixed 40/60). No changed production solver code was needed.
- After these samples, wire-start/wire-completion and unexpected-failure records were added to the journal. The lead reran the control 40/60 Fast/Exact cases after rebuilding; this worker verified all 16 final-format records, exact Started/Completed/WireStarted/WireCompleted ordering, and native/wire JSON equality with all four aggregate cases. `journal-validation.json` preserves counts, commands, raw file hashes and compact outcomes. The unexpected-exception path was not deliberately triggered. Full regular suites and AOT/browser checks were not run by this worker.

## Reproduction (PowerShell, repository root)

Use repository-required approved execution for restore/test and restricted execution for `build --no-restore`.

```powershell
dotnet restore YGOProbabilityCalculatorBlazor.sln
dotnet build YGOProbabilityCalculatorBlazor.sln --no-restore
$env:YGO_DANGER_BENCHMARK_EXPORT_ONLY = '1'
$env:YGO_DANGER_BENCHMARK_OUTPUT = Join-Path (Get-Location) 'research/danger-fast/exported-inputs.json'
dotnet test YGOProbabilityCalculatorBlazor.sln --no-build --filter 'FullyQualifiedName~CardEffectProfileBenchmark.MeasureDanger'
Remove-Item Env:YGO_DANGER_BENCHMARK_EXPORT_ONLY
node research/danger-fast/freeze-fixtures.mjs
```

The freezer can reuse checked `cheap-samples.json`. To regenerate cheap evidence first, execute the following loop serially, then rerun the freezer. Use fresh output filenames or remove only previously generated local journals first: append-only journals intentionally retain earlier runs.

```powershell
$env:YGO_DANGER_BENCHMARK_TRIALS = '1'
foreach ($dangerShape in @('control', 'single', 'mixed')) {
    $env:YGO_DANGER_BENCHMARK_CASE = $dangerShape
    $env:YGO_DANGER_BENCHMARK_OUTPUT = Join-Path (Get-Location) "research/danger-fast/$dangerShape-evidence.json"
    dotnet test YGOProbabilityCalculatorBlazor.sln --no-build --filter 'FullyQualifiedName~CardEffectProfileBenchmark.MeasureDanger'
}
node research/danger-fast/freeze-fixtures.mjs
```

Do not remove the case filter and run unfiltered non-export measurements casually: that invokes both heavy unlimited Exact cases multiple times.

## Journal/recovery contract

Each native trial appends and flushes `Started`, then `Completed` (with result or Work/Storage outcome) or `Failed` (with exception type). Wire validation also appends `WireStarted`/`WireCompleted`, so a costly interrupted wire parity run is distinguishable from a completed native trial. Read the complete newline-terminated JSON records in `<output>.samples.jsonl`; after a killed process, ignore an incomplete final line and retain all preceding records. A started record with no matching completed record denotes interrupted/unknown completion, never success. Native timings exclude journal serialization/flushing; allocations are thread allocation totals, not peak retained memory.

After each completed case and every export capture, the aggregate writes to `<output>.pending` and replaces `<output>` in the same directory. An interrupted replacement leaves the previous complete aggregate and possibly the pending file. Native samples survive even if subsequent wire execution, assertions, or later cases fail. This does not implement browser calculation resume or change production resource accounting.

## Handoff

Owned source change: `YGOProbabilityCalculatorBlazorTest/Services/ProbabilityCalculator/CardEffectProfileBenchmark.cs` only. Research deliverables: this note, freezer script, frozen fixtures, manifest, cheap samples. No commits made by the fixture worker; lead owns local checkpoint commits. Per updated owner direction, no further remote checkpoint pushes are requested. Raw export and duplicate full measurement files are reproducible and omitted after consolidation.
