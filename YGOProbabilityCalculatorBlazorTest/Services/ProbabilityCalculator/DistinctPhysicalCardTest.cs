using System.Diagnostics;
using Microsoft.JSInterop;
using Moq;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.Session;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public class DistinctPhysicalCardTest
{
    private static readonly CategoryBase Fire = new("Fire"), Dark = new("Dark"), Earth = new("Earth");

    [Test]
    public void EveryPositiveSlotConsumesADifferentCopyButNotADifferentName()
    {
        Card razen = new([Fire], 1, "Razen");
        Card fire = new([Fire], 1, "Other Fire");
        Card dual = new([Fire, Dark], 1, "Dual");
        Combo mixed = new([new(Fire, 1, 5)], cards: [new(razen.Id, 1, 5)]);
        AssertHand([razen], mixed, false);
        AssertHand([razen, fire], mixed, true);
        AssertHand([razen, razen], mixed, true);
        Combo categories = new([new(Fire, 1, 5), new(Dark, 1, 5)]);
        AssertHand([dual], categories, false);
        AssertHand([dual, dual], categories, true);
        // A greedy Fire assignment to Dual fails; assigning Other Fire succeeds.
        AssertHand([dual, fire], categories, true);
        Combo three = new([new(Fire, 2, 5), new(Dark, 1, 5)]);
        AssertHand([dual, fire], three, false);
        AssertHand([dual, fire, fire], three, true);
        AssertHand([razen, fire], new([new(Fire, 2, 5)], cards: [new(razen.Id, 1, 5)]), false);
        AssertHand([razen, fire, fire], new([new(Fire, 2, 5)], cards: [new(razen.Id, 1, 5)]), true);
        // Three total eligible cards is insufficient when two roles share only one.
        AssertHand([dual, new([Earth]), new([Earth])],
            new([new(Fire, 1, 3), new(Dark, 1, 3), new(Earth, 1, 3)]),
            false
        );
        AssertHand([razen, fire], new([], cards: [new(razen.Id, 1, 2), new(fire.Id, 1, 2)]), true);
        AssertHand([razen, fire], new([], cards: [new(razen.Id, 1, 2), new(razen.Id, 1, 1)]), true);
    }

    [Test]
    public void MaximumsCountTheEntireHandIncludingCopiesAssignedElsewhere()
    {
        Card dual = new([Fire, Dark]);
        Card fire = new([Fire]);
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
    public void MinimalRazenUnionCountsTwoDifferentHandsOutOfSix()
    {
        Card razen = new([Fire]);
        List<Card> deck = [razen, new([Fire]), new([Dark]), new([])];
        List<Combo> combos =
        [
            new([new(Fire, 1, 2)], cards: [new(razen.Id, 1, 2)]),
            new([new(Dark, 1, 2)], cards: [new(razen.Id, 1, 2)])
        ];
        ProbabilityCalculationResult result =
            new ProbabilityCalculatorService().CalculateProbabilityResults(deck, combos, 2);
        Assert.That(SmallDeckOracle.EnumerateProbability(deck, combos, 2), Is.EqualTo(2.0 / 6));
        Assert.That(result.TotalProbability, Is.EqualTo(2.0 / 6).Within(1e-12));
        Assert.That(result.ComboProbabilities.Select(c => c.Probability), Is.All.EqualTo(1.0 / 6).Within(1e-12));
    }

    [Test]
    public void SeparateCombosReuseCopiesWithIndependentAlternativeAssignments()
    {
        List<Card> deck = [new([Fire]), new([Dark, Earth]), new([Fire, Dark]), new([])];
        List<Combo> combos =
        [
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
    public void SeededMixedRequirementsAndGroupsMatchIndependentAssignments()
    {
        Random random = new(290926);
        CategoryBase[] categories = [Fire, Dark, Earth];

        for (int sample = 0; sample < 180; sample++)
        {
            List<Card> deck =
            [
                .. Enumerable
                    .Range(0, 4)
                    .Select(_ => new Card(
                            categories.Where(_ => random.Next(2) == 0),
                            random.Next(1, 3),
                            "Same name"
                        )
                    )
            ];
            int size = random.Next(1, Math.Min(5, deck.Sum(c => c.Copies)) + 1);
            List<Combo> combos =
            [
                .. Enumerable
                    .Range(0, 4)
                    .Select(index => new Combo(
                            categories
                                .Where(_ => random.Next(2) == 0)
                                .Select(c =>
                                    {
                                        int min = random.Next(3);

                                        return new ComboCategory(c, min, random.Next(min, size + 3));
                                    }
                                ),
                            "Duplicate",
                            groupId: index % 2 == 0 ? "a" : "b",
                            cards: deck
                                .Where(_ => random.Next(3) == 0)
                                .Select(c =>
                                    {
                                        int min = random.Next(3);

                                        return new ComboCard(c.Id, min, random.Next(min, size + 3));
                                    }
                                )
                        )
                    )
            ];
            CheckAllResults(deck, combos, size);
        }
    }

    [Test]
    public async Task ReportedSavedSessionAndThirtyOverlappingCombosMatchPhysicalEnumeration()
    {
        string json = await File.ReadAllTextAsync(Path.Combine(TestContext.CurrentContext.TestDirectory,
                "Fixtures",
                "razen_session.json"
            )
        );
        SessionState session = await new SessionService(Mock.Of<IJSRuntime>(),
            new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer()
        ).LoadSessionAsync(json);
        List<Card> deck = [.. session.Cards.Where(c => c.Active)];
        List<Combo> combos = [.. session.Combos.Where(c => c.Active)];
        Assert.That(deck.Sum(c => c.Copies), Is.EqualTo(40));
        // Independent enumeration of all C(40,5) physical hands with slot backtracking.
        double expected = SmallDeckOracle.EnumerateProbability(deck, combos, session.HandSize);
        Assert.That(expected, Is.EqualTo(149946.0 / 658008));
        double[] standalone = [.. combos.Select(c => SmallDeckOracle.EnumerateProbability(deck, [c], 5))];
        Assert.That(standalone, Is.EqualTo(new[] { 93136.0 / 658008, 128466.0 / 658008 }));
        List<Combo> repeated =
            [.. Enumerable.Range(0, 30).Select(i => combos[i % 2].WithGroup(i % 3 == 0 ? "a" : "b"))];
        long before = GC.GetAllocatedBytesForCurrentThread();
        Stopwatch timer = Stopwatch.StartNew();
        ProbabilityCalculationResult result = new ProbabilityCalculatorService().CalculateProbabilityResults(deck,
            repeated,
            5,
            [new("a", "First"), new("b", "Second")]
        );
        timer.Stop();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        TestContext.Out.WriteLine(
            $"Reported 40-card session / 30 overlapping mixed combos: {timer.Elapsed.TotalMilliseconds:F1} ms, {allocated:N0} bytes"
        );
        Assert.That(result.TotalProbability, Is.EqualTo(expected).Within(1e-12));
        Assert.That(result.GroupProbabilities!.Select(g => g.Probability), Is.All.EqualTo(expected).Within(1e-12));

        for (int i = 0; i < repeated.Count; i++)
        {
            Assert.That(result.ComboProbabilities[i].Probability, Is.EqualTo(standalone[i % 2]).Within(1e-12));
        }

        Assert.That(result.TotalProbability, Is.GreaterThan(standalone.Max()));
        Assert.That(allocated, Is.LessThan(2 * 1024 * 1024));
    }

    [Test]
    public void EligibilityMasksKeepDeckRowsBeyond64Distinct()
    {
        List<Card> deck = [.. Enumerable.Range(0, 65).Select(i => new Card(i == 64 ? [Fire] : [], 1, "Same"))];
        List<Combo> combos = [new([new(Fire, 1, 2)], cards: [new(deck[0].Id, 1, 2)])];
        CheckAllResults(deck, combos, 2);
        Assert.That(new ProbabilityCalculatorService().CalculateProbabilityForCombos(deck, combos, 2),
            Is.EqualTo(1.0 / 2080).Within(1e-12)
        );
    }

    [Test]
    public void HallSubsetGrowthStopsBeforeUnboundedExpansion()
    {
        CategoryBase[] categories = [.. Enumerable.Range(0, 20).Select(i => new CategoryBase($"Role{i}"))];
        List<Card> deck = [.. categories.Select(c => new Card([c]))];
        Combo combo = new(categories.Select(c => new ComboCategory(c, 1, 20)));
        ProbabilityCalculationLimitException? error = Assert.Throws<ProbabilityCalculationLimitException>(() =>
            new ProbabilityCalculatorService().CalculateProbabilityResults(deck, [combo], 20)
        );
        Assert.That(error!.Message, Does.Contain("Calculation stopped"));
    }

    private static void AssertHand(List<Card> hand, Combo combo, bool expected)
    {
        Assert.That(SmallDeckOracle.MatchesHand(hand, combo), Is.EqualTo(expected), "Independent slot assignment");
        List<Card> deck = [.. hand.GroupBy(c => c.Id).Select(g => g.First().WithCopies(g.Count()))];
        Assert.That(new ProbabilityCalculatorService().CalculateProbabilityForCombos(deck, [combo], hand.Count),
            Is.EqualTo(expected ? 1 : 0).Within(1e-12)
        );
    }

    private static void CheckAllResults(List<Card> deck, List<Combo> combos, int size)
    {
        ProbabilityCalculationResult result = new ProbabilityCalculatorService().CalculateProbabilityResults(deck,
            combos,
            size,
            [new("a", "First"), new("b", "Second"), new("empty", "Empty")]
        );
        Assert.That(result.TotalProbability,
            Is.EqualTo(SmallDeckOracle.EnumerateProbability(deck, combos, size)).Within(1e-12)
        );

        for (int i = 0; i < combos.Count; i++)
        {
            Assert.That(result.ComboProbabilities[i].Probability,
                Is.EqualTo(SmallDeckOracle.EnumerateProbability(deck, [combos[i]], size)).Within(1e-12)
            );
        }

        foreach (GroupProbabilityResult group in result.GroupProbabilities!)
        {
            Assert.That(group.Probability,
                Is
                    .EqualTo(SmallDeckOracle.EnumerateProbability(deck,
                            [.. combos.Where(c => c.GroupId == group.GroupId)],
                            size
                        )
                    )
                    .Within(1e-12)
            );
        }
    }
}
