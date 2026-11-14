# Engine package boundary

[YGO Work's boundary verification](../../plugins/ygo-work/skills/ygo-verify/references/engine-boundary.md)
owns setup and execution. The check requires Node.js and the .NET 10 SDK;
it publishes nothing.

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
Package adoption and upgrade guidance lives in the
[engine dependency procedure](../../plugins/ygo-work/skills/ygo-implement/references/engine-extraction.md).
