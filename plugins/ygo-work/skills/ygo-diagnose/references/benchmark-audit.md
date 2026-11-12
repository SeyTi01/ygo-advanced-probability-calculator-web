# Benchmark audit procedure

Use the existing tools below for the selected measurement; internal phase profiles, the full physical-hand oracle and frozen public-contract regressions remain separate from timing tools. Historical paired feature matrices, external probes and duplicate wrappers were retired; recover historical evidence from Git rather than restoring obsolete harnesses by default.

| Purpose | Maintained entry point (repository-relative) |
| --- | --- |
| Freeze current requests / replay natively | `YGOProbabilityCalculatorBlazorTest/Services/ProbabilityCalculator/PerformanceAuditBenchmark.cs`: `Export` / `Replay` |
| Replay the same requests in optimized WASM | `scripts/background-calculation/measure-worker.cjs` |
| Native excavation workloads and frozen requests | `YGOProbabilityCalculatorBlazorTest/Services/ProbabilityCalculator/ExcavationBenchmark.cs`: `Measure` |
| UI click to terminal result, bootstrap and request round trips | `scripts/background-calculation/measure-worker-lifecycle.cjs` |
| First catalog query and warm search feedback | `scripts/card-search/measure-search.cjs` |
| File/example action to accepted session snapshot | `scripts/workspace/measure-session-loading.cjs` |

## Prepare the selected host

Use [environment preparation](../../ygo-verify/references/environment.md) once, reusing initialized Codex tools or preparing the web/cloud host as applicable. Run commands from the checkout root. Set environment variables with the active shell (`$env:NAME = 'value'` in PowerShell; `export NAME='value'` in a POSIX shell). Prefer absolute paths to Git-ignored evidence files when comparing checkouts.

Build the solution in Release before invoking explicit native benchmarks. Use `--no-build --no-restore` to keep compilation outside the measured process, and set `DOTNET_TieredCompilation=0` consistently for native comparisons. Run timing jobs alone, without builds, tests or other measurements.

For browser measurements, use [optimized publishing and browser setup](../../ygo-verify/references/commands.md#browser-and-publishing). Its repository smoke guide owns the external Playwright installation and browser settings. Use a clean production publish without temporary probes; a native run does not substitute for an unavailable browser runtime.

## Freeze and replay the public input

Keep a frozen request and its baseline response across revisions. Native and browser replay use the current public wire, so hand-built model projections cannot silently drop newer card fields or execution policies.

Frozen files are JSON arrays of `{ Name, Json, Execution?, ReferenceResponse }`:

- `Json` is the exact serialized `CalculationInput` string; keep it byte-for-byte unchanged across revisions.
- `Execution` is absent/null for the finite direct worker path, or `interactive-exact-v1` for the UI's unlimited-work Exact path.
- `ReferenceResponse` is the complete baseline response, including typed failures. Generate references on the baseline; never replace them with candidate results during replay.

Native replay compares the whole response. Browser replay requires exact fractions, keys and integer fields; noninteger doubles permit four machine-epsilon units of relative rounding.

On the baseline, set `YGO_AUDIT_OUTPUT` to the frozen file and optionally `YGO_AUDIT_CASE` to a name filter (`tiny` selects inexpensive controls). Export once:

```sh
dotnet test YGOProbabilityCalculatorBlazor.sln -c Release --no-build --no-restore --filter FullyQualifiedName=YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator.PerformanceAuditBenchmark.Export
```

Replay that same file on baseline and candidate. Set `YGO_AUDIT_INPUT` to it, `YGO_AUDIT_OUTPUT` to a distinct output file for each revision, and optionally `YGO_AUDIT_TRIALS` (1–30 warm samples; default 3):

```sh
dotnet test YGOProbabilityCalculatorBlazor.sln -c Release --no-build --no-restore --filter FullyQualifiedName=YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator.PerformanceAuditBenchmark.Replay
node scripts/background-calculation/measure-worker.cjs <optimized-wwwroot> <fixtures.json> <worker-output.json> 3 60000
```

Replay artifacts record request hashes, native assembly or published-asset identity, first/warm samples, full replies and parity failures. Keep baseline and candidate artifacts separate. The browser command's last argument is an external cutoff in milliseconds, not a production calculation deadline.

## Measure UI lifecycle, search or session loading

Run only the tool relevant to the question, with distinct outputs per revision:

```sh
node scripts/background-calculation/measure-worker-lifecycle.cjs <optimized-wwwroot> <ui-worker.json> 3
node scripts/card-search/measure-search.cjs <optimized-wwwroot> <search.json> 3
node scripts/workspace/measure-session-loading.cjs <optimized-wwwroot> <session-output-directory> 5
```

These tools record their metric definitions, cache conditions, browser and failures. Search measures full-query input events and DOM/double-frame observations. `YGO_SEARCH_CPU_RATES` selects Chromium CPU throttle rates (default `1,4`; rates from 1 to 20). Session loading intentionally serves gzip/Brotli and measures acceptance before recovery-storage debounce.

## Excavation workloads

Set `YGO_EXCAVATION_BENCHMARK` to an ignored output JSON path. Optionally set `YGO_EXCAVATION_TRIALS` (1–20 warm samples; default 3), `YGO_EXCAVATION_CASE`, `YGO_EXCAVATION_EXCLUDE`, and `YGO_EXCAVATION_MODE` (`Fast` or `Exact`). With the native preparation above:

```sh
dotnet test YGOProbabilityCalculatorBlazor.sln -c Release --no-build --no-restore --filter FullyQualifiedName=YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator.ExcavationBenchmark.Measure
```

This saves the common frozen format, including exact worker input and full native reference response, after each fixture/mode. First/warm timings and allocations are printed; use the generic native replay for structured samples, and `measure-worker.cjs` for the same input on a clean production publish. The routine integration smoke uses a small independent excavation control; the 40-card measurement fixture is not a UI requirement. Historical acceptance results remain in `repo:scripts/background-calculation/README.md#historical-excavation-observations`.

## Interpret the samples

First request, warm requests and worker bootstrap are separate observations; they do not establish cold process/network behavior. Timeouts are censored samples, never successful calculations or Work/Storage failures. Double animation frames are scheduling observations, not paint guarantees. Allocations and working-set observations are not peak live browser memory; WASM linear-memory capacity is not live managed heap.
