# Background calculation runtime

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
**50,000,000-unit** work allowance immediately. Synchronous engine callers omitting
the explicit `CalculationWorkPolicy` overload and wire requests omitting
`WorkUnits` retain the bounded **10,000,000-unit** default. Policies are immutable,
positive `long` values; invalid input cannot select unlimited work. There is no
retry, budget UI or persisted preference. Weighted units are not elapsed seconds.
Results retain their existing public data contract. Each request captures its
own input JSON, excluding inactive
entries and including nested requirements, stable identities and group order.

Cancel, input changes, replacement and component disposal revoke request
ownership and terminate that request's worker. Later requests use fresh workers.
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

The allowance was selected from deterministic 60-card sessions with two copies
per disjoint role and one `1/1` route per role. Independent complement counting
checks exact results. Measured charged-work brackets were 11.52–11.63 million
for 20 routes / five cards, 19.53–19.63 million for 18 routes / six cards, and
43.84–43.95 million for the 15-route / 15-card stress case. Fifty million admits
these requests with bounded headroom; 22 routes / five cards still reaches
storage exhaustion. The native opt-in benchmark reports elapsed time, cumulative
allocations and sampled heap/working set; cumulative allocations are not peak
live memory. Browser timing and shutdown evidence belong in the PR discussion.

Transport tests run with Node.js 24, without a browser or additional packages:

```sh
node --test scripts/background-calculation/transport.test.mjs
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
run separately. It checks accepted 50M results, pin/unpin, historical clipboard
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
can be cached within the context; this is not a cold-network benchmark. The app
currently creates a fresh worker per request, so first-request time plus startup
is the relevant user latency; the warm repeat is diagnostic headroom only.
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
