# Calculation changes

Read the relevant current contracts, in this order for engine work:

1. `repo:docs/calculation-model.md`: supported saved rules, distinct physical requirements, ordered effects and gameplay exclusions.
2. `repo:docs/probability-engine.md`: counting, matching, inclusive bounds, valid zero constraints and overlap-safe unions.
3. `repo:docs/accuracy-and-performance.md`: accuracy/scope, Fast/Exact/Progressive behavior and work/storage/lifecycle limits.
4. `repo:docs/generic-calculation-api.md`: neutral API, ownership, validation and terminal outcomes when that boundary is affected.

Preserve these contracts unless explicitly changed. Keep game/catalog/provenance translation in the adapter and the engine independent of UI/session/browser concerns. Do not widen the API to expose implementation details. The engine implementation and neutral tests belong to the library repository; calculator changes consume its public package boundary. Boundary changes need [dependency verification](../../ygo-verify/references/engine-boundary.md); package upgrades use [engine dependency maintenance](engine-extraction.md).

For worker or progressive changes, also read `repo:scripts/background-calculation/README.md`; preserve cancellation, stale-input/reply rejection, preliminary-to-terminal transitions and worker ownership. Historical runtime findings or measurements establish only their stated host, revision and fixture.

Use [test design](../../ygo-verify/references/testing.md) for independent numerical regression expectations. Preserve saved/custom plans through [compatibility](compatibility.md) when adapters or execution inputs change.
