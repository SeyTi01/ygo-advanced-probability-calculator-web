using CardProbability.EngineTests.Fixtures;
using CardProbability.Internal;
using CardProbability.Internal.Compilation;
using CardProbability.Internal.Contracts;
using CardProbability.Internal.Model;
using CardProbability.Internal.Resources;

namespace CardProbability.EngineTests;

[TestFixture]
internal sealed class AlternativeReductionBoundaryTest {
    [Test]
    public void PermutationsAndDuplicatesReduceWithoutMutatingInput() {
        Alternative one = new([Requirement.InMembership("a"), Requirement.InMembership("b"), Requirement.InMembership("a")]);
        Alternative two = new([Requirement.InMembership("b"), Requirement.InMembership("a")]);
        PreparedCalculation model = Prepare([one, two]);
        IReadOnlyList<ComboAlternativeGroup> reduced = ComboAlternativeReducer.Reduce(model.Combos[0].AlternativeGroups, Budget());
        Assert.That(reduced, Has.Count.EqualTo(1));
        Assert.That(reduced[0].Alternatives, Has.Count.EqualTo(2));
        Assert.That(model.Combos[0].AlternativeGroups[0].Alternatives, Has.Count.EqualTo(3));
    }

    [Test]
    public void DistinctBoundsAndTargetsKeepGroupsSeparate() {
        Requirement first = Requirement.InMembership("a", 1, 2);
        Requirement[] different = [Requirement.InMembership("a", 2, 2), Requirement.InMembership("a", 1, 3),
            Requirement.InMembership("a"), Requirement.InMembership("b", 1, 2), Requirement.OfEntry("a", 1, 2),
            Requirement.OfEntry("b", 1, 2)];
        foreach (Requirement other in different) {
            PreparedCalculation model = Prepare([new([first, Requirement.InMembership("b")]), new([other, Requirement.InMembership("b")])]);
            Assert.That(ComboAlternativeReducer.Reduce(model.Combos[0].AlternativeGroups, Budget()), Has.Count.EqualTo(2));
        }
    }

    [Test]
    public void NonEquivalentProductAndPreprocessingPreserveTypedLimits() {
        List<DeckEntry> entries = [new("a", 1), new("b", 1)];
        entries.AddRange(Enumerable.Range(0, 16).Select(index => new DeckEntry($"id{index}", 0)));
        Alternative[] groups = Enumerable.Range(0, 16).Select(index => new Alternative([Requirement.InMembership("a"), Requirement.OfEntry($"id{index}")])).ToArray();
        EngineCase fixture = new("limits", new(entries, 2, [new("goal", [], groups)], [new("a", ["a"])]), new());
        PreparedCalculation model = fixture.Prepare();
        ProbabilityCalculationLimitException? product = Assert.Throws<ProbabilityCalculationLimitException>(() =>
            new ComboEventCompiler(model.Cards, 2, Budget()).CompileCombo(model.Combos[0]));
        Assert.That(product!.Reason, Is.EqualTo(ProbabilityCalculationLimitReason.Storage));
        string longId = new('x', 270000);
        EngineCase longFixture = new("long-key", new([new(longId, 1)], 1,
            [new("goal", [], [new([Requirement.OfEntry(longId)]), new([Requirement.OfEntry(longId)])])]), new());
        PreparedCalculation longModel = longFixture.Prepare();
        ProbabilityCalculationLimitException? storage = Assert.Throws<ProbabilityCalculationLimitException>(() =>
            ComboAlternativeReducer.Reduce(longModel.Combos[0].AlternativeGroups, Budget()));
        Assert.That(storage!.Reason, Is.EqualTo(ProbabilityCalculationLimitReason.Storage));
        Alternative[] repeated = Enumerable.Repeat(new Alternative([Requirement.InMembership("a"), Requirement.OfEntry("b")]), 16).ToArray();
        PreparedCalculation shortModel = Prepare(repeated);
        foreach (CalculationStorageTier tier in Enum.GetValues<CalculationStorageTier>()) {
            ProbabilityCalculationLimitException? work = Assert.Throws<ProbabilityCalculationLimitException>(() =>
                ComboAlternativeReducer.Reduce(shortModel.Combos[0].AlternativeGroups, new(new(10), CalculationStoragePolicy.ForTier(tier))));
            Assert.That(work!.Reason, Is.EqualTo(ProbabilityCalculationLimitReason.Work));
        }
    }

    private static PreparedCalculation Prepare(IEnumerable<Alternative> groups) => new EngineCase("reduce",
        new([new("a", 1), new("b", 1)], 2, [new("goal", [], groups)], [new("a", ["a"]), new("b", ["b"])]), new()).Prepare();
    private static WorkBudget Budget() => new(CalculationWorkPolicy.Interactive);
}
