using System.Numerics;
using CardProbability.EngineTests.Fixtures;
using CardProbability.Internal;
using CardProbability.Internal.DrawResolution;
using CardProbability.Internal.Resources;
using Core = CardProbability.Internal.Contracts;
using static CardProbability.EngineTests.Optimizations.DeferredTestCases;

namespace CardProbability.EngineTests.Optimizations;

[TestFixture]
public sealed class DrawResourceBoundaryTest {
    private static EngineCase Population(int roleCopies = 10, int fillerCopies = 27) => Create(
        "draw population", [new("role", roleCopies), new("filler", fillerCopies), Draw("source", 3, 3, removal: 2)], 5,
        [Goal("role", Member("role", 1, 5))], [new("role", ["role"])]);

    [Test]
    public void CustomZeroRemovalHasIdenticalFixedDrawWorkAndResults() {
        EngineCase original = Population();
        EngineCase changed = Change(original, entries: original.Request.Entries.Select(entry => entry.Effect is null ? entry
            : entry with { Effect = entry.Effect with { RemovalCount = 0 } }));
        EngineCase fixedDraw = Create("fixed draw", [new("role", 10), new("filler", 27), Draw("source", 3, 3)], 5,
            original.Request.Goals, original.Request.Memberships);
        WorkBudget changedBudget = new(Core.CalculationWorkPolicy.Interactive);
        WorkBudget fixedBudget = new(Core.CalculationWorkPolicy.Interactive);
        PreparedCalculation changedInput = changed.Prepare(changedBudget);
        PreparedCalculation fixedInput = fixedDraw.Prepare(fixedBudget);
        ExactCalculationResult actual = new DrawEffectCalculation(changedInput.Cards, 5, changedBudget).Calculate(changedInput.Combos, []);
        ExactCalculationResult expected = new DrawEffectCalculation(fixedInput.Cards, 5, fixedBudget).Calculate(fixedInput.Combos, []);
        Assert.That(actual.ToRows(), Is.EqualTo(expected.ToRows()));
        Assert.That(changedBudget.Spent, Is.EqualTo(fixedBudget.Spent));
        Assert.That(original.Request.Entries[^1].Effect!.RemovalCount, Is.EqualTo(2));
    }

    [Test]
    public void PostDrawWorkFailureAbortsTheWholeRequest() {
        EngineCase fixture = Population(9, 28);
        WorkBudget measured = new(Core.CalculationWorkPolicy.Interactive);
        ExactCalculationResult expected = Calculate(fixture, measured, out _);
        Assert.That(measured.Spent, Is.GreaterThan(1));
        WorkBudget exact = new(new(measured.Spent));
        Assert.That(Calculate(fixture, exact, out _).ToRows(), Is.EqualTo(expected.ToRows()));
        ExactCalculationResult? completed = null;
        WorkBudget limited = new(new(measured.Spent - 1));
        Core.ProbabilityCalculationLimitException failure = Assert.Throws<Core.ProbabilityCalculationLimitException>(() => {
            completed = Calculate(fixture, limited, out _);
        })!;
        Assert.That(failure.Reason, Is.EqualTo(Core.ProbabilityCalculationLimitReason.Work));
        Assert.That(completed, Is.Null, "A failed calculation must not publish a partial row set.");
        Assert.That(limited.Spent, Is.LessThanOrEqualTo(measured.Spent - 1));
    }

    [TestCase(500_000_000L), TestCase(null)]
    public void PostDrawStorageFailureIsIndependentOfTheWorkAllowance(long? work) {
        EngineCase fixture = LargePopulation();
        Core.CalculationWorkPolicy policy = work.HasValue ? new(work.Value) : Core.CalculationWorkPolicy.Unlimited;
        WorkBudget budget = new(policy, Core.CalculationStoragePolicy.Standard);
        Core.ProbabilityCalculationLimitException failure = Assert.Throws<Core.ProbabilityCalculationLimitException>(() => Calculate(fixture, budget, out _))!;
        Assert.That(failure.Reason, Is.EqualTo(Core.ProbabilityCalculationLimitReason.Storage));
    }

    [Test]
    public void PostDrawEquivalentOrdinaryEntriesMergeWithoutChangingFractions() {
        EngineCase merged = Create("merged ordinary", [new("a", 2), new("f", 2), Draw("g", 1, 3, limit: 1, removal: 2)], 2,
            [Goal("role", Member("role", 1, 2))], [new("role", ["a"])]);
        EngineCase split = Change(merged, entries: [new("a", 1), new("other-a", 1), new("f", 1), new("other-f", 1), merged.Request.Entries[^1]],
            memberships: [new("role", ["a", "other-a"])]);
        DrawSequenceOracle.Counts oracle = DrawSequenceOracle.Enumerate(split);
        ExactCalculationResult combined = Calculate(merged, new(Core.CalculationWorkPolicy.Interactive), out PostDrawCalculation mergedExecution);
        ExactCalculationResult divided = Calculate(split, new(Core.CalculationWorkPolicy.Interactive), out PostDrawCalculation splitExecution);
        AssertRows(combined, oracle);
        AssertRows(divided, oracle);
        Assert.That(splitExecution.ReducedRows, Is.EqualTo(mergedExecution.ReducedRows));
        Assert.That(splitExecution.ReducedRows, Is.LessThan(split.Request.Entries.Length));
    }

    [Test]
    public void StressCompletesUnderInteractiveBudget() {
        EngineCase fixture = Population(9, 28);
        WorkBudget budget = new(Core.CalculationWorkPolicy.Interactive);
        ExactCalculationResult result = Calculate(fixture, budget, out PostDrawCalculation execution);
        Assert.That(result.Total.Numerator, Is.GreaterThan(BigInteger.Zero).And.LessThan(result.Total.Denominator));
        Assert.That(budget.Spent, Is.LessThan(Core.CalculationWorkPolicy.Interactive.WorkUnits));
        Assert.That(execution.PeakCells, Is.LessThanOrEqualTo(Core.CalculationStoragePolicy.Standard.CellLimit));
    }

    [Test]
    public void MixedPostDrawMapsMatchPhysicalOracleAcrossAllStorageTiers() {
        EngineCase fixture = Create("mixed maps", [new("a", 2), new("b", 1), new("f", 1),
            Draw("small", count: 1, removal: 1), Draw("large", count: 3, limit: 1, removal: 2)], 2,
            [new("distinct", [Member("a", 1, 2), Member("b", 1, 2)], groupId: "g"),
                new("direct", [Entry("b", 1, 1)], groupId: "g")], [new("a", ["a"]), new("b", ["a", "b"])], ["g"]);
        DrawSequenceOracle.Counts oracle = DrawSequenceOracle.Enumerate(fixture);
        long? work = null;
        foreach (Core.CalculationStoragePolicy policy in new[] {
            Core.CalculationStoragePolicy.Standard, Core.CalculationStoragePolicy.High, Core.CalculationStoragePolicy.VeryHigh
        }) {
            WorkBudget budget = new(Core.CalculationWorkPolicy.Interactive, policy);
            ExactCalculationResult result = Calculate(fixture, budget, out PostDrawCalculation execution);
            AssertRows(result, oracle);
            Assert.That(execution.TerminalStates, Is.GreaterThan(0));
            if (work.HasValue) {
                Assert.That(budget.Spent, Is.EqualTo(work.Value));
            }
            work = budget.Spent;
        }
    }

    [Test]
    public void PostDrawStorageIsRequestLocalAndRaisedTiersDoNotRaiseWork() {
        EngineCase fixture = LargePopulation();
        ExactCalculationResult? previous = null;
        long? work = null;
        foreach (Core.CalculationStoragePolicy policy in new[] { Core.CalculationStoragePolicy.High, Core.CalculationStoragePolicy.VeryHigh }) {
            WorkBudget budget = new(Core.CalculationWorkPolicy.Interactive, policy);
            ExactCalculationResult result = Calculate(fixture, budget, out PostDrawCalculation execution);
            Assert.That(result.Total.Numerator, Is.EqualTo(result.Total.Denominator));
            Assert.That(execution.PeakCells, Is.GreaterThan(Core.CalculationStoragePolicy.Standard.CellLimit));
            Assert.That(execution.PeakCells, Is.LessThanOrEqualTo(policy.CellLimit));
            if (previous is not null) {
                Assert.That(result.ToRows(), Is.EqualTo(previous.ToRows()));
                Assert.That(budget.Spent, Is.EqualTo(work!.Value));
            }
            previous = result;
            work = budget.Spent;
        }
        WorkBudget standard = new(Core.CalculationWorkPolicy.Interactive);
        Assert.That(Assert.Throws<Core.ProbabilityCalculationLimitException>(() => Calculate(fixture, standard, out _))!.Reason,
            Is.EqualTo(Core.ProbabilityCalculationLimitReason.Storage));
    }

    private static EngineCase LargePopulation() => Create("large binomial table", [new("role", 358), new("filler", 1),
        Draw("source", count: 3, removal: 2)], 5, [Goal("role", Member("role"))], [new("role", ["role"])]);

    private static ExactCalculationResult Calculate(EngineCase fixture, WorkBudget budget, out PostDrawCalculation execution) {
        PreparedCalculation input = fixture.Prepare(budget);
        execution = new(input.Cards, fixture.Request.HandSize, budget, Priority(fixture));
        return execution.Calculate(input.Combos, input.Groups);
    }
}
