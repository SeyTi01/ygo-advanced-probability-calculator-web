# Calculation changes

Read the relevant current contracts, in this order for engine work:

1. `repo:docs/calculation-model.md`: supported saved rules, distinct physical requirements, ordered effects and gameplay exclusions.
2. `repo:docs/probability-engine.md`: counting, matching, inclusive bounds, valid zero constraints and overlap-safe unions.
3. `repo:docs/accuracy-and-performance.md`: accuracy/scope, Fast/Exact/Progressive behavior and work/storage/lifecycle limits.
4. `repo:docs/generic-calculation-api.md`: neutral API, ownership, validation and terminal outcomes when that boundary is affected.

Preserve these contracts unless the task explicitly changes them. Keep game/catalog/provenance translation in the application adapter and the engine independent of UI/session/browser concerns. Do not widen the public API merely to expose implementation details. For boundary changes use `repo:docs/engine-extraction.md` and `repo:scripts/engine-boundary/README.md`.

For worker or progressive changes, also read `repo:scripts/background-calculation/README.md`; preserve cancellation, stale-input/reply rejection, preliminary-to-terminal transitions and worker ownership. Historical runtime findings or measurements establish only their stated host, revision and fixture.

Use [test design](../../ygo-verify/references/testing.md) for independent numerical regression expectations. Preserve saved/custom plans through [compatibility](compatibility.md) when adapters or execution inputs change.
