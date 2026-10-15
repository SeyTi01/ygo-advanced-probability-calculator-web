using System.Text.Json.Nodes;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public class PostDrawCalculationSettingsTest {
    [Test]
    public void MissingAndDefaultNativeAndWorkerOptionsHonorSavedCosts() {
        CategoryBase role = new("Role");
        List<Card> deck = [new([], id: "effect", drawCount: 1, drawOncePerTurn: false, postDrawDiscardCount: 1), new([role], 5, id: "r")];
        List<Combo> combos = [new([new(role, 5, 5)], "Five", groupId: "g")];
        List<ComboGroup> groups = [new("g", "Group")];
        DrawSequenceOracle.Counts oracle = DrawSequenceOracle.Enumerate(deck, 5, combos, groups);
        WorkBudget budget = new(CalculationWorkPolicy.Interactive);
        ExactCalculationResult result = new DrawEffectCalculation(deck, 5, budget).Calculate(combos, groups);
        ExactProbability[] rows = [result.Total, .. result.Combos, .. result.Groups];
        for (int i = 0; i < rows.Length; i++) {
            Assert.That(rows[i].Numerator * oracle.Total, Is.EqualTo(oracle.Wins[i] * rows[i].Denominator));
        }
        ProbabilityCalculatorService service = new();
        ProbabilityCalculationResult expected = result.ToPublic(combos, groups, budget);
        SearchAvailabilityCertificateTest.AssertPrimary(expected, service.CalculateProbabilityResults(deck, combos, 5, groups));
        Assert.That(service.CalculateProbabilityForCombos(deck, combos, 5), Is.EqualTo(expected.TotalProbability));
        Assert.That(service.CalculateProbabilityForCombos(deck, combos, 5, CalculationWorkPolicy.Default, options: new()), Is.EqualTo(expected.TotalProbability));
        CalculationSnapshot snapshot = CalculationSnapshot.Capture(deck, combos, 5, groups);
        foreach (JsonNode? options in new JsonNode?[] { null, new JsonObject() }) {
            JsonObject input = JsonNode.Parse(snapshot.Json)!.AsObject();
            input.Remove(nameof(CalculationInput.Options));
            if (options is not null) {
                input[nameof(CalculationInput.Options)] = options;
            }
            SearchAvailabilityCertificateTest.AssertPrimary(expected, CalculationWire.ReadResult(CalculationWire.Execute(input.ToJsonString())));
        }
        List<Card> custom = deck.Select(card => card.WithPostDrawDiscardCount(0)).ToList();
        Assert.That(service.CalculateProbabilityForCombos(custom, combos, 5), Is.Not.EqualTo(expected.TotalProbability));
        CalculationSnapshot opening = CalculationSnapshot.Capture(deck, combos, 5, groups, drawEffectsEnabled: false);
        Assert.That(CalculationWire.ReadResult(CalculationWire.Execute(opening.Json)).TotalProbability,
            Is.EqualTo((double)SmallDeckOracle.EnumerateProbability(deck.Select(card => card.WithDrawCount(null)).ToList(), combos, 5)));
    }

    [TestCase(false), TestCase(true)]
    public void ObsoleteWorkerOptionsAreRejectedAsInput(bool enabled) {
        CalculationSnapshot snapshot = CalculationSnapshot.Capture([new([], 6)], [new([])], 5, []);
        JsonObject input = JsonNode.Parse(snapshot.Json)!.AsObject();
        input[nameof(CalculationInput.Options)] = new JsonObject { ["ConditionalEffectsEnabled"] = enabled };
        ArgumentException error = Assert.Throws<ArgumentException>(() => CalculationWire.ReadResult(CalculationWire.Execute(input.ToJsonString())))!;
        Assert.That(error.Message, Does.Contain("ConditionalEffectsEnabled"));
    }

    [Test]
    public void AdvancedWithoutDiscardsHasIdenticalFixedDrawWork() {
        List<Card> deck = [new([], 3, id: "effect", drawCount: 2), new([], 37, id: "r")];
        List<Combo> combos = [new([], cards: [new("effect", 1, 5)])];
        WorkBudget basic = new(CalculationWorkPolicy.Interactive);
        WorkBudget advanced = new(CalculationWorkPolicy.Interactive);
        ExactCalculationResult expected = new DrawEffectCalculation(deck, 5, basic).Calculate(combos, []);
        ExactCalculationResult actual = new DrawEffectCalculation(deck, 5, advanced).Calculate(combos, []);
        Assert.That(actual.Total, Is.EqualTo(expected.Total));
        Assert.That(actual.Combos, Is.EqualTo(expected.Combos));
        Assert.That(advanced.Spent, Is.EqualTo(basic.Spent));
    }

    [Test]
    public void MixedPostDrawMapsMatchPhysicalOracleAcrossAllStorageTiers() {
        CategoryBase a = new("A");
        CategoryBase b = new("B");
        List<Card> deck = [new([a, b], 2, id: "a"), new([b], id: "b"), new([], id: "f"),
            new([], id: "dealings", drawCount: 1, drawOncePerTurn: false, postDrawDiscardCount: 1),
            new([], id: "graceful", drawCount: 3, postDrawDiscardCount: 2)];
        List<Combo> combos = [new([new(a, 1, 2), new(b, 1, 2)], groupId: "g"),
            new([], cards: [new("b", 1, 1)], groupId: "g")];
        List<ComboGroup> groups = [new("g", "Group")];
        DrawSequenceOracle.Counts oracle = DrawSequenceOracle.Enumerate(deck, 2, combos, groups);
        long? work = null;
        foreach (CalculationStoragePolicy policy in new[] {
            CalculationStoragePolicy.Standard, CalculationStoragePolicy.High, CalculationStoragePolicy.VeryHigh
        }) {
            WorkBudget budget = new(CalculationWorkPolicy.Interactive, policy);
            PostDrawCalculation calculation = new(deck, 2, budget);
            ExactCalculationResult result = calculation.Calculate(combos, groups);
            ExactProbability[] rows = [result.Total, .. result.Combos, .. result.Groups];
            for (int i = 0; i < rows.Length; i++) {
                Assert.That(rows[i].Numerator * oracle.Total, Is.EqualTo(oracle.Wins[i] * rows[i].Denominator));
            }
            Assert.That(calculation.TerminalStates, Is.GreaterThan(0));
            if (work is not null) {
                Assert.That(budget.Spent, Is.EqualTo(work));
            }
            work = budget.Spent;
        }
    }

    [Test]
    public void PostDrawStorageIsRequestLocalAndRaisedTiersDoNotRaiseWork() {
        // A large population makes the exact binomial table the storage boundary;
        // the tiny schedule stays well below Interactive work in every tier.
        CategoryBase role = new("Role");
        List<Card> deck = [new([role], 358, id: "r"), new([], id: "f"),
            new([], id: "effect", drawCount: 3, drawOncePerTurn: false, postDrawDiscardCount: 2)];
        List<Combo> combos = [new([new(role, 1, 0, RequirementMaximumMode.HandSize)])];
        ProbabilityCalculationLimitException failure = Assert.Throws<ProbabilityCalculationLimitException>(() =>
            new PostDrawCalculation(deck, 5, new(CalculationWorkPolicy.Interactive)).Calculate(combos, []))!;
        Assert.That(failure.Reason, Is.EqualTo(ProbabilityCalculationLimitReason.Storage));
        ExactCalculationResult? previous = null;
        long? work = null;
        foreach (CalculationStoragePolicy policy in new[] { CalculationStoragePolicy.High, CalculationStoragePolicy.VeryHigh }) {
            WorkBudget budget = new(CalculationWorkPolicy.Interactive, policy);
            PostDrawCalculation calculation = new(deck, 5, budget);
            ExactCalculationResult result = calculation.Calculate(combos, []);
            Assert.That(result.Total.Numerator, Is.EqualTo(result.Total.Denominator));
            Assert.That(calculation.PeakCells, Is.GreaterThan(CalculationStoragePolicy.Standard.CellLimit));
            Assert.That(calculation.PeakCells, Is.LessThanOrEqualTo(policy.CellLimit));
            if (previous is not null) {
                Assert.That(result.Total, Is.EqualTo(previous.Total));
                Assert.That(result.Combos, Is.EqualTo(previous.Combos));
                Assert.That(budget.Spent, Is.EqualTo(work));
            }
            previous = result;
            work = budget.Spent;
            CalculationSnapshot snapshot = CalculationSnapshot.Capture(deck, combos, 5, [], CalculationWorkPolicy.Interactive,
                storagePolicy: policy, options: new());
            Assert.That(CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json)).TotalProbability, Is.EqualTo(1));
            CalculationSnapshot limited = CalculationSnapshot.Capture(deck, combos, 5, [], new(1), storagePolicy: policy, options: new());
            Assert.That(Assert.Throws<ProbabilityCalculationLimitException>(() => CalculationWire.ReadResult(CalculationWire.Execute(limited.Json)))!.Reason,
                Is.EqualTo(ProbabilityCalculationLimitReason.Work));
        }
        CalculationSnapshot standard = CalculationSnapshot.Capture(deck, combos, 5, [], CalculationWorkPolicy.Interactive, options: new());
        Assert.That(Assert.Throws<ProbabilityCalculationLimitException>(() => CalculationWire.ReadResult(CalculationWire.Execute(standard.Json)))!.Reason,
            Is.EqualTo(ProbabilityCalculationLimitReason.Storage));
    }
}
