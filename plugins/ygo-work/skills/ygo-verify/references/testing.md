# Meaningful tests

Keep a test when it catches a meaningful defect at the simplest reliable layer. Preserve independent probability oracles, compatibility, user state, accessibility and calculation ownership. Remove redundant tests and unused helpers rather than skipping them.

Calculation changes need focused regression cases under `repo:YGOProbabilityCalculatorBlazorTest/Services/ProbabilityCalculator/` or the appropriate neutral engine tests. Prefer existing `SmallDeckOracleTest.cs` independent physical-hand enumeration. Verify expected fractions/counts with exhaustive enumeration or an actual independent calculation, never intuition, memory or production output. Cover relevant overlap, distinct-copy collisions, union counting and boundary constraints.

Editor behavior changes need bUnit interaction coverage in the relevant `CalculatorEditor*` test under `repo:YGOProbabilityCalculatorBlazorTest/Components/`; other component changes need focused coverage where useful. Persistence/import changes need coverage at their own service boundary. Preserve numerical formatting and protocol/serialization assertions.

Treat explanatory prose and incidental styling as editable. Assert selected data/source, accessible associations and consequential usability rather than complete help sentences, rejected wording, exact CSS lists or subpixel layout matrices.

Await terminal results without requiring calculations to remain busy. Explicitly control completion for cancellation, stale replies and progressive transitions. Retain compact optimized-published-WASM integration coverage; mocked timing and a button returning idle do not prove CPU-bound worker shutdown. Keep opt-in measurements outside correctness gates.
