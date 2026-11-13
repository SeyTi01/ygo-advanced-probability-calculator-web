# Engine boundary proof

[YGO Work's boundary verification](../../plugins/ygo-work/skills/ygo-verify/references/engine-boundary.md)
owns setup and execution. The tool requires Node.js and the .NET 10 SDK;
no project, package or deployment is published.

The check first rejects application/host dependencies in the owned engine
sources and production/application-test references to `CardProbability.Internal`.
Engine friend access is allowed only for the neutral `CardProbability.EngineTests`
assembly, in the exact `Engine/EngineTestVisibility.cs` declaration. Use
`--guards-only` to run just these fast source guards. The guards are conservative
tripwires; compiler checks establish actual dependency and accessibility closure.

The full check creates a unique system TEMP directory outside the checkout:

1. Copy only `YGOProbabilityCalculatorBlazor/Engine/**/*.cs` and root `LICENSE`
   into an SDK-only `net10.0`, C# 14 project, with implicit usings and
   `Nullable=annotations`. Package feeds are cleared for this engine and its
   consumer. No application build properties, resources or source files are used.
   Generate XML documentation and reject missing public documentation, malformed
   XML, mismatched/missing parameter docs, invalid references and misplaced docs.
2. Compile and run `Consumer.cs` as a separate assembly using a normal project
   reference. It uses fictional opaque identities and independently counted
   fractions for overlapping goals/unions, distinct copies, zero maxima, a
   seven-card opening hand, `(A OR B) AND C`, and draw/search/excavation effects.
   It checks shared entry allowances and effect suppression with validation in
   OpeningHand mode, as well as read-only runtime model metadata. It also checks
   snapshots, concurrent request reuse, terminal failures, exported namespace,
   BCL assembly references, absence of embedded resources and the sole approved
   neutral-test friend. The consumer has no friend access.
3. Copy the complete `CardProbability.EngineTests` source/fixture tree without
   build outputs. Replace its application project reference with a reference to
   the isolated engine, then build and run the entire neutral test suite. At
   this point no application source exists in the temporary tree.
4. Copy the production app without its engine sources or build outputs, with a
   normal project reference to the isolated engine. Copy the complete application
   test tree, preserving sibling paths so existing fixtures/resource links work,
   then build the entire application test project and its app dependency. The
   engine grants no friend access to either assembly, so the compiler rejects
   internal engine access in production and application tests. These checks use
   the projects' existing NuGet dependencies and do not run or publish the app.
   The complete application test suite is built here; run it normally through
   the solution for behavior verification.

The temporary directory is removed after success or failure. Add `--keep` to
retain it for diagnosis. The proof creates no production library: the future
package substitution still removes local `Engine/**/*.cs` compile inputs and
replaces this temporary project reference with the package reference.

This scoped check complements the repository's correctness, browser and AOT
checks. It does not replace them or assert a public JSON serialization format.
