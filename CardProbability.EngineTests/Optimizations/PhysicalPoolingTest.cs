using System.Numerics;
using CardProbability.EngineTests.Fixtures;
using CardProbability.Internal;
using CardProbability.Internal.DrawResolution;
using CardProbability.Internal.Resources;
using CardProbability.Internal.Search;
using Core = CardProbability.Internal.Contracts;

namespace CardProbability.EngineTests.Optimizations;

[TestFixture, NonParallelizable]
public sealed class PhysicalPoolingTest {
    [Test]
    public void PooledFillersPreserveBoundedSearchAndHiddenLossAcrossPhysicalRoutes() {
        DeckEntry[] entries = [new("source", 1, new(Search: new(new("target")), HandCost: 1, HandCostEligibility: new("cost"))),
            new("target", 1), new("cost", 1), new("draw", 1, new(DrawCount: 1, ActivationLimit: null, HiddenDeckCost: 1)),
            new("filler-a", 1), new("filler-b", 1)];
        EngineCase fixture = new("bounded-search", new(entries, 2, [new("target", [Requirement.InMembership("target", 1, 1)], groupId: "g")],
            [new("target", ["target"]), new("cost", ["cost"])], ["g", "empty"]), new());
        OracleModel model = new(fixture.Request);
        int[] order = Enumerable.Range(0, entries.Length).ToArray();
        BigInteger outcomes = 0;
        BigInteger wins = 0;
        Enumerate(0);
        Assert.That(outcomes, Is.EqualTo(new BigInteger(720)));
        Assert.That(wins, Is.EqualTo(new BigInteger(352)));
        foreach (PhysicalEnumerationMethod method in new[] { PhysicalEnumerationMethod.Streaming, PhysicalEnumerationMethod.BatchStreaming,
            PhysicalEnumerationMethod.SingleSource, PhysicalEnumerationMethod.Frontier }) {
            foreach (bool pool in new[] { false, true }) {
                SearchCountingDiagnostics diagnostics = new();
                WorkBudget budget = new(Core.CalculationWorkPolicy.Unlimited) { SearchDiagnostics = diagnostics };
                ExactCalculationResult result = PhysicalEnumerationTest.Calculate(fixture, method, budget, out PostDrawCalculation calculation, pool);
                Assert.That(calculation.ReducedRows, Is.EqualTo(pool ? 5 : 6));
                if (method == PhysicalEnumerationMethod.SingleSource) {
                    Assert.That(diagnostics.Counts.GetValueOrDefault("primary/physical single-source frames"), Is.EqualTo(2));
                }
                PhysicalEnumerationTest.AssertRows(result, [wins, wins, wins, BigInteger.Zero], outcomes, "bounded/" + method + "/pool=" + pool);
            }
        }

        void Enumerate(int position) {
            if (position == order.Length) {
                outcomes++;
                List<DeckEntry>? hand = DrawSequenceOracle.Resolve(fixture, model, order.Select(index => entries[index]).ToArray(), out IReadOnlyList<DeckEntry> remaining);
                if (hand is not null && (hand.Any(entry => entry.Id == "target") ||
                    (hand.Any(entry => entry.Id == "source") && hand.Any(entry => entry.Id == "cost") && remaining.Any(entry => entry.Id == "target")))) {
                    wins++;
                }
                return;
            }
            for (int index = position; index < order.Length; index++) {
                (order[position], order[index]) = (order[index], order[position]);
                Enumerate(position + 1);
                (order[position], order[index]) = (order[index], order[position]);
            }
        }
    }

    [Test]
    public void FiveCardOpenerPreservesPooledPhysicalMassAcrossAllRoutes() {
        EngineCase fixture = FiveCard();
        SearchCertificateOracle.Counts oracle = SearchCertificateOracle.Enumerate(fixture);
        foreach (PhysicalEnumerationMethod method in new[] { PhysicalEnumerationMethod.Streaming, PhysicalEnumerationMethod.BatchStreaming, PhysicalEnumerationMethod.Frontier }) {
            foreach (bool pool in new[] { false, true }) {
                ExactCalculationResult result = PhysicalEnumerationTest.Calculate(fixture, method, new(Core.CalculationWorkPolicy.Unlimited), out PostDrawCalculation execution, pool);
                Assert.That(execution.ReducedRows, Is.EqualTo(pool ? 5 : 6));
                PhysicalEnumerationTest.AssertRows(result, oracle.Wins[1], oracle.Outcomes, "five-card/" + method + "/pool=" + pool);
            }
        }
    }

    [Test]
    public void PooledCopiesHonorWorkAndRetainedStorageBoundaries() {
        EngineCase fixture = FiveCard();
        WorkBudget measured = new(Core.CalculationWorkPolicy.Unlimited);
        ExactCalculationResult expected = PhysicalEnumerationTest.Calculate(fixture, PhysicalEnumerationMethod.BatchStreaming, measured, out _, pool: true);
        Core.CalculationStoragePolicy storage = PhysicalEnumerationTest.Storage(measured.PeakEntries, measured.PeakCells);
        ExactCalculationResult actual = PhysicalEnumerationTest.Calculate(fixture, PhysicalEnumerationMethod.BatchStreaming, new(new(measured.Spent), storage), out _, pool: true);
        Assert.That(PhysicalEnumerationTest.Rows(actual), Is.EqualTo(PhysicalEnumerationTest.Rows(expected)));
        Core.ProbabilityCalculationLimitException workFailure = Assert.Throws<Core.ProbabilityCalculationLimitException>(() =>
            PhysicalEnumerationTest.Calculate(fixture, PhysicalEnumerationMethod.BatchStreaming, new(new(measured.Spent - 1), storage), out _, pool: true))!;
        Assert.That(workFailure.Reason, Is.EqualTo(Core.ProbabilityCalculationLimitReason.Work));
        foreach (bool cells in new[] { false, true }) {
            Core.CalculationStoragePolicy tooSmall = PhysicalEnumerationTest.Storage(measured.PeakEntries - (cells ? 0 : 1), measured.PeakCells - (cells ? 1 : 0));
            Core.ProbabilityCalculationLimitException failure = Assert.Throws<Core.ProbabilityCalculationLimitException>(() =>
                PhysicalEnumerationTest.Calculate(fixture, PhysicalEnumerationMethod.BatchStreaming, new(Core.CalculationWorkPolicy.Unlimited, tooSmall), out _, pool: true))!;
            Assert.That(failure.Reason, Is.EqualTo(Core.ProbabilityCalculationLimitReason.Storage));
        }
    }

    [Test]
    public void AdjacentOrdinaryClassesMatchIndependentLabeledOrdersAcrossOrderedPolicies() {
        foreach (EngineCase original in SearchCertificateFixtures.OrderedCoverage()) {
            // Adjacent opaque entries have identical predicate incidence. They
            // remain different public identities but share a solver quotient.
            DeckEntry[] entries = [.. original.Request.Entries.SelectMany(entry => entry.Id == "t"
                ? new[] { entry with { Copies = 1 }, new DeckEntry("t-alias", 1) } : new[] { entry })];
            Membership[] memberships = original.Request.Memberships.Select(membership => new Membership(membership.Id,
                membership.EntryIds.Contains("t") ? membership.EntryIds.Append("t-alias") : membership.EntryIds)).ToArray();
            Goal[] goals = original.Request.Goals.Where(goal => goal.Requirements.All(requirement => requirement.Kind != RequirementTargetKind.Entry)).ToArray();
            EngineCase fixture = original with { Request = new(entries, original.Request.HandSize, goals, memberships, original.Request.Groups) };
            SearchCertificateOracle.Counts oracle = SearchCertificateOracle.Enumerate(fixture);
            bool pooledAtLeastOneRoute = false;
            foreach (PhysicalEnumerationMethod method in new[] { PhysicalEnumerationMethod.Streaming, PhysicalEnumerationMethod.BatchStreaming, PhysicalEnumerationMethod.Frontier }) {
                foreach (bool pool in new[] { false, true }) {
                    ExactCalculationResult result = PhysicalEnumerationTest.Calculate(fixture, method, new(Core.CalculationWorkPolicy.Unlimited), out PostDrawCalculation execution, pool);
                    PhysicalEnumerationTest.AssertRows(result, oracle.Wins[1], oracle.Outcomes, fixture.Name + "/pool=" + pool + "/" + method);
                    pooledAtLeastOneRoute |= pool && execution.ReducedRows < entries.Length;
                }
            }
            Assert.That(pooledAtLeastOneRoute, Is.True, fixture.Name + ": the candidate must exercise actual pooling");
        }
    }

    [TestCase("resolved-predicate"), TestCase("resolved-eligibility"), TestCase("mode-incidence")]
    public void DifferentResolvedObservationsCannotBePooled(string variant) {
        DeckEntry source = variant == "mode-incidence"
            ? new("source", 2, new(ActivationLimit: 2, Search: new(new("targets"), [new(new("left"), 1), new(new("right"), 1)])))
            : new("source", 1, new(Search: new(new(variant == "resolved-eligibility" ? "eligible" : "targets"))));
        CalculationRequest request = new([source, new("low", 1), new("high-a", 1), new("high-b", 1), new("filler", 1)], 2,
            [new("one", [Requirement.InMembership("high", 1, 1)], groupId: "g"),
                new("zero", [Requirement.InMembership("high", 0, 0)], groupId: "g"),
                new("two", [Requirement.InMembership("targets", 2, 2)])],
            [new("high", ["high-a", "high-b"]), new("targets", ["low", "high-a", "high-b"]),
                new("eligible", ["high-a"]), new("left", ["low", "high-a"]), new("right", ["high-b"])], ["g", "empty"]);
        EngineCase fixture = new(variant, request, new());
        SearchCertificateOracle.Counts oracle = SearchCertificateOracle.Enumerate(fixture);
        foreach (bool pool in new[] { false, true }) {
            foreach (PhysicalEnumerationMethod method in new[] { PhysicalEnumerationMethod.Streaming, PhysicalEnumerationMethod.BatchStreaming }) {
                ExactCalculationResult result = PhysicalEnumerationTest.Calculate(fixture, method, new(Core.CalculationWorkPolicy.Unlimited), out _, pool);
                PhysicalEnumerationTest.AssertRows(result, oracle.Wins[1], oracle.Outcomes, variant + "/pool=" + pool);
            }
        }
    }

    [Test]
    public void NonAdjacentEquivalentTargetsCannotCrossAPaymentTieBoundary() {
        DeckEntry[] entries = [new("a", 1), new("m", 1), new("z", 1),
            new("draw", 1, new(DrawCount: 1, RemovalCount: 1, RemovalEligibility: new("material"))),
            new("pay", 1, new(DrawCount: 1, HandCost: 1, HandCostEligibility: new("payment"))),
            new("search", 1, new(Search: new(new("material")))), new("filler", 2)];
        CalculationRequest request = new(entries, 4, [new("pay-absent", [Requirement.OfEntry("pay", 0, 0)], groupId: "g"),
            new("material-absent", [Requirement.InMembership("material", 0, 0)], groupId: "g")],
            [new("material", ["a", "m", "z"]), new("payment", ["m"])], ["g", "empty"]);
        EngineCase fixture = new("non-adjacent", request, new(activationOrder: ActivationOrder.Priority, priorityEntryIds: ["draw", "pay"]));
        DeckEntry Find(string id) => entries.Single(entry => entry.Id == id);
        List<DeckEntry>? witness = DrawSequenceOracle.Resolve(fixture, new(request),
            [Find("draw"), Find("pay"), Find("m"), Find("z"), Find("filler"), Find("filler"), Find("a"), Find("search")], out _);
        Assert.That(witness, Is.Not.Null);
        Assert.That(witness!.Any(entry => entry.Id == "pay"), Is.False);
        SearchCertificateOracle.Counts oracle = SearchCertificateOracle.Enumerate(fixture);
        foreach (PhysicalEnumerationMethod method in new[] { PhysicalEnumerationMethod.Streaming, PhysicalEnumerationMethod.BatchStreaming, PhysicalEnumerationMethod.Frontier }) {
            ExactCalculationResult result = PhysicalEnumerationTest.Calculate(fixture, method, new(Core.CalculationWorkPolicy.Unlimited), out PostDrawCalculation execution, pool: true);
            Assert.That(execution.ReducedRows, Is.EqualTo(entries.Length));
            PhysicalEnumerationTest.AssertRows(result, oracle.Wins[1], oracle.Outcomes, "payment boundary/" + method);
        }
    }

    [Test]
    public void WideMembershipCopiesHonorWorkAndRetainedStorageBoundaries() {
        Membership[] memberships = [.. Enumerable.Range(0, 128).Select(index => new Membership("first-" + index, ["a-rich"])),
            .. Enumerable.Range(0, 96).Select(index => new Membership("second-" + index, ["y-rich"]))];
        EngineCase fixture = new("wide-membership", new([new("a-rich", 1), new("b-empty", 1),
            new("m-source", 1, new(Search: new(Eligibility.Any))), new("y-rich", 1), new("z-empty", 1)],
            0, [new("completed", [])], memberships), new());
        WorkBudget measured = new(Core.CalculationWorkPolicy.Unlimited);
        ExactCalculationResult expected = Run(measured, out PostDrawCalculation execution);
        Assert.That(expected.Total.Numerator, Is.EqualTo(expected.Total.Denominator));
        Assert.That(execution.ReducedRows, Is.EqualTo(3));
        Assert.That(execution.LargestStructure, Is.EqualTo("physical row partition"));
        Assert.That(measured.PeakCells, Is.GreaterThan(2L * (128 + 96)));
        Core.CalculationStoragePolicy enough = PhysicalEnumerationTest.Storage(Core.CalculationStoragePolicy.Standard.EntryLimit, measured.PeakCells);
        Assert.That(Run(new(new(measured.Spent), enough), out _).Total, Is.EqualTo(expected.Total));
        Core.ProbabilityCalculationLimitException workFailure = Assert.Throws<Core.ProbabilityCalculationLimitException>(() =>
            Run(new(new(measured.Spent - 1), enough), out _))!;
        Assert.That(workFailure.Reason, Is.EqualTo(Core.ProbabilityCalculationLimitReason.Work));
        PostDrawCalculation rejected = null!;
        Core.ProbabilityCalculationLimitException storageFailure = Assert.Throws<Core.ProbabilityCalculationLimitException>(() =>
            Run(new(Core.CalculationWorkPolicy.Unlimited, PhysicalEnumerationTest.Storage(Core.CalculationStoragePolicy.Standard.EntryLimit, measured.PeakCells - 1)), out rejected))!;
        Assert.That(storageFailure.Reason, Is.EqualTo(Core.ProbabilityCalculationLimitReason.Storage));
        Assert.That(rejected.LargestStructure, Is.EqualTo("physical row partition"));

        ExactCalculationResult Run(WorkBudget budget, out PostDrawCalculation calculation) =>
            PhysicalEnumerationTest.Calculate(fixture, PhysicalEnumerationMethod.Streaming, budget, out calculation, pool: true);
    }

    private static EngineCase FiveCard() => new("five-card", new([
        new("search", 1, new(Search: new(new("a")))), new("aa", 1), new("az", 2), new("middle", 1),
        new("draw", 1, new(DrawCount: 1, HiddenDeckCost: 1, RemovalCount: 1)), new("filler", 1)
    ], 5, [new("one-two", [Requirement.InMembership("a", 1, 2)], groupId: "positive"),
        new("distinct", [Requirement.InMembership("a", 1, 3), Requirement.InMembership("b", 1, 1)], groupId: "positive"),
        new("zero", [Requirement.InMembership("a", 0, 0)], groupId: "zero"),
        new("direct-zero", [Requirement.OfEntry("middle", 0, 0)], groupId: "zero"), new("completed", [])],
        [new("a", ["aa", "az"]), new("b", ["middle"])], ["positive", "zero", "empty"]), new());
}
