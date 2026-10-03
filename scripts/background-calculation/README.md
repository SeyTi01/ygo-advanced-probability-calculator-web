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
deadline; no request timeout or retry is increased. The slowdown argument applies Chromium main-thread
CPU throttling. Worker slowdown is not established by this setting; this tests
slower UI handling rather than a physical low-power device or worker CPU ceiling.
Run CPU/memory probes serially, away from concurrent builds. The native harness
sets `DOTNET_TieredCompilation=0` and terminates its own test
process tree after two minutes.
Neither smoke changes the bundled example or session schema.
