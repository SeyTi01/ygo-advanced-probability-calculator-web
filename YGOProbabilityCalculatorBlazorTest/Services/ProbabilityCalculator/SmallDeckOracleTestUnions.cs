using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public sealed class SmallDeckOracleTestUnions : SmallDeckOracleTestBase {

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
        var expectedTotal = SmallDeckOracle.EnumerateProbability(deck, combos, handSize);
        var expectedStandalone = combos
            .Select(combo => SmallDeckOracle.EnumerateProbability(deck, [combo], handSize))
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
        var expectedTotal = SmallDeckOracle.EnumerateProbability(deck, active, handSize);
        Assert.That(result.TotalProbability, Is.EqualTo(expectedTotal).Within(1e-12));
        Assert.That(result.TotalProbability,
            Is.EqualTo(service.CalculateProbabilityForCombos(
                deck, active.Select(combo => combo.WithGroup(null)).ToList(), handSize)).Within(1e-12));

        Assert.That(result.GroupProbabilities, Has.Count.EqualTo(3));
        foreach (var group in groups) {
            var members = active.Where(combo => combo.GroupId == group.Id).ToList();
            var actual = result.GroupProbabilities!.Single(item => item.GroupId == group.Id);
            var expected = SmallDeckOracle.EnumerateProbability(deck, members, handSize);
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
            Assert.That(actual, Is.EqualTo(SmallDeckOracle.EnumerateProbability(deck, combos, handSize)).Within(1e-12),
                $"Seed 120925, sample {sample}, hand size {handSize}");
            var grouped = combos.Select((combo, index) => combo.WithGroup(index % 3 == 2 ? null : $"g{index % 2}")).ToList();
            var groups = new List<ComboGroup> { new("g0", "First"), new("g1", "Second"), new("empty", "Empty") };
            var results = new ProbabilityCalculatorService().CalculateProbabilityResults(deck, grouped, handSize, groups);
            Assert.That(results.TotalProbability, Is.EqualTo(actual).Within(1e-12));
            for (var index = 0; index < combos.Count; index++)
                Assert.That(results.ComboProbabilities[index].Probability,
                    Is.EqualTo(SmallDeckOracle.EnumerateProbability(deck, [combos[index]], handSize)).Within(1e-12));
            foreach (var group in results.GroupProbabilities!)
                Assert.That(group.Probability, Is.EqualTo(SmallDeckOracle.EnumerateProbability(deck,
                    grouped.Where(combo => combo.GroupId == group.GroupId).ToList(), handSize)).Within(1e-12));
        }
    }

    [TestCase(20261004)]
    [TestCase(20261005)]
    public void SeededMixedOrModelsMatchPhysicalHandsUnderPermutationAndWorkerFiltering(int seed) {
        var random = new Random(seed);
        CategoryBase[] selectors = [new("FIRE"),
            new("FIRE", CategorySource.Metadata, "attribute:fire"),
            new("Relabeled FIRE", CategorySource.Metadata, "attribute:fire"), B];
        List<ComboGroup> groups = [new("g0", "Same"), new("g1", "Same"), new("empty", "Empty")];
        var service = new ProbabilityCalculatorService();
        for (var sample = 0; sample < 160; sample++) {
            var deck = Enumerable.Range(0, 5).Select(i => new Card(
                selectors.Where(_ => random.Next(2) == 0), random.Next(3), "Same",
                active: i != 4, id: $"row{i}", externalCardId: 123)).ToList();
            var population = deck.Sum(c => c.Copies);
            var hand = (sample % 4) switch { 0 => 0, 1 => population, 2 => Math.Min(1, population), _ => random.Next(population + 1) };
            var combos = Enumerable.Range(0, 4).Select(i => {
                var required = Enumerable.Range(0, random.Next(3)).Select(_ => Leaf()).ToArray();
                var alternatives = Enumerable.Range(0, random.Next(1, 3)).Select(_ =>
                    new ComboAlternativeGroup(Enumerable.Range(0, random.Next(1, 4)).Select(_ => Leaf()).ToArray()));
                return new Combo(required.Where(r => r.Category is not null).Select(r => r.Category!), "Same",
                    active: i != 3, groupId: i % 3 == 2 ? null : $"g{i % 2}",
                    cards: required.Where(r => r.Card is not null).Select(r => r.Card!), alternativeGroups: alternatives);
            }).ToList();
            combos.Add(combos[0].WithGroup("g1"));
            var context = $"Seed {seed}, sample {sample}, hand {hand}";
            Check(service.CalculateProbabilityResults(deck, combos, hand, groups), deck, combos, hand, groups, context);

            var reversedDeck = deck.AsEnumerable().Reverse().Select(c => c.WithCategories(c.Categories.AsEnumerable().Reverse())).ToList();
            var reversedCombos = combos.AsEnumerable().Reverse().Select(c => new Combo(
                c.Categories.AsEnumerable().Reverse(), c.Name, c.Active, c.GroupId, c.Cards.AsEnumerable().Reverse(),
                c.AlternativeGroups.AsEnumerable().Reverse().Select(g => new ComboAlternativeGroup(g.Alternatives.Reverse().ToArray())))).ToList();
            Check(service.CalculateProbabilityResults(reversedDeck, reversedCombos, hand, groups),
                reversedDeck, reversedCombos, hand, groups, context + ", permuted");

            var activeDeck = deck.Where(c => c.Active).ToList();
            var activeCombos = combos.Where(c => c.Active).ToList();
            var activeHand = Math.Min(hand, activeDeck.Sum(c => c.Copies));
            var snapshot = CalculationSnapshot.Capture(deck, combos, activeHand, groups);
            Check(CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json)),
                activeDeck, activeCombos, activeHand, groups, context + ", filtered wire");

            ComboAlternative Leaf() {
                var min = random.Next(4) == 0 ? 2 : random.Next(2);
                var mode = random.Next(2) == 0 ? RequirementMaximumMode.Fixed : RequirementMaximumMode.HandSize;
                var max = mode == RequirementMaximumMode.Fixed ? min + random.Next(3) : 0;
                return random.Next(2) == 0
                    ? ComboAlternative.For(new ComboCategory(selectors[random.Next(selectors.Length)], min, max, mode))
                    : ComboAlternative.For(new ComboCard($"row{random.Next(6)}", min, max, mode));
            }
        }

        static void Check(YGOProbabilityCalculatorBlazor.Services.Interface.ProbabilityCalculationResult result,
            List<Card> deck, List<Combo> combos, int hand, List<ComboGroup> groups, string context) {
            Assert.That(result.TotalProbability, Is.EqualTo(SmallDeckOracle.EnumerateProbability(deck, combos, hand)).Within(1e-12), context);
            Assert.That(result.ComboProbabilities, Has.Count.EqualTo(combos.Count), context);
            for (var i = 0; i < combos.Count; i++) {
                var actual = result.ComboProbabilities[i];
                Assert.That((actual.ComboIndex, actual.ComboName, actual.GroupId), Is.EqualTo((i, combos[i].Name, combos[i].GroupId)), context);
                Assert.That(actual.Probability, Is.EqualTo(SmallDeckOracle.EnumerateProbability(deck, [combos[i]], hand)).Within(1e-12), context + $", combo {i}");
            }
            Assert.That(result.GroupProbabilities!.Select(g => (g.GroupId, g.GroupName)),
                Is.EqualTo(groups.Select(g => (g.Id, g.Name))), context);
            foreach (var group in result.GroupProbabilities!) {
                var members = combos.Where(c => c.GroupId == group.GroupId).ToList();
                Assert.That(group.ActiveComboCount, Is.EqualTo(members.Count), context);
                Assert.That(group.Probability, Is.EqualTo(SmallDeckOracle.EnumerateProbability(deck, members, hand)).Within(1e-12), context + $", group {group.GroupId}");
            }
        }
    }

    [Test]
    public void EveryThreeRoleMembershipPatternMatchesPhysicalAssignments() {
        CategoryBase[] roles = [A, B, C];
        // All 8^3 ways for three card rows to carry three roles. Row 0 has
        // two physical copies; each other row has one. Exhaust every hand size.
        for (var pattern = 0; pattern < 512; pattern++) {
            var deck = Enumerable.Range(0, 3).Select(row => new Card(
                roles.Where((_, role) => (pattern & (1 << (row * 3 + role))) != 0),
                row == 0 ? 2 : 1, id: $"row{row}")).ToList();
            List<Combo> combos = [new(roles.Select(r => new ComboCategory(r, 1, 0, RequirementMaximumMode.HandSize))),
                new([new(A, 1, 2), new(B, 0, 1)], cards: [new("row0", 1, 0, RequirementMaximumMode.HandSize)])];
            for (var hand = 0; hand <= 4; hand++) {
                var result = new ProbabilityCalculatorService().CalculateProbabilityResults(deck, combos, hand);
                Assert.That(result.TotalProbability, Is.EqualTo(SmallDeckOracle.EnumerateProbability(deck, combos, hand)).Within(1e-12), $"Pattern {pattern}, hand {hand}");
                for (var i = 0; i < combos.Count; i++)
                    Assert.That(result.ComboProbabilities[i].Probability,
                        Is.EqualTo(SmallDeckOracle.EnumerateProbability(deck, [combos[i]], hand)).Within(1e-12), $"Pattern {pattern}, hand {hand}, combo {i}");
            }
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
        Assert.That(result.TotalProbability, Is.EqualTo(SmallDeckOracle.EnumerateProbability(deck, combos, 3)).Within(1e-12));
        for (var i = 0; i < combos.Count; i++) {
            Assert.That(result.ComboProbabilities[i].ComboIndex, Is.EqualTo(i));
            Assert.That(result.ComboProbabilities[i].ComboName, Is.EqualTo(combos[i].Name));
            Assert.That(result.ComboProbabilities[i].GroupId, Is.EqualTo(combos[i].GroupId));
            Assert.That(result.ComboProbabilities[i].Probability,
                Is.EqualTo(SmallDeckOracle.EnumerateProbability(deck, [combos[i]], 3)).Within(1e-12));
        }
        foreach (var group in result.GroupProbabilities!) {
            var members = combos.Where(c => c.GroupId == group.GroupId).ToList();
            Assert.That(group.ActiveComboCount, Is.EqualTo(members.Count));
            Assert.That(group.Probability, Is.EqualTo(SmallDeckOracle.EnumerateProbability(deck, members, 3)).Within(1e-12));
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
            AssertTotalAndStandaloneResultsMatchEveryPhysicalHand(deck, combos, h);
            AssertTotalAndStandaloneResultsMatchEveryPhysicalHand(deck, combos.AsEnumerable().Reverse().ToList(), h);
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
                    Is.EqualTo(SmallDeckOracle.EnumerateProbability(deck, combos, h)).Within(1e-12));
    }

    [Test]
    public void EquivalentEligibilityDoesNotChangeUnionOrGroupResults() {
        var categories = Enumerable.Range(0, 11).Select(i => new CategoryBase($"C{i}")).ToArray();
        List<Card> deck = [new(categories), new([])];
        // Different selectors with identical eligible rows canonicalize to the same
        // event, independently checked by the physical-hand oracle.
        var combos = categories.Select(c => new Combo([new(c, 1, 1)], groupId: "g")).ToList();
        var result = new ProbabilityCalculatorService().CalculateProbabilityResults(deck, combos, 1, [new("g", "All")]);
        var expected = SmallDeckOracle.EnumerateProbability(deck, combos, 1);
        Assert.That(result.TotalProbability, Is.EqualTo(expected).Within(1e-12));
        Assert.That(result.GroupProbabilities![0].Probability, Is.EqualTo(expected).Within(1e-12));
    }

    [Test]
    public void SingleSlotAlternativesNoLongerRequireExponentialIntersections() {
        var categories = Enumerable.Range(0, 18).Select(i => new CategoryBase($"C{i}")).ToArray();
        var deck = categories.Select(c => new Card([c])).Append(new Card([], 22)).ToList();
        var combos = categories.Select(c => new Combo([new(c, 1, 5)])).ToList();
        // This used to exhaust the intersection map. Independently enumerate
        // the same physical hands before changing its expected behavior.
        var expected = SmallDeckOracle.EnumerateProbability(deck, combos, 5);
        Assert.That(new ProbabilityCalculatorService().CalculateProbabilityResults(deck, combos, 5).TotalProbability,
            Is.EqualTo(expected).Within(1e-12));
    }

    [Test]
    public void DistinctRestrictiveIntersectionGrowthStillStopsExplicitly() {
        var categories = Enumerable.Range(0, 18).Select(i => new CategoryBase($"C{i}")).ToArray();
        var deck = categories.Select(c => new Card([c])).Append(new Card([], 22)).ToList();
        var combos = categories.Select(c => new Combo([new(c, 0, 0)])).ToList();
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
        AssertTotalAndStandaloneResultsMatchEveryPhysicalHand(deck, combos, 1);
    }

    [Test]
    public void LargeDistributionStopsWithAnExplicitResourceError() {
        var categories = Enumerable.Range(0, 18).Select(i => new CategoryBase($"C{i}")).ToArray();
        var deck = categories.Select(c => new Card([c], 2)).ToList();
        List<Combo> combos = [new(categories.Select(c => new ComboCategory(c, 0, 1)))];
        Assert.Throws<YGOProbabilityCalculatorBlazor.Services.Interface.ProbabilityCalculationLimitException>(
            () => new ProbabilityCalculatorService().CalculateProbabilityForCombos(deck, combos, 9));
    }

}
