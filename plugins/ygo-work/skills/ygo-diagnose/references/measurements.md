# Performance evidence

Choose representative fixtures tied to the reported regression. Compare baseline and candidate in the same host, runtime, build mode, workload, input, cache/worker state and measurement harness. Record revisions and distinguish cold startup, warm calculation, allocations/state growth and cancellation responsiveness where relevant. Do not compare native timing to browser AOT as though they were equivalent.

Use existing tools described in `repo:scripts/background-calculation/README.md`, including worker, lifecycle, search and session-loading measurements as applicable. Inspect the harness before interpreting its output. Keep temporary probes and measurements out of production exports and commits unless a small reproducible regression check is part of the deliverable.

Establish correctness independently of speed and compare result fractions, accuracy/scope and terminal outcomes on equivalent inputs. Preserve engine boundaries and public contracts while optimizing. Treat material regressions and unavailable historical fixtures explicitly; do not claim a missing baseline was matched.

Report the measured comparison and relevant limitations. Such non-routine evidence belongs in a PR when useful, following [GitHub writing](../../ygo-issue/references/github-writing.md). Do not turn an optional benchmark into a universal correctness gate.
