# Engine extraction checklist

[Documentation index](README.md) · [Public API guide](generic-calculation-api.md) · [Calculation model](calculation-model.md)

This checklist describes the intended later source transfer and package substitution for the engine currently owned by `YGOProbabilityCalculatorBlazor/Engine/`. It is a migration procedure, not evidence that a separate library or package already exists. Recheck the source tree and project files when starting that work.

## Owned production source

Transfer the complete engine subtree as one unit. Do not cherry-pick solver files: public contracts, internal contracts, resource policies, model types, compilation, counting, draw resolution, search, and certification form a closed source set.

```text
YGOProbabilityCalculatorBlazor/Engine/
  CalculationContracts.cs
  CalculationEngine.cs
  CalculationRequest.cs
  Internal/...
```

The exact inventory at the time this guide was written is every `*.cs` file under that directory. There are no engine-owned resources or external packages. Build the future library as `CardProbability` for `net10.0`, using the .NET 10 BCL, C# 14, implicit usings, and nullable annotations. Preserve `CardProbability` as the public namespace. Keep host frameworks, browser/JavaScript support, card datasets, artwork, localization, app services, and catalog metadata outside the library.

Before extraction, verify closure again: list all files recursively, inspect project compile items and conditional symbols, search references and assembly-qualified names, and verify there are no files read at runtime, generated sources, source-generation contexts, reflection hooks, or build targets outside the declared set. The intended engine is synchronous and self-contained; do not add a framework target or dependency without an identified public API need.

## Tests that move

The complete neutral test project, `CardProbability.EngineTests/`, is part of the engine-owned transfer set. Move the project and its entire source/fixture tree together. It contains public-boundary coverage, internal mathematical and optimizer tests, and independent neutral fixtures/oracles; it has no application namespaces, models, card metadata, or application resources. Keep its .NET test dependencies in the test project, outside the production engine dependency set.

The engine includes `Engine/EngineTestVisibility.cs`, which grants `InternalsVisibleTo` only to the exact assembly name `CardProbability.EngineTests`. Preserve that exact friend relationship for white-box engine tests. The public consumer, production application, and application test assembly are never friends of the engine and must use only its public API. Do not transfer generated `bin`, `obj`, or `TestResults` directories.

Keep application integration/adapter tests and product-specific probability oracles in the calculator repository. They validate host translation and retained product behavior. Preserve public-only consumer coverage and all independent mathematical fixtures in the neutral test project.

The repository's repeatable isolated-build, neutral-test, and app-substitution check is documented in [`scripts/engine-boundary/README.md`](../scripts/engine-boundary/README.md). From the repository root, run `node scripts/engine-boundary/check.mjs` to build a copied engine, a separate public-only consumer, the complete `CardProbability.EngineTests` project, and the app plus its complete application test project after substituting the isolated engine assembly. Run `node scripts/engine-boundary/check.mjs --guards-only` for the fast source-boundary guards only. These commands are verification instructions; their presence here does not claim that the proof has been run for a particular change.

## Package substitution steps

1. Create the standalone `net10.0` class library and copy the complete `Engine/**/*.cs` inventory, including `EngineTestVisibility.cs`. Keep internal implementation beneath `CardProbability.Internal`; expose only the current public API.
2. Copy the complete `CardProbability.EngineTests` project and its source/fixture tree. Replace its application project reference with the engine project/package reference. Keep its exact assembly name so the engine's sole friend declaration remains valid; do not grant friend access to any other assembly.
3. Build and run the full neutral test project and a separate public consumer without application source, linked files, or privileged consumer access. The consumer exercises fictional entries and IDs, overlapping goals/unions, non-default hand size, supported effects, cancellation, input failures, immutable snapshots, and concurrent reuse with independent expected fractions.
4. In the calculator project, exclude/remove local `Engine/**/*.cs` compile inputs and add the package reference. Keep application adapters, effect registry, catalog resolution, UI, browser workers, worker envelopes, and worker JS exports in the application. The adapter supplies `PolicyOrderKey` values derived from its original sorted atomic category/direct-entry memberships so compound predicates retain the established removal policy; the engine receives only opaque ordinal keys, never game metadata. Update the adapter's direct dependency to `CardProbability`; no namespace shim should be required if the existing integration calls only the public API.
5. Confirm there is exactly one engine implementation in the application build. Search application production source for `CardProbability.Internal`, internal solver types, linked engine source, and old solver calls; those are prohibited outside the engine. Application tests must consume the public API; only the neutral `CardProbability.EngineTests` assembly may access engine internals. Build the whole application test project after substituting the isolated engine project reference, then run the full repository suite for behavior verification. The calculator should consume the public API for ordinary calculations and every worker/stage path.
6. Keep package distribution infrastructure separate from this code transfer. Add package metadata, signing or publishing automation only when the package repository's distribution policy is decided; none is required by the current source boundary.

## Compatibility and versioning

- Public API namespace: `CardProbability`.
- Calculation `ModelVersion` is currently `1`; it identifies calculation-model semantics, not serialized request JSON.
- There is no public serialized JSON schema. App session JSON, built-in effect semantic revisions, browser worker envelopes, and package API compatibility are separate version domains. Preserve and migrate each at its owning host boundary.
- SemVer for a future package owns compatibility of public type/member signatures and documented meanings. Changes to requirement assignment, operation order, accuracy meaning, defaults, capability bounds, or other documented semantics require explicit versioning review, even if a signature stays the same.
- Keep result rows, exact rational strings, certification intervals, status distinctions, and scope when adapting to host transport. Do not flatten preliminary host stages into a terminal engine result.
- Treat `PolicyOrderKey` and the keep/removal tie order it defines as public model behavior. Preserve the host's established key derivation when substituting the engine package; equal keys permit solver merging only for entries with equivalent goal/effect truth.
- The repository's current `LICENSE` is MIT. Keep software attribution and license notices with transferred engine code. Artwork, card content, translations, and catalog assets have separate ownership and do not transfer under that software license.

## Final extraction review

- [ ] Standalone engine build, public consumer, and complete neutral test project build/run pass from the declared source set alone.
- [ ] No production application or application test path accesses `CardProbability.Internal`; only `CardProbability.EngineTests` is granted friend access.
- [ ] Calculator project compiles no local engine source after package substitution.
- [ ] App adapter and browser worker own staged preliminary/terminal orchestration, stale-result handling, leases, and cancellation lifecycle.
- [ ] Current result fractions, unions, exact/bounded/uncertified distinctions, and representative supported effect behavior match the pre-substitution integration fixtures.
- [ ] Session, effect-plan, and worker protocol compatibility remain owned by the calculator and are covered by its tests.
- [ ] Package version and any public semantic change are reviewed before publishing.
