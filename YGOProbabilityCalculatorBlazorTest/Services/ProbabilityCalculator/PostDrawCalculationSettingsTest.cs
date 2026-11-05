using System.Numerics;
using System.Text.Json.Nodes;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.CalculationEngineAdapter;
using CP = CardProbability;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public class PostDrawCalculationSettingsTest {
    private static CP.CalculationResult RunEngine(List<Card> deck, int hand, List<Combo> combos, IReadOnlyList<ComboGroup> groups, CalculationStoragePolicy policy) {
        CP.CalculationRequest request = new CalculationRequestAdapter(deck, true).Create(combos, hand, groups);
        return CP.CalculationEngine.Calculate(request, new CP.CalculationOptions(CP.CalculationMode.Exact,
            new CP.ExecutionPolicy(CalculationWorkPolicy.Interactive.WorkUnits, policy.EntryLimit, policy.CellLimit)));
    }

    [Test]
    public void MissingAndDefaultNativeAndWorkerOptionsHonorSavedCosts() {
        CategoryBase role = new("Role");
        List<Card> deck = [new([], id: "effect", drawCount: 1, drawOncePerTurn: false, postDrawDiscardCount: 1), new([role], 5, id: "r")];
        List<Combo> combos = [new([new(role, 5, 5)], "Five", groupId: "g")];
        List<ComboGroup> groups = [new("g", "Group")];
        DrawSequenceOracle.Counts oracle = DrawSequenceOracle.Enumerate(deck, 5, combos, groups);
        ProbabilityCalculatorService service = new();
        ProbabilityCalculationResult expected = service.CalculateProbabilityResults(deck, combos, 5, groups);
        ApplicationResultFractions result = ApplicationResultFractions.From(expected);
        ApplicationFraction[] rows = [result.Total, .. result.Combos, .. result.Groups];
        for (int i = 0; i < rows.Length; i++) {
            Assert.That(rows[i].Numerator * oracle.Total, Is.EqualTo(oracle.Wins[i] * rows[i].Denominator));
        }
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
            CP.CalculationResult result = RunEngine(deck, 2, combos, groups, policy);
            Assert.That(result.Status, Is.EqualTo(CP.CalculationStatus.Completed));
            CP.RationalProbability[] rows = [result.Total!.Primary, .. result.Goals.Select(goal => goal.Value.Primary), .. result.Groups.Select(group => group.Value.Primary)];
            for (int i = 0; i < rows.Length; i++) {
                Assert.That(BigInteger.Parse(rows[i].Numerator) * oracle.Total,
                    Is.EqualTo(oracle.Wins[i] * BigInteger.Parse(rows[i].Denominator)));
            }
            if (work is not null) {
                Assert.That(result.Metrics.WorkSpent, Is.EqualTo(work));
            }
            work = result.Metrics.WorkSpent;
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
        Assert.That(RunEngine(deck, 5, combos, [], CalculationStoragePolicy.Standard).Status, Is.EqualTo(CP.CalculationStatus.StorageLimit));
        CP.CalculationResult? previous = null;
        long? work = null;
        foreach (CalculationStoragePolicy policy in new[] { CalculationStoragePolicy.High, CalculationStoragePolicy.VeryHigh }) {
            CP.CalculationResult result = RunEngine(deck, 5, combos, [], policy);
            Assert.That(result.Status, Is.EqualTo(CP.CalculationStatus.Completed));
            Assert.That(result.Total!.Primary.Numerator, Is.EqualTo(result.Total.Primary.Denominator));
            Assert.That(result.Metrics.PeakCells, Is.GreaterThan(CalculationStoragePolicy.Standard.CellLimit));
            Assert.That(result.Metrics.PeakCells, Is.LessThanOrEqualTo(policy.CellLimit));
            if (previous is not null) {
                Assert.That(result.Total, Is.EqualTo(previous.Total));
                Assert.That(result.Goals, Is.EqualTo(previous.Goals));
                Assert.That(result.Metrics.WorkSpent, Is.EqualTo(work));
            }
            previous = result;
            work = result.Metrics.WorkSpent;
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
