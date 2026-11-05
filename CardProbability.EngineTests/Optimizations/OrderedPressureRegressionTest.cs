using CardProbability.EngineTests.Fixtures;
using CardProbability.Internal;
using CardProbability.Internal.Certification;
using CardProbability.Internal.Counting;
using CardProbability.Internal.DrawResolution;
using CardProbability.Internal.Model;
using CardProbability.Internal.Resources;
using Core = CardProbability.Internal.Contracts;

namespace CardProbability.EngineTests.Optimizations;

[TestFixture, NonParallelizable]
internal sealed class OrderedPressureRegressionTest {
    [TestCase(false), TestCase(true)]
    public void EagerAggregationRescuesStandardWithEveryHistoricalHighRow(bool customPriority) {
        EngineCase fixture = Pressure(10, customPriority);
        PreparedCalculation input = fixture.Prepare();
        DrawActivationPriority priority = DeferredTestCases.Priority(fixture);
        WorkBudget oldBudget = new(Core.CalculationWorkPolicy.Interactive);
        PostDrawCalculation old = new(input.Cards, 5, oldBudget, priority, eagerTerminalAggregation: false);
        Core.ProbabilityCalculationLimitException failure = Assert.Throws<Core.ProbabilityCalculationLimitException>(() =>
            old.CalculateSearch(input.Combos, input.Groups, false, new(input.Cards, oldBudget)))!;
        Assert.That(failure.Reason, Is.EqualTo(Core.ProbabilityCalculationLimitReason.Storage));
        WorkBudget referenceBudget = new(Core.CalculationWorkPolicy.Interactive, Core.CalculationStoragePolicy.High);
        PostDrawCalculation reference = new(input.Cards, 5, referenceBudget, priority, eagerTerminalAggregation: false);
        ExactCalculationResult expected = reference.CalculateSearch(input.Combos, input.Groups, false, new(input.Cards, referenceBudget));
        WorkBudget budget = new(Core.CalculationWorkPolicy.Interactive);
        PostDrawCalculation candidate = new(input.Cards, 5, budget, priority);
        ExactCalculationResult actual = candidate.CalculateSearch(input.Combos, input.Groups, false, new(input.Cards, budget));
        Assert.That(actual.ToRows(), Is.EqualTo(expected.ToRows()));
        Assert.That(candidate.PeakCells, Is.LessThan(240000));
        Assert.That(candidate.PeakConcurrentCells, Is.LessThan(reference.PeakConcurrentCells));
        Assert.That(budget.Spent, Is.LessThan(referenceBudget.Spent));
        Assert.That(candidate.TerminalStates, Is.EqualTo(reference.TerminalStates));
    }

    [Test]
    public void ElevenRolePackedTemplatesMatchHistoricalHighPrimaryAndEveryCertificateRow() {
        PreparedCalculation input = Pressure(11).Prepare();
        WorkBudget oldBudget = new(Core.CalculationWorkPolicy.Interactive);
        PostDrawCalculation old = new(input.Cards, 5, oldBudget, reuseSearchTemplates: false, compactSearchTerminals: false);
        Core.ProbabilityCalculationLimitException failure = Assert.Throws<Core.ProbabilityCalculationLimitException>(() =>
            old.CalculateSearch(input.Combos, input.Groups, false, new(input.Cards, oldBudget)))!;
        Assert.That(failure.Reason, Is.EqualTo(Core.ProbabilityCalculationLimitReason.Storage));
        WorkBudget referenceBudget = new(new(1000000000), Core.CalculationStoragePolicy.High);
        PostDrawCalculation reference = new(input.Cards, 5, referenceBudget, reuseSearchTemplates: false, compactSearchTerminals: false);
        ExactCalculationResult expected = reference.CalculateSearch(input.Combos, input.Groups, false, new(input.Cards, referenceBudget));
        WorkBudget budget = new(Core.CalculationWorkPolicy.Interactive);
        PostDrawCalculation candidate = new(input.Cards, 5, budget);
        ExactCalculationResult actual = candidate.CalculateSearch(input.Combos, input.Groups, false, new(input.Cards, budget));
        Assert.That(actual.ToRows(), Is.EqualTo(expected.ToRows()));
        foreach (bool decline in new[] { true, false }) {
            Assert.That(candidate.CalculateSearchLower(input.Combos, input.Groups, decline).ToRows(),
                Is.EqualTo(reference.CalculateSearchLower(input.Combos, input.Groups, decline).ToRows()));
        }
        Assert.That(budget.Spent, Is.LessThan(Core.CalculationWorkPolicy.Interactive.WorkUnits));
        Assert.That(candidate.PeakCells, Is.LessThan(reference.PeakCells));
        Assert.That(budget.PeakCells, Is.LessThanOrEqualTo(Core.CalculationStoragePolicy.Standard.CellLimit));
    }

    [Test]
    public void UnpackedSmallRequestsKeepEveryResourceCounter() {
        PreparedCalculation input = Pressure(4).Prepare();
        WorkBudget[] budgets = [new(Core.CalculationWorkPolicy.Interactive), new(Core.CalculationWorkPolicy.Interactive)];
        ExactCalculationResult[] rows = new ExactCalculationResult[2];
        for (int side = 0; side < 2; side++) {
            PostDrawCalculation calculation = new(input.Cards, 5, budgets[side], eagerTerminalAggregation: side == 1);
            rows[side] = calculation.CalculateSearch(input.Combos, input.Groups, false, new(input.Cards, budgets[side]));
            Assert.That(calculation.PackedLayers, Is.Zero);
        }
        Assert.That(rows[1].ToRows(), Is.EqualTo(rows[0].ToRows()));
        Assert.That(budgets[1].Spent, Is.EqualTo(budgets[0].Spent));
        Assert.That(budgets[1].PeakEntries, Is.EqualTo(budgets[0].PeakEntries));
        Assert.That(budgets[1].PeakCells, Is.EqualTo(budgets[0].PeakCells));
    }

    [TestCase(10), TestCase(11)]
    public void LimitedRefinementPreservesCompletedPrimaryAndEveryWitnessEndpoint(int roles) {
        PreparedCalculation input = Pressure(roles).Prepare();
        WorkBudget measurement = new(new(1000000000));
        DrawEffectCalculation measured = new(input.Cards, 5, measurement);
        ExactCalculationResult upper = measured.Calculate(input.Combos, input.Groups);
        measured.DescribeCapability();
        ExactCalculationResult witness = measured.SearchExecution!.CalculateLower(input.Combos, input.Groups, true);
        WorkBudget budget = new(new(measurement.Spent + 10000));
        DrawEffectCalculation execution = new(input.Cards, 5, budget);
        ExactCalculationResult actual = execution.Calculate(input.Combos, input.Groups);
        Core.SearchAvailabilityCertificate certificate = SearchAvailabilityCertification.TryCalculate(input.Combos, input.Groups, actual, budget, execution);
        Assert.That(actual.ToRows(), Is.EqualTo(upper.ToRows()));
        Assert.That(certificate.Status, Is.EqualTo(Core.SearchAvailabilityStatus.Bounded));
        Assert.That(certificate.Detail, Does.Contain("Limited refinement"));
        Core.SearchAvailabilityInterval[] intervals = [certificate.Total!, .. certificate.Combos, .. certificate.Groups];
        Assert.That(intervals.Select(interval => interval.Lower), Is.EqualTo(witness.ToRows().Select(row => row.ToFraction())));
        Assert.That(intervals.Select(interval => interval.Upper), Is.EqualTo(upper.ToRows().Select(row => row.ToFraction())));
    }

    private static EngineCase Pressure(int roles, bool priority = false) {
        List<DeckEntry> entries = Enumerable.Range(0, roles).Select(index => new DeckEntry("r" + index, 3)).ToList();
        entries.AddRange([new("d", 2, new(DrawCount: 2, ActivationLimit: 1, RemovalCount: 1)),
            new("e", 2, new(DrawCount: 1, ActivationLimit: null, RemovalCount: 1)),
            new("s", 1, new(Search: new(new("role0"))))]);
        entries.Add(new("f", 40 - entries.Sum(entry => entry.Copies)));
        Goal[] goals = Enumerable.Range(0, roles).Select(index => new Goal("goal" + index,
            [Requirement.InMembership("role" + index, 1, 5)], groupId: "g")).ToArray();
        Membership[] memberships = Enumerable.Range(0, roles).Select(index => new Membership("role" + index, ["r" + index])).ToArray();
        return new("pressure", new(entries, 5, goals, memberships, ["g"]),
            priority ? new(activationOrder: ActivationOrder.Priority, priorityEntryIds: ["e", "d"]) : new());
    }
}
