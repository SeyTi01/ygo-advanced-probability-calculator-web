using System.Text.Json;
using System.Numerics;
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
            new([level, dark, role, new("Monster", CategorySource.Metadata, "kind:monster")], id: "both"), new([dark], id: "dark"),
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
                ApplicationResultFractions result = ApplicationResultFractions.From(new ProbabilityCalculatorService().CalculateProbabilityResults(
                    deck, combos, hand, groups, policy, options: new(priority)));
                ApplicationFraction[] rows = [result.Total, .. result.Combos, .. result.Groups];
                for (int i = 0; i < rows.Length; i++) {
                    Assert.That(rows[i].Numerator * expected.Total, Is.EqualTo(expected.Wins[i] * rows[i].Denominator));
                }
            }
        }
    }

    // Frozen on unchanged post-#195 dev with an explicit test-only allowance.
    // Compare the entire exact/public output, not just the displayed total.
    [TestCase(false, CalculationStorageTier.Standard, 5)]
    [TestCase(true, CalculationStorageTier.Standard, 5)]
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
            ProbabilityCalculationResult result = new ProbabilityCalculatorService().CalculateProbabilityResults(
                session.Cards, session.Combos, hand, session.ComboGroups, policy, CalculationStoragePolicy.ForTier(tier), new(priority));
            ApplicationResultFractions exact = ApplicationResultFractions.From(result);
            ApplicationFraction[] actualRows = [exact.Total, .. exact.Combos, .. exact.Groups];
            string[] frozenRows = expected.GetProperty("ExactResults").EnumerateArray().Select(value => value.GetString()!).ToArray();
            Assert.That(actualRows, Has.Length.EqualTo(frozenRows.Length));
            for (int row = 0; row < actualRows.Length; row++) {
                BigInteger[] fraction = frozenRows[row].Split('/').Select(BigInteger.Parse).ToArray();
                Assert.That(actualRows[row].Numerator * fraction[1], Is.EqualTo(fraction[0] * actualRows[row].Denominator), $"row {row}");
            }
            Assert.That(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(result with { Contract = null }), expected.GetProperty("ReferenceResults")), Is.True);
        }
        CalculationSnapshot snapshot = CalculationSnapshot.Capture(session.Cards, session.Combos, hand, session.ComboGroups,
            CalculationWorkPolicy.Interactive, storagePolicy: CalculationStoragePolicy.ForTier(tier), options: new(priority));
        Assert.That(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json)) with { Contract = null }),
            expected.GetProperty("ReferenceResults")), Is.True);
    }

    [TestCase(CalculationStorageTier.Standard, 50_000_000, ProbabilityCalculationLimitReason.Work)]
    [TestCase(CalculationStorageTier.High, 50_000_000, ProbabilityCalculationLimitReason.Work)]
    [TestCase(CalculationStorageTier.VeryHigh, 50_000_000, ProbabilityCalculationLimitReason.Work)]
    public async Task HistoricalAndIndependentResourceLimitsRemainTyped(CalculationStorageTier tier, long units, ProbabilityCalculationLimitReason reason) {
        SessionState session = await TradeInAllureFixture.Load();
        CalculationSnapshot snapshot = CalculationSnapshot.Capture(session.Cards, session.Combos, 5, session.ComboGroups,
            new(units), storagePolicy: CalculationStoragePolicy.ForTier(tier), options: new());
        ProbabilityCalculationLimitException error = Assert.Throws<ProbabilityCalculationLimitException>(() =>
            CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json)))!;
        Assert.That(error.Reason, Is.EqualTo(reason));
    }

}
