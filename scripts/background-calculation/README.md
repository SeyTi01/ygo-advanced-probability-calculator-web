# Background calculation verification

The ordinary CI suite runs .NET tests and `node --test`. Browser smokes are separate checks against a published application, using the commands below.
`owner.test.mjs`, `transport.test.mjs`,
`worker-policy.test.mjs`, and `fast-deadline.test.mjs` use fake workers, explicit
messages and mocked timers for ownership, late responses, deadlines and recovery.
The .NET component/service tests cover captured inputs, cancellation, drafts,
session compatibility, mode routing, progressive fallback and independent
physical-card probability oracles.

## Publish and browser setup

Use Node.js, a .NET 10 SDK, and the SDK-scoped `wasm-tools` workload. From the repository root:

```sh
dotnet workload install wasm-tools
dotnet publish YGOProbabilityCalculatorBlazor/YGOProbabilityCalculatorBlazor.csproj -c Release
```

Install the workload only if it is missing for the selected SDK. The project enables AOT and stripped IL in Release. Use a clean output from the revision being checked; Debug output and builds containing temporary measurement probes are different artifacts. The default `<published-wwwroot>` is `YGOProbabilityCalculatorBlazor/bin/Release/net10.0/publish/wwwroot`; use the actual output location if publishing elsewhere.

Browser scripts load Playwright through Node's module lookup. Reuse an external installation, or run `npm install playwright` and `npx playwright install chromium` in a tools directory outside this checkout. Set `NODE_PATH` to that directory's `node_modules` before returning here. For example, use `export NODE_PATH=/absolute/tools/node_modules` in a POSIX shell or `$env:NODE_PATH = 'C:\tools\node_modules'` in PowerShell.

The normal smoke/replay helpers use Playwright's installed Chromium by default. Set `YGO_BROWSER_EXECUTABLE` to an installed Chromium executable, or `YGO_BROWSER_CHANNEL` to an installed channel such as `msedge`, when using a different installation. The frozen worker regression has its own Edge default, documented below. Each script serves the supplied files on loopback; no separate preview server is required.

## Small published-app smoke set

Run from the repository root after the setup above:

```sh
node scripts/smoke.cjs <published-wwwroot>
```

For a focused check, invoke a child script listed below with the same `<published-wwwroot>` argument. The optional idle-owner check is `node scripts/background-calculation/lifecycle-smoke.cjs <published-wwwroot>`; it waits for the real 60-second idle timer.

The serial entrypoint fails on the first failed child and runs:

- `integration-smoke.cjs`: actual production worker bootstrap, optimized WASM,
  request decoding, all four UI modes and rational total/combo/group binding.
  A separate inspection rejects benchmark probes in the published worker exports.
  Small independently enumerated controls distinguish dealt-hand `2/5`, Fast
  primary `5/6`, and physical Exact `23/30`. It checks usable controls, keyboard
  evidence disclosure, pins and copied fractions at representative desktop/mobile
  sizes. Ordinary completion never requires a spinner, Cancel or Preliminary frame.
  Explicit outgoing transport gates separately exercise pending Exact/Progressive
  success, cancellation, editing and failure; upgrade checks run when the real
  Fast result needs an upgrade. A six-copy excavation control also verifies the
  optimized wire, simulation toggle and saved-session reload without benchmark
  exports. Current and pinned rows must remain readable on desktop/mobile.
  Gates never fabricate probabilities;
  real worker close events confirm retired execution contexts. These gated cases
  establish lifecycle behavior, **not natural CPU responsiveness or latency**.
- `../workspace/browser-smoke.cjs`: real keyboard edit/cancel/focus, move validation,
  category draft preservation, deletion dismissal, and download/upload round trip.
  Desktop and one narrow layout catch unusable controls/overflow. Precise spacing,
  incidental markup and exhaustive theme/viewport matrices are manual UI review.
- `../workspace/session-loading-smoke.cjs`: a controlled Blob-read handshake proves
  accessible pending status and delayed replacement through the actual browser
  InputFile boundary. Immediate completion and supersession stay in bUnit.
- `../localization/browser-smoke.cjs`: production satellite and on-demand card-name
  loading, numeric culture and real language reload/restoration. It uses the same
  production publish; pseudo-resource validation stays in the regular tests.

The optional `lifecycle-smoke.cjs` observes worker-context reuse, expiry,
recreation, Help deactivation and a
successful calculation after returning to the retained workspace.
Normal smoke cancellation also observes real closure, but held transport is not
evidence of stopping a CPU-bound engine. Do not infer CPU shutdown from a button
returning to idle or a call to `terminate()` alone.

## Published HTML checks

After the normal [solution build](../../README.md#documentation-and-development), set `YGO_PUBLISH_ROOT` to the absolute published `wwwroot` path in the active shell (`export YGO_PUBLISH_ROOT=...` in POSIX, `$env:YGO_PUBLISH_ROOT = '...'` in PowerShell), then run:

```sh
dotnet test YGOProbabilityCalculatorBlazorTest/YGOProbabilityCalculatorBlazorTest.csproj --no-build --no-restore --filter FullyQualifiedName~PublishedOutputSeoTest
```

The POSIX wrapper is `bash scripts/verify-published-seo.sh <published-wwwroot>`. Optional HTTP checks use `YGO_SEO_BASE_URL`, or the wrapper's second argument, to identify a local Pages server or matching preview. Static HTML checks require no browser installation.

## Frozen worker regression

`cold-worker-regression.cjs` is the separate correctness gate for issue #295. It does not publish the app. Prepare separate clean outputs for these opt-in configurations using `dotnet publish YGOProbabilityCalculatorBlazor/YGOProbabilityCalculatorBlazor.csproj -c Release -o <distinct-output>` and the indicated MSBuild properties:

| Configuration | Properties |
| --- | --- |
| Non-AOT | `-p:RunAOTCompilation=false` |
| AOT with retained IL | `-p:RunAOTCompilation=true -p:WasmStripILAfterAOT=false` |
| Production AOT with stripped IL | `-p:RunAOTCompilation=true -p:WasmStripILAfterAOT=true` |

Verify the actual flags/logs, then run once for each output:

```sh
node scripts/background-calculation/cold-worker-regression.cjs <published-wwwroot> <frozen-worker-inputs.json> bin/<distinct-results.json> 4
```

The results path must be Git-ignored in this checkout and must not overwrite the input. An optional fifth argument supplies a complete reference response; the reproduction wrapper's `reference` is accepted automatically. Chromium defaults to the installed `msedge` channel for this script. Override it with `YGO_BROWSER_EXECUTABLE` or `YGO_BROWSER_CHANNEL`, or set `YGO_BROWSER_ENGINE=firefox` for an installed Playwright Firefox. This matrix is not part of the routine smoke/CI set.

The input must be the saved `mixed-vision-custom-fast-40` entry from the original
capture, a raw captured input object, or the `mixed-reproduction.json` wrapper.
The gate requires canonical SHA-256
`73449cb0ea76fca01d2a61959caab7df770001c61f4c3364171b8348b7f91177`;
regenerating `MixedDeferredBenchmark.ExportExamples` is not the same capture.
The eleven stored reference fractions are checked
with integer arithmetic, alongside route/proof/completion metadata and row
identities. These stored references are regression expectations, not an
independent probability oracle.

Each of at least four fresh browser contexts creates a fresh production worker
and sends the mixed request as its first calculation, followed by two repeats.
The real page boots as in the original reproduction; its UI runtime is separate
from the worker. The gate rejects any preliminary or additional page worker, and
does not issue a dummy calculation. A failure exits nonzero and retains
raw replies, request hashes and the worker/framework artifact manifest at the
specified output path. Its Chromium default is Edge; browser selection is
configurable. This gate establishes correctness, not performance.

## Benchmark and measurement tools

Compare baseline and candidate on the same host/runtime, build configuration, inputs, cache/worker state and harness. Record revisions and keep output paths separate. Run timing jobs without concurrent builds, tests or other measurements. Compare fractions, accuracy/scope and terminal outcomes before interpreting speed. First requests, warm requests, worker bootstrap, allocations and cancellation latency measure different things; timeouts are censored observations, not successful calculations or Work/Storage failures.

### Frozen export and native replay

Build the solution in Release before timing, and set `DOTNET_TieredCompilation=0` consistently on both revisions. Commands below run from the checkout root. Export baseline fixtures by setting `YGO_AUDIT_OUTPUT` to a new JSON file, optionally `YGO_AUDIT_CASE` to a name substring (`tiny` selects inexpensive controls), then run:

```sh
dotnet build YGOProbabilityCalculatorBlazor.sln -c Release
dotnet test YGOProbabilityCalculatorBlazor.sln -c Release --no-build --no-restore --filter FullyQualifiedName=YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator.PerformanceAuditBenchmark.Export
```

Replay the unchanged baseline file by setting `YGO_AUDIT_INPUT` to it and `YGO_AUDIT_OUTPUT` to a different output for each revision. `YGO_AUDIT_TRIALS` selects 1–30 warm samples (default 3):

```sh
dotnet test YGOProbabilityCalculatorBlazor.sln -c Release --no-build --no-restore --filter FullyQualifiedName=YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator.PerformanceAuditBenchmark.Replay
```

The frozen file is a JSON array of `{ Name, Json, Execution?, ReferenceResponse }`. `Json` is the exact serialized production `CalculationInput`; preserve its bytes rather than constructing a reduced model. `Execution` is absent/null for the finite direct-worker path, or `interactive-exact-v1` for the UI's unlimited-work Exact path. `ReferenceResponse` is the complete baseline response, including typed failures; candidate replay must not replace it.

### Browser replay and UI timing

Using the browser setup above, replay that same frozen file against each revision's clean optimized output:

```sh
node scripts/background-calculation/measure-worker.cjs <published-wwwroot> <fixtures.json> <worker-output.json> 3 60000
```

The final arguments select warm samples (default 3) and an external cutoff in milliseconds (default 60000), not a production calculation deadline. Native replay compares the complete response; browser replay requires exact fractions, keys and integer fields, allowing four machine-epsilon units of relative rounding for noninteger doubles. Outputs record request hashes, native assembly/published-asset identity, replies and parity failures. Native timing does not substitute for optimized browser timing.

| UI measurement | Command |
| --- | --- |
| Click to terminal result, bootstrap and request round trips | `node scripts/background-calculation/measure-worker-lifecycle.cjs <published-wwwroot> <ui-worker.json> 3` |
| First catalog query and warm search feedback | `node scripts/card-search/measure-search.cjs <published-wwwroot> <search.json> 3` |
| File/example action to accepted session | `node scripts/workspace/measure-session-loading.cjs <published-wwwroot> <session-output-directory> 5` |

`YGO_SEARCH_CPU_RATES` selects Chromium CPU throttle rates (default `1,4`; each 1–20). Search measures full-query input and DOM/double-frame observations, which do not guarantee paint. Session loading serves gzip/Brotli and measures acceptance before recovery-storage debounce. Tools record metric definitions, browser/cache conditions and failures.

### Excavation and Storage corpora

For excavation export, use the native Release setup above and set `YGO_EXCAVATION_BENCHMARK` to an output JSON path. Optional controls are `YGO_EXCAVATION_TRIALS` (1–20 warm samples; default 3), `YGO_EXCAVATION_CASE`, `YGO_EXCAVATION_EXCLUDE`, and `YGO_EXCAVATION_MODE` (`Fast` or `Exact`):

```sh
dotnet test YGOProbabilityCalculatorBlazor.sln -c Release --no-build --no-restore --filter FullyQualifiedName=YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator.ExcavationBenchmark.Measure
```

This exports exact worker inputs and full native reference responses for replay. First/warm timings and allocations are printed. The routine browser smoke instead uses a small independent excavation control; a 40-card timing fixture is not a UI requirement.

The opt-in `StorageTierBenchmark.Export` test captures the Storage comparison
corpus through the production wire. Set `YGO_AUDIT_OUTPUT` to a new file and
optionally `YGO_AUDIT_CASE` to a workload-name substring. `ExportRequests` lists
the inputs without executing them. Names use `<workload>/<tier>/<Fast|Exact>`;
Fast uses interactive finite Work and Exact uses `interactive-exact-v1`.
Replay frozen responses with `PerformanceAuditBenchmark.Replay` and
`measure-worker.cjs`. Interrupted exports retain completed cases in a
`.partial.json` file, not under the completed output name.

`measure-storage-progressive.cjs <wwwroot> <fixtures.json> <output.json> [warm=3]
[cutoff-ms=60000]` pairs those Fast/Exact captures and exercises production staged
worker leases. It measures first and terminal delivery separately from bootstrap,
not UI rendering or cold network loading. `YGO_STORAGE_CASE` selects a name
substring. External cutoffs are censored measurements, not engine failures.

The library's [StorageTierProfile](https://github.com/SeyTi01/card-probability-engine/blob/v1.0.0/tests/CardProbability.EngineTests/StorageTierProfile.cs)
collects cache diagnostics separately from timings. Run its
`CardProbability.EngineTests.StorageTierProfile.Profile` test in the library
repository; the neutral test project is no longer part of the calculator solution.
`ProfilePublic` records public terminal results and
allocations without diagnostic maps; `ProfileOrderedProjection` explicitly forces
an internal projection and is not evidence for production solver routing.
Configure `CARD_STORAGE_PROFILE_CASE`, `CARD_STORAGE_PROFILE_TIERS`,
`CARD_STORAGE_PROFILE_MODES` and `CARD_STORAGE_PROFILE_OUTPUT` as needed. These
diagnostic fixtures are not all identical to the wire corpus; compare matching
inputs before attributing timing differences to their counters. Per-structure
cell peaks and allocation totals do not measure peak live browser memory.

### Historical excavation observations

These observations describe the 2026-11-01 acceptance harness and its fixtures.
Measured on Windows with .NET 10.0.401 and Edge 156.0.4314.58 on 2026-11-01,
using a clean Release AOT publish with stripped IL. Native and browser jobs ran
separately from builds and correctness tests. Each warm value below is the median
of three measurements; the browser range covers Fast and Exact.

| Fixture | Native warm | Browser warm |
| --- | ---: | ---: |
| 40-card Duality / 40–60-card Prosperity, seven combos | 0.4–0.6 ms | 4.4–6.0 ms |
| 40-card excavation plus two search sources | 1.7–2.5 ms | 9.2–12.4 ms |
| 40-card excavation, draw-two and searches | 7.8–13.1 ms | 38.7–59.2 ms |
| 60-card mixed Pots, two searches, 15 overlapping combos | 235–239 ms | 909–918 ms |
| 40-card Duality, banish-ten/draw-two and searches, Exact | 4.05 s | 13.90 s |

Every completed browser response matched the native exact total/combo/group
fractions. In the final hidden-loss fixture, Fast reached its unchanged 150M Work
limit (about 2.13 seconds for the first browser calculation); it returned no
probability. Ordinary fixtures had roughly 0.20–0.29-second first calculations,
plus a separately measured 0.20–0.24-second worker bootstrap.

Production assessment: viable for the documented single-excavation model and
ordinary workloads; detailed hidden-loss mixtures remain expensive and may need
Exact. These desktop measurements do not establish mobile CPU performance or a
universal completion guarantee. The fixed phase/source policy and single-activation
restriction remain semantic limitations regardless of the reported accuracy.

Historical feature acceptance scripts, old copy/layout comparisons, removed
certification-toggle experiments, and source-rewriting physical-enumeration
variants were deleted. Git history preserves those one-time investigations.
The benchmark-fed bounded-search and excavation UI smokes were also retired:
regular tests, the small production smoke and retained research replayers cover
their reusable contracts without preserving old measurement file shapes.

## Runtime contracts

The module worker boots the app's published `_framework/dotnet.js` and assembly
without starting Blazor's UI entrypoint. Deploy all published assets together.
Each component owns one lazily created runtime and one request lease. Accepted
success reuses the runtime; cancellation, invalid results, transport failures,
limits and disposal retire it. Idle expiry is 60 seconds; continuous use recycles
after 20 accepted requests. Initialization has its own 30-second timeout.

Progressive presents completed Fast and attempts physical Exact on the same
captured input/lease. Interactive Exact and Progressive's upgrade use unlimited
Work with the selected Storage limit. Failed/cancelled upgrades keep accepted
Fast. Fast only and no-effect UI requests retain their finite 150M Work policy;
bounded synchronous callers retain their existing default allowance. The legacy
`run(json, true)` path alone retains its seven-second opportunity/five-second
upgrade window. Session plans stay authoritative; no test harness should refresh
saved plans implicitly or change production resource policies to stay green.
