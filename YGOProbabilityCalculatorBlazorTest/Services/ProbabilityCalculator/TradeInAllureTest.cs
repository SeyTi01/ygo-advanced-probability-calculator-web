using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public class TradeInAllureTest {
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void MaintainedMixedEffectsMatchPhysicalOrderOracleAcrossBudgets(int hand) {
        CategoryBase level = new("Level 8", CategorySource.Metadata, "level:8");
        CategoryBase dark = new("Attribute: DARK", CategorySource.Metadata, "attribute:dark");
        CategoryBase role = new("Role");
        MaintainedCardEffect trade = CardEffectRegistry.Default.Find(38120068)!;
        MaintainedCardEffect allure = CardEffectRegistry.Default.Find(1475311)!;
        List<Card> deck = [
            new Card([], id: "trade").WithEffect(trade.Plan, trade.Profile),
            new Card([], id: "allure").WithEffect(allure.Plan, allure.Profile),
            new([level, dark, role], id: "both"), new([dark], id: "dark"),
            new([role], id: "target"), new([role], id: "other"), new([], id: "blank")
        ];
        List<Combo> combos = [new([new(role, 2, 0, RequirementMaximumMode.HandSize)], groupId: "g"),
            new([], cards: [new("target", 1, 1)], groupId: "h")];
        List<ComboGroup> groups = [new("g", "Role"), new("h", "Target")];
        foreach (DrawActivationPriority priority in new[] { DrawActivationPriority.Default,
            new(DrawActivationMode.CustomPriority, ["allure", "trade"]) }) {
            DrawSequenceOracle.Counts expected = DrawSequenceOracle.Enumerate(deck, hand, combos, groups,
                collapseIdenticalCopies: true, priorityIds: priority.CardIds);
            foreach (CalculationWorkPolicy policy in new[] { CalculationWorkPolicy.Default, new(50_000_000), CalculationWorkPolicy.Interactive }) {
                ExactCalculationResult result = new DrawEffectCalculation(deck, hand, new(policy), true, priority).Calculate(combos, groups);
                ExactProbability[] rows = [result.Total, .. result.Combos, .. result.Groups];
                for (int i = 0; i < rows.Length; i++) {
                    Assert.That(rows[i].Numerator * expected.Total, Is.EqualTo(expected.Wins[i] * rows[i].Denominator));
                }
            }
        }
    }

    // Frozen on unchanged post-#195 dev with an explicit test-only allowance.
    // Compare the entire exact/public output, not just the displayed total.
    [TestCase(false, CalculationStorageTier.High, 5)]
    [TestCase(false, CalculationStorageTier.VeryHigh, 5)]
    [TestCase(true, CalculationStorageTier.High, 5)]
    [TestCase(true, CalculationStorageTier.VeryHigh, 6)]
    public async Task NormalInteractivePolicyCompletesFrozenExample(bool custom, CalculationStorageTier tier, int hand) {
        SessionState session = await TradeInAllureFixture.Load();
        DrawActivationPriority priority = custom ? new(DrawActivationMode.CustomPriority, ["benchmark-allure", "benchmark-trade-in"])
            : DrawActivationPriority.Default;
        JsonElement[] frozen = JsonSerializer.Deserialize<JsonElement[]>(File.ReadAllText(Path.Combine(
            TestContext.CurrentContext.TestDirectory, "Fixtures", "trade_in_allure_results.json")))!;
        JsonElement expected = frozen.Single(row => row.GetProperty("Custom").GetBoolean() == custom
            && row.GetProperty("HandSize").GetInt32() == hand);
        foreach (CalculationWorkPolicy policy in new[] { CalculationWorkPolicy.Interactive, new(250_000_000) }) {
            WorkBudget budget = new(policy, CalculationStoragePolicy.ForTier(tier));
            PostDrawCalculation calculation = new(session.Cards, hand, budget, priority);
            ExactCalculationResult exact = calculation.Calculate(session.Combos, session.ComboGroups);
            Assert.That(new[] { exact.Total }.Concat(exact.Combos).Concat(exact.Groups)
                .Select(value => $"{value.Numerator}/{value.Denominator}"),
                Is.EqualTo(expected.GetProperty("ExactResults").EnumerateArray().Select(value => value.GetString())));
            ProbabilityCalculationResult result = exact.ToPublic(session.Combos, session.ComboGroups, budget);
            Assert.That(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(result), expected.GetProperty("ReferenceResults")), Is.True);
            Assert.That(budget.Spent, Is.LessThan(policy.WorkUnits));
        }
        CalculationSnapshot snapshot = CalculationSnapshot.Capture(session.Cards, session.Combos, hand, session.ComboGroups,
            CalculationWorkPolicy.Interactive, storagePolicy: CalculationStoragePolicy.ForTier(tier), options: new(true, priority));
        Assert.That(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json))),
            expected.GetProperty("ReferenceResults")), Is.True);
    }

    [TestCase(CalculationStorageTier.Standard, 150_000_000, ProbabilityCalculationLimitReason.Storage)]
    [TestCase(CalculationStorageTier.High, 50_000_000, ProbabilityCalculationLimitReason.Work)]
    [TestCase(CalculationStorageTier.VeryHigh, 50_000_000, ProbabilityCalculationLimitReason.Work)]
    public async Task HistoricalAndIndependentResourceLimitsRemainTyped(CalculationStorageTier tier, long units, ProbabilityCalculationLimitReason reason) {
        SessionState session = await TradeInAllureFixture.Load();
        CalculationSnapshot snapshot = CalculationSnapshot.Capture(session.Cards, session.Combos, 5, session.ComboGroups,
            new(units), storagePolicy: CalculationStoragePolicy.ForTier(tier), options: new(true));
        ProbabilityCalculationLimitException error = Assert.Throws<ProbabilityCalculationLimitException>(() =>
            CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json)))!;
        Assert.That(error.Reason, Is.EqualTo(reason));
    }
}
