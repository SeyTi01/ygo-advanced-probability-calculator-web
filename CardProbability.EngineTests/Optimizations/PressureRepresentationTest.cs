using CardProbability.EngineTests.Fixtures;
using CardProbability.Internal;
using CardProbability.Internal.Certification;
using CardProbability.Internal.DrawResolution;
using CardProbability.Internal.Model;
using CardProbability.Internal.Resources;
using Core = CardProbability.Internal.Contracts;

namespace CardProbability.EngineTests.Optimizations;

[TestFixture, NonParallelizable]
public sealed class PressureRepresentationTest {
    [TestCase(false), TestCase(true)]
    public void WideReferenceStillHitsStandardStorageAndPackingReducesRetainedPayload(bool custom) {
        EngineCase fixture = NeutralPressureFixtures.Load("mixed-cost-pressure");
        PreparedCalculation input = fixture.Prepare();
        DrawActivationPriority priority = custom
            ? new(DrawActivationMode.CustomPriority, fixture.Request.Entries.Where(entry => entry.Effect?.DrawCount is not null)
                .Select(entry => entry.Id).Order(StringComparer.Ordinal).ToArray())
            : DrawActivationPriority.Default;
        Assert.That(fixture.Request.Entries.Sum(entry => entry.Copies), Is.EqualTo(40));
        Assert.That(fixture.Request.Entries.Count(entry => entry.Effect?.DrawCount is not null), Is.EqualTo(2));
        PostDrawCalculation limited = new(input.Cards, 5, new(Core.CalculationWorkPolicy.Interactive), priority, compactFrontierThreshold: int.MaxValue);
        Core.ProbabilityCalculationLimitException failure = Assert.Throws<Core.ProbabilityCalculationLimitException>(() => limited.Calculate(input.Combos, input.Groups))!;
        Assert.That(failure.Reason, Is.EqualTo(Core.ProbabilityCalculationLimitReason.Storage));
        PostDrawCalculation reference = new(input.Cards, 5, new(Core.CalculationWorkPolicy.Interactive, Core.CalculationStoragePolicy.High), priority,
            compactFrontierThreshold: int.MaxValue);
        ExactCalculationResult expected = reference.Calculate(input.Combos, input.Groups);
        // Eager terminal aggregation has separate state-count checks. This
        // comparison isolates the retained byte-packed frontier representation.
        PostDrawCalculation packed = new(input.Cards, 5, new(Core.CalculationWorkPolicy.Interactive), priority, eagerTerminalAggregation: false);
        ExactCalculationResult actual = packed.Calculate(input.Combos, input.Groups);
        Assert.Multiple(() => {
            Assert.That(actual.ToRows(), Is.EqualTo(expected.ToRows()));
            Assert.That(packed.PackedLayers, Is.GreaterThan(0));
            Assert.That(packed.TerminalStates, Is.EqualTo(reference.TerminalStates));
            Assert.That(packed.PeakStates, Is.EqualTo(reference.PeakStates));
            Assert.That(packed.PeakCells, Is.LessThan(reference.PeakCells / 2));
            Assert.That(packed.PeakConcurrentCells, Is.LessThan(reference.PeakConcurrentCells / 2));
        });
    }

    [TestCase("hidden-deck-cost"), TestCase("hidden-deck-mill")]
    public void FortyCardHiddenLossRetainsPhysicalCountStateWithinDefaultStandardLimits(string name) {
        EngineCase fixture = NeutralPressureFixtures.Load(name);
        Assert.That(fixture.Request.Entries.Sum(entry => entry.Copies), Is.EqualTo(40));
        Assert.That(fixture.Request.Entries.Count(entry => entry.Effect?.Search is not null), Is.EqualTo(3));
        WorkBudget budget = new(Core.CalculationWorkPolicy.Default);
        PreparedCalculation input = fixture.Prepare(budget);
        DrawEffectCalculation calculation = new(input.Cards, fixture.Request.HandSize, budget, physicalSearch: true);
        ExactCalculationResult result = calculation.Calculate(input.Combos, input.Groups);
        Assert.That(calculation.DescribeCapability().Route, Is.EqualTo(SolverRoute.SearchPhysicalCountState));
        Assert.That(result.Combos, Has.Length.EqualTo(fixture.Request.Goals.Length));
        Assert.That(result.Groups, Has.Length.EqualTo(fixture.Request.Groups.Length));
        Assert.That(budget.Spent, Is.LessThanOrEqualTo(Core.CalculationWorkPolicy.Default.WorkUnits));
        Assert.That(budget.PeakEntries, Is.LessThanOrEqualTo(Core.CalculationStoragePolicy.Standard.EntryLimit));
        Assert.That(budget.PeakCells, Is.LessThanOrEqualTo(Core.CalculationStoragePolicy.Standard.CellLimit));
    }
}
