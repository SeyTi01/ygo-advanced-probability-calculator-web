using System.Diagnostics;
using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Counting;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public class DrawActivationPriorityTest {
    private static readonly CategoryBase A = new("A");
    private static readonly CategoryBase B = new("B");
    private static DrawActivationPriority Custom(params string[] ids) => new(DrawActivationMode.CustomPriority, ids);

    [Test]
    public void ReversedPriorityChangesExactExhaustionResultsAndDisabledRankingRestoresFifo() {
        List<Card> deck = [new([], id: "a", drawCount: 1), new([], id: "b", drawCount: 2), new([], 2, id: "f")];
        List<Combo> combos = [new([], cards: [new("b", 1, 1)])];
        foreach (DrawActivationPriority policy in new[] { DrawActivationPriority.Default, Custom("a", "b"), Custom("b", "a"), new(DrawActivationMode.EncounterOrder, ["b", "a"]) }) {
            DrawSequenceOracle.Counts oracle = AssertParity(deck, 2, combos, [], policy);
            int expectedWins = policy.Mode == DrawActivationMode.EncounterOrder ? 6 : policy.CardIds[0] == "a" ? 8 : 4;
            Assert.That(oracle.Wins[0], Is.EqualTo(new BigInteger(expectedWins)));
            Assert.That(oracle.Total, Is.EqualTo(new BigInteger(24)));
        }
        ExactProbability first = Calculate(deck, 2, combos, Custom("a", "b")).Total;
        ExactProbability reversed = Calculate(deck, 2, combos, Custom("b", "a")).Total;
        Assert.That(first, Is.Not.EqualTo(reversed));
        Assert.That(Calculate(deck, 2, combos, new(DrawActivationMode.EncounterOrder, ["b", "a"])).Total,
            Is.EqualTo(Calculate(deck, 2, combos, DrawActivationPriority.Default).Total));
        Assert.That(Calculate(deck.AsEnumerable().Reverse().ToList(), 2, combos, Custom("a", "b")).Total, Is.EqualTo(first));
    }

    [Test]
    public void NewHighPriorityWaitsForBatchAndThenOvertakesOlderPendingSource() {
        Card a = new([], id: "a", drawCount: 1);
        Card b = new([], id: "b", drawCount: 2);
        Card c = new([], id: "c", drawCount: 2);
        List<Card> order = [a, b, c, new([], id: "x"), new([], id: "y"), new([], id: "z")];
        Assert.That(DrawSequenceOracle.Resolve(order, 2, [], ["a", "c", "b"])!.Select(card => card.Id),
            Is.EquivalentTo(new[] { "b", "x", "y" }));
        Assert.That(DrawSequenceOracle.Resolve(order, 2, [])!.Select(card => card.Id),
            Is.EquivalentTo(new[] { "c", "x", "y" }));
        AssertParity(order, 2, [new([], cards: [new("b", 1, 1)]), new([], cards: [new("c", 1, 1)])], [], Custom("a", "c", "b"));

        // C is drawn first in A's batch, but cannot activate before A's mandatory removal.
        Card batch = new([], id: "batch", drawCount: 2, postDrawDiscardCount: 1, postDrawSelector: CardSelector.For(A));
        List<Card> atomic = [batch, c.WithCategories([A]), new([], id: "x"), new([], id: "y"), new([], id: "z")];
        Assert.That(DrawSequenceOracle.Resolve(atomic, 1, [], ["c", "batch"])!.Select(card => card.Id), Is.EqualTo(new[] { "x" }));
        AssertParity(atomic, 1, [new([], cards: [new("c", 0, 0)])], [], Custom("c", "batch"));
    }

    [Test]
    public void BlockedHighPriorityDefersUntilMaterialArrivesAndImpossibleSourcesTerminate() {
        Card cost = new([], id: "cost", drawCount: 1, preDrawRemovalCount: 1, preDrawSelector: CardSelector.For(A));
        Card draw = new([], id: "draw", drawCount: 1);
        List<Card> order = [cost, draw, new([A], id: "material"), new([], id: "reward"), new([], id: "f")];
        Assert.That(DrawSequenceOracle.Resolve(order, 2, [], ["cost", "draw"])!.Select(card => card.Id), Is.EqualTo(new[] { "reward" }));
        AssertParity(order, 2, [new([], cards: [new("reward", 1, 1)])], [], Custom("cost", "draw"));
        order[2] = order[2].WithCategories([]);
        AssertParity(order, 2, [new([], cards: [new("cost", 1, 1)])], [], Custom("cost", "draw"));
    }

    [TestCase(false), TestCase(true)]
    public void IndependentOracleCoversCopiesCostsRemovalsHiddenPositionsAndOverlappingRequirements(bool opt) {
        List<Combo> combos = [new([new(A, 1, 3), new(B, 1, 3)], groupId: "g"),
            new([new(A, 1, 2)], cards: [new("reward", 1, 1)], groupId: "g"),
            new([], cards: [new("a", 1, 2)]), new([new(B, 0, 0)])];
        List<ComboGroup> groups = [new("g", "Group"), new("empty", "Empty")];
        foreach (int variant in Enumerable.Range(0, 4)) {
            List<Card> deck = [new([], 2, "Same name", id: "a", externalCardId: 100, drawCount: 1, drawOncePerTurn: opt,
                    postDrawDiscardCount: variant == 0 ? 1 : 0, preDrawDeckRemovalCount: variant == 1 ? 1 : 0),
                new([A], name: "Same name", id: "b", externalCardId: 100, drawCount: 2,
                    preDrawRemovalCount: variant == 2 ? 1 : 0, preDrawSelector: variant == 2 ? CardSelector.For(A) : null,
                    postDrawDeckMillCount: variant == 3 ? 1 : 0),
                new([A, B], id: "reward"), new([A], id: "material"), new([B], id: "other"), new([], id: "f")];
            foreach (int handSize in new[] { 1, 2, 3 }) {
                foreach (DrawActivationPriority priority in new[] { Custom("b", "a"), Custom("a"), Custom(), DrawActivationPriority.Default }) {
                    AssertParity(deck, handSize, combos, groups, priority);
                }
            }
        }
    }

    [Test]
    public void UnlistedTiesKeepFifoAndPriorityNeverOrdersDiscardChoices() {
        List<Card> deck = [new([], id: "a", drawCount: 1, postDrawDiscardCount: 1), new([], id: "b", drawCount: 2),
            new([A], id: "reward"), new([], 2, id: "f")];
        List<Combo> combos = [new([new(A, 1, 1)]), new([], cards: [new("b", 1, 1)])];
        Assert.That(Calculate(deck, 2, combos, Custom("unknown")).Total,
            Is.EqualTo(Calculate(deck, 2, combos, DrawActivationPriority.Default).Total));
        AssertParity(deck, 2, combos, [], Custom("b", "a"));
    }

    [Test]
    public void ProvenFastPathsRemainOrderIndependentAndWithinInteractiveBudget() {
        List<Card> deck = [new([], 3, id: "a", drawCount: 2), new([], 3, id: "b", drawCount: 1),
            new([A], 10, id: "reward"), new([], 24, id: "f")];
        List<Combo> combos = [new([new(A, 1, 5)])];
        Assert.That(DrawExecutionAssessment.Assess(deck, combos, 5, true).Path, Is.EqualTo(DrawExecutionPath.DrawCountState));
        Assert.That(Calculate(deck, 5, combos, Custom("a", "b")).Total, Is.EqualTo(Calculate(deck, 5, combos, Custom("b", "a")).Total));
        // A small version also checks the retained path against independent physical enumeration.
        List<Card> small = [deck[0].WithCopies(1), deck[1].WithCopies(1), deck[2].WithCopies(2), deck[3].WithCopies(2)];
        AssertParity(small, 2, combos, [], Custom("a", "b"));
        AssertParity(small, 2, combos, [], Custom("b", "a"));
        List<Card> exchangeable = [new([], 3, id: "exchange", drawCount: 2, drawOncePerTurn: false, postDrawDiscardCount: 1),
            new([A], 10, id: "reward"), new([], 27, id: "f")];
        Assert.That(DrawExecutionAssessment.Assess(exchangeable, combos, 5, true).Path, Is.EqualTo(DrawExecutionPath.ExchangeableConditional));
        Assert.That(Calculate(exchangeable, 5, combos, Custom("exchange")).Total,
            Is.EqualTo(Calculate(exchangeable, 5, combos, DrawActivationPriority.Default).Total));
    }

    [TestCase(false), TestCase(true)]
    public void CustomAndBuiltInPlansAgreeWithIndependentPolicyOracle(bool configured) {
        List<Card> deck = [new([], id: "a", drawCount: 2, preDrawRemovalCount: 1, preDrawSelector: CardSelector.For(A), postDrawDeckMillCount: 1),
            new([], id: "b", drawCount: 1, preDrawDeckRemovalCount: 1), new([A], id: "reward"), new([], 3, id: "f")];
        List<Combo> combos = [new([], cards: [new("a", 1, 1)]), new([new(A, 1, 1)])];
        DrawActivationPriority priority = Custom("a", "b");
        List<Card> modeled = configured ? deck : deck.Select(card => card.WithPreDrawRemovalCount(0)
            .WithPostDrawDiscardCount(0).WithPreDrawDeckRemovalCount(0).WithPostDrawDeckMillCount(0)).ToList();
        DrawSequenceOracle.Counts oracle = DrawSequenceOracle.Enumerate(modeled, 2, combos, [], priorityIds: priority.CardIds);
        ExactCalculationResult result = new DrawEffectCalculation(modeled, 2, new(CalculationWorkPolicy.Interactive), priority).Calculate(combos, []);
        Assert.That(result.Total.Numerator * oracle.Total, Is.EqualTo(oracle.Wins[0] * result.Total.Denominator));
        CalculationSnapshot snapshot = CalculationSnapshot.Capture(modeled, combos, 2, [], options: new(priority));
        Assert.That(CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json)).TotalProbability,
            Is.EqualTo((double)oracle.Wins[0] / (double)oracle.Total).Within(1e-12));
    }

    [Test]
    public void InactiveAndZeroCopyEntriesDoNotInfluenceWorkerProbability() {
        List<Card> active = [new([], id: "a", drawCount: 1), new([], id: "b", drawCount: 2), new([], 2, id: "f")];
        List<Card> all = [.. active, new([], 3, active: false, id: "inactive", drawCount: 3), new([], 0, id: "zero", drawCount: 3)];
        List<Combo> combos = [new([], cards: [new("b", 1, 1)])];
        DrawActivationPriority priority = Custom("inactive", "zero", "b", "a");
        CalculationSnapshot snapshot = CalculationSnapshot.Capture(all, combos, 2, [], options: new(priority));
        Assert.That(CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json)).TotalProbability, Is.EqualTo(1d / 6).Within(1e-12));
    }

    [Test]
    public void PriorityStillHonorsWorkLimits() {
        List<Card> deck = [new([], 2, id: "a", drawCount: 2, postDrawDiscardCount: 1), new([], 2, id: "b", drawCount: 2), new([A], 4, id: "reward")];
        List<Combo> combos = [new([new(A, 1, 5)])];
        Assert.Throws<ProbabilityCalculationLimitException>(() => new DrawEffectCalculation(deck, 2, new(new CalculationWorkPolicy(1)), Custom("b", "a")).Calculate(combos, []));
    }

    [Test, Explicit("Repeatable local benchmark; run with an explicit test filter.")]
    public void BenchmarkPriorityPolicies() {
        List<Combo> combos = [new([new(A, 1, 5)])];
        List<Card> deck = [new([], 2, id: "a", drawCount: 2, preDrawRemovalCount: 1, preDrawSelector: CardSelector.For(B)),
            new([], 2, id: "b", drawCount: 2, postDrawDiscardCount: 1, preDrawDeckRemovalCount: 1),
            new([A], 8, id: "reward"), new([B], 6, id: "cost"), new([A, B], 4, id: "both"), new([], 18, id: "f")];
        foreach (bool configured in new[] { true, false }) {
            List<Card> modeled = configured ? deck : deck.Select(card => card.WithPreDrawRemovalCount(0).WithPostDrawDiscardCount(0).WithPreDrawDeckRemovalCount(0)).ToList();
            foreach (DrawActivationPriority priority in new[] { DrawActivationPriority.Default, Custom("b", "a") }) {
                List<double> times = [];
                long work = 0;
                for (int repeat = 0; repeat < 6; repeat++) {
                    Stopwatch watch = Stopwatch.StartNew();
                    WorkBudget budget = new(CalculationWorkPolicy.Interactive);
                    ExactCalculationResult result = new DrawEffectCalculation(modeled, 5, budget, priority).Calculate(combos, []);
                    work = budget.Spent;
                    if (repeat > 0) {
                        times.Add(watch.Elapsed.TotalMilliseconds);
                    }
                }
                TestContext.Out.WriteLine($"configured={configured}, priority={priority.Mode}, path={DrawExecutionAssessment.Assess(modeled, combos, 5, true).Path}, work={work}, median-ms={times.Order().ElementAt(2):F3}");
            }
        }
    }

    private static ExactCalculationResult Calculate(List<Card> deck, int handSize, List<Combo> combos, DrawActivationPriority priority) =>
        new DrawEffectCalculation(deck, handSize, new(CalculationWorkPolicy.Interactive), priority).Calculate(combos, []);

    private static DrawSequenceOracle.Counts AssertParity(List<Card> deck, int handSize, List<Combo> combos, List<ComboGroup> groups, DrawActivationPriority priority) {
        DrawSequenceOracle.Counts oracle = DrawSequenceOracle.Enumerate(deck, handSize, combos, groups,
            priorityIds: priority.Mode == DrawActivationMode.CustomPriority ? priority.CardIds : null);
        ExactCalculationResult result = new DrawEffectCalculation(deck, handSize, new(CalculationWorkPolicy.Interactive), priority).Calculate(combos, groups);
        ExactProbability[] rows = [result.Total, .. result.Combos, .. result.Groups];
        for (int i = 0; i < rows.Length; i++) {
            Assert.That(rows[i].Numerator * oracle.Total, Is.EqualTo(oracle.Wins[i] * rows[i].Denominator), $"row {i}, hand {handSize}, priority {string.Join(',', priority.CardIds)}");
        }
        CalculationSnapshot snapshot = CalculationSnapshot.Capture(deck, combos, handSize, groups, options: new(priority));
        Assert.That(CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json)).TotalProbability,
            Is.EqualTo((double)oracle.Wins[0] / (double)oracle.Total).Within(1e-12));
        Assert.That(new ProbabilityCalculatorService().CalculateProbabilityForCombos(deck, combos, handSize,
            CalculationWorkPolicy.Interactive, new CalculationOptions(priority)),
            Is.EqualTo((double)oracle.Wins[0] / (double)oracle.Total).Within(1e-12));
        return oracle;
    }
}
