using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public class UnionFactoringTest {
    private static readonly CategoryBase A = new("A"), B = new("B"), C = new("C"), D = new("D");

    [Test]
    public void UniversalEventDoesNotNeedAnEnormousBinomialDenominator() {
        Assert.That(new ProbabilityCalculatorService().CalculateProbabilityResults(
            [new([], int.MaxValue)], [new([])], int.MaxValue / 2).TotalProbability, Is.EqualTo(1));
    }

    [Test]
    public void FactoredEligibilityKeepsRowsBeyond64AndInactiveInputsDistinct() {
        var deck = Enumerable.Range(0, 66).Select(i => new Card(i >= 64 ? [A] : [],
            name: "Same", active: i != 65, id: $"row{i}")).ToList();
        Check(deck, [new([new(A, 1, 2)], groupId: "g0", cards: [new(deck[0].Id, 1, 2)]),
            new([new(A, 1, 2)], groupId: "g1", cards: [new(deck[65].Id, 1, 2)])], 2);
    }

    [Test]
    public async Task SuppliedModelPreservesExactCountsAndAllocatesLessThanTwoMegabytes() {
        var session = await UnionBenchmark.LoadModel();
        var deck = session.Cards.Where(c => c.Active).ToList();
        var combos = session.Combos.Where(c => c.Active).ToList();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var result = new ProbabilityCalculatorService().CalculateProbabilityResults(deck, combos, 5, session.ComboGroups);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        // Derived by UnionBenchmark.SuppliedModelMatchesEveryPhysicalHand:
        // independent assignment for every one of the 658,008 physical hands.
        Assert.That(result.TotalProbability, Is.EqualTo(525176.0 / 658008).Within(1e-12));
        Assert.That(result.GroupProbabilities!.Select(g => g.Probability),
            Is.EqualTo(new[] { 362986.0 / 658008, 315996.0 / 658008 }).Within(1e-12));
        long[] successes = [93136, 128466, 150480, 117819, 54846, 67301, 71625, 134814, 134814, 71625];
        Assert.That(result.ComboProbabilities.Select(c => c.Probability),
            Is.EqualTo(successes.Select(n => (double)n / 658008)).Within(1e-12));
        Assert.That(allocated, Is.LessThan(2 * 1024 * 1024));
    }

    [Test]
    public void AlternativeDemandAboveOneCannotMixCopiesFromDifferentBranches() {
        List<Card> deck = [new([A]), new([B]), new([C])];
        List<Combo> combos = [new([new(C, 1, 3), new(A, 2, 3)]), new([new(C, 1, 3), new(B, 2, 3)])];
        Check(deck, combos, 3);
        Assert.That(SmallDeckOracle.EnumerateProbability(deck, combos, 3), Is.Zero);
        // C + two from (A union B) would incorrectly accept this hand.
        var unionCategory = new CategoryBase("A or B");
        var rewrittenDeck = deck.Select(c => c.Categories.Contains(C) ? c : c.WithCategories([unionCategory])).ToList();
        Assert.That(SmallDeckOracle.EnumerateProbability(rewrittenDeck,
            [new([new(C, 1, 3), new(unionCategory, 2, 3)])], 3), Is.EqualTo(1));
    }

    [Test]
    public void BranchMaximaAndZeroConstraintsCannotBeReplacedByUnionEligibility() {
        List<Card> deck = [new([A], 2), new([B], 2), new([C])];
        List<Combo> bounded = [new([new(C, 1, 5), new(A, 1, 1)]), new([new(C, 1, 5), new(B, 1, 1)])];
        Check(deck, bounded, 5);
        Assert.That(SmallDeckOracle.EnumerateProbability(deck, bounded, 5), Is.Zero);
        Check(deck, [new([new(C, 1, 3), new(A, 1, 3), new(B, 0, 0)]),
            new([new(C, 1, 3), new(B, 1, 3), new(A, 0, 0)])], 3);
        Check(deck, [new([new(C, 1, 3), new(A, 1, 1)]), new([new(C, 1, 3), new(B, 1, 3)])], 3);
    }

    [Test]
    public void EqualEligibilityAndRepeatedSelectorsRetainTheirDifferentSlotSemantics() {
        var first = new Card([A, B], 2, "Same", id: "first");
        var second = new Card([C], 2, "Same", id: "second");
        List<Card> deck = [first, second, new([], 2)];
        List<Combo> combos = [
            new([new(A, 1, 3), new(A, 1, 3)], "Same", groupId: "g0"),
            new([new(A, 1, 3), new(B, 1, 3)], "Same", groupId: "g1"),
            new([new(A, 1, 3)], "Same", groupId: "g0", cards: [new(first.Id, 1, 3)]),
            new([new(C, 1, 3)], "Same", groupId: "g1", cards: [new(first.Id, 1, 3)]),
            new([new(A, 0, 0), new(A, 1, 3)], cards: [new(second.Id, 1, 3)]),
            new([], cards: [new("missing", 1, 3)]),
            new([], cards: [new(first.Id, 0, 0), new(first.Id, 1, 3)])
        ];
        Check(deck, combos, 3);
        Check(deck.Where(c => c.Id != first.Id).ToList(), combos, 3);
        Assert.Throws<ArgumentException>(() => new ProbabilityCalculatorService().CalculateProbabilityResults(
            [first, first.WithName("Another")], combos, 3));
    }

    [Test]
    public void SeededCommonRolesAndAlternativeAssignmentsPreserveEveryOutput() {
        var random = new Random(290929);
        CategoryBase[] categories = [A, B, C, D];
        for (var sample = 0; sample < 200; sample++) {
            var deck = Enumerable.Range(0, 5).Select(i => new Card(
                categories.Where(_ => random.Next(2) == 0), random.Next(1, 3), "Same", id: $"row{i}")).ToList();
            var handSize = random.Next(2, 5);
            var commonMin = random.Next(1, 3);
            var combos = categories.Skip(1).Select((c, i) => new Combo(
                [new(A, commonMin, handSize), new(c, 1, handSize)], "Same", groupId: $"g{i % 2}")).ToList();
            combos.AddRange(deck.Take(2).Select((c, i) => new Combo([new(A, commonMin, handSize)],
                "Same", groupId: $"g{i}", cards: [new(c.Id, 1, handSize)])));
            combos.Add(combos[0].WithGroup("g1"));
            if (sample % 3 == 0) combos.Add(new([new(B, 0, 0), new(C, 1, handSize)], groupId: "g0"));
            if (sample % 5 == 0) combos.Add(new([new(D, 1, 1)], groupId: "g1"));
            Check(deck, combos, handSize);
            Check(deck.AsEnumerable().Reverse().ToList(), combos.AsEnumerable().Reverse().ToList(), handSize);
        }
    }

    [Test]
    public void DisjointDemandPruningPreservesFeasiblePairIntersections() {
        var categories = Enumerable.Range(0, 6).Select(i => new CategoryBase($"C{i}")).ToArray();
        var deck = categories.Select(c => new Card([c], 2)).ToList();
        var combos = categories.Select((c, i) => new Combo([new(c, 2, 2)], groupId: $"g{i % 2}")).ToList();
        Check(deck, combos, 5);
        // Three disjoint minima need six slots, but pairs fit into five.
        // On a 60-card deck this also guards the nonfactorable scaling path.
        deck.Add(new Card([], 48));
        var before = GC.GetAllocatedBytesForCurrentThread();
        var result = new ProbabilityCalculatorService().CalculateProbabilityResults(deck, combos, 5);
        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.LessThan(1024 * 1024));
        Assert.That(result.TotalProbability, Is.GreaterThan(result.ComboProbabilities.Max(c => c.Probability)));
    }

    [TestCase(6, 184296)]
    [TestCase(10, 306040)]
    [TestCase(18, 546840)]
    public void DistinctSixtyCardUnionsMatchIndependentAllocationCounts(int count, long successes) {
        var categories = Enumerable.Range(0, count).Select(i => new CategoryBase($"C{i}")).ToArray();
        var deck = categories.Select(c => new Card([c], 2)).Append(new Card([], 60 - 2 * count)).ToList();
        var combos = categories.Select(c => new Combo([new(c, 2, 2)])).ToList();
        // Separate allocation calculation, evaluated with integer arithmetic:
        // count*C(58,3) - C(count,2)*56. A hand can contain at most two pairs.
        // Denominator C(60,5)=5,461,512; no production Hall/DP helpers used.
        Assert.That(new ProbabilityCalculatorService().CalculateProbabilityResults(deck, combos, 5).TotalProbability,
            Is.EqualTo((double)successes / 5461512).Within(1e-12));
    }

    private static void Check(List<Card> deck, List<Combo> combos, int handSize) {
        List<ComboGroup> groups = [new("g0", "Same"), new("g1", "Same"), new("empty", "Empty")];
        var service = new ProbabilityCalculatorService();
        var result = service.CalculateProbabilityResults(deck, combos, handSize, groups);
        var expected = SmallDeckOracle.EnumerateProbability(deck, combos, handSize);
        Assert.That(result.TotalProbability, Is.EqualTo(expected).Within(1e-12));
        Assert.That(service.CalculateProbabilityForCombos(deck, combos, handSize), Is.EqualTo(expected).Within(1e-12));
        for (var i = 0; i < combos.Count; i++) {
            Assert.That(result.ComboProbabilities[i].ComboIndex, Is.EqualTo(i));
            Assert.That(result.ComboProbabilities[i].ComboName, Is.EqualTo(combos[i].Name));
            Assert.That(result.ComboProbabilities[i].GroupId, Is.EqualTo(combos[i].GroupId));
            Assert.That(result.ComboProbabilities[i].Probability,
                Is.EqualTo(SmallDeckOracle.EnumerateProbability(deck, [combos[i]], handSize)).Within(1e-12));
        }
        foreach (var group in result.GroupProbabilities!) {
            var members = combos.Where(c => c.GroupId == group.GroupId).ToList();
            Assert.That(group.ActiveComboCount, Is.EqualTo(members.Count));
            Assert.That(group.Probability, Is.EqualTo(SmallDeckOracle.EnumerateProbability(deck, members, handSize)).Within(1e-12));
        }
    }
}
