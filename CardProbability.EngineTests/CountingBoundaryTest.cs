using CardProbability.EngineTests.Fixtures;
using CardProbability.Internal;
using CardProbability.Internal.Compilation;
using CardProbability.Internal.Contracts;
using CardProbability.Internal.Counting;
using CardProbability.Internal.DrawResolution;
using CardProbability.Internal.Resources;

namespace CardProbability.EngineTests;

[TestFixture]
public sealed class CountingBoundaryTest {
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

    [TestCase(255, true), TestCase(256, false)]
    public void WideCountsUseOriginalRepresentation(int copies, bool packs) {
        EngineCase fixture = PackingCase(copies, false);
        PreparedCalculation prepared = fixture.Prepare();
        PostDrawCalculation reference = new(prepared.Cards, 2, Budget(), compactFrontierThreshold: int.MaxValue);
        PostDrawCalculation candidate = new(prepared.Cards, 2, Budget(), compactFrontierThreshold: 0);
        Assert.That(candidate.Calculate(prepared.Combos, prepared.Groups).Total, Is.EqualTo(reference.Calculate(prepared.Combos, prepared.Groups).Total));
        Assert.That(candidate.PackedLayers > 0, Is.EqualTo(packs));
    }

    [Test]
    public void WideSourceIndicesUseOriginalRepresentation() {
        EngineCase fixture = PackingCase(4, true);
        PreparedCalculation prepared = fixture.Prepare();
        PostDrawCalculation reference = new(prepared.Cards, 2, Budget(), compactFrontierThreshold: int.MaxValue);
        PostDrawCalculation candidate = new(prepared.Cards, 2, Budget(), compactFrontierThreshold: 0);
        Assert.That(candidate.Calculate(prepared.Combos, prepared.Groups).Total, Is.EqualTo(reference.Calculate(prepared.Combos, prepared.Groups).Total));
        Assert.That(candidate.ReducedRows, Is.EqualTo(256));
        Assert.That(candidate.PackedLayers, Is.Zero);
    }

    private static EngineCase PackingCase(int copies, bool wide) {
        List<DeckEntry> entries = [new("source", 1, new(DrawCount: 1, HandCost: 1, HandCostEligibility: new("material"))), new("material", copies - 1)];
        if (wide) {
            entries.AddRange(Enumerable.Range(0, 254).Select(index => new DeckEntry($"unused-{index}", 0, new(DrawCount: 1))));
        }
        return new("packing", new(entries, 2, [new("goal", [Requirement.InMembership("material", 1, 2)])],
            [new("material", ["material"]) ]), new());
    }

    [TestCase(1, 0, 0), TestCase(2, 1, 0), TestCase(2, 0, 1), TestCase(2, 1, 1), TestCase(3, 0, 0)]
    public void PhysicalCapacityIncludesWholeBatchAndHiddenPositions(int draw, int cost, int mill) {
        EngineCase fixture = new("capacity", new([new("source", 2, new(DrawCount: draw, HiddenDeckCost: cost, HiddenPostDrawMill: mill)),
            new("target", 1), new("filler", 3)], 0, []), new());
        int required = draw + cost + mill;
        foreach (int remaining in new[] { required - 1, required, required + 1 }.Distinct()) {
            PreparedCalculation model = fixture.Prepare();
            Assert.That(DrawEffectFeasibility.FindInert(model.Cards, 6, 6 - remaining)?.Contains(model.Cards[0]) ?? false,
                Is.EqualTo(remaining < required));
        }
    }

    [TestCase(1, 1, 0), TestCase(1, 1, 1), TestCase(2, 1, 0), TestCase(1, 2, 1), TestCase(2, 2, 0), TestCase(2, 2, 1), TestCase(3, 2, 0)]
    public void CostBoundReservesOneSourceCopy(int copies, int cost, int extra) {
        EngineCase fixture = new("cost", new([new("source", copies, new(DrawCount: 1, HandCost: cost, HandCostEligibility: new("material"))),
            new("target", extra), new("filler", 3)], 1, [], [new("material", ["source", "target"])]), new());
        PreparedCalculation model = fixture.Prepare();
        Assert.That(DrawEffectFeasibility.FindInert(model.Cards, copies + extra + 3, 1)?.Contains(model.Cards[0]) ?? false,
            Is.EqualTo(copies + extra - 1 < cost));
    }

    [Test]
    public void FutureDrawCanSupplyCostAndHandOperationsPreventUnsafeInertRemoval() {
        EngineCase fixture = new("retry", new([
            new("cost", 1, new(DrawCount: 1, HandCost: 1, HandCostEligibility: new("material"))),
            new("draw", 1, new(DrawCount: 1)), new("material", 1), new("filler", 2)
        ], 2, [new("goal", [Requirement.InMembership("material")])], [new("material", ["material"])]), new());
        PreparedCalculation model = fixture.Prepare();
        Assert.That(DrawEffectFeasibility.FindInert(model.Cards, 5, 2), Is.Null);
        DrawSequenceOracle.Counts truth = DrawSequenceOracle.Enumerate(fixture);
        ExactCalculationResult actual = new DrawEffectCalculation(model.Cards, 2, Budget()).Calculate(model.Combos, model.Groups);
        Assert.That(actual.Total.Numerator * truth.Total, Is.EqualTo(truth.Wins[0] * actual.Total.Denominator));
    }

    [Test]
    public void MissingCostSourceRemainsVisibleWhenOtherHandOperationCanChangePolicy() {
        EngineCase fixture = new("deferred-policy", new([
            new("inert", 1, new(DrawCount: 1, HandCost: 1, HandCostEligibility: new("missing"))),
            new("live", 1, new(DrawCount: 2, RemovalCount: 1)), new("target", 1), new("filler", 3)
        ], 2, [new("goal", [Requirement.OfEntry("target")])], [new("missing", [])]), new());
        PreparedCalculation model = fixture.Prepare();
        Assert.That(DrawEffectFeasibility.FindInert(model.Cards, 6, 2), Is.Null);
        DrawSequenceOracle.Counts truth = DrawSequenceOracle.Enumerate(fixture);
        ExactCalculationResult actual = new DrawEffectCalculation(model.Cards, 2, Budget()).Calculate(model.Combos, model.Groups);
        Assert.That(actual.Total.Numerator * truth.Total, Is.EqualTo(truth.Wins[0] * actual.Total.Denominator));
    }
    [TestCase(16), TestCase(64)]
    public void RepeatedAlternativeGroupsReduceBeforeProductLimit(int count) {
        Alternative group = new([Requirement.InMembership("a"), Requirement.InMembership("b")]);
        EngineCase fixture = new("alternatives", new([new("ab", 2), new("a", 1), new("b", 1), new("blank", 2)], 2,
            [new("goal", [], Enumerable.Repeat(group, count))], [new("a", ["ab", "a"]), new("b", ["ab", "b"])]), new());
        PreparedCalculation model = fixture.Prepare();
        Assert.That(ComboAlternativeReducer.Reduce(model.Combos[0].AlternativeGroups, Budget()), Has.Count.EqualTo(1));
        Assert.That(new ComboEventCompiler(model.Cards, 2, Budget()).CompileCombo(model.Combos[0]), Has.Count.EqualTo(2));
        // Repeating the same choice group is equivalent to one group; enumerate
        // physical labels against that independent, bounded oracle input.
        EngineCase oracleFixture = fixture with { Request = new(fixture.Request.Entries, 2,
            [new("goal", [], [group])], fixture.Request.Memberships) };
        DrawSequenceOracle.Counts truth = DrawSequenceOracle.Enumerate(oracleFixture);
        ExactProbability actual = new ProbabilityCalculation(model.Cards, 2, Budget()).CalculateUnionExact(model.Combos);
        Assert.That(actual.Numerator * truth.Total, Is.EqualTo(truth.Wins[0] * actual.Denominator));
    }

    private static WorkBudget Budget() => new(CalculationWorkPolicy.Interactive);
}
