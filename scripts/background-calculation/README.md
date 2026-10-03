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
foreground fallback. Worker initialization times out after 30 seconds; the
engine retains its original work/storage limits. Results retain their existing
public data contract. Each request captures its own input JSON, excluding inactive
entries and including nested requirements, stable identities and group order.

Cancel, input changes, replacement and component disposal revoke request
ownership and terminate that request's worker. Later requests use fresh workers.
The cancellation signal is sent from the UI thread, not queued behind the
worker's synchronous computation. Browser termination is asynchronous:
[Chromium schedules forcible termination after a two-second grace period](https://github.com/chromium/chromium/blob/main/third_party/blink/renderer/core/workers/worker_thread.cc).
The UI clears its busy state immediately; a terminated Chromium worker can still
consume CPU during that grace period. Firefox's observed shutdown was faster.
Do not treat the button state or an animation as evidence that CPU work stopped.
Future larger-budget work must retain measured termination coverage and account
for this browser behavior.

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
```

It uses isolated browser contexts and bounded real engine workloads at the
production limits. It measures foreground event-loop blocking, interactions
during worker computation, worker execution-context destruction and cancellation
latency. It never raises budgets or changes the bundled example.
