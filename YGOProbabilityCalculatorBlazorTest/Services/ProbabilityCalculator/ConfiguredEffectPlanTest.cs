using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

public class ConfiguredEffectPlanTest {
    private static readonly CategoryBase Level = new("Level 8", CategorySource.Metadata, "level:8");
    private static readonly CategoryBase Dark = new("DARK", CategorySource.Metadata, "attribute:dark");
    private static readonly CategoryBase Monster = new("Monster", CategorySource.Metadata, "kind:monster");

    private static Card BuiltIn(int id, string entry) => CardEffectRegistry.Default.RestoreBuiltIn(
        new([], name: "Same name", id: entry, externalCardId: id));

    [TestCase("trade", false), TestCase("trade", true)]
    [TestCase("allure", false), TestCase("allure", true)]
    [TestCase("mixed", false), TestCase("mixed", true)]
    public void EffectivePlansMatchAllPhysicalOrderRowsAndRestoration(string shape, bool material) {
        Card trade = BuiltIn(38120068, "trade");
        Card allure = BuiltIn(1475311, "allure");
        List<Card> original = [shape == "allure" ? allure : trade,
            new(material ? [Level, Dark, Monster] : [], id: "material"),
            new([Dark, Monster], id: "dark"), new([], id: "target"), new([], 2, id: "filler")];
        if (shape == "mixed") {
            original.Add(allure);
        }
        List<Combo> combos = [new([], "Target", groupId: "g", cards: [new("target", 1, 1)]),
            new([new(Dark, 1, 2)], "DARK", groupId: "g"),
            new([new(Level, 0, 0)], "No Level 8", groupId: "exclusion"),
            new([], "Retained source", cards: [new(original[0].Id, 1, 1)])];
        List<ComboGroup> groups = [new("g", "Routes"), new("exclusion", "Exclusion"), new("empty", "Empty")];
        ExactCalculationResult baseline = Verify(original, combos, groups);
        List<Card> custom = original.ToList();
        custom[0] = shape == "allure" ? custom[0].WithPostDrawDiscardCount(0) : custom[0].WithPreDrawRemovalCount(0);
        PinnedCalculationContext before = PinnedCalculationContext.Capture(0, 2, original, [], [], _ => Guid.Empty, _ => Guid.Empty);
        PinnedCalculationContext after = PinnedCalculationContext.Capture(0, 2, custom, [], [], _ => Guid.Empty, _ => Guid.Empty);
        Assert.That(before.Model, Is.EqualTo(after.Model));
        Assert.That(before.DeckDefinition, Is.Not.EqualTo(after.DeckDefinition));
        ExactCalculationResult modified = Verify(custom, combos, groups);
        Assert.That(custom[0].DrawCount, Is.EqualTo(2));
        Assert.That(custom[0].EffectProfile.Source, Is.EqualTo(EffectSource.Custom));
        if (shape == "mixed") {
            Assert.That(custom[^1].EffectPlan, Is.EqualTo(allure.EffectPlan));
        }
        if (shape == "trade" && material) {
            Assert.That(baseline.Combos[2].ToDouble(new(CalculationWorkPolicy.Default)),
                Is.GreaterThan(modified.Combos[2].ToDouble(new(CalculationWorkPolicy.Default))),
                "Removing a cost is not an upper bound for exclusion requirements.");
        }
        custom[0] = CardEffectRegistry.Default.RestoreBuiltIn(custom[0]);
        Assert.That(custom[0].EffectPlan, Is.EqualTo(original[0].EffectPlan));
        Assert.That(custom[0].EffectProfile, Is.EqualTo(original[0].EffectProfile));
        ExactCalculationResult restored = Verify(custom, combos, groups);
        Assert.That(restored.Total, Is.EqualTo(baseline.Total));
        Assert.That(restored.Combos, Is.EqualTo(baseline.Combos));
        Assert.That(restored.Groups, Is.EqualTo(baseline.Groups));
    }

    [Test]
    public void ZeroHiddenBanishAndIndependentSameNameEntriesRemainExact() {
        Card desires = BuiltIn(35261759, "desires").WithCopies(2);
        List<Card> deck = [desires, new([], id: "target"), new([], 11, id: "filler")];
        List<Combo> combos = [new([], groupId: "g", cards: [new("target", 1, 1)]),
            new([], cards: [new("desires", 1, 2)])];
        Verify(deck, combos, [new("g", "Routes")]);
        deck[0] = desires.WithPreDrawDeckRemovalCount(0);
        Verify(deck, combos, [new("g", "Routes")]);
        Assert.That(deck[0].DrawOncePerTurn, Is.True);
        Assert.That(CardEffectRegistry.Default.RestoreBuiltIn(deck[0]).EffectPlan, Is.EqualTo(desires.EffectPlan));

        Card trade = BuiltIn(38120068, "costed");
        Card free = BuiltIn(38120068, "custom").WithPreDrawRemovalCount(0).WithCopies(2);
        Verify([trade, free, new([Level, Monster], id: "material"), new([], id: "target"), new([], 2, id: "filler")],
            [new([], cards: [new("costed", 1, 1)]), new([], cards: [new("target", 1, 1)])], []);
        Assert.That(trade.PreDrawRemovalCount, Is.EqualTo(1));
    }

    private static ExactCalculationResult Verify(List<Card> deck, List<Combo> combos, List<ComboGroup> groups) {
        ExactCalculationResult result = null!;
        foreach (DrawActivationPriority priority in new[] { DrawActivationPriority.Default,
            new(DrawActivationMode.CustomPriority, deck.Where(card => card.DrawCount is not null).Reverse().Select(card => card.Id).ToArray()) }) {
            DrawSequenceOracle.Counts oracle = DrawSequenceOracle.Enumerate(deck, 2, combos, groups,
                collapseIdenticalCopies: true, priorityIds: priority.Mode == DrawActivationMode.CustomPriority ? priority.CardIds : null);
            WorkBudget budget = new(CalculationWorkPolicy.Interactive);
            result = new DrawEffectCalculation(deck, 2, budget, priority).Calculate(combos, groups);
            ExactProbability[] rows = [result.Total, .. result.Combos, .. result.Groups];
            for (int i = 0; i < rows.Length; i++) {
                Assert.That(rows[i].Numerator * oracle.Total, Is.EqualTo(oracle.Wins[i] * rows[i].Denominator), $"row {i}");
            }
            CalculationSnapshot snapshot = CalculationSnapshot.Capture(deck, combos, 2, groups,
                CalculationWorkPolicy.Interactive, options: new(priority));
            CalculationInput input = JsonSerializer.Deserialize(snapshot.Json, CalculationJsonContext.Default.CalculationInput)!;
            Assert.That(input.Cards.Select(card => (card.Id, card.PreDrawRemovalCount, card.PostDrawDiscardCount, card.PreDrawDeckRemovalCount)),
                Is.EqualTo(deck.Select(card => (card.Id, card.PreDrawRemovalCount, card.PostDrawDiscardCount, card.PreDrawDeckRemovalCount))));
            ProbabilityCalculationResult expected = result.ToPublic(combos, groups, budget);
            Assert.That(JsonSerializer.Serialize(CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json))), Is.EqualTo(JsonSerializer.Serialize(expected)));
            Assert.That(JsonSerializer.Serialize(new ProbabilityCalculatorService().CalculateProbabilityResults(deck, combos, 2, groups,
                CalculationWorkPolicy.Interactive, options: new(priority))), Is.EqualTo(JsonSerializer.Serialize(expected)));
        }

        return result;
    }
}
