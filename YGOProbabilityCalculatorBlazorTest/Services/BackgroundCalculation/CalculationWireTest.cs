using System.Text.Json;
using System.Text.Json.Nodes;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.BackgroundCalculation;

[TestFixture]
public class CalculationWireTest {
    [Test]
    public void LargeFiniteProbabilitySurvivesWorkerSerialization() {
        CategoryBase a = new("A");
        CalculationSnapshot snapshot = CalculationSnapshot.Capture([new([a]), new([], 1099)],
            [new([new(a, 1, 1)], groupId: "g")], 550, [new("g", "Group")]);
        // A distinguished copy occurs in exactly h/n = 550/1100 of all hands.
        ProbabilityCalculationResult result = CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json));
        Assert.That(result.TotalProbability, Is.EqualTo(0.5));
        Assert.That(result.ComboProbabilities.Single().Probability, Is.EqualTo(0.5));
        Assert.That(result.GroupProbabilities!.Single().Probability, Is.EqualTo(0.5));
    }

    [Test]
    public void SnapshotFiltersActiveInputsAndOwnsNestedValuesAndGroupOrder() {
        CategoryBase role = new("Role");
        CategoryBase property = new("FIRE", CategorySource.Metadata, "attribute:fire");
        List<Card> cards = [new([role, property], 2, "Card", id: "stable", externalCardId: 123,
            manualMetadataCategoryKeys: [property.MetadataKey!]), new([], 2, "Blank", id: "blank"), new([], 10, active: false)];
        List<Combo> combos = [new([new(property, 0, 2, RequirementMaximumMode.HandSize)], "Combo", groupId: "g",
            cards: [new("stable", 1, 2)]), new([new(role, 0, 0)], active: false)];
        List<ComboGroup> groups = [new("other", "Other"), new("g", "Group")];
        double expected = SmallDeckOracle.EnumerateProbability(cards.Where(c => c.Active).ToList(), [combos[0]], 2);
        CalculationSnapshot snapshot = CalculationSnapshot.Capture(cards, combos, 2, groups);
        cards[0].Categories.Clear();
        combos[0].Categories.Clear();
        combos[0].Cards.Clear();
        combos[0].GroupId = "changed";
        groups.Reverse();
        CalculationInput input = JsonSerializer.Deserialize<CalculationInput>(snapshot.Json)!;
        ProbabilityCalculationResult result = CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json));
        Assert.Multiple(() => {
            Assert.That(input.Cards, Has.Length.EqualTo(2));
            Assert.That(input.Cards[0].Id, Is.EqualTo("stable"));
            Assert.That(input.Cards[0].ExternalCardId, Is.EqualTo(123));
            Assert.That(input.Cards[0].Categories[1].Identity, Is.EqualTo(property.Identity));
            Assert.That(input.Cards[0].ManualMetadataCategoryKeys, Is.EqualTo(new[] { property.MetadataKey }));
            Assert.That(input.Combos[0].Categories[0].MaximumMode, Is.EqualTo(RequirementMaximumMode.HandSize));
            Assert.That(input.Groups.Select(g => g.Id), Is.EqualTo(new[] { "other", "g" }));
            Assert.That(result.TotalProbability, Is.EqualTo(expected).Within(1e-12));
            Assert.That(result.ComboProbabilities.Single().GroupId, Is.EqualTo("g"));
            Assert.That(result.GroupProbabilities!.Single(g => g.GroupId == "g").Probability, Is.EqualTo(expected).Within(1e-12));
        });
    }

    [Test]
    public void DisabledDrawEffectsSnapshotUsesOrdinaryCardsWithoutChangingWorkspaceCards() {
        CategoryBase starter = new("Starter");
        CategoryBase fire = new("FIRE", CategorySource.Metadata, "attribute:fire");
        List<Card> configuredCards = [
            new([starter, fire], 2, "Configured draw", id: "draw", externalCardId: 55144522,
                manualMetadataCategoryKeys: [fire.MetadataKey!], drawCount: 2, drawOncePerTurn: false),
            new([starter], 2, "Ordinary", id: "ordinary"),
            new([], 1, "Inactive draw", active: false, id: "inactive", drawCount: 3, drawOncePerTurn: false)
        ];
        List<Combo> combos = [
            new([new(starter, 1, 2), new(fire, 0, 0)], "Role and exclusion", cards: [new("draw", 1, 2)]),
            new([], "Alternative", alternativeGroups: [new([
                ComboAlternative.For(new ComboCategory(fire, 1, 2)),
                ComboAlternative.For(new ComboCard("ordinary", 1, 2))
            ])])
        ];

        CalculationSnapshot enabledSnapshot = CalculationSnapshot.Capture(configuredCards, combos, 2, []);
        CalculationSnapshot disabledSnapshot = CalculationSnapshot.Capture(
            configuredCards,
            combos,
            2,
            [],
            drawEffectsEnabled: false
        );
        CalculationInput enabledInput = JsonSerializer.Deserialize<CalculationInput>(enabledSnapshot.Json)!;
        CalculationInput disabledInput = JsonSerializer.Deserialize<CalculationInput>(disabledSnapshot.Json)!;

        Assert.That(enabledInput.Cards[0].DrawCount, Is.EqualTo(2));
        Assert.That(enabledInput.Cards[0].DrawOncePerTurn, Is.False);
        Assert.That(disabledInput.Cards, Has.Length.EqualTo(2));
        Assert.That(disabledInput.Cards[0].DrawCount, Is.Null);
        Assert.That(disabledInput.Cards[0].DrawOncePerTurn, Is.True);
        Assert.That((disabledInput.Cards[0].Id, disabledInput.Cards[0].Copies, disabledInput.Cards[0].Name,
                disabledInput.Cards[0].ExternalCardId, disabledInput.Cards[0].ManualMetadataCategoryKeys),
            Is.EqualTo(("draw", 2, "Configured draw", (int?)55144522, new[] { fire.MetadataKey! })));
        Assert.That(disabledInput.Cards[0].Categories.Select(category => category.Identity),
            Is.EqualTo(new[] { starter.Identity, fire.Identity }));
        Assert.That((configuredCards[0].DrawCount, configuredCards[0].DrawOncePerTurn), Is.EqualTo(((int?)2, false)));

        List<Card> ordinaryCards = configuredCards
            .Where(card => card.Active)
            .Select(card => card.WithDrawCount(null))
            .ToList();
        ProbabilityCalculationResult disabledResult = CalculationWire.ReadResult(CalculationWire.Execute(disabledSnapshot.Json));
        ProbabilityCalculationResult ordinaryResult = CalculationWire.ReadResult(CalculationWire.Execute(
            CalculationSnapshot.Capture(ordinaryCards, combos, 2, []).Json));
        Assert.That(disabledResult.TotalProbability, Is.EqualTo(ordinaryResult.TotalProbability));
        Assert.That(disabledResult.ComboProbabilities, Is.EqualTo(ordinaryResult.ComboProbabilities));
        Assert.That(disabledResult.TotalProbability, Is.EqualTo(
            SmallDeckOracle.EnumerateProbability(ordinaryCards, combos, 2)));
    }

    [Test]
    public void OverlappingDistinctRequirementsAndZeroMaximumMatchIndependentPhysicalOracle() {
        CategoryBase a = new("A");
        CategoryBase b = new("B");
        List<Card> cards = [new([a, b], 2, id: "ab"), new([a], id: "a"), new([], id: "blank")];
        List<Combo> combos = [new([new(a, 1, 2), new(b, 1, 2)], "Both"),
            new([new(b, 0, 0)], "Direct", cards: [new("a", 1, 2, RequirementMaximumMode.HandSize)])];
        ProbabilityCalculationResult result = CalculationWire.ReadResult(CalculationWire.Execute(CalculationSnapshot.Capture(cards, combos, 2, []).Json));
        Assert.That(result.TotalProbability, Is.EqualTo(SmallDeckOracle.EnumerateProbability(cards, combos, 2)).Within(1e-12));
        for (int i = 0; i < combos.Count; i++) {
            Assert.That(result.ComboProbabilities[i].Probability,
                Is.EqualTo(SmallDeckOracle.EnumerateProbability(cards, [combos[i]], 2)).Within(1e-12));
        }
    }

    [Test]
    public void ActualEngineLimitRetainsItsTypeAndMeaningAcrossTheWire() {
        CategoryBase[] categories = Enumerable.Range(0, 18).Select(i => new CategoryBase($"C{i}")).ToArray();
        IEnumerable<Card> cards = categories.Select(c => new Card([c])).Append(new Card([], 22));
        IEnumerable<Combo> combos = categories.Select(c => new Combo([new(c, 0, 0)]));
        string response = CalculationWire.Execute(CalculationSnapshot.Capture(cards, combos, 5, []).Json);
        Assert.Throws<ProbabilityCalculationLimitException>(() => CalculationWire.ReadResult(response));
    }

    [Test]
    public void OrdinaryAndMalformedResponsesNeverBecomeZeroResults() {
        string response = CalculationWire.Execute("not JSON");
        Assert.Throws<ArgumentException>(() => CalculationWire.ReadResult(response));
        Assert.Throws<CalculationProtocolException>(() => CalculationWire.ReadResult("{}"));
        Assert.Throws<JsonException>(() => CalculationWire.ReadResult("not JSON"));
    }

    [TestCase("null", CalculationProtocolFailureReason.MissingResponse)]
    [TestCase("{}", CalculationProtocolFailureReason.MissingResult)]
    [TestCase("{\"Error\":\"conflict\",\"LimitReason\":1,\"FailureKind\":1}", CalculationProtocolFailureReason.InvalidResponse)]
    public void ProtocolFailuresCarryAReasonWithoutImpersonatingWorkerFailures(string response, CalculationProtocolFailureReason reason) {
        CalculationProtocolException exception = Assert.Throws<CalculationProtocolException>(() => CalculationWire.ReadResult(response))!;
        Assert.That(exception.Reason, Is.EqualTo(reason));
        Assert.That(exception.RowIndex, Is.Null);
    }

    [Test]
    public void InvalidRationalReportsItsFlattenedRowWithoutChangingOtherRows() {
        string snapshot = CalculationSnapshot.Capture([new([], 2, id: "card")],
            [new([], cards: [new("card", 1, 1)])], 1, []).Json;
        JsonNode response = JsonNode.Parse(CalculationWire.Execute(snapshot))!;
        response["Result"]!["Contract"]!["Fractions"]!["Combos"]![0]!["Denominator"] = "0";
        CalculationProtocolException exception = Assert.Throws<CalculationProtocolException>(() => CalculationWire.ReadResult(response.ToJsonString()))!;
        Assert.That(exception.Reason, Is.EqualTo(CalculationProtocolFailureReason.InvalidRationalValue));
        Assert.That(exception.RowIndex, Is.EqualTo(1));
    }
}
