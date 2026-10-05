using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.BackgroundCalculation;

[TestFixture]
public class CalculationWireTest
{
    [Test]
    public void LargeFiniteProbabilitySurvivesWorkerSerialization()
    {
        CategoryBase a = new("A");
        CalculationSnapshot snapshot = CalculationSnapshot.Capture([new([a]), new([], 1099)],
            [new([new(a, 1, 1)], groupId: "g")],
            550,
            [new("g", "Group")]);
        // A distinguished copy occurs in exactly h/n = 550/1100 of all hands.
        ProbabilityCalculationResult result = CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json));
        Assert.That(result.TotalProbability, Is.EqualTo(0.5));
        Assert.That(result.ComboProbabilities.Single().Probability, Is.EqualTo(0.5));
        Assert.That(result.GroupProbabilities!.Single().Probability, Is.EqualTo(0.5));
    }

    [Test]
    public void SnapshotFiltersActiveInputsAndOwnsNestedValuesAndGroupOrder()
    {
        CategoryBase role = new("Role");
        CategoryBase property = new("FIRE", CategorySource.Metadata, "attribute:fire");
        List<Card> cards =
        [
            new([role, property],
                2,
                "Card",
                id: "stable",
                externalCardId: 123,
                manualMetadataCategoryKeys: [property.MetadataKey!]),
            new([], 2, "Blank", id: "blank"), new([], 10, active: false)
        ];
        List<Combo> combos =
        [
            new([new(property, 0, 2, RequirementMaximumMode.HandSize)],
                "Combo",
                groupId: "g",
                cards: [new("stable", 1, 2)]),
            new([new(role, 0, 0)], active: false)
        ];
        List<ComboGroup> groups = [new("other", "Other"), new("g", "Group")];
        double expected = SmallDeckOracle.EnumerateProbability([.. cards.Where(c => c.Active)], [combos[0]], 2);
        CalculationSnapshot snapshot = CalculationSnapshot.Capture(cards, combos, 2, groups);
        cards[0].Categories.Clear();
        combos[0].Categories.Clear();
        combos[0].Cards.Clear();
        combos[0].GroupId = "changed";
        groups.Reverse();
        CalculationInput input = JsonSerializer.Deserialize<CalculationInput>(snapshot.Json)!;
        ProbabilityCalculationResult result = CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json));
        Assert.Multiple(() =>
        {
            Assert.That(input.Cards, Has.Length.EqualTo(2));
            Assert.That(input.Cards[0].Id, Is.EqualTo("stable"));
            Assert.That(input.Cards[0].ExternalCardId, Is.EqualTo(123));
            Assert.That(input.Cards[0].Categories[1].Identity, Is.EqualTo(property.Identity));
            Assert.That(input.Cards[0].ManualMetadataCategoryKeys, Is.EqualTo(new[] { property.MetadataKey }));
            Assert.That(input.Combos[0].Categories[0].MaximumMode, Is.EqualTo(RequirementMaximumMode.HandSize));
            Assert.That(input.Groups.Select(g => g.Id), Is.EqualTo(new[] { "other", "g" }));
            Assert.That(result.TotalProbability, Is.EqualTo(expected).Within(1e-12));
            Assert.That(result.ComboProbabilities.Single().GroupId, Is.EqualTo("g"));
            Assert.That(result.GroupProbabilities!.Single(g => g.GroupId == "g").Probability,
                Is.EqualTo(expected).Within(1e-12));
        });
    }

    [Test]
    public void OverlappingDistinctRequirementsAndZeroMaximumMatchIndependentPhysicalOracle()
    {
        CategoryBase a = new("A");
        CategoryBase b = new("B");
        List<Card> cards = [new([a, b], 2, id: "ab"), new([a], id: "a"), new([], id: "blank")];
        List<Combo> combos =
        [
            new([new(a, 1, 2), new(b, 1, 2)], "Both"),
            new([new(b, 0, 0)], "Direct", cards: [new("a", 1, 2, RequirementMaximumMode.HandSize)])
        ];
        ProbabilityCalculationResult result =
            CalculationWire.ReadResult(CalculationWire.Execute(CalculationSnapshot.Capture(cards, combos, 2, []).Json));
        Assert.That(result.TotalProbability,
            Is.EqualTo(SmallDeckOracle.EnumerateProbability(cards, combos, 2)).Within(1e-12));

        for (int i = 0; i < combos.Count; i++)
        {
            Assert.That(result.ComboProbabilities[i].Probability,
                Is.EqualTo(SmallDeckOracle.EnumerateProbability(cards, [combos[i]], 2)).Within(1e-12));
        }
    }

    [Test]
    public void ActualEngineLimitRetainsItsTypeAndMeaningAcrossTheWire()
    {
        CategoryBase[] categories = [.. Enumerable.Range(0, 18).Select(i => new CategoryBase($"C{i}"))];
        IEnumerable<Card> cards = categories.Select(c => new Card([c])).Append(new Card([], 22));
        IEnumerable<Combo> combos = categories.Select(c => new Combo([new(c, 0, 0)]));
        string response = CalculationWire.Execute(CalculationSnapshot.Capture(cards, combos, 5, []).Json);
        Assert.Throws<ProbabilityCalculationLimitException>(() => CalculationWire.ReadResult(response));
    }

    [Test]
    public void OrdinaryAndMalformedResponsesNeverBecomeZeroResults()
    {
        string response = CalculationWire.Execute("not JSON");
        Assert.Throws<InvalidOperationException>(() => CalculationWire.ReadResult(response));
        Assert.Throws<InvalidOperationException>(() => CalculationWire.ReadResult("{}"));
        Assert.Throws<JsonException>(() => CalculationWire.ReadResult("not JSON"));
    }
}
