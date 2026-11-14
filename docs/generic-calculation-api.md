# Generic calculation API

[Documentation index](README.md) · [Calculation model](calculation-model.md) · [Dependency maintenance](../plugins/ygo-work/skills/ygo-implement/references/engine-extraction.md)

The `CardProbability` API describes a finite deck of physical copies, resolved set memberships, goals, and a closed set of optional operations. A host translates its domain into this model; the engine does not interpret card text, metadata, or catalog identities. It calculates synchronously and returns one terminal result.

The calculator references the published [CardProbability.Engine 1.0.0-preview.1](https://www.nuget.org/packages/CardProbability.Engine/1.0.0-preview.1) package. The [library repository](https://github.com/SeyTi01/card-probability-engine/tree/v1.0.0-preview.1) owns its implementation and neutral tests; the calculator retains its adapters, application integration tests and independent probability oracles. The [release](https://github.com/SeyTi01/card-probability-engine/releases/tag/v1.0.0-preview.1) identifies source commit `2c6a86246fb28e401fc2a2426233959478ab3d3f` and carries the package-consumer, transfer and calculator rehearsal evidence. The application project pins the version used by this checkout.

## Run the public examples

From the repository root with Node.js and the .NET 10 SDK installed:

```sh
node scripts/engine-boundary/check.mjs
```

This restores the pinned package from NuGet.org and compiles and runs [Consumer.cs](../scripts/engine-boundary/Consumer.cs) in a separate assembly with only the public `CardProbability` API. Its fictional fixtures independently check the examples below, runtime model metadata, terminal failures, collection ownership and concurrent reuse. The command also runs the [dependency boundary checks](../scripts/engine-boundary/README.md). It does not create or publish a package. Each C# snippet below belongs in a console program with the same `PackageReference`, `using CardProbability;`, and standard implicit usings enabled.

## Basic calculation

Ten physical copies contain two lanterns and eight stones. Deal seven and ask for at least one lantern:

```csharp
using CardProbability;

CalculationRequest request = new(
    entries: [new DeckEntry("lantern", 2), new DeckEntry("stone", 8)],
    handSize: 7,
    goals: [new Goal("light", [Requirement.OfEntry("lantern")])]
);
CalculationResult result = CalculationEngine.Calculate(request, new(CalculationMode.OpeningHand));
if (result.Status != CalculationStatus.Completed) {
    throw new InvalidOperationException(result.Detail);
}

Console.WriteLine($"Model {result.ModelVersion}: {result.Total!.Primary.Numerator}/{result.Total.Primary.Denominator}");
// Model 1: 14/15. Of C(10,7) = 120 hands, C(8,7) = 8 contain only stones.
```

`result.ModelVersion` is read-only runtime metadata from the engine assembly. A compiled consumer calls its instance getter rather than retaining an inlined public constant. The current value remains **1**; it identifies model semantics, independently of package, application session, effect-plan and worker-wire versions.

## Overlapping memberships and distinct copies

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

Consequently a Fast union-only result can have weaker `Accuracy`, `Certified` and `Certification` information than the full calculation, even with the same primary fraction. Use `Calculate` when optional physical certification matters.

The returned `Primary` is an exact rational for the selected primary model. For Fast results, inspect each row's `Accuracy` and `Certified` fields before treating it as a physical probability; an uncertified primary can be a projection. `Probability` is a convenience `double` for that primary, not the certified midpoint. A projected primary can lie outside its certified interval; even equal certified endpoints can establish a different physical fraction. Fractions are represented as decimal strings so hosts can transport arbitrarily large integers without numeric JSON loss.

## `(A OR B) AND C`

Put the OR choices in one `Alternative` and C in the required list. In this four-copy deck, the six pairs are ember+tide, ember+key, ember+blank, tide+key, tide+blank and key+blank. Only ember+key and tide+key succeed: **2/6 = 1/3**.

```csharp
CalculationRequest request = new(
    entries: [new("ember", 1), new("tide", 1), new("key", 1), new("blank", 1)],
    handSize: 2,
    goals: [new Goal(
        id: "route",
        requirements: [Requirement.OfEntry("key")],
        alternatives: [new Alternative([Requirement.OfEntry("ember"), Requirement.OfEntry("tide")])],
        groupId: "routes"
    )],
    groups: ["routes"]
);
CalculationResult result = CalculationEngine.Calculate(request);
if (result.Status != CalculationStatus.Completed) {
    throw new InvalidOperationException(result.Detail);
}
Console.WriteLine($"{result.Total!.Primary.Numerator}/{result.Total.Primary.Denominator}"); // 1/3
```

Choices **inside one `Alternative` are OR**. **Separate `Alternative` groups are AND**: moving C into a second group containing only `Requirement.OfEntry("key")` gives the same answer, as the consumer also checks. `Goal.GroupId` is unrelated to this logic: it assigns the completed goal to a reporting **union**. Two identical goals in `routes` still give 1/3 for the group, not 2/3.

## Draw, search and excavation

Each of these requests has one source, one target and two blanks, and deals one copy. Opening the target succeeds with probability 1/4. Opening the source also has probability 1/4: drawing one finds the target with probability 1/3, searching finds it with certainty, and revealing two finds it with probability 2/3. Thus the respective totals are **1/3**, **1/2**, and **5/12**.

```csharp
EffectPlan[] plans = [
    new(DrawCount: 1),
    new(Search: new SearchOperation(new Eligibility("targets"))),
    new(Excavation: new ExcavationOperation(2))
];
foreach (EffectPlan plan in plans) {
    CalculationRequest request = new(
        entries: [new("source", 1, plan), new("target", 1), new("blank", 2)],
        handSize: 1,
        goals: [new Goal("reach", [Requirement.OfEntry("target")])],
        memberships: [new Membership("targets", ["target"])]
    );
    CalculationResult result = CalculationEngine.Calculate(request); // Exact by default
    if (result.Status != CalculationStatus.Completed) {
        throw new InvalidOperationException(result.Detail);
    }
    Console.WriteLine($"{result.Total!.Primary.Numerator}/{result.Total.Primary.Denominator}");
}
```

The source is consumed on activation. `OpeningHand` returns 1/4 for all three valid plans because it suppresses execution. It still rejects invalid references and unsupported plan compositions. Failed effect branches stay in the original denominator.

## Constructing a request

- `DeckEntry` is a class of physical copies with one opaque ID and one multiplicity. Copies within that entry share an activation allowance; different entries do not. Split entries when their operations or allowance identity differ. Its optional `PolicyOrderKey` configures the deterministic goal-dependent keep/removal order; treat this as part of the modeled behavior when using effects that can remove cards.
- `Membership` resolves a caller-defined predicate to entry IDs. Memberships may overlap. An empty membership is valid. The engine uses the supplied sets and does not infer their meaning.
- `Goal` has required `Requirement`s, optional `Alternative` groups, and an optional group ID. All required requirements and alternative groups must be satisfied; one complete choice within each alternative group is enough. A group result is the union of its active goals.
- A requirement points to either an entry or membership and has inclusive minimum and optional maximum. A missing maximum means the evaluated hand size. Repeated references to the same target are combined by intersecting their bounds. Positive demands for different targets consume distinct copies; a single copy cannot fill two slots. A maximum counts every matching copy in the hand, including copies used for another requirement. A fixed `0/0` is valid and excludes every matching copy. There is no separate predicate-only minimum query apart from these slot-bearing requirements.
- The total result is the union of all goals. A physical outcome satisfying several goals contributes once. Each goal's optional search choices are evaluated for that goal; different goals can have different successful search-existence witnesses. Do not interpret goal rows as one globally optimal play sequence.

IDs must be nonblank and unique within each ID space (entries, memberships, goals, and groups). References to undeclared entries, memberships, or groups are invalid. IDs are ordinal and case-sensitive. `PolicyOrderKey` values are compared ordinally. Supply a key for every entry or for none; keys may be empty, and equal keys are valid. Keys sort the canonical policy partition before the existing ordinary-entry-before-effect-source order and ordinal entry-ID tie-breaks. Entries with equal keys can be merged only when their full goal and effect truth is equivalent. If all keys are omitted, the engine derives the generic order from ordinal membership and direct-entry requirement bit strings. A host may provide its own opaque keys to preserve a different domain's established deterministic policy; this does not expose domain metadata to the engine.

The request and nested collection inputs are copied into immutable arrays when constructed, so callers can reuse the resulting request. As with any constructor that enumerates caller-owned data, do not mutate an enumerable while it is being enumerated. The engine creates request-local solver state; the stateless API permits concurrent calls on an immutable request.

The API does not define a public JSON schema. Hosts choose their own transport and persistence contracts; adding runtime model metadata does not revise those contracts.

## Execution and outcomes

```csharp
CalculationOptions options = new(
    mode: CalculationMode.Fast,
    policy: new ExecutionPolicy(WorkUnits: 150_000_000, EntryLimit: 32_768, CellLimit: 262_144)
);

using CancellationTokenSource cancellation = new();
CancellationToken cancellationToken = cancellation.Token;
CalculationResult result = CalculationEngine.Calculate(request, options, cancellationToken);
```

The default mode is `Exact`, with unlimited Work and the default per-structure Storage limits. `Fast` defaults to 150,000,000 Work units when no policy is supplied; an explicit `new ExecutionPolicy()` uses unlimited Work. `OpeningHand` validates supplied plans but suppresses their execution and evaluates only the dealt hand. `OptimisticSearchProjection` is an explicit compatibility mode for hosts that need the historical optimistic search projection; it is not the ordinary default. Work is deterministic operation accounting, not elapsed time. Entry and 32-bit cell limits apply to individual live structures, including retained integer payload, not total process memory. All finite limits must be positive. Cancellation is cooperative through `CancellationToken`.

For priority ordering, set `activationOrder: ActivationOrder.Priority` and `priorityEntryIds: ["first-source", "second-source"]` using declared, unique entry IDs. The first entry has highest priority; unlisted entries follow, with encounter order breaking ties. Priorities operate separately within draws and searches, never across phases. Excavation uses its fixed largest-count/entry-ID rule. `PolicyOrderKey` is a different, advanced control for deterministic removal ordering, not activation priority or cosmetic sorting.

Each call returns a terminal `CalculationResult`; there is no preliminary-result status in this API. `Completed` contains rows and an overall accuracy. Failures (`Cancelled`, `InvalidInput`, `Unsupported`, `WorkLimit`, and `StorageLimit`) contain no probability rows. A host that offers a staged experience such as Progressive must own stage coordination, retain and label preliminary values, guard stale requests, and manage worker leases and cancellation.

| Status | Consumer action |
| --- | --- |
| `Completed` | Read rows and their proof; optional certification may still have been limited or omitted. |
| `Cancelled` | Keep cancellation separate from a numerical result. |
| `InvalidInput` | Correct IDs, references, counts, bounds or options using `Detail` as human-readable context. |
| `Unsupported` | Change the requested model; more Work or Storage cannot add support. |
| `WorkLimit` | Choose a larger or unlimited work allowance, simplify deliberately, or stop. |
| `StorageLimit` | Consider a larger per-structure allowance or a simpler request. |

Failure results have null `Total`/`Accuracy` and empty `Goals`/`Groups`. `Detail` and `CertificationDetail` are explanations, not machine-readable codes; branch on the enums. Constructors enumerate inputs and may throw for null or failing enumerables before a calculation is invoked.

`ModelScope` distinguishes `OpeningHand` from `FixedOrderEffects`. `ProbabilityValue.Primary` is the primary rational. `Certified` is either absent or an interval of rational endpoints; exact proof is represented by equal endpoints. Accuracy is explicit as `Exact`, `Bounded`, or `Uncertified`. A completed `Fast` result may be exact, bounded, or uncertified. A completed `Exact` result is physical-exact within the supported model; unsupported or exhausted work/storage is reported as a failure instead of an Exact fallback.

`CalculationResult.Certification` describes the separate optional search-availability certificate attempt. `Exact` and `Bounded` mean a certificate was produced; `NotApplicable` means there were no active Deck searches in the executed model; `NotCertified` means no usable certificate was established; `Limited` and `Error` mean the optional certificate work stopped at a resource limit or error. `NotRequired` means the primary calculation itself established an exact result, so optional certification was unnecessary. Read this alongside `Status`, overall `Accuracy`, and each row's `Certified` interval: optional certification can be limited or fail after a valid primary result completed, and that does not turn the completed primary into a calculation failure. `CalculateUnion` omits the optional certificate and reports structural exactness when the primary route proves it.

`CalculationEngine.Assess` returns conservative structural guidance (`ElevatedRisk`), not a completion prediction or Work estimate. **False means no warning was produced**: invalid, unsupported and assessment-budget-limited inputs can also return false. Assessment has its own bounded budget and is not input validation or a guarantee that the calculation will finish. Always handle the terminal result.

## Supported operations and boundaries

`EffectPlan` is a closed declarative instruction set: exactly one draw, search, or excavation operation per source. It has no callbacks. Draw plans can express activation limits, hand and hidden-deck costs, post-draw removals and hidden milling, first-activation rules, and draw-blocking constraints. Search plans can express target eligibility, alternative modes, and mode activation limits. Excavation can reveal a fixed positive number and keep one, with an optional no-prior-effect-draw condition.

Use `DeckEntry.Effect = null` for an ordinary entry; `new EffectPlan()` with no operation is unsupported. The following table describes legal field combinations; unspecified fields keep their defaults.

| Field | Draw | Search | Excavation |
| --- | --- | --- | --- |
| Operation | Positive `DrawCount`, other operations null | Non-null `Search`, other operations null | Non-null `Excavation`, other operations null |
| `ActivationLimit` | Positive integer or null | Positive integer or null | Exactly 1 |
| `HandCost`, `RemovalCount`, `MinimumHandSize` | Nonnegative | Nonnegative | Zero |
| `HandCostEligibility`, `RemovalEligibility` | Null/Any, or a declared membership with a positive corresponding count | Same as draw | Null/Any |
| `HiddenDeckCost`, `HiddenPostDrawMill` | Nonnegative | Zero | Zero |
| `FirstActivationOnly`, `BlocksLaterDraws` | Boolean | False | False |
| Operation-specific fields | Whole draw batch must fit | Targets and optional modes as below | Positive reveal `Count`; optional `RequiresNoPriorEffectDraw` |

`ActivationLimit` is **entry-wide**, shared by all physical copies of that entry. It defaults to one. `null` removes that cap, so another held or newly drawn source copy may activate; each activation still consumes one source and must pay all costs. Two separate entries have independent allowances even if their plans are equal. For example, deal two from two search-source copies plus two targets and require two targets: limit one succeeds in 5/6 hands (source+source fails), while null succeeds in all six. The consumer checks both cases.

Hand costs remove eligible other held copies before activation. `MinimumHandSize` counts the source before consumption. Draw activation requires the entire hidden deck cost + draw batch + post-draw mill to fit; unavailable sources remain held without paying costs or spending allowance. Legal draws finish consumption/costs, drawing, milling and mandatory removals before another source is chosen. Mandatory removals can remove newly acquired cards; insufficient eligible removal material fails the branch. Removal uses the shared deterministic goal-dependent keep policy. `FirstActivationOnly` allows a draw only before any draw effect has activated; `BlocksLaterDraws` closes the draw phase after that activation.

`SearchOperation.Targets` is non-null; `Eligibility.Any` selects every entry and `new Eligibility("membership")` selects a declared set. Empty `Modes` means no additional mode restrictions. With modes, the **resolved union of all mode target sets must exactly equal `Targets`**, including zero-copy entries. Each activation chooses one mode and one remaining physical target. A mode's positive `ActivationLimit` is shared across all source copies, in addition to the enclosing effect limit; null adds no mode cap. Hand costs, removals and minimum hand size are allowed on searches. Targets already held or lost cannot be fetched from the deck again.

Excavation is standalone and activates at most once overall, even with multiple source entries. The eligible held source with highest `Count` wins, with ordinal entry ID breaking ties. The full reveal must fit. `RequiresNoPriorEffectDraw` disables that source if an effect has already drawn cards; draws are never skipped to reserve it. The selected source is consumed even when none of the revealed choices helps, and unselected revealed copies remain available for ordinary searches.

Execution follows the fixed model: draws, at most one excavation, then optional searches. Search choices use only information available at that point. The engine preserves hand-wide maxima, distinct-copy requirements, physical copy competition, activation allowances, priority/encounter order, and deterministic keep/removal policy. That policy depends on the active goal set. The existence semantics for searches can choose different legal witnesses for different goals and do not describe a single optimal-play strategy. See [calculation model](calculation-model.md) for detailed ordering and gameplay exclusions.

Newly **drawn** effects may activate. Newly **searched or excavated** effects do not: earlier phases are closed, and search opportunities are restricted to sources held before the excavation addition. This is a deliberate single-pass model, not recursive effect execution. Search choices are optional; automatic draws and the selected excavation cannot be declined.

Not every composition of operation fields is supported. For example, a source cannot combine draw, search, and excavation instructions; excavation is standalone with one activation; and search mode target memberships must union to the operation's declared targets. Negative counts/bounds and unknown references return `InvalidInput`; a minimum above its maximum remains an impossible requirement. Unsupported compositions and solver capability limits return `Unsupported`. Callers must handle these outcomes rather than infer behavior from effect presence.

The total physical-copy count may be at most `Int32.MaxValue`. Internal packed-coordinate thresholds (including 255 and 64) select representations and are not public deck-size caps. Solver limits are exposed through Work and per-structure Storage policies. More resources do not make an unsupported operation supported.

## Runtime and compatibility

The package targets `net10.0`, uses the .NET 10 BCL, and has no external runtime package or embedded resource dependencies. Its assembly and public namespace are `CardProbability`. Model meanings and public signatures are library SemVer responsibilities; intentional API or semantic changes require a package-version decision. Runtime `ModelVersion` remains distinct from package, application session and effect-plan versions.

The application adapter remains responsible for resolving domain selectors and effect definitions into this neutral model, preserving saved plans and historical session behavior, and translating results into UI/worker contracts. It also preserves the original sorted atomic category/direct-entry memberships in opaque `PolicyOrderKey` values, so package consumption retains the calculator's deterministic keep/removal behavior. UI, worker envelopes and JavaScript exports, Progressive coordination, leases, cancellation ownership and stale-result rejection remain in the calculator. The engine carries its own [MIT license](https://github.com/SeyTi01/card-probability-engine/blob/v1.0.0-preview.1/LICENSE); that software license does not grant rights to artwork or card/catalog data.
