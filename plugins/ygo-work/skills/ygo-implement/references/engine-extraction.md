# Maintain the engine dependency

Use for a requested engine package upgrade or adoption change. Read `repo:docs/generic-calculation-api.md` for public contracts, package provenance, host boundaries and version domains. Resolve the actual pinned version from `repo:YGOProbabilityCalculatorBlazor/YGOProbabilityCalculatorBlazor.csproj`.

## Ownership

The calculator consumes the published `CardProbability.Engine` package through a normal, pinned `PackageReference`. Its `CardProbability` assembly and public namespace are the boundary. The [library repository](https://github.com/SeyTi01/card-probability-engine) owns `src/CardProbability/`, including the internal solvers, and `tests/CardProbability.EngineTests/`, including neutral fixtures and white-box tests. Library fixes and releases follow that repository's workflow.

Keep exactly one engine implementation in the calculator build. Do not copy or link library sources into the app, add a namespace shim, or grant the app or application tests friend access. Preserve calculator-specific integration tests and independent probability oracles here.

Adapters, card/catalog translation, saved effect definitions and compatibility, UI, worker envelopes and JavaScript exports, Progressive coordination, leases, cancellation ownership and stale-result handling remain in the calculator. Preserve adapter-derived `PolicyOrderKey` values from original sorted atomic category/direct-entry memberships; the engine receives opaque ordinal keys, never game metadata. These keys preserve deterministic keep/removal behavior across package upgrades.

## Adopt and verify a version

1. Identify the published package ID/version, library source commit/release and relevant library verification. Review signature and semantic changes, including requirement assignment, operation/tie order (`PolicyOrderKey`), accuracy, defaults and capability bounds. Package SemVer and runtime `ModelVersion` remain separate from application session, effect-plan and worker-wire versions.
2. Pin the intended published version through the application project's normal package reference. Reconcile any solver changes with the library before removing or replacing an implementation; keep their provenance and transfer evidence reviewable. Do not use a local feed or linked source as the delivered dependency.
3. Use [package-boundary verification](../../ygo-verify/references/engine-boundary.md) to check source ownership and restore/run the separate public consumer. Compile and run the solution's application tests against the package. Production and application-test source must consume only the public API, without `CardProbability.Internal`, linked engine source or old solver calls.
4. Compare relevant pre-upgrade fixtures: fractions/unions, result rows and exact rational strings, certification intervals, accuracy/scope/status distinctions and supported effects. Verify the production Release AOT/stripped-IL worker, including Fast/Exact, Progressive completion, cancellation and stale-result rejection. Preserve host-owned preliminary/terminal stages and leases.
5. Compare representative native and browser performance fixtures on the same host/runtime and distinguish first and warm requests. Reuse unchanged evidence where it still applies, while verifying the actual adoption revision. Record package provenance and consequential integration evidence in the PR.

Package contents, license notices, signing and publication automation belong to the library release workflow. The engine's MIT license does not grant rights to artwork or card/catalog data.
