# Background calculation runtime

## Bounded-search performance comparison

`BoundedSearchBenchmark.Export` is an explicit test that writes the complete
catalog-backed 40-card workspace matrix to `YGO_BOUNDED_FIXTURES`. Export once
and record its SHA-256; use that exact file for both revisions. It includes
Fast/Exact, 5/6-card hands, grouped/distinct fillers, removal/mode allowances,
hidden target loss, simple-search/no-search controls, and ordered #219 references.
The matrix uses Interactive Work and Standard Storage. Keep higher-budget
reference runs separate and labeled.

Build a frozen baseline in a separate checkout. Both measurement builds must
externally include `bounded-search-benchmark-probe.cs` in the app project and
define `ADAPTIVE_FAST`. This probe preserves #221's numeric activation limits
and minimum hand size; older probes do not. Never include it in a production
publish. Use the same SDK and Release settings on both sides, and publish with
SDK-scoped `wasm-tools` (normal optimization, no AOT).

Compile `paired-bounded-search.cs` in a temporary dependency-free `net10.0`
console project with implicit usings and nullable enabled. Run with
`DOTNET_TieredCompilation=0`, passing baseline **test output** directory,
candidate test output directory, fixture JSON, and output JSON. Test output
avoids loading browser-specific framework assemblies into the native runner.
An optional fifth argument requests a diagnostic number of samples without
warmup; omit it for the paired comparison (two warmups, ten samples).

Run `node paired-bounded-search.cjs <baseline-wwwroot> <candidate-wwwroot>
<fixtures.json> <output.json>` with Playwright available through `NODE_PATH`
and, optionally, `YGO_BROWSER_EXECUTABLE`. This uses two separate optimized WASM
module workers, randomized interleaved requests, two warmups and ten samples.
Run each matrix alone. Both runners preserve first observations and all raw
samples, exact rows, full accuracy/certificate contracts, typed limits, Work,
checked entries/cells, and allocated bytes. The WASM runner separately records
bootstrap time and linear-memory capacity. Managed-heap readings are estimates
after each request, not peak live bytes; checked storage is a per-structure
payload bound, not a process-memory bound. Timings cover the complete engine
and certificate, excluding input decoding and worker transport.

After a separate clean production publish, run `bounded-search-wire-smoke.cjs
<wwwroot> <fixtures.json> <native-results.json> <output.json>` to compare every
shipped worker response with the measured candidate, verify typed failures have
no partial fractions, and check that no measurement probe is exported.

The .NET 10 app boots a separate runtime in a dedicated module Web Worker, using
the same published `_framework/dotnet.js`, fingerprinted runtime assets, and app
assembly as the UI. The worker calls the existing C# engine through a trimmed,
source-generated JSON boundary; it never starts the Blazor UI entry point.
No second engine, worker build project, additional package, calculation server,
SharedArrayBuffer, COOP, or COEP headers are required. The normal `dotnet run`
and `dotnet publish -c Release` commands include the worker assets. Serve `.js`
as JavaScript and `.wasm` as WebAssembly, as for the existing app. Deploy the
entire published output together; do not mix runtime assets from different builds.

The browser must support module workers and the app's .NET WebAssembly runtime.
Startup/transport failures are shown in the existing calculation alert, with no
foreground fallback. Worker initialization times out after 30 seconds; this is
not a running-time deadline. Ordinary Calculate captures a request-local
**150,000,000-unit** work allowance immediately. Synchronous engine callers omitting
the explicit `CalculationWorkPolicy` overload and wire requests omitting
`WorkUnits` retain the bounded **10,000,000-unit** default. Policies are immutable,
positive `long` values; invalid input cannot select unlimited work. There is no
retry, budget UI or persisted preference. Weighted units are not elapsed seconds.
Results retain primary doubles and add a versioned immutable accuracy contract with rational total/combo/group rows. Each request captures its
own input JSON, excluding inactive
entries and including nested requirements, stable identities and group order.

Both effect modes execute each card's saved effective plan, including
custom zero-valued costs or removals. `CalculationOptions` carries activation priority and the requested OpeningHand, Fast or Exact mode. The old native certification flag is a compatibility adapter only; Capture strips it and the worker rejects it. Its version-local worker JSON rejects obsolete/unknown options as typed
input errors; schema v8 persists one explicit mode while retaining schema-7 grouped selectors and objective `LevelAtLeast` predicates. Schema-6 plans retain their exact saved operations and
provenance; loading never restores maintained definitions. Opening-hand-only snapshots clear
draw and search execution fields on their owned worker cards while preserving the workspace plans.

`search-effects-smoke.cjs <published-wwwroot> <evidence-directory>` checks the
search example, editable grouped eligibility, numeric validation, optional configuration, repeated session loads,
opening-only transport, cancellation/stale delivery, hidden-removal diagnostics,
and desktop/mobile target UI. `SearchFeatureBenchmark.Measure` exports frozen
worker inputs and reference rows to `YGO_SEARCH_BENCHMARK`; set
`YGO_SEARCH_SAMPLE` to include the refreshed bundled example. Existing no-search
acceptance fixtures retain the schema-5 example as a frozen test input.

`custom-plan-smoke.cjs <published-wwwroot> <frozen-cases.json> <evidence-directory>`
checks both global models and selective Trade-In, Allure and Desires overrides,
cancel/save, independent same-name entries, pins, restore, focus and narrow layouts.
Supply frozen exports containing maintained card sessions, such as the mixed and
Trade-In/Allure fixtures below. `YGO_BROWSER_CHANNEL=msedge` selects installed Edge.

Each calculator component receives a transient `BackgroundCalculator` owner.
The first request lazily imports the module and creates a runtime; successful
requests reuse that runtime with distinct IDs. C# validates the wire result before
acknowledging the lease. Cancellation or handle-cleanup failure after acknowledgement
uses the released request ID to discard only that lease, never a newer one. Request handles and the temporary module handle are
released separately from the physical Worker. Neither snapshots nor results are
cached. Duplicate/concurrent callers are rejected while a lease is busy; nothing
queues behind synchronous engine work.

At most one idle runtime is retained per component. It expires after **60 seconds
idle** and is recycled after **20 successful requests**, even under continuous use.
There is no prewarming. Work/Storage limits, input errors, invalid responses and
transport faults all retire the runtime immediately, avoiding retention of a heap
potentially enlarged by a failed request. Successful requests can also grow the
WASM heap; idle expiry/recycling bounds retention, not peak bytes or per-request
allocation. These lifecycle limits do not alter engine resource budgets.

Cancel, input changes, replacement and component disposal revoke active request
ownership and terminate that request's worker. A cancelled worker is never reused;
the next request starts a fresh runtime. Edits between completed requests may reuse
the idle runtime because it owns no request data. Component `DisposeAsync` explicitly
terminates idle or busy workers and releases the owner handle on navigation, rather
than waiting for the browser-long DI scope to end. Initialization still cleans up
late handles during cancellation or teardown.
The cancellation signal is sent from the UI thread, not queued behind the
worker's synchronous computation. Browser termination is asynchronous:
[Chromium schedules forcible termination after a two-second grace period](https://github.com/chromium/chromium/blob/main/third_party/blink/renderer/core/workers/worker_thread.cc).
The UI clears its busy state immediately; a terminated Chromium worker can still
consume CPU during that grace period. Firefox's observed shutdown was faster.
Do not treat the button state or an animation as evidence that CPU work stopped.
Rapid restarts can temporarily overlap workers during that grace period; the
opt-in policy smoke verifies that a finite burst closes every execution context.

Work exhaustion and storage/state growth have distinct typed reasons in the
source-generated worker response (`LimitReason`), preserved by the caller's
`ProbabilityCalculationLimitException.Reason`. Input/compatibility errors use a
separate `FailureKind`; startup/transport errors remain JS failures. Unknown or
contradictory responses fail closed. Normal cancellation remains silent.

The **32,768-entry / 262,144-cell** storage thresholds and **30-combo** ceiling
are unchanged. Binomial rows and DP maps now also count retained BigInteger
payload in 32-bit cells; a large integer denominator has the same cell check,
and cache retention checks integer payload separately. Count vectors, transient
clones, old/new DP maps, Hall snapshots, union/change maps, cache and integer
temporaries can coexist. Object overhead and input JSON are additional. These
checks are per structure, not a total-byte ceiling. Inclusion-exclusion
coefficients stay within the 30-combo `long` boundary; deck totals use checked
arithmetic, and binomial array lengths are checked in `long` before allocation.

The earlier 50-million allowance was measured against deterministic 60-card sessions with two copies
per disjoint role and one `1/1` route per role. Independent complement counting
checks exact results. Measured charged-work brackets were 11.52–11.63 million
for 20 routes / five cards, 19.53–19.63 million for 18 routes / six cards, and
43.84–43.95 million for the 15-route / 15-card stress case. The complete bundled
40-card / five-card / eight-combo example with six nonparticipating copies replaced
by three maintained Trade-In and three Allure charges **85,695,137** units in
encounter order and **78,570,835** with Allure-first priority. Both finish on
**Standard**, with peak checked frontier payloads of **161,669** and **147,720**
cells. Storage thresholds and the work allowance are unchanged. Larger inputs
can still reach independent Work or Storage limits.

Large ordered frontiers whose counts and source indices fit in one byte retain
four values per integer, plus a full-width vector-length word. This is a lossless
encoding of the entire original state, including FIFO order and trailing zeros;
it introduces no new equivalence between play histories. Packing starts after a
layer reaches 65,536 cells. Small frontiers and the count-state/exchangeable fast
paths keep their original representation. Each map uses one encoding, and only
one parent is decoded at a time. Checks count the actual retained integer array
and exact weight payload. There are no partitions, persistent decoded caches,
or changes to the N! weights or terminal counting. Retained memory decreases at
the cost of additional transient allocations; this is not a universal speedup.
The native opt-in benchmark reports elapsed time, cumulative
allocations and sampled heap/working set; cumulative allocations are not peak
live memory. Browser timing and shutdown evidence belong in the PR discussion.

The six-copy fixture removes 3 Fuwalos, 1 Maxx "C", 1 Triple Tactics Talent and
1 Snow Devil, after checking all original active direct/property requirements.
`TradeInAllureBenchmark.Export` exports its native session and wire request to
`YGO_AUDIT_INPUT`; `YGO_ACCEPTANCE_WORK`, `YGO_ACCEPTANCE_TIER` and
`YGO_ACCEPTANCE_CUSTOM=1` select explicit diagnostic inputs. The export defaults
to the historical 50M comparison. Run `MixedDeferredBenchmark.Replay` with
`YGO_AUDIT_INPUT` and `YGO_AUDIT_OUTPUT` to freeze all exact/public rows and
measure four native Release calls (discard the first for warm measurements).
`YGO_FRONTIER_WIDE_REFERENCE=1` retains the original frontier representation for
paired comparisons. `YGO_FRONTIER_MEMORY=1` separately samples the whole test
process's managed heap after full GC and resident working set at completed layer
boundaries while both layers are live. Those sampled maxima are not exact process
peaks, and those timings must not be used as ordinary runtime measurements.
`PeakConcurrentCells` sums the retained old/new/terminal map payloads at completed
layer boundaries; individual `CheckStorage` protections remain separate.
Use `--blame-hang-timeout 3m` to bound each opt-in test process. For normal-policy
verification, export both priorities with 150M and Standard/High, combine their completed
replay records, then run:

```sh
node scripts/background-calculation/trade-in-allure-smoke.cjs <publish/wwwroot> <frozen-cases.json> <evidence.json>
```

This smoke checks the actual UI allowance and selected tier, complete native
parity on cold/reused workers, cancellation/immediate restart, edit/session
replacement invalidation, UI heartbeats, runtime heap
size and rendered error state. Its isolated browser has a three-minute deadline;
this does not add a product calculation deadline.

Transport tests run with Node.js 24, without a browser or additional packages:

```sh
node --test scripts/background-calculation/*.test.mjs
dotnet test YGOProbabilityCalculatorBlazor.sln --filter FullyQualifiedName~BackgroundCalculation
```

The opt-in browser smoke requires Playwright and its Chromium/Firefox runtimes.
Run it against a freshly built development server and static published output:

```sh
node scripts/background-calculation/browser-smoke.cjs http://localhost:5157 chromium
node scripts/background-calculation/browser-smoke.cjs http://localhost:5157 firefox
node scripts/background-calculation/work-policy-smoke.cjs http://localhost:5157 chromium
node scripts/background-calculation/work-policy-smoke.cjs http://localhost:5157 firefox 1 wire
node scripts/background-calculation/work-policy-smoke.cjs http://localhost:5157 firefox 1 ui
node scripts/background-calculation/work-policy-smoke.cjs http://localhost:5157 chromium 1 comparison
node scripts/background-calculation/work-policy-smoke.cjs http://localhost:5157 chromium 4
node scripts/background-calculation/measure-work-policy.cjs
```

It uses isolated browser contexts and bounded real engine workloads at the
production limits. It measures foreground event-loop blocking, interactions
during worker computation, worker execution-context destruction and cancellation
latency. The policy smoke compares explicit bounded allowances through actual
workers, validates independent total/group/individual expectations, and checks
long-work cancellation, rapid restart, edits, stored recovery bytes and file
replacement at verified desktop/narrow viewports. Unique saved-session markers
prove asynchronous replacements completed before the next calculation; anonymous
fixture cards avoid metadata-provider requests. Its browser process has a
three-minute external deadline per invocation. Firefox's slower execution uses
separate wire and UI invocations so comparisons cannot consume the UI probe's
deadline; no request timeout or retry is increased. The comparison phase can also
run separately. It checks accepted interactive results, pin/unpin, historical clipboard
and fallback bytes with and without a pin, captured summaries and inline colored
deltas in both themes at desktop and narrow widths. The combined recovery/export
smoke has a five-minute browser deadline. The slowdown argument applies Chromium main-thread
CPU throttling. Worker slowdown is not established by this setting; this tests
slower UI handling rather than a physical low-power device or worker CPU ceiling.
Run CPU/memory probes serially, away from concurrent builds. The native harness
sets `DOTNET_TieredCompilation=0` and terminates its own test
process tree after two minutes.
Neither smoke changes the bundled example or session schema.

### Frozen-input engine audit

The explicit `PerformanceAuditBenchmark.Replay` measures synchronous engine and
result conversion only, with one untimed engine warmup, a successful-result wire
parity call, and seven recorded trials. Set
`DOTNET_TieredCompilation=0`, `YGO_AUDIT_INPUT` to a worker-case JSON array, and
`YGO_AUDIT_OUTPUT` to a new output file. `YGO_AUDIT_TRIALS` accepts 1–30. Run
`node scripts/background-calculation/measure-engine-audit.cjs`; it bounds the
Release test process tree to three minutes, using `--no-build` and filter
`FullyQualifiedName~PerformanceAuditBenchmark`. Build first and run all benchmark
jobs serially, including browser jobs. Timing is never a CI assertion.

To create the representative matrix, first set `YGO_DRAW_BENCHMARK_OUTPUT` to an
empty output directory and run the existing explicit exporters with filter
`FullyQualifiedName~DrawEffectsBenchmark.Export|FullyQualifiedName~SameCardBenchmark.Export`.
Then run:

```sh
node scripts/background-calculation/prepare-audit-cases.cjs <export-directory> <matrix.json>
```

Replay the matrix at the baseline, then select its `Trial == 0` records as the
frozen input array for candidate replay and browser measurements. Keep the
baseline exports: expected exact fractions and complete public result rows must
come from that baseline, not be regenerated from the candidate. New synthetic
inputs initially lack expectations; their first replay freezes the references.
This benchmark parity check supplements independent physical-card oracle tests.

Native allocation counts cover the current thread's synchronous engine call;
they exclude input deserialization, model construction and benchmark output.
Managed heap and process working set are end-of-request observations, not peak
live bytes or per-request memory. Conditional state/cell counters are per
structure and do not represent a process memory cap. Report medians and ranges
for small trial counts rather than unsupported percentile estimates.

Publish with installed SDK-scoped `wasm-tools` and `dotnet publish -c Release`.
Run `measure-draw-effects.cjs <publish/wwwroot> <frozen.json> 3` against that fresh
output. Each trial starts a new worker and records bootstrap, first request and
a second request in the same worker separately, checking both against the native
reference. These request times include serialization/transport.
The worker log's `work` field is frozen native reference metadata; obtain the
candidate's current work from its native replay. Browser assets
can be cached within the context; this is not a cold-network benchmark. This raw-worker harness bypasses the production service owner, so its warm repeat
is diagnostic headroom only. Use the UI lifecycle harness below to measure user
latency and actual reuse.
The per-trial 60-second deadline covers bootstrap and both requests; a timeout is
a censored failure, never a completed measurement. `YGO_BROWSER_CHANNEL=msedge`
selects installed Edge; `YGO_BROWSER_ENGINE=firefox` selects Playwright Firefox.
The heartbeat-gap observation checks the page event loop, not worker shutdown;
use the separate browser/policy smoke for cancellation and UI interaction.

For the focused OR-route comparison, run
`prepare-or-route-cases.cjs <matrix.json> <or-inputs.json>` after the audit matrix
generator. Replay and freeze these inputs on the baseline before implementation.
The 16-group ordinary/Basic/Advanced cases originally return Storage; when testing
exact repeated-group reduction, use the corresponding frozen eight-group exact
fractions and public rows as their success expectations. Keep the original limit
records for before/after reporting. The non-equivalent 16-group and restrictive
union controls must still return Storage. `ComboAlternativeReductionTest.InspectFrozenRouteReduction`
is an explicit, separate diagnostic using `YGO_AUDIT_INPUT`; it reports raw and
retained route products, preprocessing work, and compiled-event counts at the
input hand size. It is not a timing run or a draw-scenario storage measurement.

For a compact Advanced-frontier matrix, run
`prepare-frontier-cases.cjs <matrix.json> <frontier-inputs.json>` after the audit
generator. Replay on the unchanged baseline and freeze `Trial == 0` before any
engine edits. The added Return/Allure cases are role-aggregated models, not
tournament deck lists: 9 Orcust monsters, 6 World Legacy monsters (all 15 modeled
as DARK), 3 Orcust spells, 3 other extenders, 2 OPT Return, 2 non-OPT Allure and
15 unrelated cards. The 60-card variants add 20 unrelated cards; the copy variant
uses 3 Return and 1 Allure. Opening hands are 5 or 6. The matrix includes a Basic
control and overlapping routes with restrictive maxima and same-card AND/OR.
The historical `post-mixed` and hand-7 cases remain synthetic stress controls;
they are not evidence about a representative deck. Existing single-effect,
no-conditional and exchangeable Graceful controls are also retained. Native
replay checks complete frozen public rows as well as exact fractions.

### Production worker lifecycle

```sh
node scripts/background-calculation/measure-worker-lifecycle.cjs <baseline/wwwroot> <before.json> 3
node scripts/background-calculation/measure-worker-lifecycle.cjs <candidate/wwwroot> <after.json> 3 <before.json>
node scripts/background-calculation/lifecycle-smoke.cjs <candidate/wwwroot> <lifecycle.json>
# Optional unchanged baseline (workers terminate after every success):
YGO_EXPECT_REUSE=0 node scripts/background-calculation/lifecycle-smoke.cjs <baseline/wwwroot> <lifecycle-before.json>
```

These opt-in Playwright harnesses serve freshly published output themselves and
use the actual Calculate button, `CalculationSnapshot`, C# service, JS owner, worker,
and rendered results. The measurement defaults to Firefox; set `YGO_BROWSER_ENGINE`
to `chromium` to select Chromium. Use the same browser and build configuration for
before/after runs, serially and away from builds. Each case uses a fresh browser
context and four calculations (hand sizes 5, 6, 5, 5). Timing starts at the button
click and ends when the UI returns to idle with the result; worker bootstrap and
send-to-response times are recorded separately. Assets may be cached within the
context; this is not a cold-network comparison. The frozen baseline is required
for input equality and full public response parity, including every group/combo.
The fixtures cover a 40-card opening control, configured Allure-style post-draw
removal, and Return/Allure pre/post-removal interaction. They aggregate equivalent
roles from the existing frontier matrix (15 eligible DARK/Return monsters, 6 other
engine cards, 2 of each modeled effect and 15 blanks for the mixed case); they are
not tournament deck lists. Session version and effect-count assertions prevent
legacy migrations from silently removing the configured effects.

The lifecycle smoke asserts physical execution-context reuse, cancellation of
heavy synchronous work, fresh-worker restart, real 60-second idle expiry/recreation,
and idle-owner teardown on navigation. It records runtime heap bytes only if the
browser runtime exposes them; a missing observation is not a zero-byte heap.
All timing values are observations, never CI thresholds. Existing browser/policy
smokes still assert zero live workers after cancellation and explicit teardown;
successful sequential UI requests now assert one retained execution context.

### Paired search counting benchmarks

`SearchFeatureBenchmark` exports frozen inputs, sessions, every exact fraction,
work/storage counters, and timings. Set `YGO_SEARCH_SAMPLE` to the appropriate
example session and `YGO_SEARCH_BENCHMARK` to an output JSON path when running
that explicit Release test. Keep the historical ROTA fixture for the old arm.

Compile `paired-search-benchmark.cs` as a temporary `net10.0` console application
with implicit usings and no packages. Its arguments are the three Release test
assembly directories (dev, original PR, candidate), the old/new export JSONs,
and an output JSON. Default: eight warmups, 24 samples, fixed-seed randomized
interleaving and GC outside timing. Run with `DOTNET_TieredCompilation=0` and
without concurrent tests/builds. It compares every result fraction and, for
no-search controls, every work/storage counter. `YGO_SEARCH_SUBSETS=1` selects
all eight subsets of the three maintained searches on the new fixture, comparing
the original PR and candidate without changing the other cards or combos.

`SearchPerformanceProbe.PrepareDiagnostic` is a separate, untimed opt-in probe
for search phase timings, assignment/predicate counts, diagram nodes, and cache
hits. Normal worker/service requests never instantiate it. Search counting
uses no conditional source/target vectors or inclusion-exclusion terms after
compilation; the weighted decision diagram shares exact union subproblems.

For optimized browser timings, `paired-search-workers.cjs` takes the three
published `wwwroot` directories, the old/new export JSONs, and an output path.
It measures a fresh worker's first calculation separately, then four warmups
and 12 interleaved warm samples; optional final arguments change those counts.
It asserts complete worker-response parity. `YGO_SEARCH_SUBSETS=1` enables the
seven proper source subsets; the normal run supplies the all-three comparison
without repeating that slow baseline. `YGO_BROWSER_EXECUTABLE` selects an installed
Chromium executable when needed. Worker startup is reported separately from
calculation time. Do not run native and browser benchmarks concurrently.

Three-mode verification: `three-modes-smoke.cjs <optimized-wwwroot> <modes.json> <evidence-dir>` checks the real worker contract, settings, accepted results, pins/copy, Unsupported, one-shot recovery, repeated loading and light/dark mobile layout. `ThreeModeBenchmark.Export` writes fixtures to `YGO_MODE_FIXTURES`.

For paired engine/acceptance measurements, include `mode-benchmark-probe.cs` with an external MSBuild Compile item in two benchmark-only builds; define `MODE_BASELINE` only for base 7252c7d. No project file or production export is changed. The probe excludes JSON/materialization from timing, records managed allocations (not peak heap), exact internal fractions and Work/Storage counters, and reports Unsupported without exposing a production probability. The baseline bound fixture enables the old certificate; other baseline fixtures measure the old primary. `paired-mode-benchmark.cs` (temporary net10.0 console) and `paired-mode-workers.cjs` each use five warmups and twenty randomized interleaved samples. Run alone. Then publish normally without the external Compile item and run the browser smoke against that normal output.


Physical-survivor verification uses `PhysicalSearchBenchmark.Export` with
`YGO_PHYSICAL_FIXTURES=<output.json>` and the same `three-modes-smoke.cjs` command.
The fixtures include Default/Standard VS/K9 mill-2 and banish-10 Exact requests,
Fast certificates, no-search/opening controls, consumed sources and unsupported
filtered discards. `PhysicalSearchBenchmark.Profile` separately exposes counting
work, storage and diagnostic phases; it is not part of the regular test suite.

For paired measurements against pre-Milestone-3 `dev`, externally include
`physical-search-benchmark-probe.cs` instead of `mode-benchmark-probe.cs` in both
benchmark-only app builds. Define `PHYSICAL_BASELINE` only in the baseline build.
Use `paired-mode-benchmark.cs` and `paired-mode-workers.cjs` with the exported
physical fixtures. Their `Unchanged` flag enables exact parity assertions for all
fractions, outcomes and Work/Storage counters on controls; new Exact cases retain
both the baseline Unsupported outcome and the completed candidate result.
First observed calls and twenty interleaved warm samples are saved separately;
first calls after earlier fixtures share an already-running runtime. Browser
measurements accept `YGO_BROWSER_EXECUTABLE`. Publish normally without the probe
before running the production-worker/UI smoke.

### Adaptive Fast routing

Freeze `FastCertificateBenchmark.Export` on the base revision using
`YGO_FAST_CERTIFICATE_FIXTURES` and `YGO_FAST_COVERAGE_FIXTURES`. Run
`prepare-adaptive-fast-cases.py <fixtures.json> <coverage.json> <matrix.json>`
to add multiplicity, branch-heavy 8/9-entry, alternative/group and reduced-budget
controls. Keep the base responses, including failures, when replaying the candidate.

Externally include `fast-certificate-benchmark-probe.cs` in both measurement
builds; define `ADAPTIVE_FAST` only for the candidate. Compile
`adaptive-fast-benchmark.cs` as a temporary package-free `net10.0` console app.
Its arguments are base test-bin, candidate test-bin, matrix JSON and output JSON.
Run alone with `DOTNET_TieredCompilation=0`. `adaptive-fast-workers.cjs` takes
base optimized `wwwroot`, candidate optimized `wwwroot`, matrix and output JSON.
Both compare full Fast, direct Exact for `Physical` fixtures, and adaptive Fast
with five warmups and twenty randomly interleaved recorded samples. `First`
retains the first observed call separately; later fixtures share a warmed runtime.
They verify all fractions and resource counters against Exact for selected inputs,
and complete result parity against base Fast for unselected inputs. Allocations
are cumulative current-thread managed bytes, not peak live memory. Browser times
exclude transport; resource/contract checks use the same measured completion.
`YGO_BROWSER_EXECUTABLE` selects Chromium; `YGO_BROWSER_SINGLE_PROCESS=1` supports
restricted hosts using headless shell with in-process GPU/no zygote. Report this
configuration explicitly and compare only matched browser configurations.
Publish again without the external probe for production-worker/UI smoke checks.

### Ordered frontier storage pressure

`OrderedStorageBenchmark.Export` writes three synthetic 40-card ordered inputs
and sessions to `YGO_ORDERED_STORAGE_FIXTURES`: ten distinct role classes, an OPT
draw-two/discard-one entry, an unrestricted draw-one/discard-one entry, and one
search source. Variants change source multiplicities or activation priority.
They complement the frozen `vs-k9-ordered-40` in `FastCertificateBenchmark.Export`;
they are not a replacement for that representative example.

Freeze exports and baseline outcomes before edits. Use the external
`fast-certificate-benchmark-probe.cs` in both measurement builds, with
`ADAPTIVE_FAST` defined for both revisions after PR #212. Measure primary and full
certificate calls separately (`MeasureCertificate`), at Default/Interactive Work
and Standard/High/VeryHigh Storage. For a rescued Standard result, compare every
upper fraction against a successful unchanged High reference. Keep failures as
failures when no reference completes. Unchanged controls must also match their
resource counters and complete result contracts.

`OrderedTerminalAggregationTest` forces both historical and immediate terminal
aggregation, compares complete weighted terminal maps, and checks upper/lower
rows against the independent labeled-copy oracle. The internal constructor flag
and terminal observer are diagnostic only; no worker or session option selects
them. Normal execution switches only on layers already using frontier packing.
The existing `ObserveFrontier` callback can sample managed heap while current,
next and terminal maps coexist. `PeakConcurrentCells` sums their checked payloads;
it excludes object overhead and is not a physical memory limit. Keep forced-GC
profiling separate from interleaved native/WASM timing runs.

### Ordered terminal throughput

`OrderedStorageBenchmark.Export` also exports `ordered-storage-eleven`, an
11-role 40-card pressure case. Use `prepare-ordered-throughput.py` with the
frozen Fast, pressure and physical exporter JSON files to produce one paired
matrix; record its SHA-256 before running either revision. Fast requests use
Interactive Work; Exact controls retain the exporter's Default Work. Keep any
additional Interactive Exact diagnostics separately and label their budget.

Include `fast-certificate-benchmark-probe.cs` externally in both measurement
builds and define `ADAPTIVE_FAST` for both post-#212 revisions. Compile
`paired-ordered-throughput.cs` in a temporary package-free `net10.0` console
project. Arguments: baseline test-bin, candidate test-bin, matrix, output JSON.
Run with `DOTNET_TieredCompilation=0`. It measures primary and full certificate
requests separately, retaining the first call, two warmups and ten randomized
interleaved samples. The script checks all successful baseline fractions and
contracts, plus exact resource counters on marked unchanged controls. Keep
failed baseline records; a newly completed result needs an independent reference.

`paired-ordered-throughput.cjs` takes baseline optimized `wwwroot`, candidate
optimized `wwwroot`, matrix and output JSON. It measures complete requests with
the same warmup/sample schedule. Bootstrap is separate; engine time excludes
JSON and transport. Heap readings are WASM linear-memory capacity, not live
managed memory. `YGO_BROWSER_EXECUTABLE` selects Chromium;
`YGO_BROWSER_SINGLE_PROCESS=1` enables the documented restricted-host arguments.
Run native, browser and test jobs serially. Report empirical p50/p90 values and
allocation bytes; do not add timing assertions to CI.

The explicit `OrderedThroughputProfile.Profile` uses
`YGO_ORDERED_PROFILE_INPUT` / `YGO_ORDERED_PROFILE_OUTPUT` for a separate untimed
phase/lifetime run. It samples live managed heap after forced GC and working set
at completed layer boundaries, alongside per-structure and concurrent-map
payload counters. These samples are not exact process peaks. An external Compile
item can include the identical profile test in an unmodified baseline checkout.
Primary/witness/lower Work snapshots are cumulative. Terminal counts and mass
are observed once when the completed distribution is emitted. Publish again without the external probe for production
worker/UI and SEO smoke checks.

The internal template/terminal-packing constructor switches are only for forced
reference tests. No worker input, session setting or production routing gate
selects them. Terminal packing uses the existing lossless byte-vector codec only
when every value fits and its actual retained payload is smaller. Every key has
one encoding throughout its map lifetime; each counting pass decodes only the
current key. Certificates reuse the completed checked map, without replay.
