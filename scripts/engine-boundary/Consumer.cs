using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using CardProbability;

// Independent, fictional fixtures. This file is compiled in its own assembly,
// with only a ProjectReference to the copied engine and no privileged access.
List<DeckEntry> entries = [new("prism", 1), new("ember", 1), new("tide", 1), new("blank", 2)];
List<string> redEntries = ["prism", "ember"];
List<Requirement> distinctRequirements = [Requirement.InMembership("red"), Requirement.InMembership("blue")];
CalculationRequest overlap = new(
    entries,
    2,
    [new Goal("distinct", distinctRequirements, groupId: "shared"), new Goal("prism", [Requirement.OfEntry("prism")], groupId: "shared")],
    [new Membership("red", redEntries), new Membership("blue", ["prism", "tide"])],
    ["shared"]
);
// Constructors snapshot caller-owned mutable collections all the way down.
entries.Clear();
redEntries.Clear();
distinctRequirements.Clear();
CalculationResult overlapResult = Completed(overlap, CalculationMode.OpeningHand);
// Enumerate physical pairs independently: ab, ac, bc fill distinct red/blue
// slots; the prism goal has four pairs; their union adds only bc = five pairs.
int distinctHands = 0;
int prismHands = 0;
int unionHands = 0;
int exclusionHands = 0;
string[] physicalDeck = ["prism", "ember", "tide", "blank-1", "blank-2"];
for (int first = 0; first < physicalDeck.Length; first++) {
    for (int second = first + 1; second < physicalDeck.Length; second++) {
        string a = physicalDeck[first];
        string b = physicalDeck[second];
        bool distinct = (Red(a) && Blue(b)) || (Blue(a) && Red(b));
        bool prism = a == "prism" || b == "prism";
        bool exclusion = (Red(a) || Red(b)) && !Blue(a) && !Blue(b);
        distinctHands += distinct ? 1 : 0;
        prismHands += prism ? 1 : 0;
        unionHands += distinct || prism ? 1 : 0;
        exclusionHands += exclusion ? 1 : 0;
    }
}
Fraction(overlapResult.Goals[0].Value, distinctHands, 10, "distinct copies");
Fraction(overlapResult.Goals[1].Value, prismHands, 10, "overlap goal");
Fraction(overlapResult.Total!, unionHands, 10, "overlap-safe total union");
Fraction(overlapResult.Groups.Single().Value, unionHands, 10, "overlap-safe group union");
Union(overlap, CalculationMode.OpeningHand, unionHands, 10, "public union-only opening hand");
CalculationRequest exclusions = new(
    overlap.Entries,
    2,
    [new Goal("exclusion", [Requirement.InMembership("red", 1, 1), Requirement.InMembership("blue", 0, 0)])],
    overlap.Memberships
);
Fraction(Completed(exclusions, CalculationMode.OpeningHand).Total!, exclusionHands, 10, "hand-wide zero maximum");

CalculationRequest seven = new([new("lantern", 2), new("stone", 8)], 7, [new Goal("light", [Requirement.OfEntry("lantern")])]);
// Complement: all-stone hands C(8,7), out of C(10,7).
Fraction(Completed(seven, CalculationMode.OpeningHand).Total!, Choose(10, 7) - Choose(8, 7), Choose(10, 7), "seven-card opening hand");

// Four physical cards, one-card opening hand. Natural target = 1/4.
// Source = 1/4. Draw finds target 1/3, search finds it certainly,
// and a two-card excavation finds it 2/3. Failed branches stay in denominator.
Effect("draw", new EffectPlan(DrawCount: 1), 1, 3);
Effect("search", new EffectPlan(Search: new SearchOperation(new Eligibility("targets"))), 1, 2);
Effect("excavation", new EffectPlan(Excavation: new ExcavationOperation(2)), 5, 12);

using CancellationTokenSource cancelled = new();
cancelled.Cancel();
Failure(CalculationEngine.Calculate(overlap, cancellationToken: cancelled.Token), CalculationStatus.Cancelled);
Failure(CalculationEngine.Calculate(new([new("duplicate", 1), new("duplicate", 1)], 1, [])), CalculationStatus.InvalidInput);
Failure(CalculationEngine.Calculate(new([new("unsupported", 1, new EffectPlan())], 1, [])), CalculationStatus.Unsupported);
Failure(CalculationEngine.Calculate(overlap, new(policy: new ExecutionPolicy(WorkUnits: 1))), CalculationStatus.WorkLimit);
Failure(CalculationEngine.Calculate(overlap, new(policy: new ExecutionPolicy(EntryLimit: 1, CellLimit: 1))), CalculationStatus.StorageLimit);
Parallel.For(0, 8, _ => Fraction(Completed(overlap, CalculationMode.OpeningHand).Total!, 1, 2, "concurrent immutable request reuse"));

Assembly engine = typeof(CalculationEngine).Assembly;
Require(engine.GetCustomAttributes<InternalsVisibleToAttribute>().Select(attribute => attribute.AssemblyName).SequenceEqual(["CardProbability.EngineTests"]), "only neutral engine tests have friend access; consumer and app tests do not");
Require(engine.GetManifestResourceNames().Length == 0, "isolated engine has no embedded resource dependency");
Require(engine.GetExportedTypes().All(type => type.Namespace == "CardProbability"), "only intentional public namespace is exported");
Require(engine.GetReferencedAssemblies().All(name => name.Name == "System" || name.Name!.StartsWith("System.", StringComparison.Ordinal)), "only BCL assembly dependencies");
Console.WriteLine("Public-only consumer passed: independent fractions, overlap, exclusions, hand seven, draw/search/excavation, snapshots, concurrency and terminal failures.");

static bool Red(string id) => id is "prism" or "ember";
static bool Blue(string id) => id is "prism" or "tide";

static CalculationResult Completed(CalculationRequest request, CalculationMode mode) {
    CalculationResult result = CalculationEngine.Calculate(request, new(mode));
    Require(result.Status == CalculationStatus.Completed && result.IsTerminal, $"completed terminal result: {result.Status}: {result.Detail}");
    Require(result.Accuracy == ProbabilityAccuracy.Exact, "exact calculation");
    Require(result.Scope == (mode == CalculationMode.OpeningHand ? ModelScope.OpeningHand : ModelScope.FixedOrderEffects), "model scope");
    return result;
}

static void Effect(string name, EffectPlan plan, long numerator, long denominator) {
    CalculationRequest request = new(
        [new("source", 1, plan), new("target", 1), new("blank", 2)],
        1,
        [new Goal("reach", [Requirement.OfEntry("target")])],
        [new Membership("targets", ["target"])]
    );
    Fraction(Completed(request, CalculationMode.Exact).Total!, numerator, denominator, name);
    Union(request, CalculationMode.Exact, numerator, denominator, $"public union-only {name}");
}

static void Union(CalculationRequest request, CalculationMode mode, long numerator, long denominator, string name) {
    CalculationResult result = CalculationEngine.CalculateUnion(request, new(mode));
    Require(result.Status == CalculationStatus.Completed && result.IsTerminal, $"{name}: terminal completion");
    Require(result.Goals.IsEmpty && result.Groups.IsEmpty, $"{name}: union-only output");
    Fraction(result.Total!, numerator, denominator, name);
}

static void Fraction(ProbabilityValue value, BigInteger numerator, BigInteger denominator, string name) {
    BigInteger divisor = BigInteger.GreatestCommonDivisor(numerator, denominator);
    RationalProbability expected = new((numerator / divisor).ToString(), (denominator / divisor).ToString());
    Require(value.Primary == expected, $"{name}: expected {expected}, got {value.Primary}");
    Require(value.Accuracy == ProbabilityAccuracy.Exact && value.Certified == new CertifiedInterval(expected, expected), $"{name}: exact physical certification");
    Require(Math.Abs(value.Probability - (double)numerator / (double)denominator) < 1e-12, $"{name}: scalar agrees with fraction");
}

static BigInteger Choose(int total, int count) {
    BigInteger result = BigInteger.One;
    for (int index = 1; index <= count; index++) {
        result = result * (total - index + 1) / index;
    }
    return result;
}

static void Failure(CalculationResult result, CalculationStatus status) {
    Require(result.Status == status && result.IsTerminal, $"failure status {status}, got {result.Status}");
    Require(result.Total is null && result.Accuracy is null && result.Goals.IsEmpty && result.Groups.IsEmpty, "failure contains no probability rows");
}

static void Require(bool condition, string description) {
    if (!condition) {
        throw new InvalidOperationException(description);
    }
}
