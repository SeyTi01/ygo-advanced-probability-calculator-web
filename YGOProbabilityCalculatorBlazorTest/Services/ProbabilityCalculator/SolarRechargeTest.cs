using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

public class SolarRechargeTest {
    private static readonly CategoryBase Lightsworn = new("Lightsworn", CategorySource.Metadata, "archetype:lightsworn");
    private static readonly CategoryBase Monster = new("Monster", CategorySource.Metadata, "kind:monster");
    private static readonly CategoryBase A = new("A");

    private static Card Solar(int copies = 1) => CardEffectRegistry.Default.RestoreBuiltIn(new([], copies, id: "solar", externalCardId: 691925));
    private static List<Combo> Routes() => [
        new([new(Lightsworn, 1, 3), new(Monster, 1, 3)], groupId: "g"),
        new([ComboCategory.SameCard([Lightsworn, Monster], 1, 2)], groupId: "g"),
        new([], cards: [new("target", 1, 1), new("solar", 1, 1)]),
        new([new(A, 0, 0)]),
        new([], alternativeGroups: [new([ComboAlternative.For(ComboCategory.SameCard([Lightsworn, Monster], 1, 2)),
            ComboAlternative.For(new ComboCard("solar", 1, 1))])])
    ];

    [TestCase(1, 1), TestCase(2, 1), TestCase(1, 2), TestCase(2, 2)]
    public void PhysicalPostDrawOrdersMatchEveryExactResult(int mill, int copies) {
        List<Card> deck = [Solar(copies).WithPostDrawDeckMillCount(mill),
            new([Lightsworn, Monster, A], id: "target"), new([Lightsworn], id: "spell"), new([Monster], id: "unrelated"),
            new([], id: "draw", drawCount: 1, drawOncePerTurn: false), new([Lightsworn, Monster], id: "material")];
        Verify(deck, 2, Routes());
        deck.Reverse();
        Verify(deck, 2, Routes());
    }

    [TestCase(2), TestCase(3), TestCase(4)]
    public void FullFourCardBatchIsRequiredBeforePaying(int remaining) {
        Card solar = Solar();
        Card material = new([Lightsworn, Monster], id: "material");
        Card[] order = [solar, material, .. Enumerable.Range(0, remaining).Select(i => new Card([], id: $"filler{i}"))];
        List<Card> hand = DrawSequenceOracle.Resolve(order, 2, [])!;
        Assert.That(hand.Select(card => card.Id), remaining < 4
            ? Is.EquivalentTo(new[] { "solar", "material" }) : Is.EquivalentTo(new[] { "filler0", "filler1" }));
        Verify(order.ToList(), 2, [new([], cards: [new("solar", 1, 1)]), new([], cards: [new("material", 1, 1)])]);
    }

    [TestCase("absent"), TestCase("spell"), TestCase("monster"), TestCase("both"), TestCase("manual")]
    public void CostRequiresBothPropertiesOnOneOtherCard(string kind) {
        Card material = kind switch {
            "spell" => new([Lightsworn], id: "material"),
            "monster" => new([Monster], id: "material"),
            "both" => new([Lightsworn, Monster], id: "material"),
            "manual" => new Card([], id: "material").WithManualMetadataCategory(Lightsworn).WithManualMetadataCategory(Monster),
            _ => new([], id: "material")
        };
        // Even manually making the source eligible cannot let it pay its own cost.
        Card solar = Solar().WithCategories([Lightsworn, Monster]);
        List<Card> deck = [solar, material, new([], 4, id: "filler")];
        List<Card> hand = DrawSequenceOracle.Resolve([solar, material, .. Enumerable.Repeat(deck[2], 4)], 2, [])!;
        Assert.That(hand.Any(card => card.Id == solar.Id), Is.EqualTo(kind is not ("both" or "manual")));
        Verify(deck, 2, Routes());
    }

    [TestCase("desires"), TestCase("allure"), TestCase("trade"), TestCase("graceful"), TestCase("simultaneous")]
    public void MixedCostsDeferredMaterialAndFailedBranchesMatchOracle(string kind) {
        Card other = kind switch {
            "desires" => new([], id: "other", drawCount: 2, preDrawDeckRemovalCount: 1),
            "allure" => new([], id: "other", drawCount: 2, drawOncePerTurn: false, postDrawDiscardCount: 1, postDrawSelector: CardSelector.For(A)),
            "trade" => new([Lightsworn, Monster], id: "other", drawCount: 2, preDrawRemovalCount: 1, preDrawSelector: CardSelector.For(A)),
            "simultaneous" => new([], id: "other", drawCount: 1, preDrawRemovalCount: 1, preDrawDeckRemovalCount: 1, postDrawDiscardCount: 1, postDrawDeckMillCount: 1),
            _ => new([], id: "other", drawCount: 3, drawOncePerTurn: false, postDrawDiscardCount: 2)
        };
        List<Card> deck = [Solar(2).WithPostDrawDeckMillCount(1), other, new([Lightsworn, Monster, A], id: "target"),
            new([Lightsworn, Monster], id: "material"), new([], 2, id: "filler")];
        Verify(deck, 3, Routes());
    }

    [Test]
    public void RealDesiresCostAndSolarMillMatchPhysicalOrders() {
        List<Card> deck = [Solar(), CardEffectRegistry.Default.RestoreBuiltIn(new([], id: "desires", externalCardId: 35261759)),
            new([Lightsworn, Monster, A], id: "target"), new([], 12, id: "filler")];
        Verify(deck, 2, Routes(), true);
    }

    [Test]
    public void NewlyDrawnSolarResolvesAfterMillingAndHasNoOpt() {
        Card solar = Solar(2);
        Card material = new([Lightsworn, Monster], 2, id: "material");
        Card target = new([], id: "target");
        Card filler = new([], id: "filler");
        // Second Solar and its material are drawn; the target is milled, not drawn.
        List<Card> hand = DrawSequenceOracle.Resolve([solar, material, solar, material, target, filler, filler, filler, filler, filler], 2, [])!;
        Assert.That(hand.Select(card => card.Id), Is.EquivalentTo(new[] { "filler", "filler" }));
        Verify([solar, material, target, filler.WithCopies(5)], 2, Routes(), true);
    }

    [TestCase(1), TestCase(2)]
    public void CapacityProofReusesFastPathAndGracefulSpecialist(int mill) {
        Card effect = new([], id: "effect", drawCount: 1, postDrawDeckMillCount: mill);
        List<Card> deck = [effect, new([A], id: "target"), new([], 4, id: "filler")];
        List<Combo> combos = [new([new(A, 1, 1)])];
        Assert.That(DrawExecutionAssessment.Assess(deck, combos, 1, true).Path, Is.EqualTo(DrawExecutionPath.DrawCountState));
        Verify(deck, 1, combos);
        deck[0] = effect.WithDrawCount(2).WithPostDrawDiscardCount(1).WithDrawOncePerTurn(false);
        Assert.That(DrawExecutionAssessment.Assess(deck, combos, 1, true).Path, Is.EqualTo(DrawExecutionPath.ExchangeableConditional));
        Verify(deck, 1, combos);
        Assert.That(DrawExecutionAssessment.Assess(deck, combos, 4, true).Path, Is.EqualTo(DrawExecutionPath.Ordinary));
        Verify(deck, 4, combos);
    }

    [Test]
    public void CustomPlanAndBothModelsPreserveTheSavedPlanAndWorkerResults() {
        List<Card> deck = [Solar(), new([], id: "target"), new([], id: "filler")];
        List<Combo> combos = [new([], cards: [new("target", 1, 1)])];
        double Run(bool enabled, bool conditions) => CalculationWire.ReadResult(CalculationWire.Execute(
            CalculationSnapshot.Capture(conditions ? deck : deck.Select(card => card.WithPreDrawRemovalCount(0).WithPostDrawDeckMillCount(0)), combos, 1, [], drawEffectsEnabled: enabled, options: new()).Json)).TotalProbability;
        Assert.That(Run(true, true), Is.EqualTo(1d / 3));
        Assert.That(Run(false, true), Is.EqualTo(1d / 3));
        Assert.That(Run(true, false), Is.EqualTo(2d / 3));
        Assert.That(deck[0].EffectPlan, Is.EqualTo(new SolarRechargeEffect().Compile()));
    }

    private static void Verify(List<Card> deck, int handSize, List<Combo> combos, bool collapse = false) {
        List<ComboGroup> groups = [new("g", "Routes"), new("empty", "Empty")];
        DrawSequenceOracle.Counts expected = DrawSequenceOracle.Enumerate(deck, handSize, combos, groups, collapse);
        foreach (ExactCalculationResult actual in new[] {
            new PostDrawCalculation(deck, handSize, new(CalculationWorkPolicy.Interactive)).Calculate(combos, groups),
            new DrawEffectCalculation(deck, handSize, new(CalculationWorkPolicy.Interactive)).Calculate(combos, groups)
        }) {
            ExactProbability[] rows = [actual.Total, .. actual.Combos, .. actual.Groups];
            for (int i = 0; i < rows.Length; i++) {
                Assert.That(rows[i].Numerator * expected.Total, Is.EqualTo(expected.Wins[i] * rows[i].Denominator), $"row {i}");
            }
        }
    }
}
