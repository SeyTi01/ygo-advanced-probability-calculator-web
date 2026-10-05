using Microsoft.JSInterop;
using Moq;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.Session;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public class DistinctPhysicalCardTest {
    private static readonly CategoryBase Fire = new("Fire"), Dark = new("Dark"), Earth = new("Earth");

    [Test]
    public void EveryPositiveSlotConsumesADifferentCopyButNotADifferentName() {
        var razen = new Card([Fire], 1, "Razen");
        var fire = new Card([Fire], 1, "Other Fire");
        var dual = new Card([Fire, Dark], 1, "Dual");
        var mixed = new Combo([new(Fire, 1, 5)], cards: [new(razen.Id, 1, 5)]);
        AssertHand([razen], mixed, false);
        AssertHand([razen, fire], mixed, true);
        AssertHand([razen, razen], mixed, true);
        var categories = new Combo([new(Fire, 1, 5), new(Dark, 1, 5)]);
        AssertHand([dual], categories, false);
        AssertHand([dual, dual], categories, true);
        // A greedy Fire assignment to Dual fails; assigning Other Fire succeeds.
        AssertHand([dual, fire], categories, true);
        var three = new Combo([new(Fire, 2, 5), new(Dark, 1, 5)]);
        AssertHand([dual, fire], three, false);
        AssertHand([dual, fire, fire], three, true);
        AssertHand([razen, fire], new([new(Fire, 2, 5)], cards: [new(razen.Id, 1, 5)]), false);
        AssertHand([razen, fire, fire], new([new(Fire, 2, 5)], cards: [new(razen.Id, 1, 5)]), true);
        // Three total eligible cards is insufficient when two roles share only one.
        AssertHand([dual, new([Earth]), new([Earth])],
            new([new(Fire, 1, 3), new(Dark, 1, 3), new(Earth, 1, 3)]), false);
        AssertHand([razen, fire], new([], cards: [new(razen.Id, 1, 2), new(fire.Id, 1, 2)]), true);
        AssertHand([razen, fire], new([], cards: [new(razen.Id, 1, 2), new(razen.Id, 1, 1)]), true);
    }

    [Test]
    public void MaximumsCountTheEntireHandIncludingCopiesAssignedElsewhere() {
        var dual = new Card([Fire, Dark]);
        var fire = new Card([Fire]);
        AssertHand([dual, fire], new([new(Fire, 1, 1), new(Dark, 1, 2)]), false);
        AssertHand([dual, fire], new([new(Fire, 0, 0), new(Dark, 1, 2)]), false);
        AssertHand([dual], new([new(Fire, 0, 1), new(Dark, 1, 1)]), true);
        AssertHand([dual, dual], new([new(Fire, 1, 2)], cards: [new(dual.Id, 1, 1)]), false);
        AssertHand([dual], new([new(Fire, 0, 0)], cards: [new(dual.Id, 1, 1)]), false);
        AssertHand([dual], new([new(Fire, 1, 1)], cards: [new(dual.Id, 0, 0)]), false);
        AssertHand([dual], new([new(Fire, 0, 0), new(Fire, 1, 1)]), false);
        AssertHand([dual], new([], cards: [new(dual.Id, 0, 0), new(dual.Id, 1, 1)]), false);
    }

    [Test]
    public void MinimalRazenUnionCountsTwoDifferentHandsOutOfSix() {
        var razen = new Card([Fire]);
        List<Card> deck = [razen, new([Fire]), new([Dark]), new([])];
        List<Combo> combos = [
            new([new(Fire, 1, 2)], cards: [new(razen.Id, 1, 2)]),
            new([new(Dark, 1, 2)], cards: [new(razen.Id, 1, 2)])
        ];
        var result = new ProbabilityCalculatorService().CalculateProbabilityResults(deck, combos, 2);
        Assert.That(SmallDeckOracle.EnumerateProbability(deck, combos, 2), Is.EqualTo(2.0 / 6));
        Assert.That(result.TotalProbability, Is.EqualTo(2.0 / 6).Within(1e-12));
        Assert.That(result.ComboProbabilities.Select(c => c.Probability), Is.All.EqualTo(1.0 / 6).Within(1e-12));
    }

    [Test]
    public void SeparateCombosReuseCopiesWithIndependentAlternativeAssignments() {
        List<Card> deck = [new([Fire]), new([Dark, Earth]), new([Fire, Dark]), new([])];
        List<Combo> combos = [
            new([new(Fire, 1, 2), new(Dark, 1, 2)], groupId: "a"),
            new([new(Fire, 1, 2), new(Earth, 1, 2)], groupId: "a"),
            new([new(Dark, 1, 2), new(Earth, 1, 2)], groupId: "b"),
            new([new(Fire, 1, 2)], groupId: "b")
        ];
        Assert.That(SmallDeckOracle.MatchesHand(deck.Take(2).ToList(), combos[0]), Is.True);
        Assert.That(SmallDeckOracle.MatchesHand(deck.Take(2).ToList(), combos[1]), Is.True);
        CheckAllResults(deck, combos, 2);
        combos.Add(combos[0]);
        CheckAllResults(deck, combos, 2);
        combos.Add(new([new(Fire, 0, 2)], groupId: "a"));
        CheckAllResults(deck, combos, 2);
    }

    [Test]
    public void SeededMixedRequirementsAndGroupsMatchIndependentAssignments() {
        var random = new Random(290926);
        CategoryBase[] categories = [Fire, Dark, Earth];
        for (var sample = 0; sample < 180; sample++) {
            var deck = Enumerable.Range(0, 4).Select(_ => new Card(
                categories.Where(_ => random.Next(2) == 0), random.Next(1, 3), "Same name")).ToList();
            var size = random.Next(1, Math.Min(5, deck.Sum(c => c.Copies)) + 1);
            var combos = Enumerable.Range(0, 4).Select(index => new Combo(
                categories.Where(_ => random.Next(2) == 0).Select(c => {
                    var min = random.Next(3);
                    return new ComboCategory(c, min, random.Next(min, size + 3));
                }), "Duplicate", groupId: index % 2 == 0 ? "a" : "b",
                cards: deck.Where(_ => random.Next(3) == 0).Select(c => {
                    var min = random.Next(3);
                    return new ComboCard(c.Id, min, random.Next(min, size + 3));
                }))).ToList();
            CheckAllResults(deck, combos, size);
        }
    }

    [Test]
    public async Task ReportedSavedSessionAndThirtyOverlappingCombosMatchPhysicalEnumeration() {
        var json = await File.ReadAllTextAsync(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "razen_session.json"));
        var session = await new SessionService(Mock.Of<IJSRuntime>(),
            new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer()).LoadSessionAsync(json);
        var deck = session.Cards.Where(c => c.Active).ToList();
        var combos = session.Combos.Where(c => c.Active).ToList();
        Assert.That(deck.Sum(c => c.Copies), Is.EqualTo(40));
        // Independent enumeration of all C(40,5) physical hands with slot backtracking.
        var expected = SmallDeckOracle.EnumerateProbability(deck, combos, session.HandSize);
        Assert.That(expected, Is.EqualTo(149946.0 / 658008));
        var standalone = combos.Select(c => SmallDeckOracle.EnumerateProbability(deck, [c], 5)).ToArray();
        Assert.That(standalone, Is.EqualTo(new[] { 93136.0 / 658008, 128466.0 / 658008 }));
        var repeated = Enumerable.Range(0, 30).Select(i => combos[i % 2].WithGroup(i % 3 == 0 ? "a" : "b")).ToList();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var result = new ProbabilityCalculatorService().CalculateProbabilityResults(deck, repeated, 5,
            [new("a", "First"), new("b", "Second")]);
        timer.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        TestContext.Out.WriteLine($"Reported 40-card session / 30 overlapping mixed combos: {timer.Elapsed.TotalMilliseconds:F1} ms, {allocated:N0} bytes");
        Assert.That(result.TotalProbability, Is.EqualTo(expected).Within(1e-12));
        Assert.That(result.GroupProbabilities!.Select(g => g.Probability), Is.All.EqualTo(expected).Within(1e-12));
        for (var i = 0; i < repeated.Count; i++)
            Assert.That(result.ComboProbabilities[i].Probability, Is.EqualTo(standalone[i % 2]).Within(1e-12));
        Assert.That(result.TotalProbability, Is.GreaterThan(standalone.Max()));
        Assert.That(allocated, Is.LessThan(2 * 1024 * 1024));
    }

    [Test]
    public void EligibilityMasksKeepDeckRowsBeyond64Distinct() {
        var deck = Enumerable.Range(0, 65).Select(i => new Card(i == 64 ? [Fire] : [], 1, "Same")).ToList();
        List<Combo> combos = [new([new(Fire, 1, 2)], cards: [new(deck[0].Id, 1, 2)])];
        CheckAllResults(deck, combos, 2);
        Assert.That(new ProbabilityCalculatorService().CalculateProbabilityForCombos(deck, combos, 2),
            Is.EqualTo(1.0 / 2080).Within(1e-12));
    }

    [Test]
    public void HallSubsetGrowthStopsBeforeUnboundedExpansion() {
        var categories = Enumerable.Range(0, 20).Select(i => new CategoryBase($"Role{i}")).ToArray();
        var deck = categories.Select(c => new Card([c])).ToList();
        var combo = new Combo(categories.Select(c => new ComboCategory(c, 1, 20)));
        var error = Assert.Throws<ProbabilityCalculationLimitException>(() =>
            new ProbabilityCalculatorService().CalculateProbabilityResults(deck, [combo], 20));
        Assert.That(error!.Message, Does.Contain("Calculation stopped"));
    }

    private static void AssertHand(List<Card> hand, Combo combo, bool expected) {
        Assert.That(SmallDeckOracle.MatchesHand(hand, combo), Is.EqualTo(expected), "Independent slot assignment");
        var deck = hand.GroupBy(c => c.Id).Select(g => g.First().WithCopies(g.Count())).ToList();
        Assert.That(new ProbabilityCalculatorService().CalculateProbabilityForCombos(deck, [combo], hand.Count),
            Is.EqualTo(expected ? 1 : 0).Within(1e-12));
    }

    private static void CheckAllResults(List<Card> deck, List<Combo> combos, int size) {
        var result = new ProbabilityCalculatorService().CalculateProbabilityResults(deck, combos, size,
            [new("a", "First"), new("b", "Second"), new("empty", "Empty")]);
        Assert.That(result.TotalProbability, Is.EqualTo(SmallDeckOracle.EnumerateProbability(deck, combos, size)).Within(1e-12));
        for (var i = 0; i < combos.Count; i++)
            Assert.That(result.ComboProbabilities[i].Probability,
                Is.EqualTo(SmallDeckOracle.EnumerateProbability(deck, [combos[i]], size)).Within(1e-12));
        foreach (var group in result.GroupProbabilities!)
            Assert.That(group.Probability, Is.EqualTo(SmallDeckOracle.EnumerateProbability(deck,
                combos.Where(c => c.GroupId == group.GroupId).ToList(), size)).Within(1e-12));
    }
}
