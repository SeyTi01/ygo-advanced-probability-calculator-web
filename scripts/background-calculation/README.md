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

Publish a clean optimized Release build with SDK-scoped `wasm-tools`, without
external measurement sources or AOT. Use the existing external Playwright
installation through `NODE_PATH`. Set `YGO_BROWSER_EXECUTABLE` to an installed
Chromium executable, or optionally set `YGO_BROWSER_CHANNEL`.

```sh
dotnet publish YGOProbabilityCalculatorBlazor/YGOProbabilityCalculatorBlazor.csproj -c Release
node scripts/smoke.cjs YGOProbabilityCalculatorBlazor/bin/Release/net10.0/publish/wwwroot
```

The serial entrypoint fails on the first failed child and runs:

- `integration-smoke.cjs`: actual production worker bootstrap, optimized WASM,
  request decoding, all four UI modes and rational total/combo/group binding.
  Small independently enumerated controls distinguish dealt-hand `2/5`, Fast
  primary `5/6`, and physical Exact `23/30`. It checks usable controls, keyboard
  evidence disclosure, pins and copied fractions at representative desktop/mobile
  sizes. Ordinary completion never requires a spinner, Cancel or Preliminary frame.
  Explicit outgoing transport gates separately exercise pending Exact/Progressive
  success, cancellation, editing and failure. Gates never fabricate probabilities;
  real worker close events confirm retired execution contexts. These gated cases
  establish lifecycle behavior, **not natural CPU responsiveness or latency**.
- `../workspace/browser-smoke.cjs`: real keyboard edit/cancel/focus, move validation,
  category draft preservation, deletion dismissal, and download/upload round trip.
  Desktop and one narrow layout catch unusable controls/overflow. Precise spacing,
  incidental markup and exhaustive theme/viewport matrices are manual UI review.
- `../workspace/session-loading-smoke.cjs`: a controlled Blob-read handshake proves
  accessible pending status and delayed replacement; a separate immediate small
  load checks the terminal state without imposing a minimum visible duration.

For the distinct physical idle-owner lifecycle, run this optional check once;
it intentionally waits for the unchanged production 60-second idle timer:

```sh
node scripts/background-calculation/lifecycle-smoke.cjs <published-wwwroot>
```

It observes worker-context reuse, expiry, recreation and navigation teardown.
Normal smoke cancellation also observes real closure, but held transport is not
evidence of stopping a CPU-bound engine. Do not infer CPU shutdown from a button
returning to idle or a call to `terminate()` alone.

Run `scripts/verify-published-seo.sh` against the published directory as required
by the existing publish verification workflow. Keep this responsibility separate
from browser correctness and do not add expensive smoke matrices to every push.

## Opt-in measurement tools

These are explicit research/measurement entrypoints, outside normal discovery.
Freeze the same exported fixture file for both revisions, record its hash, and
run timing jobs alone without builds/tests/other calculations. Keep first calls
separate from warm samples. Timeouts are externally censored observations, not
successful calculations or Storage failures. Never commit generated output.

| Purpose | Export/source | Native / browser entrypoint |
| --- | --- | --- |
| General frozen engine replay | current retained worker-input exports, `PerformanceAuditBenchmark` | `measure-engine-audit.cjs` |
| Work policy comparison | `WorkPolicyBenchmark` | `measure-work-policy.cjs` |
| Draw-effect UI/worker replay | `DrawEffectsBenchmark` and retained draw exporters | `measure-draw-effects.cjs` |
| Worker bootstrap/reuse observations | in-script frozen sessions | `measure-worker-lifecycle.cjs` |
| Search comparison | `SearchFeatureBenchmark`, `SearchPerformanceProbe` | `paired-search-benchmark.cs` / `paired-search-workers.cjs` |
| Mode and physical-survivor comparison | `ThreeModeBenchmark` / `PhysicalSearchBenchmark`, `mode-benchmark-probe.cs` / `physical-search-benchmark-probe.cs` | `paired-mode-benchmark.cs` / `paired-mode-workers.cjs` |
| Fast/certificate comparison | `FastCertificateBenchmark`, `fast-certificate-benchmark-probe.cs` | `paired-fast-certificate-benchmark.cs` / `paired-fast-certificate-benchmark.cjs` |
| Adaptive Fast comparison | Fast exports, `prepare-adaptive-fast-cases.py` | `adaptive-fast-benchmark.cs` / `adaptive-fast-workers.cjs` |
| Bounded-search comparison | `BoundedSearchBenchmark`, `bounded-search-benchmark-probe.cs` | `paired-bounded-search.cs` / `paired-bounded-search.cjs` |
| Ordered throughput | `OrderedStorageBenchmark`, `prepare-ordered-throughput.py` | `paired-ordered-throughput.cs` / `paired-ordered-throughput.cjs` |
| Search throughput | `SelectiveSurvivorTest`, `search-throughput-benchmark-probe.cs` | `paired-search-throughput.cs` / `paired-search-throughput.cjs` |
| Session loading | published sample session | `../workspace/measure-session-loading.cjs` |

The paired native programs compile in a temporary dependency-free `net10.0`
console with implicit usings and nullable enabled. Use test-bin directories to
avoid loading browser-only framework assemblies. Browser counterparts take
optimized published roots. Each script's header defines its arguments and
sampling controls. Benchmark probes must be externally included only in isolated
measurement builds; never add them to the normal project or production publish.
Use `DOTNET_TieredCompilation=0` for paired native measurements. Resource counters
and diagnostic phases are not peak process/browser memory or latency evidence.
WASM linear-memory capacity is not live managed heap.

`bounded-search-wire-smoke.cjs <clean-wwwroot> <fixtures.json>
<native-measurements.json> <output.json>` remains a specialized post-benchmark
check of full shipped-worker response parity, typed failures without fractions,
and absence of measurement exports. It is not needed for the normal small smoke
set, and requires frozen measured references.

Historical feature acceptance scripts, old copy/layout comparisons, removed
certification-toggle experiments, and source-rewriting physical-enumeration
variants were deleted. Git history preserves those one-time investigations.

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
