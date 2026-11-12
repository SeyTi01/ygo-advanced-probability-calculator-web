# Performance evidence

Choose fixtures tied to the regression or audit question. Compare baseline and candidate on the same host, runtime, build mode, input, cache/worker state and harness. Record revisions; keep their outputs separate and prefer absolute, Git-ignored evidence paths across checkouts. Run timings without concurrent builds, tests or measurements. Inspect the harness before interpreting output.

Load only the selected procedure:

| Measurement | Procedure |
| --- | --- |
| Export baseline requests or replay natively | [Native replay](native-replay.md) |
| Replay frozen requests in optimized WASM | [Browser replay](browser-replay.md) |
| UI-to-worker lifecycle, search or session loading | [UI timings](ui-measurements.md) |
| Generate excavation workloads and references | [Excavation export](excavation-measurements.md) |

Internal phase profiles, independent physical-hand oracles and frozen correctness regressions remain separate from timings. Retired feature matrices, external probes and duplicate wrappers remain in Git history; do not restore obsolete harnesses by default. Keep temporary probes and generated evidence out of production exports and commits; retain a small reproducible regression check when part of the deliverable.

Establish correctness independently of speed: compare fractions, accuracy/scope and terminal outcomes on equivalent inputs. Preserve engine boundaries and public contracts while optimizing. Report material regressions and unavailable historical fixtures; never claim a missing baseline was matched.

Separate first requests, warm requests, worker bootstrap, allocations/state growth and cancellation responsiveness. They do not establish cold process/network behavior. Native and browser AOT timings are not equivalent. Timeouts are censored samples, never successful calculations or Work/Storage failures. Double animation frames observe scheduling, not guaranteed paint. Allocations/working set are not peak live browser memory; WASM linear-memory capacity is not live managed heap.

Report the comparison and limits. For useful PR evidence, follow [GitHub writing](../../ygo-issue/references/github-writing.md). Optional benchmarks are not universal correctness gates.
