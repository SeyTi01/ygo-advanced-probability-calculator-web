# Generic calculation API

[Documentation index](README.md) · [Calculation model](calculation-model.md) · [Extraction procedure](../plugins/ygo-work/skills/ygo-implement/references/engine-extraction.md)

The `CardProbability` API describes a finite deck of physical copies, resolved set memberships, goals, and a closed set of optional operations. A host translates its domain into this model; the engine does not interpret card text, metadata, or catalog identities. It calculates synchronously and returns one terminal result.

## Minimal example

This fictional five-copy deck is dealt two cards. `x` belongs to both memberships, `y` only to `starter`, and `z` only to `fire`. Goal `distinct` needs one copy from each membership; `overlap` needs one copy from their intersection. The two goals together succeed in five of the ten equally likely physical hands.

```csharp
using CardProbability;

CalculationRequest request = new(
    entries: [
        new DeckEntry("x", 1),
        new DeckEntry("y", 1),
        new DeckEntry("z", 1),
        new DeckEntry("f1", 1),
        new DeckEntry("f2", 1)
    ],
    handSize: 2,
    memberships: [
        new Membership("starter", ["x", "y"]),
        new Membership("fire", ["x", "z"]),
        new Membership("both", ["x"])
    ],
    goals: [
        new Goal("distinct", [
            Requirement.InMembership("starter"),
            Requirement.InMembership("fire")
        ]),
        new Goal("overlap", [Requirement.InMembership("both")])
    ]
);

CalculationResult result = CalculationEngine.Calculate(request);
if (result.Status != CalculationStatus.Completed) {
    throw new InvalidOperationException(result.Detail);
}

// Total.Primary is 5/10, reduced to 1/2.
Console.WriteLine($"{result.Total!.Primary.Numerator}/{result.Total.Primary.Denominator}");
```

The ten unordered hands are `xy`, `xz`, `xf1`, `xf2`, `yz`, `yf1`, `yf2`, `zf1`, `zf2`, and `f1f2`. `distinct` succeeds for `xy`, `xz`, and `yz`; `overlap` succeeds for `xy`, `xz`, `xf1`, and `xf2`. Their union is five hands. This is an independent physical-copy enumeration, not a solver-specific expectation.

For a consumer that needs only the union result, call `CalculateUnion` with the same request and options:

```csharp
CalculationResult union = CalculationEngine.CalculateUnion(request);
if (union.Status != CalculationStatus.Completed) {
    throw new InvalidOperationException(union.Detail);
}

// The total uses the same goal set and goal-set-dependent keep policy.
// Goal and group rows are omitted; optional certification is omitted too.
Console.WriteLine($"{union.Total!.Primary.Numerator}/{union.Total.Primary.Denominator}");
```

`CalculateUnion` leaves `Goals` and `Groups` empty and returns only the total row. It evaluates the same goal set with the same active-goal-dependent keep policy as `Calculate`; it does not change the modeled problem. It omits optional certification to avoid the separate certificate work, while retaining any structural exact proof established by the primary calculation.

The returned `Primary` is an exact rational for the selected primary model. For Fast results, inspect each row's `Accuracy` and `Certified` fields before treating it as a physical probability; an uncertified primary can be a projection. `Probability` is a convenience `double`. Fractions are represented as decimal strings so hosts can transport arbitrarily large integers without numeric JSON loss.

## Constructing a request

- `DeckEntry` is a class of physical copies with one opaque ID and one multiplicity. Copies within that entry share an activation allowance; different entries do not. Split entries when their operations or allowance identity differ. Its optional `PolicyOrderKey` configures the deterministic goal-dependent keep/removal order; treat this as part of the modeled behavior when using effects that can remove cards.
- `Membership` resolves a caller-defined predicate to entry IDs. Memberships may overlap. An empty membership is valid. The engine uses the supplied sets and does not infer their meaning.
- `Goal` has required `Requirement`s, optional `Alternative` groups, and an optional group ID. All required requirements and alternative groups must be satisfied; one complete choice within each alternative group is enough. A group result is the union of its active goals.
- A requirement points to either an entry or membership and has inclusive minimum and optional maximum. A missing maximum means the evaluated hand size. Repeated references to the same target are combined by intersecting their bounds. Positive demands for different targets consume distinct copies; a single copy cannot fill two slots. A maximum counts every matching copy in the hand, including copies used for another requirement. A fixed `0/0` is valid and excludes every matching copy. There is no separate predicate-only minimum query apart from these slot-bearing requirements.
- The total result is the union of all goals. A physical outcome satisfying several goals contributes once. Each goal's optional search choices are evaluated for that goal; different goals can have different successful search-existence witnesses. Do not interpret goal rows as one globally optimal play sequence.

IDs must be nonblank and unique within each ID space (entries, memberships, goals, and groups). References to undeclared entries, memberships, or groups are invalid. IDs are ordinal and case-sensitive. `PolicyOrderKey` values are compared ordinally. Supply a key for every entry or for none; keys may be empty, and equal keys are valid. Keys sort the canonical policy partition before the existing ordinary-entry-before-effect-source order and ordinal entry-ID tie-breaks. Entries with equal keys can be merged only when their full goal and effect truth is equivalent. If all keys are omitted, the engine derives the generic order from ordinal membership and direct-entry requirement bit strings. A host may provide its own opaque keys to preserve a different domain's established deterministic policy; this does not expose domain metadata to the engine.

The request and nested collection inputs are copied into immutable arrays when constructed, so callers can reuse the resulting request. As with any constructor that enumerates caller-owned data, do not mutate an enumerable while it is being enumerated. The engine creates request-local solver state; the stateless API permits concurrent calls on an immutable request.

`ModelVersion = 1` on results identifies calculation-model semantics, not an app session, effect-plan, or worker-wire version. The API does not define a public JSON schema. Hosts may choose their own transport and persistence contracts.

## Execution and outcomes

```csharp
CalculationOptions options = new(
    mode: CalculationMode.Fast,
    policy: new ExecutionPolicy(WorkUnits: 150_000_000, EntryLimit: 32_768, CellLimit: 262_144));

CalculationResult result = CalculationEngine.Calculate(request, options, cancellationToken);
```

The default mode is `Exact`, with unlimited Work and the default per-structure Storage limits. `Fast` defaults to 150,000,000 Work units. `OpeningHand` ignores operations and evaluates only the dealt hand. `OptimisticSearchProjection` is an explicit compatibility mode for hosts that need the historical optimistic search projection; it is not the ordinary default. Work is deterministic operation accounting, not elapsed time. Entry and cell limits apply to individual live structures, not total process memory. Cancellation is cooperative through `CancellationToken`.

Each call returns a terminal `CalculationResult`; there is no preliminary-result status in this API. `Completed` contains rows and an overall accuracy. Failures (`Cancelled`, `InvalidInput`, `Unsupported`, `WorkLimit`, and `StorageLimit`) contain no probability rows. A host that offers a staged experience such as Progressive must own stage coordination, retain and label preliminary values, guard stale requests, and manage worker leases and cancellation.

`ModelScope` distinguishes `OpeningHand` from `FixedOrderEffects`. `ProbabilityValue.Primary` is the primary rational. `Certified` is either absent or an interval of rational endpoints; exact proof is represented by equal endpoints. Accuracy is explicit as `Exact`, `Bounded`, or `Uncertified`. A completed `Fast` result may be exact, bounded, or uncertified. A completed `Exact` result is physical-exact within the supported model; unsupported or exhausted work/storage is reported as a failure instead of an Exact fallback.

`CalculationResult.Certification` describes the separate optional search-availability certificate attempt. `Exact` and `Bounded` mean a certificate was produced; `NotApplicable` means there were no active Deck searches in the executed model; `NotCertified` means no usable certificate was established; `Limited` and `Error` mean the optional certificate work stopped at a resource limit or error. `NotRequired` means the primary calculation itself established an exact result, so optional certification was unnecessary. Read this alongside `Status`, overall `Accuracy`, and each row's `Certified` interval: optional certification can be limited or fail after a valid primary result completed, and that does not turn the completed primary into a calculation failure. `CalculateUnion` omits the optional certificate and reports structural exactness when the primary route proves it.

`CalculationEngine.Assess` returns conservative structural guidance (`ElevatedRisk`), not a completion prediction or Work estimate. It is not a substitute for handling the terminal result.

## Supported operations and boundaries

`EffectPlan` is a closed declarative instruction set: exactly one draw, search, or excavation operation per source. It has no callbacks. Draw plans can express activation limits, hand and hidden-deck costs, post-draw removals and hidden milling, first-activation rules, and draw-blocking constraints. Search plans can express target eligibility, alternative modes, and mode activation limits. Excavation can reveal a fixed positive number and keep one, with an optional no-prior-effect-draw condition.

Execution follows the fixed model: draws, at most one excavation, then optional searches. Search choices use only information available at that point. The engine preserves hand-wide maxima, distinct-copy requirements, physical copy competition, activation allowances, priority/encounter order, and deterministic keep/removal policy. That policy depends on the active goal set. The existence semantics for searches can choose different legal witnesses for different goals and do not describe a single optimal-play strategy. See [calculation model](calculation-model.md) for detailed ordering and gameplay exclusions.

Not every composition of operation fields is supported. For example, a source cannot combine draw, search, and excavation instructions; excavation is standalone with one activation; and search mode target memberships must union to the operation's declared targets. Negative counts/bounds and unknown references return `InvalidInput`; a minimum above its maximum remains an impossible requirement. Unsupported compositions and solver capability limits return `Unsupported`. Callers must handle these outcomes rather than infer behavior from effect presence.

The total physical-copy count may be at most `Int32.MaxValue`. Internal packed-coordinate thresholds (including 255 and 64) select representations and are not public deck-size caps. Solver limits are exposed through Work and per-structure Storage policies. More resources do not make an unsupported operation supported.

## Runtime and compatibility

The current source uses the .NET 10 BCL and targets `net10.0`, with C# 14, implicit usings, and nullable annotations. It has no engine-specific external package or embedded resource dependency. The eventual package should keep `CardProbability` as its public namespace and treat model meanings and public signatures as SemVer responsibilities. Intentional API or semantic changes require a package-version decision; model versioning remains distinct from application session and effect-plan revisions.

The application adapter remains responsible for resolving domain selectors and effect definitions into this neutral model, preserving saved plans and historical session behavior, and translating results into UI/worker contracts. The engine license is the repository's MIT license; that software license does not grant rights to bundled artwork or card/catalog data.
