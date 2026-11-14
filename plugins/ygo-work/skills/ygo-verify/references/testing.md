# Meaningful tests

Keep a test when it catches a meaningful defect at the simplest reliable layer. Preserve independent probability oracles, compatibility, user state, accessibility and calculation ownership. Remove redundant tests and unused helpers rather than skipping them.

Calculator calculation changes need focused regression cases under `repo:YGOProbabilityCalculatorBlazorTest/Services/ProbabilityCalculator/`; neutral engine regressions belong in the library's test project. Prefer existing `SmallDeckOracleTest.cs` independent physical-hand enumeration for application coverage. Verify expected fractions/counts with exhaustive enumeration or an actual independent calculation, never intuition, memory or production output. Cover relevant overlap, distinct-copy collisions, union counting and boundary constraints. Keep application tests on the public engine API; do not restore transferred white-box tests or friend access here.

Editor behavior changes need bUnit interaction coverage in the relevant `CalculatorEditor*` test under `repo:YGOProbabilityCalculatorBlazorTest/Components/`; other component changes need focused coverage where useful. Persistence/import changes need coverage at their own service boundary. Preserve numerical formatting and protocol/serialization assertions.

Treat explanatory prose and incidental styling as editable. Assert selected data/source, accessible associations and consequential usability rather than complete help sentences, rejected wording, exact CSS lists or subpixel layout matrices.

Await terminal results without requiring calculations to remain busy. Coordinate concurrency, cancellation, stale replies and progressive transitions with explicit start/completion barriers instead of fixed sleeps or event-loop turns. Fake sleeps register deadlines; only the test advances shared time, once for concurrent waiters. Await real I/O or hashing when completion is required, and exercise timeout/lease-expiry schedules separately. Assert outcomes and side effects; wall-clock timeouts are deadlock guards. Check timing fixes with deliberately delayed dependency completion.

Retain compact optimized-published-WASM integration coverage; mocked timing and a button returning idle do not prove CPU-bound worker shutdown. Keep opt-in measurements outside correctness gates.
