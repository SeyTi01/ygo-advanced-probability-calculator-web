using CardProbability.EngineTests.Fixtures;
using CardProbability.Internal;
using CardProbability.Internal.Contracts;
using CardProbability.Internal.Counting;
using CardProbability.Internal.DrawResolution;
using CardProbability.Internal.Resources;

namespace CardProbability.EngineTests;

[TestFixture]
public sealed class OrderedPackingTest {
    [Test]
    public void DifferentQueueOrdersProduceDifferentHandsAndPackedRowsMatchLabeledOrders() {
        DeckEntry first = new("first", 1, new(DrawCount: 1, RemovalCount: 1));
        DeckEntry second = new("second", 1, new(DrawCount: 2, RemovalCount: 1));
        DeckEntry material = new("material", 1);
        DeckEntry reward = new("reward", 1);
        EngineCase fixture = new("queue", new([first, second, material, reward], 2,
            [new("reward", [Requirement.InMembership("role")], groupId: "g"), new("no-second", [Requirement.OfEntry("second", 0, 0)])],
            [new("role", ["reward"])], ["g"]), new());
        OracleModel oracleModel = new(fixture.Request);
        List<DeckEntry>? one = DrawSequenceOracle.Resolve(fixture, oracleModel, [first, second, material, reward], out _);
        List<DeckEntry>? two = DrawSequenceOracle.Resolve(fixture, oracleModel, [second, first, material, reward], out _);
        Assert.That(one!.Select(entry => entry.Id), Is.Not.EqualTo(two!.Select(entry => entry.Id)));
        DrawSequenceOracle.Counts truth = DrawSequenceOracle.Enumerate(fixture);
        PreparedCalculation model = fixture.Prepare();
        PostDrawCalculation packed = new(model.Cards, 2, new(CalculationWorkPolicy.Interactive), compactFrontierThreshold: 0);
        ExactCalculationResult actual = packed.Calculate(model.Combos, model.Groups);
        ExactProbability[] rows = [actual.Total, .. actual.Combos, .. actual.Groups];
        for (int index = 0; index < rows.Length; index++) {
            Assert.That(rows[index].Numerator * truth.Total, Is.EqualTo(truth.Wins[index] * rows[index].Denominator));
        }
        Assert.That(packed.PackedLayers, Is.GreaterThan(0));
    }
}
