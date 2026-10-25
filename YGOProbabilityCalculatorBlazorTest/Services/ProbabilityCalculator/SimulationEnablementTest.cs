using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public sealed class SimulationEnablementTest {
    [TestCase(false)]
    [TestCase(true)]
    public void DisabledEntryIsAnOrdinaryPhysicalCardAcrossNativeAndWorkerStages(bool disableSearch) {
        CategoryBase role = new("Role");
        Card draw = new([role], name: "Same name", id: "draw", drawCount: 1);
        Card search = new([], name: "Same name", id: "search", search: new(CardSelector.For(role), false));
        List<Card> enabled = [draw, search, new([role], 2, id: "target"), new([], id: "blank")];
        int index = disableSearch ? 1 : 0;
        List<Card> disabled = enabled.ToList();
        disabled[index] = enabled[index].WithSimulationEnabled(false);
        List<Card> ordinary = enabled.ToList();
        Card source = enabled[index];
        ordinary[index] = new(source.Categories, source.Copies, source.Name, source.Active, source.Id);
        List<Combo> combos = [new([new(role, 1, 3)], groupId: "g"),
            new([], groupId: "g", cards: [new(source.Id, 1, 3)]),
            new([new(role, 0, 0)], cards: [new("blank", 1, 3)])];
        List<ComboGroup> groups = [new("g", "Routes")];
        ProbabilityCalculatorService engine = new();
        SearchCertificateOracle.Counts oracle = SearchCertificateOracle.Enumerate(ordinary, 2, combos, groups);
        ProbabilityCalculationResult exact = Run(disabled, CalculationModel.Exact);
        PhysicalSearchTest.AssertRows(exact, oracle.Wins[1], oracle.Outcomes);
        AssertFractions(exact, Run(ordinary, CalculationModel.Exact));
        AssertFractions(exact, Worker(disabled, CalculationModel.Exact));
        AssertFractions(Run(ordinary, CalculationModel.Fast), Run(disabled, CalculationModel.Fast));
        AssertFractions(Run(disabled, CalculationModel.Fast), Worker(disabled, CalculationModel.Fast));
        Assert.That(exact.Contract!.Fractions!.Combos.SequenceEqual(Run(enabled, CalculationModel.Exact).Contract!.Fractions!.Combos), Is.False,
            "the selected effect changes a consequential row");
        Assert.That(exact.Contract.Fractions.Combos.SequenceEqual(Run(disabled, CalculationModel.OpeningHand).Contract!.Fractions!.Combos), Is.False,
            "the other entry's effect still executes");

        string snapshot = CalculationSnapshot.Capture(disabled, combos, 2, groups).Json;
        AutomaticFastStage progressive = JsonSerializer.Deserialize<AutomaticFastStage>(CalculationWire.ExecuteProgressiveUpgrade(snapshot))!;
        AssertFractions(exact, CalculationWire.ReadResult(progressive.Response));
        Assert.That(engine.CalculateProbabilityForCombos(disabled, combos, 2),
            Is.EqualTo(engine.CalculateProbabilityForCombos(ordinary, combos, 2)));

        disabled[index] = disabled[index].WithSimulationEnabled(true);
        AssertFractions(Run(enabled, CalculationModel.Exact), Run(disabled, CalculationModel.Exact));
        SearchCertificateOracle.Counts enabledOracle = SearchCertificateOracle.Enumerate(enabled, 2, combos, groups);
        PhysicalSearchTest.AssertRows(Run(disabled, CalculationModel.Exact), enabledOracle.Wins[1], enabledOracle.Outcomes);
        AssertFractions(Run(enabled, CalculationModel.OpeningHand), Run(ordinary, CalculationModel.OpeningHand));
        Assert.That(enabled[index].EffectPlan, Is.EqualTo(source.EffectPlan));

        ProbabilityCalculationResult Run(List<Card> cards, CalculationModel mode) => engine.CalculateProbabilityResults(
            cards, combos, 2, groups, CalculationWorkPolicy.Default, new(Mode: mode));
        ProbabilityCalculationResult Worker(List<Card> cards, CalculationModel mode) => CalculationWire.ReadResult(
            CalculationWire.Execute(CalculationSnapshot.Capture(cards, combos, 2, groups, options: new(Mode: mode)).Json));
    }

    [Test]
    public void ProjectionRemovesEveryOperationWithoutMutatingTheSavedSnapshot() {
        Card source = new([], id: "source", drawCount: 2, preDrawRemovalCount: 1,
            preDrawDeckRemovalCount: 1, postDrawDeckMillCount: 1, postDrawDiscardCount: 1,
            firstActivationOnly: true, blocksLaterDraws: true, activationLimit: 2, minimumHandSize: 2,
            effectProfile: new(EffectSource.Custom, 123));
        Card disabled = source.WithSimulationEnabled(false);
        WorkerCard projected = JsonSerializer.Deserialize<CalculationInput>(CalculationSnapshot.Capture([disabled], [], 2, []).Json)!.Cards.Single();
        Assert.Multiple(() => {
            Assert.That(projected.DrawCount, Is.Null);
            Assert.That(projected.Search, Is.Null);
            Assert.That(projected.PreDrawRemovalCount + projected.PreDrawDeckRemovalCount + projected.PostDrawDeckMillCount + projected.PostDrawDiscardCount, Is.Zero);
            Assert.That(projected.FirstActivationOnly || projected.BlocksLaterDraws, Is.False);
            Assert.That(projected.ActivationLimit, Is.Null);
            Assert.That(projected.MinimumHandSize, Is.Zero);
            Assert.That(disabled.EffectPlan, Is.EqualTo(source.EffectPlan));
            Assert.That(disabled.EffectProfile, Is.SameAs(source.EffectProfile));
            Assert.That(projected.Id, Is.EqualTo(source.Id));
            Assert.That(projected.Copies, Is.EqualTo(source.Copies));
        });
    }

    private static void AssertFractions(ProbabilityCalculationResult expected, ProbabilityCalculationResult actual) {
        Assert.That(actual.Contract!.Fractions!.Total, Is.EqualTo(expected.Contract!.Fractions!.Total));
        Assert.That(actual.Contract.Fractions.Combos, Is.EqualTo(expected.Contract.Fractions.Combos));
        Assert.That(actual.Contract.Fractions.Groups, Is.EqualTo(expected.Contract.Fractions.Groups));
    }
}
