using System.Diagnostics;
using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public class PostDrawEffectsTest {
    private static readonly CategoryBase A = new("A");
    private static readonly CategoryBase B = new("B");

    private static IEnumerable<TestCaseData> Decks() {
        foreach (bool once in new[] { false, true }) {
            yield return new TestCaseData(new List<Card> {
                new([A, B], id: "a"), new([], id: "filler"),
                new([], 3, "Same", id: "d", drawCount: 1, drawOncePerTurn: once, postDrawDiscardCount: 1),
                new([A], 3, "Same", id: "e", drawCount: 3, drawOncePerTurn: once, postDrawDiscardCount: 2)
            }, 2).SetName($"PostDrawThreeCopiesOfBoth_{once}");
        }
        foreach ((int draw, int discard) in new[] { (1, 1), (3, 2), (1, 2) }) {
            foreach (bool once in new[] { false, true }) {
                foreach (int copies in new[] { 1, 2, 3 }) {
                    foreach (int size in new[] { 0, 1, 2, 4 }) {
                        List<Card> deck = [new([A, B], id: "a"), new([A], id: "b"), new([], id: "filler"),
                            new([], copies, "Same", id: "d", drawCount: draw, drawOncePerTurn: once, postDrawDiscardCount: discard)];
                        yield return new TestCaseData(deck, size).SetName($"PostDraw_{draw}_{discard}_{once}_{copies}_{size}");
                    }
                }
            }
        }
        foreach (int draw in new[] { 1, 2, 3 }) {
            foreach (int discard in new[] { 0, 1, 2 }) {
                foreach (bool once in new[] { false, true }) {
                    List<Card> deck = [new([A, B], id: "a"), new([B], id: "b"), new([], id: "filler"),
                        new([], 2, "Same", id: "d", drawCount: 1, drawOncePerTurn: false, postDrawDiscardCount: 1),
                        new([A], 2, "Same", id: "e", drawCount: draw, drawOncePerTurn: once, postDrawDiscardCount: discard)];
                    yield return new TestCaseData(deck, 2).SetName($"PostDrawMixed_{draw}_{discard}_{once}");
                }
            }
        }
    }

    [TestCaseSource(nameof(Decks))]
    public void PostDrawMatchesLabeledOracleExactly(List<Card> deck, int size) {
        List<Combo> combos = [
            new([new(A, 1, 0, RequirementMaximumMode.HandSize), new(B, 1, 0, RequirementMaximumMode.HandSize)], groupId: "g"),
            new([], cards: [new("a", 1, 1)], groupId: "g"),
            new([], cards: [new("d", 1, 2)]),
            new([new(A, 0, 0)]), new([new(B, 0, 1)]),
            new([new(A, 3, 0, RequirementMaximumMode.HandSize)]),
            new([new(A, 1, 1)], cards: [new("a", 1, 1)]),
            new([], alternativeGroups: [new([ComboAlternative.For(new ComboCategory(B, 1, 1)), ComboAlternative.For(new ComboCard("e", 1, 1))])])
        ];
        List<ComboGroup> groups = [new("g", "Group"), new("empty", "Empty")];
        DrawSequenceOracle.Counts oracle = DrawSequenceOracle.Enumerate(deck, size, combos, groups);
        foreach (List<Card> ordered in new[] { deck, deck.AsEnumerable().Reverse().ToList() }) {
            WorkBudget budget = new(CalculationWorkPolicy.Interactive);
            ExactCalculationResult result = new DrawEffectCalculation(ordered, size, budget).Calculate(combos, groups);
            ExactProbability[] rows = [result.Total, .. result.Combos, .. result.Groups];
            for (int i = 0; i < rows.Length; i++) {
                Assert.That(rows[i].Numerator * oracle.Total, Is.EqualTo(oracle.Wins[i] * rows[i].Denominator), $"row {i}");
            }
        }
    }

    [Test]
    public void DeferredCostPriorityMatchesExactPhysicalOracleAndReversedDeckRows() {
        CategoryBase dark = new("Dark");
        Card b = new([], id: "b-post-draw", drawCount: 1, postDrawDiscardCount: 1);
        Card a = new([], id: "a-deferred", drawCount: 1, preDrawRemovalCount: 1,
            preDrawSelector: CardSelector.For(dark));
        Card c = new([], id: "c-draw", drawCount: 1);
        Card filler = new([], id: "filler");
        Card material = new([dark], id: "dark-material");
        Card firstReward = new([], id: "reward-r");
        Card secondReward = new([], id: "reward-q");
        List<Card> deck = [b, a, c, filler, material, firstReward, secondReward];
        List<Combo> combos = [
            new([], cards: [new(firstReward.Id, 1, 1)], groupId: "targets"),
            new([], cards: [new(secondReward.Id, 1, 1)], groupId: "targets")
        ];
        List<ComboGroup> groups = [new("targets", "Targets")];
        DrawSequenceOracle.Counts oracle = DrawSequenceOracle.Enumerate(deck, 3, combos, groups);

        Assert.That(oracle.Total, Is.EqualTo(new BigInteger(5_040)));
        foreach (List<Card> ordered in new[] { deck, deck.AsEnumerable().Reverse().ToList() }) {
            ExactCalculationResult result = new PostDrawCalculation(ordered, 3,
                new(CalculationWorkPolicy.Interactive)).Calculate(combos, groups);
            ExactProbability[] rows = [result.Total, .. result.Combos, .. result.Groups];
            for (int i = 0; i < rows.Length; i++) {
                Assert.That(rows[i].Numerator * oracle.Total, Is.EqualTo(oracle.Wins[i] * rows[i].Denominator), $"row {i}");
            }
        }
    }

    [Test]
    public void DeferredOptDuplicatesMatchExactPhysicalOracleAndReversedDeckRows() {
        CategoryBase dark = new("Dark");
        Card b = new([], id: "b-post-draw", drawCount: 1, postDrawDiscardCount: 1);
        Card opt = new([], copies: 2, id: "a-opt", drawCount: 1, drawOncePerTurn: true,
            preDrawRemovalCount: 1, preDrawSelector: CardSelector.For(dark));
        Card c = new([], id: "c-draw", drawCount: 1);
        Card filler = new([], id: "filler");
        Card material = new([dark], id: "dark-material");
        Card firstReward = new([], id: "reward-r");
        Card secondReward = new([], id: "reward-q");
        List<Card> deck = [b, opt, c, filler, material, firstReward, secondReward];
        List<Combo> combos = [
            new([], cards: [new(firstReward.Id, 1, 1)], groupId: "targets"),
            new([], cards: [new(secondReward.Id, 1, 1)], groupId: "targets")
        ];
        List<ComboGroup> groups = [new("targets", "Targets")];
        DrawSequenceOracle.Counts oracle = DrawSequenceOracle.Enumerate(deck, 4, combos, groups);

        Assert.That(oracle.Total, Is.EqualTo(new BigInteger(40_320)));
        foreach (List<Card> ordered in new[] { deck, deck.AsEnumerable().Reverse().ToList() }) {
            ExactCalculationResult result = new PostDrawCalculation(ordered, 4,
                new(CalculationWorkPolicy.Interactive)).Calculate(combos, groups);
            ExactProbability[] rows = [result.Total, .. result.Combos, .. result.Groups];
            for (int i = 0; i < rows.Length; i++) {
                Assert.That(rows[i].Numerator * oracle.Total, Is.EqualTo(oracle.Wins[i] * rows[i].Denominator), $"row {i}");
            }
        }
    }

    [Test]
    public void CustomZeroRemovalHasIdenticalFixedDrawWorkAndResults() {
        List<Card> deck = [new([A], 10, id: "a"), new([], 27, id: "filler"),
            new([], 3, id: "d", drawCount: 3, drawOncePerTurn: false, postDrawDiscardCount: 2)];
        List<Combo> combos = [new([new(A, 1, 5)])];
        WorkBudget disabled = new(CalculationWorkPolicy.Interactive);
        WorkBudget ordinary = new(CalculationWorkPolicy.Interactive);
        ExactCalculationResult actual = new DrawEffectCalculation(deck.Select(card => card.WithPostDrawDiscardCount(0)).ToList(), 5, disabled).Calculate(combos, []);
        ExactCalculationResult expected = new DrawEffectCalculation(deck.Select(card => card.WithPostDrawDiscardCount(0)).ToList(), 5, ordinary).Calculate(combos, []);
        Assert.That(actual.Total, Is.EqualTo(expected.Total));
        Assert.That(disabled.Spent, Is.EqualTo(ordinary.Spent));
        Assert.That(deck[^1].PostDrawDiscardCount, Is.EqualTo(2));
    }

    [Test]
    public void PostDrawPolicyIsGlobalAndActiveRouteChangesCanChangeTheHand() {
        List<Card> deck = [new([], id: "a"), new([], id: "b"),
            new([], id: "source", drawCount: 1, postDrawDiscardCount: 1)];
        Combo first = new([], cards: [new("a", 1, 1)]);
        Combo second = new([], cards: [new("b", 1, 1)]);
        foreach (List<Combo> combos in new[] { new List<Combo> { first }, [second], [first, second], [new([])] }) {
            DrawSequenceOracle.Counts oracle = DrawSequenceOracle.Enumerate(deck, 2, combos, []);
            ExactCalculationResult result = new DrawEffectCalculation(deck, 2, new(CalculationWorkPolicy.Interactive)).Calculate(combos, []);
            ExactProbability[] rows = [result.Total, .. result.Combos];
            for (int i = 0; i < rows.Length; i++) {
                Assert.That(rows[i].Numerator * oracle.Total, Is.EqualTo(oracle.Wins[i] * rows[i].Denominator));
            }
        }
        ProbabilityCalculatorService service = new();
        double protectedAlone = service.CalculateProbabilityForCombos(deck, [first], 2, CalculationWorkPolicy.Interactive, options: new());
        double inGlobalPolicy = service.CalculateProbabilityResults(deck, [first, second], 2, [], CalculationWorkPolicy.Interactive, options: new()).ComboProbabilities[0].Probability;
        Assert.That(protectedAlone, Is.GreaterThan(inGlobalPolicy));
    }

    [Test]
    public void PostDrawWorkFailureAbortsTheWholeRequest() {
        List<Card> deck = [new([A], 9, id: "a"), new([], 28, id: "f"),
            new([], 3, id: "g", drawCount: 3, drawOncePerTurn: false, postDrawDiscardCount: 2)];
        List<Combo> combos = [new([new(A, 1, 5)])];
        WorkBudget budget = new(CalculationWorkPolicy.Interactive);
        new PostDrawCalculation(deck, 5, budget).Calculate(combos, []);
        ProbabilityCalculationLimitException? error = Assert.Throws<ProbabilityCalculationLimitException>(() =>
            new PostDrawCalculation(deck, 5, new(new(budget.Spent - 1))).Calculate(combos, []));
        Assert.That(error!.Reason, Is.EqualTo(ProbabilityCalculationLimitReason.Work));
    }

    [Test]
    public void PostDrawStorageFailureIsIndependentOfTheWorkAllowance() {
        List<Card> deck = Enumerable.Range(0, 20).Select(index => new Card([], id: $"c{index}")).ToList();
        deck.Add(new([], 3, id: "g", drawCount: 3, postDrawDiscardCount: 2));
        List<Combo> combos = deck.Select(card => new Combo([], cards: [new(card.Id, 1, 1)])).ToList();
        ProbabilityCalculationLimitException? error = Assert.Throws<ProbabilityCalculationLimitException>(() =>
            new PostDrawCalculation(deck, 5, new(new(500_000_000))).Calculate(combos, []));
        Assert.That(error!.Reason, Is.EqualTo(ProbabilityCalculationLimitReason.Storage));
    }

    [Test]
    public void PostDrawEquivalentOrdinaryEntriesMergeWithoutChangingFractions() {
        List<Card> merged = [new([A], 2, id: "a"), new([], 2, id: "f"),
            new([], id: "g", drawCount: 3, postDrawDiscardCount: 2)];
        List<Card> split = [merged[0].WithCopies(1), new([A], id: "other-a"), merged[1].WithCopies(1), new([], id: "other-f"), merged[2]];
        List<Combo> combos = [new([new(A, 1, 2)])];
        DrawSequenceOracle.Counts oracle = DrawSequenceOracle.Enumerate(split, 2, combos, []);
        ExactProbability result = new PostDrawCalculation(merged, 2, new(CalculationWorkPolicy.Interactive)).Calculate(combos, []).Total;
        Assert.That(result.Numerator * oracle.Total, Is.EqualTo(oracle.Wins[0] * result.Denominator));
    }

    [Test, Explicit("Mathematical spike performance gate")]
    public void PostDrawFortyCardSpike() {
        foreach (string kind in new[] { "dealings", "graceful", "both", "mixed" }) {
            List<Card> deck = [new([A], 9, id: "a"), new([B], 9, id: "b")];
            if (kind != "graceful") {
                deck.Add(new([], 3, id: "d", drawCount: 1, drawOncePerTurn: false, postDrawDiscardCount: 1));
            }
            if (kind != "dealings") {
                deck.Add(new([], 3, id: "g", drawCount: 3, drawOncePerTurn: false, postDrawDiscardCount: 2));
            }
            if (kind == "mixed") {
                deck.Add(new([], 3, id: "p", drawCount: 2));
            }
            deck.Add(new([], 40 - deck.Sum(card => card.Copies), id: "z"));
            List<Combo> combos = [new([new(A, 1, 0, RequirementMaximumMode.HandSize), new(B, 1, 0, RequirementMaximumMode.HandSize)])];
            WorkBudget budget = new(CalculationWorkPolicy.Interactive);
            PostDrawCalculation calculation = new(deck, 5, budget);
            Stopwatch timer = Stopwatch.StartNew();
            try {
                ExactCalculationResult result = calculation.Calculate(combos, []);
                TestContext.Out.WriteLine($"{kind}: p={result.Total.ToDouble(budget):R}; work={budget.Spent}; terminal={calculation.TerminalStates}; peak={calculation.PeakStates}; ms={timer.Elapsed.TotalMilliseconds:F2}");
            }
            catch (ProbabilityCalculationLimitException error) {
                TestContext.Out.WriteLine($"{kind}: {error.Reason}; work={budget.Spent}; peak={calculation.PeakStates}; ms={timer.Elapsed.TotalMilliseconds:F2}");
                throw;
            }
        }
    }
}
