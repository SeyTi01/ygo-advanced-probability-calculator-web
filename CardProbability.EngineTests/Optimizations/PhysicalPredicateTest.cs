using System.Numerics;
using CardProbability.EngineTests.Fixtures;
using CardProbability.Internal;
using CardProbability.Internal.Counting;
using CardProbability.Internal.DrawResolution;
using CardProbability.Internal.Search;
using CardProbability.Internal.Resources;
using Core = CardProbability.Internal.Contracts;

namespace CardProbability.EngineTests.Optimizations;

[TestFixture]
public sealed class PhysicalPredicateTest {
    [Test]
    public void GrowingCompilationBoundPreservesEverySmallerPhysicalHandAndNormalizedRoute() {
        DeckEntry[] entries = [new("ab", 2), new("a", 2), new("b", 1), new("f", 1)];
        Alternative alternatives = new([Requirement.InMembership("b", 2), Requirement.OfEntry("f", 1, 1)]);
        Goal[] goals = [new("empty", []), new("zero", [Requirement.InMembership("a", 0, 0)]),
            new("fixed", [Requirement.InMembership("a", 0, 2)]), new("dynamic", [Requirement.InMembership("a", 3)]),
            new("distinct", [Requirement.InMembership("a", 1, 6), Requirement.InMembership("b", 1, 6)]),
            new("same", [Requirement.InMembership("both", 1)]),
            new("direct-role", [Requirement.InMembership("a", 1, 6), Requirement.OfEntry("ab", 1, 6)]),
            new("ids", [Requirement.OfEntry("ab", 1, 6), Requirement.OfEntry("a", 0, 0)]),
            new("repeat-category", [Requirement.InMembership("a", 1, 6), Requirement.InMembership("a", 2, 2)]),
            new("repeat-direct", [Requirement.OfEntry("ab", 1, 6), Requirement.OfEntry("ab", 2, 2)]),
            new("repeat-or", [Requirement.InMembership("a", 1)], [alternatives, alternatives])];
        CalculationRequest request = new(entries, 0, goals, [new("a", ["ab", "a"]), new("b", ["ab", "b"]), new("both", ["ab"])]);
        OracleModel oracle = new(request);
        PreparedCalculation prepared = new EngineCase("growing-bound", request, new()).Prepare();
        SearchCountingDiagnostics diagnostics = new();
        WorkBudget budget = new(Core.CalculationWorkPolicy.Unlimited) { SearchDiagnostics = diagnostics };
        PhysicalSearchHandCounter counter = new(prepared.Cards, prepared.Combos, budget);
        int[] labels = entries.SelectMany((entry, row) => Enumerable.Repeat(row, entry.Copies)).ToArray();
        int largest = -1;
        int compilations = 0;
        foreach (int size in new[] { 0, 2, 1, 4, 3, 0, 6, 2 }) {
            if (size > largest) { largest = size; compilations++; }
            for (int subset = 0; subset < 1 << labels.Length; subset++) {
                if (BitOperations.PopCount((uint)subset) != size) { continue; }
                int[] counts = new int[entries.Length];
                List<DeckEntry> hand = [];
                for (int label = 0; label < labels.Length; label++) {
                    if ((subset & 1 << label) != 0) { counts[labels[label]]++; hand.Add(entries[labels[label]]); }
                }
                Assert.That(counter.Evaluate(counts, new int[entries.Length]), Is.EqualTo(goals.Select(goal => oracle.GoalMatches(hand, goal))),
                    $"size={size}, subset={subset}");
            }
            Assert.That(diagnostics.Counts.GetValueOrDefault("primary/physical event compilations"), Is.EqualTo(compilations));
        }
        Assert.That(budget.PeakCells, Is.LessThanOrEqualTo(Core.CalculationStoragePolicy.Standard.CellLimit));
    }

    [Test]
    public void ResolvedMembershipAndIntersectionKeepMeaningBelowCompiledHandBound() {
        CalculationRequest request = new([new("low", 1), new("middle", 1), new("high", 1), new("other", 1)], 0,
            [new("range", [Requirement.InMembership("range", 1)]), new("zero", [Requirement.InMembership("range", 0, 0)]),
                new("separate", [Requirement.InMembership("range", 1, 2), Requirement.OfEntry("low", 1, 4)]),
                new("intersection", [Requirement.InMembership("intersection", 1, 4)])],
            [new("range", ["low", "middle"]), new("role", ["middle", "high"]), new("intersection", ["middle"])]);
        PreparedCalculation prepared = new EngineCase("resolved-selectors", request, new()).Prepare();
        PhysicalSearchHandCounter counter = new(prepared.Cards, prepared.Combos, new(Core.CalculationWorkPolicy.Unlimited));
        foreach (int subset in Enumerable.Range(0, 16).Reverse()) {
            int[] hand = Enumerable.Range(0, 4).Select(row => (subset >> row) & 1).ToArray();
            bool[] expected = [hand[0] + hand[1] > 0, hand[0] + hand[1] == 0, hand[0] == 1 && hand[1] == 1, hand[1] == 1];
            Assert.That(counter.Evaluate(hand, new int[4]), Is.EqualTo(expected), "subset=" + subset);
        }
    }

    [Test]
    public void DecodedSearchPredicatesPreserveHighRowsDistinctRequirementsAndRecompilation() {
        DeckEntry[] entries = Enumerable.Range(0, 66).Select(index => new DeckEntry("row-" + index, 1)).ToArray();
        CalculationRequest request = new(entries, 0, [
            new("distinct", [Requirement.InMembership("a", 1, 5), Requirement.InMembership("b", 1, 5)]),
            new("one", [Requirement.InMembership("a", 1, 1)]), new("zero", [Requirement.InMembership("a", 0, 0)]),
            new("direct", [Requirement.OfEntry("row-64", 1, 5)]), new("always", [])],
            [new("a", ["row-0", "row-64"]), new("b", ["row-32", "row-64"])]);
        PreparedCalculation prepared = new EngineCase("high-rows", request, new()).Prepare();
        PhysicalSearchHandCounter counter = new(prepared.Cards, prepared.Combos, new(Core.CalculationWorkPolicy.Unlimited));
        int[] hand = new int[66];
        int[] available = new int[66];
        hand[64] = 1;
        Assert.That(counter.Evaluate(hand, available), Is.EqualTo(new[] { false, true, false, true, true }));
        hand[0] = 1;
        Assert.That(counter.Evaluate(hand, available), Is.EqualTo(new[] { true, false, false, true, true }));
        hand[0] = 0; hand[64] = 0; hand[32] = 1;
        Assert.That(counter.Evaluate(hand, available), Is.EqualTo(new[] { false, false, true, false, true }));
    }

    [TestCase(false), TestCase(true)]
    public void MixedRepeatedDrawsPaymentsAndTerminalSearchMatchIndependentPhysicalOrders(bool hiddenLoss) {
        EngineCase fixture = new("mixed-repeated", new([
            new("d", 1, new(DrawCount: 2, RemovalCount: 1, RemovalEligibility: new("b"))),
            new("e", 2, new(DrawCount: 1, ActivationLimit: 2, HandCost: 1, HandCostEligibility: new("a"), HiddenDeckCost: hiddenLoss ? 1 : 0)),
            new("s", 1, new(Search: new(new("a-or-b")))), new("ab", 1), new("a", 1), new("b", 1)
        ], 2, [
            new("distinct", [Requirement.InMembership("a", 1, 3), Requirement.InMembership("b", 1, 3)], groupId: "g"),
            new("zero", [Requirement.InMembership("a", 0, 0), Requirement.InMembership("b", 1, 3)], groupId: "g"),
            new("source", [Requirement.OfEntry("e", 1), Requirement.OfEntry("s", 0, 0)]),
            new("separate", [Requirement.InMembership("a", 1, 3), Requirement.OfEntry("ab", 1, 3)]),
            new("completed", [], groupId: "complete")
        ], [new("a", ["ab", "a"]), new("b", ["ab", "b"]), new("a-or-b", ["ab", "a", "b"])], ["g", "complete", "empty"]),
            new(activationOrder: ActivationOrder.Priority, priorityEntryIds: ["e", "s", "d"]));
        SearchCertificateOracle.Counts oracle = SearchCertificateOracle.Enumerate(fixture);
        Assert.That(oracle.Wins[1][5], Is.GreaterThan(BigInteger.Zero));
        Assert.That(oracle.Wins[1][5], Is.LessThan(oracle.Outcomes));
        foreach (PhysicalEnumerationMethod method in new[] { PhysicalEnumerationMethod.BatchStreaming, PhysicalEnumerationMethod.Frontier }) {
            ExactCalculationResult result = PhysicalEnumerationTest.Calculate(fixture, method, new(Core.CalculationWorkPolicy.Unlimited), out _);
            PhysicalEnumerationTest.AssertRows(result, oracle.Wins[1], oracle.Outcomes, "mixed-repeated/" + hiddenLoss + "/" + method);
        }
    }
}
