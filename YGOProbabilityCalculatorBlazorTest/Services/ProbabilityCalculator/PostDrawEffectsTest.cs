using System.Diagnostics;
using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.CalculationEngineAdapter;
using CP = CardProbability;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public class PostDrawEffectsTest {
    private static ApplicationResultFractions CalculateApp(List<Card> deck, int hand, List<Combo> combos, IReadOnlyList<ComboGroup>? groups) =>
        ApplicationResultFractions.From(new ProbabilityCalculatorService().CalculateProbabilityResults(
            deck, combos, hand, groups, CalculationWorkPolicy.Interactive, new(Mode: CalculationModel.Exact)));
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
            ApplicationResultFractions result = CalculateApp(ordered, size, combos, groups);
            ApplicationFraction[] rows = [result.Total, .. result.Combos, .. result.Groups];
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
            ApplicationResultFractions result = CalculateApp(ordered, 3, combos, groups);
            ApplicationFraction[] rows = [result.Total, .. result.Combos, .. result.Groups];
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
            ApplicationResultFractions result = CalculateApp(ordered, 4, combos, groups);
            ApplicationFraction[] rows = [result.Total, .. result.Combos, .. result.Groups];
            for (int i = 0; i < rows.Length; i++) {
                Assert.That(rows[i].Numerator * oracle.Total, Is.EqualTo(oracle.Wins[i] * rows[i].Denominator), $"row {i}");
            }
        }
    }

    [Test]
    public void PostDrawPolicyIsGlobalAndActiveRouteChangesCanChangeTheHand() {
        List<Card> deck = [new([], id: "a"), new([], id: "b"),
            new([], id: "source", drawCount: 1, postDrawDiscardCount: 1)];
        Combo first = new([], cards: [new("a", 1, 1)]);
        Combo second = new([], cards: [new("b", 1, 1)]);
        foreach (List<Combo> combos in new[] { new List<Combo> { first }, [second], [first, second], [new([])] }) {
            DrawSequenceOracle.Counts oracle = DrawSequenceOracle.Enumerate(deck, 2, combos, []);
            ApplicationResultFractions result = CalculateApp(deck, 2, combos, []);
            ApplicationFraction[] rows = [result.Total, .. result.Combos];
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
    public void PostDrawEquivalentOrdinaryEntriesMergeWithoutChangingFractions() {
        List<Card> merged = [new([A], 2, id: "a"), new([], 2, id: "f"),
            new([], id: "g", drawCount: 3, postDrawDiscardCount: 2)];
        List<Card> split = [merged[0].WithCopies(1), new([A], id: "other-a"), merged[1].WithCopies(1), new([], id: "other-f"), merged[2]];
        List<Combo> combos = [new([new(A, 1, 2)])];
        DrawSequenceOracle.Counts oracle = DrawSequenceOracle.Enumerate(split, 2, combos, []);
        ApplicationFraction result = CalculateApp(merged, 2, combos, []).Total;
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
            CP.CalculationRequest request = new CalculationRequestAdapter(deck, true).Create(combos, 5, []);
            Stopwatch timer = Stopwatch.StartNew();
            CP.CalculationResult result = CP.CalculationEngine.Calculate(request, new CP.CalculationOptions(CP.CalculationMode.Exact,
                new CP.ExecutionPolicy(CalculationWorkPolicy.Interactive.WorkUnits)));
            TestContext.Out.WriteLine($"{kind}: status={result.Status}; p={result.Total?.Probability:R}; work={result.Metrics.WorkSpent}; peakCells={result.Metrics.PeakCells}; ms={timer.Elapsed.TotalMilliseconds:F2}");
            Assert.That(result.Status, Is.EqualTo(CP.CalculationStatus.Completed));
        }
    }
}
