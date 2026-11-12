# Extract the engine into a package

Use only for the future source transfer/package substitution, not ordinary engine edits. No standalone package is implied. Recheck the working revision and read `repo:docs/generic-calculation-api.md` for public contracts, host boundaries and version domains.

## Transfer boundary

Move the complete `repo:YGOProbabilityCalculatorBlazor/Engine/**/*.cs` set together: public/internal contracts, policies, models, compilation, counting, draws, search and certification. Before extraction, inventory recursively and inspect compile items, conditional symbols, references and assembly-qualified names. Confirm closure: no external runtime files, generated source/serialization contexts, reflection hooks, resources or build targets are required.

The current target is an SDK-only `CardProbability` library using `net10.0`, the .NET 10 BCL, C# 14, implicit usings and nullable annotations. Recheck those settings before transfer. Preserve the public namespace and `CardProbability.Internal`; add dependencies/framework targets only for an identified API need. Keep host frameworks, browser/JS support, app services, catalogs, localization and artwork outside the library.

Move the complete `repo:CardProbability.EngineTests/` source/fixture tree, excluding `bin`, `obj` and `TestResults`. Its test-only dependencies stay outside production. Preserve `EngineTestVisibility.cs` and the sole exact friend assembly `CardProbability.EngineTests`; public consumers, the app and application tests must use only the public API. Leave product-specific adapter/integration tests and probability oracles in the calculator.

## Substitute and verify

1. Create the library and transfer the complete source set. Point the neutral tests at it, preserving independent fixtures, public-boundary and white-box mathematical/optimizer coverage.
2. Use [the isolation proof](../../ygo-verify/references/engine-boundary.md) to build/run the standalone engine, separate public-only consumer and complete neutral tests, then compile the app/application tests against the isolated assembly. The consumer covers fictional IDs, overlapping goals/unions, non-default hand size, effects, cancellation, input failures, immutable snapshots and concurrent reuse with independent fractions.
3. Replace local engine compile inputs with the package reference; confirm exactly one engine implementation remains. Keep adapters, effect registry, catalog resolution, UI, worker envelopes/JS exports and staged execution in the app. No namespace shim should be needed. Preserve adapter-derived `PolicyOrderKey` values from original sorted atomic category/direct-entry memberships; the engine receives opaque ordinal keys, never game metadata.
4. Search host production/application-test source for internal solver types, `CardProbability.Internal`, linked engine source and old solver calls. Only the neutral test assembly may access internals. Run the full solution suite and affected browser paths; the isolation proof's application-test build is not a behavioral test run.
5. Compare pre-substitution fixtures: fractions/unions, result rows and exact rational strings, certification intervals, accuracy/scope/status distinctions and supported effects. Preserve host-owned preliminary/terminal stages, stale-result handling, leases and cancellation. Keep session/effect-plan/worker compatibility and migrations at their owning boundaries.

Review package SemVer for signature or semantic changes, including requirement assignment, operation/tie order (`PolicyOrderKey`), accuracy, defaults and capability bounds. `ModelVersion` is separate from app serialization and package compatibility. Carry MIT attribution/license notices with engine source; third-party assets/data do not transfer under that license.

Package signing, metadata and publishing automation require a decided distribution policy and remain separate from source transfer.
