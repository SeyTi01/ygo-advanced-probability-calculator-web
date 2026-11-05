using System.Numerics;
using CardProbability.EngineTests.Fixtures;
using CardProbability.Internal;
using CardProbability.Internal.Counting;
using CardProbability.Internal.Certification;
using CardProbability.Internal.DrawResolution;
using CardProbability.Internal.Resources;
using CardProbability.Internal.Search;
using Core = CardProbability.Internal.Contracts;

namespace CardProbability.EngineTests.Optimizations;

[TestFixture, NonParallelizable]
public sealed class StreamedOrderedSearchTest {
    [Test]
    public void PrimaryWitnessAndStrongerBoundsMatchOldFastAndContainPhysicalTruth() {
        foreach (EngineCase fixture in SearchCertificateFixtures.Fixtures()) {
            ExactCalculationResult[] results = AssertParity(fixture);
            SearchCertificateOracle.Counts oracle = SearchCertificateOracle.Enumerate(fixture);
            ExactProbability[][] rows = results.Select(PhysicalEnumerationTest.Rows).ToArray();
            for (int row = 0; row < rows[0].Length; row++) {
                Assert.That(rows[0][row].Numerator * oracle.Outcomes, Is.GreaterThanOrEqualTo(oracle.Wins[1][row] * rows[0][row].Denominator), fixture.Name + "/upper");
                Assert.That(rows[2][row].Numerator * oracle.Outcomes, Is.LessThanOrEqualTo(oracle.Wins[1][row] * rows[2][row].Denominator), fixture.Name + "/lower");
                Assert.That(rows[1][row].Numerator * oracle.Outcomes, Is.EqualTo(oracle.Wins[4][row] * rows[1][row].Denominator), fixture.Name + "/witness");
            }
        }
    }

    [TestCase(3, 40), TestCase(3, 60), TestCase(4, 40), TestCase(4, 60)]
    [TestCase(5, 40), TestCase(5, 60), TestCase(6, 40), TestCase(6, 60), TestCase(7, 24)]
    public void ScaledHandsKeepAllFractionsAndTerminalMass(int hand, int size) => AssertParity(Create(hand, size), true);

    [TestCase(1, "opt"), TestCase(2, "opt"), TestCase(3, "opt")]
    [TestCase(1, "limit"), TestCase(2, "limit"), TestCase(3, "limit")]
    [TestCase(1, "first"), TestCase(2, "first")]
    public void MultipleSourceCopiesPreserveAllRowsAndCertificatePartitions(int copies, string allowance) {
        EngineCase original = Create(3, 9);
        DeckEntry[] entries = original.Request.Entries.Select(entry => entry.Id == "draw" ? entry with {
            Copies = copies, Effect = entry.Effect! with { ActivationLimit = allowance == "first" ? null : 1,
                FirstActivationOnly = allowance == "first" }
        } : entry).ToArray();
        EngineCase fixture = Replace(original, entries);
        ExactCalculationResult[] results = AssertParity(fixture, true);
        SearchCertificateOracle.Counts oracle = SearchCertificateOracle.Enumerate(fixture);
        ExactProbability[][] rows = results.Select(PhysicalEnumerationTest.Rows).ToArray();
        for (int row = 0; row < rows[0].Length; row++) {
            Assert.That(rows[0][row].Numerator * oracle.Outcomes, Is.GreaterThanOrEqualTo(oracle.Wins[1][row] * rows[0][row].Denominator));
            Assert.That(rows[2][row].Numerator * oracle.Outcomes, Is.LessThanOrEqualTo(oracle.Wins[1][row] * rows[2][row].Denominator));
            Assert.That(rows[1][row].Numerator * oracle.Outcomes, Is.EqualTo(oracle.Wins[4][row] * rows[1][row].Denominator));
        }
    }

    [Test]
    public void TotalOnlyStillPreservesIndependentCertificateRows() {
        EngineCase original = Create(3, 18);
        EngineCase fixture = original with { Request = new(original.Request.Entries, 3, original.Request.Goals.SkipLast(1),
            original.Request.Memberships, original.Request.Groups) };
        ExactCalculationResult[] expected = AssertParity(fixture, true);
        PreparedCalculation prepared = fixture.Prepare();
        WorkBudget budget = new(Core.CalculationWorkPolicy.Interactive);
        PostDrawCalculation calculation = new(prepared.Cards, fixture.Request.HandSize, budget, deferIdentities: false);
        ExactCalculationResult total = calculation.CalculateSearch(prepared.Combos, prepared.Groups, true, new(prepared.Cards, budget));
        Assert.That(total.Total.ToFraction(), Is.EqualTo(expected[0].Total.ToFraction()));
        Assert.That(total.Combos, Is.Empty);
        Assert.That(total.Groups, Is.Empty);
        AssertRows(calculation.CalculateSearchLower(prepared.Combos, prepared.Groups, true), expected[1], "witness");
        AssertRows(calculation.CalculateSearchLower(prepared.Combos, prepared.Groups, false), expected[2], "lower");
    }

    [TestCase("first-opt-priority", true), TestCase("two-copies", true), TestCase("repeatable-copies", false)]
    [TestCase("two-sources", false), TestCase("cost", false), TestCase("depleted", true)]
    [TestCase("short-deck", false), TestCase("unavailable-discard", true), TestCase("minimum-too-high", false)]
    [TestCase("zero-copy", true), TestCase("discard-exceeds-hand", true)]
    public void GuardKeepsPolicyBoundariesAndScalarCapacity(string variant, bool stream) {
        if (variant == "discard-exceeds-hand") {
            EngineCase exhausted = new("discard-exceeds-hand", new([
                new("draw", 1, new(DrawCount: 1, RemovalCount: 2, HiddenPostDrawMill: 1)),
                new("search", 1, new(Search: new(Eligibility.Any))), new("filler", 1)
            ], 1, [new("target", [Requirement.InMembership("target", 1, 1)])], [new("target", ["filler"])]), new());
            AssertParity(exhausted, stream);
            return;
        }
        EngineCase original = Create(3, 9);
        List<DeckEntry> entries = original.Request.Entries.ToList();
        EffectPlan source = entries[0].Effect!;
        switch (variant) {
            case "first-opt-priority": source = source with { FirstActivationOnly = true, BlocksLaterDraws = true }; break;
            case "two-copies": entries[0] = entries[0] with { Copies = 2 }; break;
            case "repeatable-copies": entries[0] = entries[0] with { Copies = 2 }; source = source with { ActivationLimit = null }; break;
            case "two-sources": entries.Add(new("other", 1, new(DrawCount: 1))); break;
            case "cost": source = source with { HandCost = 1 }; break;
            case "depleted": source = source with { HiddenPostDrawMill = 4 }; break;
            case "short-deck": source = source with { HiddenPostDrawMill = 5 }; break;
            case "unavailable-discard": source = source with { RemovalEligibility = new("absent") }; break;
            case "minimum-too-high": source = source with { MinimumHandSize = 4 }; break;
            case "zero-copy": entries.Add(new("zero", 0, new(DrawCount: 1))); break;
        }
        entries[0] = entries[0] with { Effect = source };
        EngineCase fixture = Replace(original, entries);
        AssertParity(fixture, stream);
    }

    [Test]
    public void ResolvedPredicateDisagreementRemainsDistinctAndResultsAreNotReusedAcrossCalls() {
        EngineCase original = Create(3, 9);
        Goal[] goals = [new("resolved", [Requirement.InMembership("resolved", 1, 3)])];
        EngineCase fixture = original with { Request = new(original.Request.Entries, 3, goals,
            original.Request.Memberships.Append(new("resolved", ["both"])), original.Request.Groups) };
        ExactCalculationResult[] expected = AssertParity(fixture, true);
        PreparedCalculation prepared = fixture.Prepare();
        WorkBudget budget = new(Core.CalculationWorkPolicy.Unlimited);
        PostDrawCalculation reused = new(prepared.Cards, 3, budget, deferIdentities: false);
        reused.CalculateSearch([new([], "Completed")], [], false, new(prepared.Cards, budget));
        AssertRows(reused.CalculateSearch(prepared.Combos, prepared.Groups, false, new(prepared.Cards, budget)), expected[0], "reused primary");
        AssertRows(reused.CalculateSearchLower(prepared.Combos, prepared.Groups, true), expected[1], "reused witness");
        AssertRows(reused.CalculateSearchLower(prepared.Combos, prepared.Groups, false), expected[2], "reused lower");
    }

    [TestCase(1), TestCase(2)]
    public void OptionalRefinementLimitRetainsCompletedPrimaryAndWitness(int copies) {
        EngineCase original = Create(3, 18);
        EngineCase fixture = Replace(original, original.Request.Entries.Select(entry => entry.Id switch {
            "draw" => entry with { Copies = copies },
            "both" => entry with { Copies = 5 },
            _ => entry
        }));
        PreparedCalculation prepared = fixture.Prepare();
        WorkBudget measure = new(Core.CalculationWorkPolicy.Unlimited);
        DrawEffectCalculation measured = new(prepared.Cards, 3, measure);
        ExactCalculationResult expected = measured.Calculate(prepared.Combos, prepared.Groups);
        measured.DescribeCapability();
        ExactCalculationResult witness = measured.SearchExecution!.CalculateLower(prepared.Combos, prepared.Groups, true);
        long completed = measure.Spent;
        bool limited = false;
        for (long allowance = completed + 1000; allowance < completed + 10_000; allowance += 1000) {
            WorkBudget budget = new(new(allowance));
            DrawEffectCalculation calculation = new(prepared.Cards, 3, budget);
            ExactCalculationResult actual = calculation.Calculate(prepared.Combos, prepared.Groups);
            Core.SearchAvailabilityCertificate certificate = SearchAvailabilityCertification.TryCalculate(prepared.Combos, prepared.Groups, actual, budget, calculation);
            AssertRows(actual, expected, "completed primary");
            if (certificate.Detail.Contains("Limited refinement") && certificate.Total is not null && certificate.Total.Lower.Numerator != "0") {
                Assert.That(certificate.Total.Lower, Is.EqualTo(witness.Total.ToFraction()));
                Assert.That(certificate.Total.Upper, Is.EqualTo(expected.Total.ToFraction()));
                Assert.That(certificate.Combos.Select(row => row.Lower), Is.EqualTo(witness.Combos.Select(value => value.ToFraction())));
                Assert.That(certificate.Combos.Select(row => row.Upper), Is.EqualTo(expected.Combos.Select(value => value.ToFraction())));
                Assert.That(certificate.Groups.Select(row => row.Lower), Is.EqualTo(witness.Groups.Select(value => value.ToFraction())));
                Assert.That(certificate.Groups.Select(row => row.Upper), Is.EqualTo(expected.Groups.Select(value => value.ToFraction())));
                Assert.That(certificate.Status, Is.EqualTo(Core.SearchAvailabilityStatus.Bounded));
                limited = true;
                break;
            }
        }
        Assert.That(limited, Is.True);
    }

    [Test]
    public void StorageFallbackDiscardsPartialAbsentRowsAndRetainsTheirWork() {
        DeckEntry[] entries = [new("draw", 2, new(DrawCount: 2, RemovalCount: 1)),
            new("search-a", 1, new(Search: new(new("a")))), new("search-b", 1, new(Search: new(new("b")))),
            .. Enumerable.Range(1, 7).Select(mask => new DeckEntry("target-" + mask, 2)), new("inert", 4)];
        Membership[] memberships = Enumerable.Range(0, 3).Select(bit => new Membership(((char)('a' + bit)).ToString(),
            Enumerable.Range(1, 7).Where(mask => (mask & 1 << bit) != 0).Select(mask => "target-" + mask))).ToArray();
        Goal[] goals = [new("ab", [Requirement.InMembership("a", 1, 2), Requirement.InMembership("b", 1, 2)], groupId: "positive"),
            new("bc", [Requirement.InMembership("b", 1, 2), Requirement.InMembership("c", 1, 2)], groupId: "positive"),
            new("ac", [Requirement.InMembership("a", 1, 2), Requirement.InMembership("c", 1, 2)], groupId: "positive"),
            new("no-a", [Requirement.InMembership("a", 0, 0)], groupId: "exclusions"),
            new("no-b", [Requirement.InMembership("b", 0, 0)], groupId: "exclusions"),
            new("direct", [Requirement.OfEntry("target-1", 1, 1), Requirement.OfEntry("draw", 0, 0)], groupId: "exclusions")];
        EngineCase fixture = new("storage-fallback", new(entries, 2, goals, memberships, ["positive", "exclusions", "empty"]), new());
        WorkBudget referenceBudget = new(Core.CalculationWorkPolicy.Unlimited);
        ExactCalculationResult[] reference = Calculate(referenceBudget, true);
        Core.CalculationStoragePolicy policy = PhysicalEnumerationTest.Storage(referenceBudget.PeakEntries, Core.CalculationStoragePolicy.Standard.CellLimit);
        SearchCountingDiagnostics diagnostics = new();
        WorkBudget budget = new(Core.CalculationWorkPolicy.Unlimited, policy) { SearchDiagnostics = diagnostics };
        ExactCalculationResult[] actual = Calculate(budget, false);
        Assert.That(diagnostics.Counts.GetValueOrDefault("primary/streamed absent storage fallback"), Is.EqualTo(1));
        Assert.That(diagnostics.Counts.GetValueOrDefault("primary/streamed absent counted rows"), Is.GreaterThan(0));
        for (int pass = 0; pass < actual.Length; pass++) { AssertRows(actual[pass], reference[pass], "pass=" + pass); }
        Assert.That(budget.Spent, Is.GreaterThan(referenceBudget.Spent));
        Assert.That(budget.PeakEntries, Is.GreaterThan(policy.EntryLimit));

        ExactCalculationResult[] Calculate(WorkBudget work, bool observe) {
            PreparedCalculation prepared = fixture.Prepare();
            PostDrawCalculation calculation = new(prepared.Cards, 2, work, deferIdentities: false) { ObserveTerminal = observe ? (_, _) => { } : null };
            return Stages(calculation, prepared, work);
        }
    }

    private static EngineCase Create(int hand, int size) => new("scaled", new([
        new("draw", 1, new(DrawCount: 2, RemovalCount: 1, HiddenPostDrawMill: 1)),
        new("search-a", 1, new(Search: new(new("first")))), new("search-b", 1, new(Search: new(new("second")))),
        new("both", 2), new("first", 1), new("second", 1), new("filler", size - 7)
    ], hand, [
        new("distinct", [Requirement.InMembership("first", 1, hand), Requirement.InMembership("second", 1, hand)], groupId: "g"),
        new("same", [Requirement.InMembership("both", 1, hand)], groupId: "g"),
        new("exclude", [Requirement.InMembership("first", 0, 0)]),
        new("direct", [Requirement.OfEntry("first", 1, hand), Requirement.OfEntry("both", 0, 0)], groupId: "g"),
        new("fetched", [Requirement.OfEntry("search-b", 1, hand)]), new("completed", [])
    ], [new("first", ["draw", "search-b", "both", "first"]), new("second", ["both", "second"]),
        new("both", ["both"]), new("absent", [])], ["g", "empty"]), new());

    private static EngineCase Replace(EngineCase original, IEnumerable<DeckEntry> entries) => original with {
        Request = new(entries, original.Request.HandSize, original.Request.Goals, original.Request.Memberships, original.Request.Groups)
    };

    private static ExactCalculationResult[] AssertParity(EngineCase fixture, bool? expectStream = null) {
        Dictionary<string, BigInteger>[] terminal = [new(), new()];
        ExactCalculationResult[][] results = new ExactCalculationResult[2][];
        for (int side = 0; side < 2; side++) {
            int captured = side;
            SearchCountingDiagnostics diagnostics = new();
            WorkBudget budget = new(Core.CalculationWorkPolicy.Unlimited, Core.CalculationStoragePolicy.VeryHigh) { SearchDiagnostics = diagnostics };
            PreparedCalculation prepared = fixture.Prepare();
            PostDrawCalculation calculation = new(prepared.Cards, fixture.Request.HandSize, budget, PhysicalEnumerationTest.Priority(fixture), deferIdentities: false) {
                StreamSearchTerminals = side == 1,
                ObserveTerminal = (hand, mass) => { string key = string.Join(',', hand); terminal[captured][key] = terminal[captured].GetValueOrDefault(key) + mass; Array.Fill(hand, -1); }
            };
            results[side] = Stages(calculation, prepared, budget);
            if (side == 1 && expectStream is bool expected) {
                Assert.That(diagnostics.Counts.ContainsKey("primary/streamed search terminals"), Is.EqualTo(expected), fixture.Name);
            }
        }
        Assert.That(terminal[1], Is.EquivalentTo(terminal[0]), fixture.Name + "/terminal mass");
        for (int pass = 0; pass < results[0].Length; pass++) { AssertRows(results[1][pass], results[0][pass], fixture.Name + "/pass=" + pass); }
        return results[1];
    }

    private static ExactCalculationResult[] Stages(PostDrawCalculation calculation, PreparedCalculation prepared, WorkBudget budget) => [
        calculation.CalculateSearch(prepared.Combos, prepared.Groups, false, new(prepared.Cards, budget)),
        calculation.CalculateSearchLower(prepared.Combos, prepared.Groups, true),
        calculation.CalculateSearchLower(prepared.Combos, prepared.Groups, false),
        calculation.CalculateSearchLower(prepared.Combos, prepared.Groups, false, useKnownLoss: false)
    ];

    private static void AssertRows(ExactCalculationResult actual, ExactCalculationResult expected, string context) =>
        Assert.That(PhysicalEnumerationTest.Rows(actual).Select(value => value.ToFraction()),
            Is.EqualTo(PhysicalEnumerationTest.Rows(expected).Select(value => value.ToFraction())), context);
}
