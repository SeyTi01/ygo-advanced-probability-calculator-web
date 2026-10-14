using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public class DrawFrontierPackingTest {
    [Test]
    public void EveryByteAndPartialWordRoundTripsWithoutAliases() {
        for (int length = 0; length <= 259; length++) {
            int[] values = Enumerable.Range(0, length).Select(index => index % 256).ToArray();
            int[] packed = DrawFrontierPacking.Pack(values);
            Assert.That(DrawFrontierPacking.Unpack(packed), Is.EqualTo(values));
            Assert.That(packed.Length, Is.EqualTo(1 + (length + 3) / 4));
        }
        Assert.That(DrawFrontierPacking.Pack([1, 2, 0]), Is.Not.EqualTo(DrawFrontierPacking.Pack([1, 2])));
        Assert.That(DrawFrontierPacking.Pack([1, 2, 0, 1]), Is.Not.EqualTo(DrawFrontierPacking.Pack([1, 2, 1, 0])));
        Assert.Throws<OverflowException>(() => DrawFrontierPacking.Pack([256]));
        Assert.Throws<OverflowException>(() => DrawFrontierPacking.Pack([-1]));
    }

    [TestCase(255, true)]
    [TestCase(256, false)]
    public void WideCountsUseOriginalRepresentation(int copies, bool packs) {
        CategoryBase material = new("Material");
        List<Card> deck = [new([], id: "source", drawCount: 1, preDrawRemovalCount: 1,
            preDrawSelector: CardSelector.For(material)), new([material], copies - 1, id: "material")];
        List<Combo> combos = [new([new(material, 1, 2)])];
        PostDrawCalculation reference = new(deck, 2, new(CalculationWorkPolicy.Interactive), compactFrontierThreshold: int.MaxValue);
        PostDrawCalculation candidate = new(deck, 2, new(CalculationWorkPolicy.Interactive), compactFrontierThreshold: 0);
        Assert.That(candidate.Calculate(combos, []).Total, Is.EqualTo(reference.Calculate(combos, []).Total));
        Assert.That(candidate.PackedLayers > 0, Is.EqualTo(packs));
    }

    [Test]
    public void WideSourceIndicesUseOriginalRepresentationEvenWithFewPhysicalCopies() {
        CategoryBase material = new("Material");
        List<Card> deck = [new([], id: "source", drawCount: 1, preDrawRemovalCount: 1,
            preDrawSelector: CardSelector.For(material)), new([material], 3, id: "material")];
        deck.AddRange(Enumerable.Range(0, 254).Select(index => new Card([], 0, id: $"unused-{index}", drawCount: 1)));
        List<Combo> combos = [new([new(material, 1, 2)])];
        PostDrawCalculation reference = new(deck, 2, new(CalculationWorkPolicy.Interactive), compactFrontierThreshold: int.MaxValue);
        PostDrawCalculation candidate = new(deck, 2, new(CalculationWorkPolicy.Interactive), compactFrontierThreshold: 0);
        Assert.That(candidate.Calculate(combos, []).Total, Is.EqualTo(reference.Calculate(combos, []).Total));
        Assert.That(candidate.ReducedRows, Is.EqualTo(256));
        Assert.That(candidate.PackedLayers, Is.Zero);
    }

    [Test]
    public void DifferentQueueOrdersStillProduceDifferentPhysicalOutcomes() {
        CategoryBase role = new("Role");
        Card first = new([], id: "first", drawCount: 1, postDrawDiscardCount: 1);
        Card second = new([], id: "second", drawCount: 2, postDrawDiscardCount: 1);
        Card material = new([], id: "material");
        Card reward = new([role], id: "reward");
        List<Card> deck = [first, second, material, reward];
        List<Combo> combos = [new([new(role, 1, 1)], groupId: "g"), new([], cards: [new("second", 0, 0)])];
        List<ComboGroup> groups = [new("g", "Group")];
        Assert.That(DrawSequenceOracle.Resolve(deck, 2, combos)!.Select(card => card.Id),
            Is.Not.EqualTo(DrawSequenceOracle.Resolve([second, first, material, reward], 2, combos)!.Select(card => card.Id)));
        DrawSequenceOracle.Counts expected = DrawSequenceOracle.Enumerate(deck, 2, combos, groups);
        PostDrawCalculation packed = new(deck, 2, new(CalculationWorkPolicy.Interactive), compactFrontierThreshold: 0);
        ExactCalculationResult actual = packed.Calculate(combos, groups);
        ExactProbability[] rows = [actual.Total, .. actual.Combos, .. actual.Groups];
        for (int i = 0; i < rows.Length; i++) {
            Assert.That(rows[i].Numerator * expected.Total, Is.EqualTo(expected.Wins[i] * rows[i].Denominator));
        }
        Assert.That(packed.PackedLayers, Is.GreaterThan(0));
    }
}
