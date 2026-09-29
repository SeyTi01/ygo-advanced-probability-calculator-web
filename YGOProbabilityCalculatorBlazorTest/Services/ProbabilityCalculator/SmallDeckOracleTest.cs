using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public class SmallDeckOracleTest {
    private static readonly CategoryBase A = new("A");
    private static readonly CategoryBase B = new("B");
    private static readonly CategoryBase C = new("C");

    [Test]
    public void DirectCardAndMixedRequirementsMatchPhysicalHandsAndGroups() {
        var starter = new CategoryBase("Starter");
        var first = new Card([starter], 2, "Starter");
        var second = new Card([B], 2, "Same");
        var third = new Card([B], 1, "Same");
        List<Card> deck = [first, second, third, new([], 2)];
        List<Combo> combos = [
            new([], "Two cards", groupId: "g", cards: [new(first.Id, 1, 2), new(second.Id, 1, 1)]),
            new([new(starter, 1, 2)], "Same physical card", groupId: "g", cards: [new(first.Id, 1, 2)]),
            new([new(B, 1, 2)], "Mixed", cards: [new(third.Id, 0, 0)]),
            new([], "Repeated range", cards: [new(first.Id, 0, 2), new(first.Id, 1, 1)]),
            new([], "Contradiction", cards: [new(first.Id, 0, 0), new(first.Id, 1, 2)])
        ];
        foreach (var handSize in new[] { 1, 2, 3 }) {
            TotalAndStandaloneResultsMatchEveryPhysicalHand(deck, combos, handSize);
            var result = new ProbabilityCalculatorService().CalculateProbabilityResults(deck, combos, handSize,
                [new("g", "Grouped")]);
            Assert.That(result.GroupProbabilities![0].Probability,
                Is.EqualTo(EnumerateProbability(deck, combos.Where(combo => combo.GroupId == "g").ToList(), handSize)).Within(1e-12));
        }

        var activeDeck = deck.Where(card => card != first).ToList();
        TotalAndStandaloneResultsMatchEveryPhysicalHand(activeDeck, combos, 2);
        Assert.That(first.WithName("Renamed").WithCopies(3).Id, Is.EqualTo(first.Id));
        Assert.That(first.WithCategories([]).WithActive(false).Id, Is.EqualTo(first.Id));
    }

    private static IEnumerable<TestCaseData> Cases() {
        List<Card> deck = [new([A], 2), new([B]), new([A, B]), new([B, C]), new([], 2)];
        yield return Case("positive maximum", deck, [new([new(A, 1, 1)])], 2);
        yield return Case("zero maximum", deck, [new([new(A, 0, 0)])], 2);
        yield return Case("overlapping categories", deck, [new([new(A, 1, 2), new(B, 1, 2)])], 3);
        yield return Case("overlapping combos", deck, [new([new(A, 1, 3)]), new([new(B, 1, 3)])], 3);
        yield return Case("shared intersection with coefficient minus two", deck, [
            new([new(A, 1, 3), new(B, 1, 3)]),
            new([new(A, 1, 3), new(C, 1, 3)]),
            new([new(B, 1, 3), new(C, 1, 3)])
        ], 3);
        yield return Case("subset combos", deck, [new([new(A, 1, 2)]), new([new(A, 1, 2), new(B, 1, 2)])], 2);
        yield return Case("disjoint ranges", deck, [new([new(A, 0, 0)]), new([new(A, 2, 2)])], 2);
        yield return Case("duplicate combos", deck, [
            new([new(A, 1, 2)], "Same name"),
            new([new(A, 1, 2)], "Same name")
        ], 2);
        yield return Case("minimum above available copies", deck, [new([new(C, 2, 3)])], 3);
        yield return Case("unconstrained combo", deck, [new([])], 3);
        yield return Case("no combos", deck, [], 2);
        yield return Case("whole deck", deck, [new([new(A, 3, 3), new(B, 3, 3)])], 7);
        yield return Case("repeated names and constraints", [new([A, new("A")], 2), new([B]), new([])],
            [new([new(A, 1, 2), new(new("A"), 0, 1)])], 2);
        yield return Case("contradictory repeated constraints", deck,
            [new([new(A, 0, 0), new(new("A"), 1, 2)])], 2);
        yield return Case("empty hand with zero bounds", deck, [new([new(A, 0, 0)])], 0);
        yield return Case("empty deck and unconstrained combo", [], [new([])], 0);
    }

    [TestCase(32)]
    [TestCase(33)]
    [TestCase(65)]
    public void CategoryPositionsRemainDistinct(int categoryCount) {
        var categories = Enumerable.Range(0, categoryCount).Select(i => new CategoryBase($"C{i}")).ToArray();
        List<Card> deck = [new([categories[0]]), new([categories[^1]])];
        List<Combo> combos = [new(categories.Select((category, index) =>
            new ComboCategory(category, index == 0 ? 1 : 0, index == 0 ? 1 : 0)))];
        TotalAndStandaloneResultsMatchEveryPhysicalHand(deck, combos, 1);
    }

    [Test]
    public void IntersectionCanIntroduceMoreThan32Categories() {
        var categories = Enumerable.Range(0, 33).Select(i => new CategoryBase($"C{i}")).ToArray();
        List<Card> deck = [new([categories[0]]), new([categories[32]]), new([])];
        List<Combo> combos = [
            new(categories.Take(16).Select((category, index) => new ComboCategory(category, index == 0 ? 1 : 0, index == 0 ? 1 : 0))),
            new(categories.Skip(16).Select(category => new ComboCategory(category, 0, 0)))
        ];
        TotalAndStandaloneResultsMatchEveryPhysicalHand(deck, combos, 1);
    }

    [TestCase(31)]
    [TestCase(32)]
    [TestCase(33)]
    [TestCase(64)]
    public void TooManyCombosAreRejectedInsteadOfReturningAnIncorrectProbability(int comboCount) {
        List<Card> deck = [new([A]), new([])];
        var combos = Enumerable.Range(0, comboCount).Select(_ => new Combo(new[] { new ComboCategory(A, 1, 1) })).ToList();
        // Physical enumeration (and Wolfram) gives 1/2; a false zero must not be
        // presented as a result while larger union calculations are unsupported.
        var service = new ProbabilityCalculatorService();
        Assert.Throws<ArgumentOutOfRangeException>(() => service.CalculateProbabilityForCombos(deck, combos, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => service.CalculateProbabilityResults(deck, combos, 1));
    }

    [Test]
    public void FortyCardDeckWithManyMembershipPatterns() {
        var categories = Enumerable.Range(0, 5).Select(i => new CategoryBase($"C{i}")).ToArray();
        var deck = Enumerable.Range(0, 20).Select(pattern =>
            new Card(categories.Where((_, index) => (pattern & (1 << index)) != 0), 2)).ToList();
        List<Combo> combos = [new(categories.Select(category => new ComboCategory(category, 0, 5)))];
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var probability = new ProbabilityCalculatorService().CalculateProbabilityForCombos(deck, combos, 5);
        timer.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        TestContext.Out.WriteLine($"40 cards / 20 patterns / hand 5: {timer.Elapsed.TotalMilliseconds:F1} ms, {allocatedBytes:N0} allocated bytes");
        Assert.That(probability, Is.EqualTo(1).Within(1e-12));
        // Generous regression budget: about 9.5 MB with equivalent states merged,
        // versus 62.7 MB when array reference equality prevents merging. No timing gate.
        Assert.That(allocatedBytes, Is.LessThan(32 * 1024 * 1024));

        // Of these 40 copies, A-only / B-only / both / neither = 12 / 8 / 8 / 12
        // for A=C0, B=C2. Wolfram: Sum[C(12,a) C(12,5-a),a=1..2]/C(40,5).
        combos = [new(categories.Select((category, index) => new ComboCategory(category,
            index == 0 ? 1 : 0, index == 0 ? 2 : index == 2 ? 0 : 5)))];
        probability = new ProbabilityCalculatorService().CalculateProbabilityForCombos(deck, combos, 5);
        Assert.That(probability, Is.EqualTo(1705.0 / 54834.0).Within(1e-12));

        // The universal event skips DP; this constrained case protects state merging.
        combos = [new(categories.Select(category => new ComboCategory(category, 1, 4)))];
        allocated = GC.GetAllocatedBytesForCurrentThread();
        probability = new ProbabilityCalculatorService().CalculateProbabilityForCombos(deck, combos, 5);
        allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        // Wolfram enumerated 346528 successful labeled hands out of C(40,5)=658008.
        Assert.That(probability, Is.EqualTo(3332.0 / 6327.0).Within(1e-12));
        Assert.That(allocatedBytes, Is.LessThan(32 * 1024 * 1024));
    }

    [Test]
    public void RenamingReorderingAndSplittingCopiesPreserveResults() {
        List<Card> deck = [new([A], 2), new([A, B]), new([B], 2), new([])];
        List<Combo> combos = [new([new(A, 1, 1)]), new([new(B, 0, 0)]), new([new(A, 1, 2), new(B, 1, 1)])];
        var expected = EnumerateProbability(deck, combos, 2);
        var service = new ProbabilityCalculatorService();
        var renamed = new Dictionary<string, CategoryBase> { ["A"] = new("Renamed A"), ["B"] = new("Renamed B") };
        var splitDeck = deck.AsEnumerable().Reverse().SelectMany(card => Enumerable.Range(0, card.Copies)
            .Select(_ => new Card(card.Categories.AsEnumerable().Reverse().Select(category => renamed[category.Name])))).ToList();
        var changedCombos = combos.AsEnumerable().Reverse().Select(combo => new Combo(
            combo.Categories.AsEnumerable().Reverse().Select(constraint => new ComboCategory(
                renamed[constraint.BaseCategory.Name], constraint.MinCount, constraint.MaxCount)), groupId: "g")).ToList();
        Assert.That(service.CalculateProbabilityForCombos(splitDeck, changedCombos, 2), Is.EqualTo(expected).Within(1e-12));
        // Adding a duplicate or a subset must not enlarge the union.
        changedCombos.Add(changedCombos[0]);
        changedCombos.Add(new Combo([new(renamed["A"], 1, 1), new(renamed["B"], 1, 1)]));
        var result = service.CalculateProbabilityResults(splitDeck, changedCombos, 2, [new("g", "Renamed group")]);
        Assert.That(result.TotalProbability, Is.EqualTo(expected).Within(1e-12));
        Assert.That(result.GroupProbabilities![0].Probability, Is.EqualTo(expected).Within(1e-12));
        changedCombos.Add(new Combo([new(renamed["A"], 0, 0)]));
        var enlarged = service.CalculateProbabilityForCombos(splitDeck, changedCombos, 2);
        Assert.That(enlarged, Is.GreaterThanOrEqualTo(expected - 1e-12));
        Assert.That(enlarged, Is.EqualTo(EnumerateProbability(splitDeck, changedCombos, 2)).Within(1e-12));
    }

    private static TestCaseData Case(string name, List<Card> deck, List<Combo> combos, int size) =>
        new TestCaseData(deck, combos, size).SetName($"Oracle: {name}");

    [TestCaseSource(nameof(Cases))]
    public void EngineMatchesEveryPhysicalHand(List<Card> deck, List<Combo> combos, int handSize) {
        var expected = EnumerateProbability(deck, combos, handSize);
        var actual = new ProbabilityCalculatorService().CalculateProbabilityForCombos(deck, combos, handSize);
        Assert.That(actual, Is.EqualTo(expected).Within(1e-12));
    }

    [TestCaseSource(nameof(Cases))]
    public void TotalAndStandaloneResultsMatchEveryPhysicalHand(List<Card> deck, List<Combo> combos, int handSize) {
        var expectedTotal = EnumerateProbability(deck, combos, handSize);
        var result = new ProbabilityCalculatorService().CalculateProbabilityResults(deck, combos, handSize);

        Assert.That(result.TotalProbability, Is.EqualTo(expectedTotal).Within(1e-12));
        Assert.That(result.ComboProbabilities, Has.Count.EqualTo(combos.Count));
        for (var index = 0; index < combos.Count; index++) {
            var comboResult = result.ComboProbabilities[index];
            Assert.That(comboResult.ComboIndex, Is.EqualTo(index));
            Assert.That(comboResult.ComboName, Is.EqualTo(combos[index].Name));
            Assert.That(comboResult.Probability,
                Is.EqualTo(EnumerateProbability(deck, [combos[index]], handSize)).Within(1e-12),
                $"Standalone result for combo at index {index}");
        }
    }

    [Test]
    public void OverlappingComboResultsRemainSeparateFromTheirUnion() {
        var deck = new List<Card> {
            new([A], 2), new([B], 2), new([A, B]), new([], 2)
        };
        var combos = new List<Combo> {
            new([new(A, 1, 2)], "Duplicate"),
            new([new(B, 1, 2)], "Duplicate")
        };
        const int handSize = 2;

        var result = new ProbabilityCalculatorService().CalculateProbabilityResults(deck, combos, handSize);
        var expectedTotal = EnumerateProbability(deck, combos, handSize);
        var expectedStandalone = combos
            .Select(combo => EnumerateProbability(deck, [combo], handSize))
            .ToArray();

        Assert.That(result.TotalProbability, Is.EqualTo(expectedTotal).Within(1e-12));
        Assert.That(result.ComboProbabilities.Select(combo => combo.Probability),
            Is.EqualTo(expectedStandalone).Within(1e-12));
        Assert.That(result.TotalProbability, Is.LessThan(expectedStandalone.Sum()));
        Assert.That(result.ComboProbabilities.Select(combo => combo.ComboName),
            Is.EqualTo(new[] { "Duplicate", "Duplicate" }));
    }

    [Test]
    public void GroupUnionsMatchPhysicalHandsAndDoNotChangeTheGlobalUnion() {
        var deck = new List<Card> {
            new([A], 2), new([B], 2), new([A, B]), new([])
        };
        var groups = new List<ComboGroup> {
            new("tier-1", "Tier 1"), new("tier-2", "Tier 2"),
            new("empty", "Empty")
        };
        var combos = new List<Combo> {
            new([new(A, 1, 2)], "Duplicate", groupId: "tier-1"),
            new([new(B, 1, 2)], "Duplicate", groupId: "tier-1"),
            new([new(B, 1, 2)], "Duplicate", groupId: "tier-2"),
            new([new(A, 0, 0)], groupId: "tier-2"),
            new([new(A, 2, 2)], "Ungrouped"),
            new([new(A, 0, 0)], "Inactive", false, "empty")
        };
        const int handSize = 2;
        var active = combos.Where(combo => combo.Active).ToList();
        var service = new ProbabilityCalculatorService();

        var result = service.CalculateProbabilityResults(deck, active, handSize, groups);
        var expectedTotal = EnumerateProbability(deck, active, handSize);
        Assert.That(result.TotalProbability, Is.EqualTo(expectedTotal).Within(1e-12));
        Assert.That(result.TotalProbability,
            Is.EqualTo(service.CalculateProbabilityForCombos(
                deck, active.Select(combo => combo.WithGroup(null)).ToList(), handSize)).Within(1e-12));

        Assert.That(result.GroupProbabilities, Has.Count.EqualTo(3));
        foreach (var group in groups) {
            var members = active.Where(combo => combo.GroupId == group.Id).ToList();
            var actual = result.GroupProbabilities!.Single(item => item.GroupId == group.Id);
            var expected = EnumerateProbability(deck, members, handSize);
            Assert.That(actual.GroupName, Is.EqualTo(group.Name));
            Assert.That(actual.ActiveComboCount, Is.EqualTo(members.Count));
            Assert.That(actual.Probability, Is.EqualTo(expected).Within(1e-12), group.Name);
        }
        Assert.That(result.GroupProbabilities![0].Probability,
            Is.LessThan(result.ComboProbabilities[0].Probability + result.ComboProbabilities[1].Probability));
        Assert.That(result.ComboProbabilities.Select(combo => combo.GroupId),
            Is.EqualTo(active.Select(combo => combo.GroupId)));
    }

    [Test]
    public void SeededSmallDecksMatchEnumeration() {
        var random = new Random(120925);
        CategoryBase[] categories = [A, B, C];
        for (var sample = 0; sample < 24; sample++) {
            var deck = Enumerable.Range(0, 6)
                .Select(_ => new Card(categories.Where(_ => random.Next(2) == 1))).ToList();
            var handSize = random.Next(1, 4);
            var combos = Enumerable.Range(0, random.Next(1, 4)).Select(_ => new Combo(
                categories.Where(_ => random.Next(2) == 1).Select(category => {
                    var min = random.Next(handSize + 1);
                    return new ComboCategory(category, min, random.Next(min, handSize + 1));
                }))).ToList();
            var actual = new ProbabilityCalculatorService().CalculateProbabilityForCombos(deck, combos, handSize);
            Assert.That(actual, Is.EqualTo(EnumerateProbability(deck, combos, handSize)).Within(1e-12),
                $"Seed 120925, sample {sample}, hand size {handSize}");
            var grouped = combos.Select((combo, index) => combo.WithGroup(index % 3 == 2 ? null : $"g{index % 2}")).ToList();
            var groups = new List<ComboGroup> { new("g0", "First"), new("g1", "Second"), new("empty", "Empty") };
            var results = new ProbabilityCalculatorService().CalculateProbabilityResults(deck, grouped, handSize, groups);
            Assert.That(results.TotalProbability, Is.EqualTo(actual).Within(1e-12));
            for (var index = 0; index < combos.Count; index++)
                Assert.That(results.ComboProbabilities[index].Probability,
                    Is.EqualTo(EnumerateProbability(deck, [combos[index]], handSize)).Within(1e-12));
            foreach (var group in results.GroupProbabilities!)
                Assert.That(group.Probability, Is.EqualTo(EnumerateProbability(deck,
                    grouped.Where(combo => combo.GroupId == group.GroupId).ToList(), handSize)).Within(1e-12));
        }
    }

    [Test]
    public void ThirtyEquivalentEventsPreserveEveryResultAndGroupIdentity() {
        List<Card> deck = [new([A], 2), new([B]), new([A, B]), new([C]), new([], 2)];
        var combos = Enumerable.Range(0, 30).Select(i => new Combo(i % 2 == 0
            ? new ComboCategory[] { new(A, 1, 3), new(B, 0, 0), new(A, 0, 1), new(C, 0, 99) }
            : [new(B, 0, 0), new(A, 1, 1)], $"Combo {i}", i % 4 != 0,
            i % 3 == 2 ? null : $"g{i % 2}")).ToList();
        List<ComboGroup> groups = [new("g0", "First"), new("g1", "Second"), new("empty", "Empty")];
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var result = new ProbabilityCalculatorService().CalculateProbabilityResults(deck, combos, 3, groups);
        var bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        // Also proves that direct callers evaluate inactive entries they supply.
        Assert.That(result.ComboProbabilities, Has.Count.EqualTo(30));
        Assert.That(result.TotalProbability, Is.EqualTo(EnumerateProbability(deck, combos, 3)).Within(1e-12));
        for (var i = 0; i < combos.Count; i++) {
            Assert.That(result.ComboProbabilities[i].ComboIndex, Is.EqualTo(i));
            Assert.That(result.ComboProbabilities[i].ComboName, Is.EqualTo(combos[i].Name));
            Assert.That(result.ComboProbabilities[i].GroupId, Is.EqualTo(combos[i].GroupId));
            Assert.That(result.ComboProbabilities[i].Probability,
                Is.EqualTo(EnumerateProbability(deck, [combos[i]], 3)).Within(1e-12));
        }
        foreach (var group in result.GroupProbabilities!) {
            var members = combos.Where(c => c.GroupId == group.GroupId).ToList();
            Assert.That(group.ActiveComboCount, Is.EqualTo(members.Count));
            Assert.That(group.Probability, Is.EqualTo(EnumerateProbability(deck, members, 3)).Within(1e-12));
        }
        Assert.That(bytes, Is.LessThan(2 * 1024 * 1024), "Duplicate unions must not enumerate 2^30 subsets.");
    }

    [Test]
    public void IntersectionsWithRepeatedAndNestedRangesMatchPhysicalHands() {
        List<Card> deck = [new([A, B]), new([A, C]), new([B, C]), new([A]), new([B]), new([])];
        List<Combo> combos = [
            new([new(A, 1, 3), new(B, 0, 2)]),
            new([new(B, 0, 1), new(A, 1, 2), new(A, 2, 3)]),
            new([new(A, 0, 0), new(A, 1, 1)]),
            new([new(C, 1, 2), new(B, 1, 3)]),
            new([new(A, 0, 0), new(C, 0, 0)]),
            new([new(A, 2, 2)]),
            new([new(C, 0, 3), new(B, 0, 2), new(A, 1, 3)])
        ];
        for (var h = 1; h <= 4; h++) {
            TotalAndStandaloneResultsMatchEveryPhysicalHand(deck, combos, h);
            TotalAndStandaloneResultsMatchEveryPhysicalHand(deck, combos.AsEnumerable().Reverse().ToList(), h);
        }
    }

    [Test]
    public void CachedProbabilitiesDoNotEscapeTheirRequest() {
        var service = new ProbabilityCalculatorService();
        List<Combo> combos = [new([new(A, 1, 1)])];
        List<Card> first = [new([A]), new([])];
        List<Card> second = [new([A], 2), new([])];
        foreach (var deck in new[] { first, second, first })
            foreach (var h in new[] { 1, 2 })
                Assert.That(service.CalculateProbabilityResults(deck, combos, h).TotalProbability,
                    Is.EqualTo(EnumerateProbability(deck, combos, h)).Within(1e-12));
    }

    [Test]
    public void CacheCapacityDoesNotChangeUnionOrGroupResults() {
        var categories = Enumerable.Range(0, 11).Select(i => new CategoryBase($"C{i}")).ToArray();
        List<Card> deck = [new(categories), new([])];
        // 2047 syntactically distinct intersections exceed the 1024-entry cache.
        // On this deck all events coincide, independently checked by the hand oracle.
        var combos = categories.Select(c => new Combo([new(c, 1, 1)], groupId: "g")).ToList();
        var result = new ProbabilityCalculatorService().CalculateProbabilityResults(deck, combos, 1, [new("g", "All")]);
        var expected = EnumerateProbability(deck, combos, 1);
        Assert.That(result.TotalProbability, Is.EqualTo(expected).Within(1e-12));
        Assert.That(result.GroupProbabilities![0].Probability, Is.EqualTo(expected).Within(1e-12));
    }

    [Test]
    public void DistinctIntersectionGrowthStopsWithAnExplicitResourceError() {
        var categories = Enumerable.Range(0, 18).Select(i => new CategoryBase($"C{i}")).ToArray();
        List<Card> deck = [new(categories, 3), new([], 37)];
        var combos = categories.Select(c => new Combo([new(c, 1, 5)])).ToList();
        var exception = Assert.Throws<YGOProbabilityCalculatorBlazor.Services.Interface.ProbabilityCalculationLimitException>(
            () => new ProbabilityCalculatorService().CalculateProbabilityResults(deck, combos, 5));
        Assert.That(exception!.Message, Does.Contain("Calculation stopped"));
    }

    [Test]
    public void UniversalEventShortCircuitsBeforeUnnecessaryIntersectionGrowth() {
        var categories = Enumerable.Range(0, 18).Select(i => new CategoryBase($"C{i}")).ToArray();
        List<Card> deck = [new(categories), new([])];
        var combos = categories.Select(c => new Combo([new(c, 1, 1)])).ToList();
        combos.Add(new Combo([new(A, 0, 99)]));
        TotalAndStandaloneResultsMatchEveryPhysicalHand(deck, combos, 1);
    }

    [Test]
    public void LargeDistributionStopsWithAnExplicitResourceError() {
        var categories = Enumerable.Range(0, 18).Select(i => new CategoryBase($"C{i}")).ToArray();
        var deck = categories.Select(c => new Card([c], 2)).ToList();
        List<Combo> combos = [new(categories.Select(c => new ComboCategory(c, 0, 1)))];
        Assert.Throws<YGOProbabilityCalculatorBlazor.Services.Interface.ProbabilityCalculationLimitException>(
            () => new ProbabilityCalculatorService().CalculateProbabilityForCombos(deck, combos, 9));
    }
    // Each physical copy has its own position. Visit each unordered hand exactly once;
    // directly evaluate OR-of-combos / AND-of-constraints, without masks, merging,
    // binomial coefficients, inclusion-exclusion, or production helper methods.
    internal static double EnumerateProbability(List<Card> deck, List<Combo> combos, int handSize) {
        var copies = new List<Card>();
        foreach (var card in deck)
            for (var i = 0; i < card.Copies; i++) copies.Add(card);
        var hand = new List<Card>();
        var total = 0;
        var successes = 0;
        Visit(0);
        return (double)successes / total;

        void Visit(int start) {
            if (hand.Count == handSize) {
                total++;
                if (combos.Any(combo => combo.Categories.All(constraint => {
                    var count = hand.Count(card => card.Categories.Any(category =>
                        category.Name == constraint.BaseCategory.Name));
                    return count >= constraint.MinCount && count <= constraint.MaxCount;
                }) && combo.Cards.All(constraint => {
                    var count = hand.Count(card => card.Id == constraint.CardId);
                    return count >= constraint.MinCount && count <= constraint.MaxCount;
                }))) successes++;
                return;
            }
            for (var i = start; i <= copies.Count - (handSize - hand.Count); i++) {
                hand.Add(copies[i]);
                Visit(i + 1);
                hand.RemoveAt(hand.Count - 1);
            }
        }
    }
}
