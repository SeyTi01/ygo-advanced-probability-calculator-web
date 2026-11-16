# Engine package boundary

The check requires Node.js and the .NET 10 SDK. Run from the repository root:

```sh
node --test scripts/engine-boundary/guards.test.mjs
node scripts/engine-boundary/check.mjs
```

It publishes nothing. Use `node scripts/engine-boundary/check.mjs --guards-only` for source checks without restoring or running the .NET consumer.

The source guards require a literal, pinned `CardProbability.Engine`
`PackageReference` in the application. They reject embedded engine sources,
namespace shims, internal engine references in application code/tests, linked
engine dependencies, and the relocated neutral test project. `--guards-only`
runs these fast checks; they also run in the ordinary JavaScript suite.
These conservative tripwires complement normal compilation against the package,
which enforces the public API for both the application and its tests.

The full check creates a unique system TEMP directory outside the checkout and
restores the application's pinned version from NuGet.org into a fresh package
cache. It rejects a different resolved version or extra package/project
dependencies, then compiles and runs `Consumer.cs` as a separate assembly with
only that normal package reference. No engine source, application build
properties, local feed or friend access supplies the consumer.

The consumer uses fictional opaque identities and independently counted
fractions for overlapping goals/unions, distinct copies, zero maxima, a
seven-card opening hand, `(A OR B) AND C`, and draw/search/excavation effects.
It checks entry allowances, OpeningHand validation and effect suppression,
runtime model metadata, immutable snapshots, concurrent reuse, terminal
failures, assembly identity, public namespaces, BCL-only dependencies and the
sole upstream neutral-test friend.

The temporary directory is removed after success or failure. Add `--keep` to
retain it for diagnosis. The full proof runs in CI alongside the normal solution
build/tests; those tests retain the calculator's adapters, independent oracles
and worker integration coverage. Browser/AOT checks remain separate.

The engine implementation and white-box suite now belong to
[card-probability-engine](https://github.com/SeyTi01/card-probability-engine).

## Adopt or upgrade the package

The application project, `YGOProbabilityCalculatorBlazor/YGOProbabilityCalculatorBlazor.csproj`, owns the literal `CardProbability.Engine` version. [The API guide](../../docs/generic-calculation-api.md) explains public contracts, current provenance and version domains.

1. Identify the published NuGet version, library release/source revision and its verification evidence. Review signature and semantic changes, especially requirement assignment, operation/tie order, accuracy, defaults and capability bounds. Package SemVer and runtime `ModelVersion` are separate from application session, effect-plan and worker-wire versions.
2. Update the application's normal `PackageReference` to the intended published version. Keep one engine implementation: do not copy/link library sources, create a namespace shim or grant application/tests friend access. Delivered dependencies come from the published package, not a local feed.
3. Run the boundary commands above and the normal [solution build/tests](../../README.md#documentation-and-development). Keep calculator adapters, integration tests and independent physical-card oracles in this repository; neutral and white-box engine tests remain with the library.
4. Compare relevant pre-upgrade fixtures: exact fractions/unions and result rows, rational strings, certification intervals, accuracy/scope/status and supported effects. Verify the [optimized production worker](../background-calculation/README.md#small-published-app-smoke-set), including Fast/Exact, Progressive completion, cancellation and stale-result rejection.
5. For changes that can affect performance, compare representative [native/browser fixtures](../background-calculation/README.md#benchmark-and-measurement-tools) on the same host and runtime, separating first and warm requests. Record the package/source provenance and consequential integration results with the upgrade.

The host still owns card/catalog translation, saved effect compatibility, UI, worker envelopes and JavaScript exports, Progressive stages, leases and cancellation. Preserve adapter-derived `PolicyOrderKey` values from the original sorted atomic category/direct-entry memberships: the engine receives opaque ordinal keys, not game metadata. These retain deterministic keep/removal behavior across upgrades.

Library package contents, license notices and package publication belong to the library. Its MIT license does not grant rights to artwork or card/catalog data.
