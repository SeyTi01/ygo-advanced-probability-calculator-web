# Background calculation verification

The ordinary CI suite runs .NET tests and `node --test`; browser smokes are
explicit local checks. Run the normal solution restore/build/test commands in
the repository instructions first. `owner.test.mjs`, `transport.test.mjs`,
`worker-policy.test.mjs`, and `fast-deadline.test.mjs` use fake workers, explicit
messages and mocked timers for ownership, late responses, deadlines and recovery.
The .NET component/service tests cover captured inputs, cancellation, drafts,
session compatibility, mode routing, progressive fallback and independent
physical-card probability oracles.

## Small published-app smoke set

Publish a clean optimized Release build with SDK-scoped `wasm-tools`, using
the production AOT settings and no external measurement sources. Use the existing external Playwright
installation through `NODE_PATH`. Set `YGO_BROWSER_EXECUTABLE` to an installed
Chromium executable, or optionally set `YGO_BROWSER_CHANNEL`.

```sh
dotnet publish YGOProbabilityCalculatorBlazor/YGOProbabilityCalculatorBlazor.csproj -c Release
node scripts/smoke.cjs YGOProbabilityCalculatorBlazor/bin/Release/net10.0/publish/wwwroot
```

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

For the distinct physical idle-owner lifecycle, run this optional check once;
it intentionally waits for the unchanged production 60-second idle timer:

```sh
node scripts/background-calculation/lifecycle-smoke.cjs <published-wwwroot>
```

It observes worker-context reuse, expiry, recreation, Help deactivation and a
successful calculation after returning to the retained workspace.
Normal smoke cancellation also observes real closure, but held transport is not
evidence of stopping a CPU-bound engine. Do not infer CPU shutdown from a button
returning to idle or a call to `terminate()` alone.

Run `scripts/verify-published-seo.sh` against the published directory as required
by the existing publish verification workflow. Keep this responsibility separate
from browser correctness and do not add expensive smoke matrices to every push.

## Frozen worker regression

`cold-worker-regression.cjs` is a separate correctness gate for issue #295. Run it
against clean non-AOT, retained-IL AOT and stripped AOT publishes; verify the
publish flags/logs separately. It does not build, change production AOT settings,
or run as part of the routine smoke/CI suite.

```sh
node scripts/background-calculation/cold-worker-regression.cjs <published-wwwroot> <frozen-worker-inputs.json> bin/cold-worker-results.json 4
```

The input must be the saved `mixed-vision-custom-fast-40` entry from the original
capture, a raw captured input object, or the `mixed-reproduction.json` wrapper.
The gate requires canonical SHA-256
`73449cb0ea76fca01d2a61959caab7df770001c61f4c3364171b8348b7f91177`;
regenerating `MixedDeferredBenchmark.ExportExamples` is not the same capture.
Keep the input and generated evidence local. An optional fifth argument supplies
a complete reference response JSON; the reproduction wrapper's `reference` is
also accepted automatically. The eleven stored reference fractions are checked
with integer arithmetic, alongside route/proof/completion metadata and row
identities. These stored references are regression expectations, not an
independent probability oracle.

Each of at least four fresh browser contexts creates a fresh production worker
and sends the mixed request as its first calculation, followed by two repeats.
The real page boots as in the original reproduction; its UI runtime is separate
from the worker. The gate rejects any preliminary or additional page worker, and
does not issue a dummy calculation. A failure exits nonzero and retains
raw replies, request hashes and the worker/framework artifact manifest at the
specified Git-ignored output path. Use external Playwright through `NODE_PATH`;
Chromium defaults to Edge. Set `YGO_BROWSER_EXECUTABLE` or
`YGO_BROWSER_CHANNEL` to override it, or `YGO_BROWSER_ENGINE=firefox` to use an
installed Playwright Firefox. Worker-cold state is distinct from a fresh browser
process or cold network cache; this gate makes no performance claims.

## Benchmark and measurement tools

Keep a frozen request and its reference response when comparing revisions. The
native and browser replays use the current public wire, so newer card fields and
execution policies cannot disappear through hand-built model projections.
The historical paired feature matrices, external probes and duplicate timing
wrappers have been retired; Git history preserves those investigations.
Explicit internal phase profiles, the full physical-hand oracle and frozen
public-contract regressions remain separate from the timing tools.

| Purpose | Maintained entrypoint |
| --- | --- |
| Freeze representative current requests / replay natively | `PerformanceAuditBenchmark.Export` / `.Replay` |
| Replay the same requests in optimized WASM | `measure-worker.cjs` |
| Native excavation workloads and frozen requests | `ExcavationBenchmark.Measure` |
| UI click to terminal result, bootstrap and request round trips | `measure-worker-lifecycle.cjs` |
| First catalog query and warm search feedback | `../card-search/measure-search.cjs` |
| File/example action to accepted session snapshot | `../workspace/measure-session-loading.cjs` |

Build the solution in Release before invoking the explicit native benchmarks;
use `--no-build --no-restore` so compilation is outside the measured process.
Set `DOTNET_TieredCompilation=0` consistently for native comparisons. Run timing
jobs alone, without builds, tests or other measurements. First request, warm
requests and worker bootstrap are separate observations; they do not establish
cold process/network behavior. Timeout samples are censored, never successful
calculations or Work/Storage failures. Never commit generated evidence.

Frozen fixture files are JSON arrays of `{ Name, Json, Execution?, ReferenceResponse }`.
`Json` is the exact serialized `CalculationInput` string; keep it byte-for-byte
unchanged across revisions. `Execution` is absent/null for the finite direct
worker path, or `interactive-exact-v1` for the UI's unlimited-work Exact path.
`ReferenceResponse` is the complete baseline response, including typed failures.
Generate references on the baseline; replay never replaces them with candidate
results. Fractions, keys and integer fields must match exactly; the browser replay
allows four machine-epsilon units of relative rounding for noninteger doubles.

To export a baseline, set `YGO_AUDIT_OUTPUT=bin/frozen-inputs.json` and optionally
`YGO_AUDIT_CASE` to a name filter (`tiny` selects the inexpensive controls), then
run the explicit `PerformanceAuditBenchmark.Export` method. For native replay,
set `YGO_AUDIT_INPUT` to that frozen file, `YGO_AUDIT_OUTPUT` to a different file,
and optionally `YGO_AUDIT_TRIALS` (1–30 warm samples), then select `.Replay`.

```sh
dotnet test YGOProbabilityCalculatorBlazor.sln -c Release --no-build --no-restore --filter FullyQualifiedName=YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator.PerformanceAuditBenchmark.Export
dotnet test YGOProbabilityCalculatorBlazor.sln -c Release --no-build --no-restore --filter FullyQualifiedName=YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator.PerformanceAuditBenchmark.Replay
```

```sh
node scripts/background-calculation/measure-worker.cjs <optimized-wwwroot> <fixtures.json> bin/worker-measurements.json 3 60000
node scripts/background-calculation/measure-worker-lifecycle.cjs <optimized-wwwroot> bin/ui-worker.json 3
node scripts/card-search/measure-search.cjs <optimized-wwwroot> bin/search.json 3
node scripts/workspace/measure-session-loading.cjs <optimized-wwwroot> bin/session-loading 5
```

Use the external Playwright installation and browser settings from the smoke
instructions. Replay artifacts include exact request and published-asset hashes,
first/warm samples, full replies and parity failures. UI tools record their own
metric definitions, cache conditions, browser and failures. Search uses full-query
input events and DOM/double-frame observations; `YGO_SEARCH_CPU_RATES=1,4` selects
the optional Chromium CPU-throttle rates. Session loading keeps its deliberate
gzip/Brotli server and measures acceptance before recovery-storage debounce.
Double animation frames are scheduling observations, not paint guarantees.
Allocation/working-set observations are not peak live browser memory, and WASM
linear-memory capacity is not live managed heap.

### Excavation workload measurements

`ExcavationBenchmark.Measure` is an explicit Release test. Set
`YGO_EXCAVATION_BENCHMARK` to a Git-ignored output JSON path; optionally set
`YGO_EXCAVATION_TRIALS` (warm samples, default 3), `YGO_EXCAVATION_CASE`,
`YGO_EXCAVATION_EXCLUDE`, and `YGO_EXCAVATION_MODE`. Use
`DOTNET_TieredCompilation=0`. The export includes the exact worker input and full
native reference response, and saves after each fixture/mode. Native first/warm
timings and allocations are printed; use the generic native replay for structured
sample evidence.

The native export uses the common frozen fixture format above. Pass it to
`measure-worker.cjs` against the clean production publish; no probe build is
required. Its optional external cutoff is not a production calculation deadline.
The routine integration smoke uses a small independent excavation control for
browser correctness; the 40-card measurement fixture is not a UI requirement.

#### Historical observations

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
