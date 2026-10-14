using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

public class HiddenDeckRemovalTest {
    private static readonly CategoryBase A = new("A");
    private static readonly CategoryBase B = new("B");

    private static List<Combo> Routes() => [
        new([new(A, 1, 3), new(B, 1, 3)], groupId: "g"),
        new([ComboCategory.SameCard([A, B], 1, 2)], groupId: "g"),
        new([], cards: [new("target", 1, 1)]),
        new([new(A, 0, 0)]),
        new([], alternativeGroups: [new([ComboAlternative.For(new ComboCategory(A, 1, 1)), ComboAlternative.For(new ComboCard("pot", 1, 1))])])
    ];

    [TestCase(1, false, false)]
    [TestCase(2, false, false)]
    [TestCase(3, false, false)]
    [TestCase(1, true, false)]
    [TestCase(2, true, false)]
    [TestCase(1, false, true)]
    [TestCase(2, true, true)]
    public void ExactRowsMatchPhysicalPermutations(int cost, bool once, bool handCost) {
        List<Card> deck = [
            new([], 2, id: "pot", drawCount: 1, drawOncePerTurn: once, preDrawDeckRemovalCount: cost,
                preDrawRemovalCount: handCost ? 1 : 0, preDrawSelector: handCost ? CardSelector.For(A) : null),
            new([], id: "draw", drawCount: 2, drawOncePerTurn: false),
            new([A, B], id: "target"), new([A], id: "a"), new([B], id: "b"), new([], id: "filler")
        ];
        Verify(deck, 2, Routes());
        deck.Reverse();
        Verify(deck, 2, Routes());
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void FilteredRemovalAndFailureMassMatchPhysicalPermutations(int cost) {
        List<Card> deck = [new([], id: "pot", drawCount: 2, preDrawDeckRemovalCount: cost),
            new([], id: "allure", drawCount: 2, drawOncePerTurn: false, postDrawDiscardCount: 1, postDrawSelector: CardSelector.For(B)),
            new([A, B], id: "target"), new([A], id: "a"), new([B], id: "b"), new([], 2, id: "filler")];
        Verify(deck, 2, Routes());
    }

    [TestCase(1)]
    [TestCase(2)]
    public void FastAndOrderedPathsHaveIdenticalTerminalHands(int cost) {
        List<Card> deck = [new([], 2, id: "pot", drawCount: 1, preDrawDeckRemovalCount: cost),
            new([A, B], id: "target"), new([A], id: "a"), new([], 2, id: "filler")];
        DrawSequenceOracle.Counts oracle = DrawSequenceOracle.Enumerate(deck, 1, [], []);
        List<Combo> hands = oracle.Hands.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair =>
            new Combo([], cards: pair.Key.Split(',').Select((count, i) => new ComboCard(oracle.CardIds[i], int.Parse(count), int.Parse(count))).ToArray(), groupId: "g")).ToList();
        Assert.That(DrawExecutionAssessment.Assess(deck, hands, 1, true).Path, Is.EqualTo(DrawExecutionPath.DrawCountState));
        Verify(deck, 1, hands);
    }

    [TestCase(0)]
    [TestCase(9)]
    [TestCase(10)]
    [TestCase(11)]
    [TestCase(12)]
    [TestCase(13)]
    public void RealPotRequiresTwelvePhysicalCards(int remaining) {
        // Opening one: when Pot is opened it draws two uniform unseen cards iff the full cost fits.
        List<Card> deck = [new([], id: "pot", drawCount: 2, preDrawDeckRemovalCount: 10)];
        if (remaining > 0) {
            deck.Add(new([], id: "target"));
            if (remaining > 1) {
                deck.Add(new([], remaining - 1, id: "filler"));
            }
        }
        List<Combo> combos = [new([], cards: [new("pot", 1, 1)]), new([], cards: [new("target", 1, 1)])];
        ExactCalculationResult result = new DrawEffectCalculation(deck, 1, new(CalculationWorkPolicy.Interactive)).Calculate(combos, []);
        AssertFraction(result.Combos[0], remaining < 12 ? 1 : 0, remaining + 1);
        AssertFraction(result.Combos[1], remaining == 0 ? 0 : remaining < 12 ? 1 : remaining + 2, remaining < 12 ? remaining + 1 : remaining * (remaining + 1));
    }

    [Test]
    public void HiddenPoolIsNotThePhysicalDrawDenominator() {
        // Five visible positions remain possible for each label despite hiding two.
        List<Card> deck = [new([], id: "pot", drawCount: 1, preDrawDeckRemovalCount: 2),
            new([], id: "target"), new([], 3, id: "filler")];
        ExactCalculationResult result = new PostDrawCalculation(deck, 1, new(CalculationWorkPolicy.Interactive))
            .Calculate([new([], cards: [new("target", 1, 1)])], []);
        AssertFraction(result.Total, 1, 4); // 1/5 opening + 1/5 * 1/4 after Pot.
    }

    [Test]
    public void AtomicCostsAndNewlyUnpayableCandidatesMatchOracle() {
        List<Card> deck = [new([], id: "pot", drawCount: 1, preDrawDeckRemovalCount: 2, preDrawRemovalCount: 1),
            new([], id: "deferred", drawCount: 1, preDrawDeckRemovalCount: 1, preDrawRemovalCount: 1, preDrawSelector: CardSelector.For(B)),
            new([], id: "graceful", drawCount: 3, drawOncePerTurn: false, postDrawDiscardCount: 2),
            new([A, B], id: "target"), new([A], id: "a"), new([], 2, id: "filler")];
        Verify(deck, 3, Routes());
    }

    [TestCase(1)]
    [TestCase(2)]
    public void ExchangeableHiddenCostRequiresCollectiveCapacity(int effectCopies) {
        Card effect = new([], effectCopies, id: "graceful", drawCount: 2, drawOncePerTurn: false, postDrawDiscardCount: 1, preDrawDeckRemovalCount: 1);
        List<Card> deck = [effect, new([A], 2, id: "a"), new([], 2 * effectCopies, id: "filler")];
        List<Combo> combos = [new([new(A, 1, 2)])];
        Assert.That(DrawExecutionAssessment.Assess(deck, combos, 2, true).Path, Is.EqualTo(DrawExecutionPath.ExchangeableConditional));
        Verify(deck, 2, combos);
        DrawExecutionPath expected = effectCopies == 1 ? DrawExecutionPath.Ordinary : DrawExecutionPath.OrderedFrontier;
        Assert.That(DrawExecutionAssessment.Assess(deck, combos, 3, true).Path, Is.EqualTo(expected));
        Verify(deck, 3, combos);
    }

    [TestCase("pot")]
    [TestCase("trade")]
    [TestCase("allure")]
    [TestCase("graceful")]
    public void RealPotChainingAndMixedCostsMatchPhysicalOrders(string kind) {
        Card other = kind switch {
            "pot" => new([], id: "second-pot", drawCount: 2, preDrawDeckRemovalCount: 10),
            "trade" => new([], id: "trade", drawCount: 2, preDrawRemovalCount: 1, preDrawSelector: CardSelector.For(B)),
            "allure" => new([], id: "allure", drawCount: 2, postDrawDiscardCount: 1, postDrawSelector: CardSelector.For(B)),
            _ => new([], id: "graceful", drawCount: 3, drawOncePerTurn: false, postDrawDiscardCount: 2)
        };
        List<Card> deck = [new([], 2, id: "pot", drawCount: 2, preDrawDeckRemovalCount: 10), other,
            new([A, B], id: "target"), new([], 11, id: "filler")];
        Verify(deck, 2, Routes(), true);
    }

    [Test]
    public void NewlyUnpayableEffectIsNotProtectedFromRemoval() {
        Card pot = new([], id: "pot", drawCount: 1, preDrawDeckRemovalCount: 2, postDrawDiscardCount: 1);
        Card blocked = new([], id: "blocked", drawCount: 1, preDrawDeckRemovalCount: 2);
        Card filler = new([], 3, id: "filler");
        Card target = new([A], id: "target");
        Card last = new([], id: "last");
        List<Combo> combos = [new([new(A, 1, 1)], cards: [new("blocked", 0, 0)])];
        List<Card>? hand = DrawSequenceOracle.Resolve([pot, blocked, filler, filler, filler, target, last], 3, combos);
        Assert.That(hand!.Select(card => card.Id), Is.EquivalentTo(new[] { "filler", "target" }));
        Verify([pot, blocked, filler, target, last], 3, combos);
    }

    [Test]
    public void CategoryRulesAndInactiveCardsPreserveWorkerSemantics() {
        CategoryBase dark = new("DARK", CategorySource.Metadata, "attribute:dark");
        CategoryBase monster = new("Monster", CategorySource.Metadata, "kind:monster");
        CategoryBase role = new("Role", rule: CardSelector.AllOf([dark.Identity, monster.Identity]));
        List<Card> raw = [new([], id: "pot", drawCount: 1, preDrawDeckRemovalCount: 2),
            new([dark, monster], 2, id: "target"), new([dark], id: "spell"), new([], 2, id: "filler"),
            new([], 3, active: false, id: "inactive", drawCount: 3, preDrawDeckRemovalCount: 60)];
        List<Card> manual = raw.Where(card => card.Active).Select(card => card.Id == "target" ? card.WithCategories([dark, monster, role]) : card).ToList();
        List<Combo> combos = [new([new(role, 1, 1)], groupId: "g"), new([ComboCategory.SameCard([role, dark], 1, 2)]), new([new(role, 0, 0)])];
        CalculationSnapshot projected = CalculationSnapshot.Capture(raw, combos, 2, [], options: new(), categoryDefinitions: [role]);
        Assert.That(projected.Json, Is.EqualTo(CalculationSnapshot.Capture(manual, combos, 2, [], options: new()).Json));
        Verify(manual, 2, combos);
        Assert.That(CalculationWire.Execute(projected.Json), Is.EqualTo(CalculationWire.Execute(CalculationSnapshot.Capture(manual, combos, 2, [], options: new()).Json)));
        Assert.That(raw[1].Categories, Does.Not.Contain(role));
    }

    [Test]
    public void CompressedStorageFailureRemainsTyped() {
        List<Card> deck = [new([], id: "pot", drawCount: 2, preDrawDeckRemovalCount: 2), new([], 262_144, id: "filler")];
        WorkBudget budget = new(CalculationWorkPolicy.Interactive);
        Assert.That(Assert.Throws<ProbabilityCalculationLimitException>(() => new PostDrawCalculation(deck, 1, budget).Calculate([new([])], []))!.Reason,
            Is.EqualTo(ProbabilityCalculationLimitReason.Storage));
    }

    [Test]
    public void ModesAndTypedLimitsUseTheSameWorkerConfiguration() {
        List<Card> deck = [new([], id: "pot", drawCount: 2, preDrawDeckRemovalCount: 10), new([], id: "target"), new([], id: "filler")];
        List<Combo> combos = [new([], cards: [new("target", 1, 1)])];
        double Run(bool enabled, bool conditions) => CalculationWire.ReadResult(CalculationWire.Execute(
            CalculationSnapshot.Capture(conditions ? deck : deck.Select(card => card.WithPreDrawDeckRemovalCount(0)), combos, 1, [], drawEffectsEnabled: enabled, options: new()).Json)).TotalProbability;
        Assert.That(Run(true, true), Is.EqualTo(1d / 3));
        Assert.That(Run(false, true), Is.EqualTo(1d / 3));
        Assert.That(Run(true, false), Is.EqualTo(2d / 3));
        string input = CalculationSnapshot.Capture(deck, combos, 1, [], new(1), options: new()).Json;
        Assert.That(Assert.Throws<ProbabilityCalculationLimitException>(() => CalculationWire.ReadResult(CalculationWire.Execute(input)))!.Reason,
            Is.EqualTo(ProbabilityCalculationLimitReason.Work));
        Assert.That(DrawExecutionAssessment.Assess(deck.Select(card => card.WithPreDrawDeckRemovalCount(0)).ToList(), combos, 1, true).Path, Is.EqualTo(DrawExecutionPath.DrawCountState));
        Assert.That(DrawExecutionAssessment.Assess(deck, combos, 1, true).Path, Is.EqualTo(DrawExecutionPath.Ordinary));
    }

    private static void Verify(List<Card> deck, int handSize, List<Combo> combos, bool collapseCopies = false) {
        List<ComboGroup> groups = [new("g", "Routes"), new("empty", "Empty")];
        DrawSequenceOracle.Counts oracle = DrawSequenceOracle.Enumerate(deck, handSize, combos, groups, collapseCopies);
        foreach (ExactCalculationResult result in new[] {
            new PostDrawCalculation(deck, handSize, new(CalculationWorkPolicy.Interactive)).Calculate(combos, groups),
            new DrawEffectCalculation(deck, handSize, new(CalculationWorkPolicy.Interactive)).Calculate(combos, groups)
        }) {
            ExactProbability[] rows = [result.Total, .. result.Combos, .. result.Groups];
            for (int i = 0; i < rows.Length; i++) {
                AssertFraction(rows[i], oracle.Wins[i], oracle.Total);
            }
        }
    }

    private static void AssertFraction(ExactProbability actual, BigInteger numerator, BigInteger denominator) =>
        Assert.That(actual.Numerator * denominator, Is.EqualTo(numerator * actual.Denominator));
}
