using CardProbability.Internal.Contracts;

using CardProbability.Internal.Resources;

namespace CardProbability.EngineTests;

[TestFixture]
internal sealed class UnlimitedWorkPolicyTest {
    [TestCase(CalculationStorageTier.Standard), TestCase(CalculationStorageTier.High), TestCase(CalculationStorageTier.VeryHigh)]
    public void UnlimitedWorkSaturatesAccountingAndPreservesEveryStorageBoundary(CalculationStorageTier tier) {
        CalculationStoragePolicy storage = CalculationStoragePolicy.ForTier(tier);
        CardProbability.Internal.Resources.WorkBudget budget = new(CalculationWorkPolicy.Unlimited, storage);
        budget.Spend(150_000_001);
        Assert.That(budget.Spent, Is.EqualTo(150_000_001));
        budget.EnsureAvailable(long.MaxValue);
        budget.Spend(long.MaxValue);
        budget.Spend(long.MaxValue);
        Assert.That(budget.Spent, Is.EqualTo(long.MaxValue));
        Assert.That(budget.Allowance, Is.Null);
        Assert.That(budget.Remaining, Is.Null);
        budget.CheckStorage(storage.EntryLimit, storage.CellLimit);
        Assert.That(Assert.Throws<ProbabilityCalculationLimitException>(() => budget.CheckStorage(storage.EntryLimit + 1, 1))!.Reason,
            Is.EqualTo(ProbabilityCalculationLimitReason.Storage));
        Assert.That(Assert.Throws<ProbabilityCalculationLimitException>(() => budget.CheckStorage(1, storage.CellLimit + 1))!.Reason,
            Is.EqualTo(ProbabilityCalculationLimitReason.Storage));
        ArgumentOutOfRangeException spendError = Assert.Throws<ArgumentOutOfRangeException>(() => budget.Spend(-1))!;
        ArgumentOutOfRangeException availabilityError = Assert.Throws<ArgumentOutOfRangeException>(() => budget.EnsureAvailable(-1))!;
        Assert.Multiple(() => {
            Assert.That(spendError.ParamName, Is.EqualTo("units"));
            Assert.That(spendError.ActualValue, Is.EqualTo(-1L));
            Assert.That(availabilityError.ParamName, Is.EqualTo("units"));
            Assert.That(availabilityError.ActualValue, Is.EqualTo(-1L));
        });
    }

    [Test]
    public void FiniteBudgetStillPreflightsAndChargesExactly() {
        CardProbability.Internal.Resources.WorkBudget budget = new(new(150_000_000));
        budget.Spend(149_999_999);
        budget.EnsureAvailable(1);
        Assert.Throws<ProbabilityCalculationLimitException>(() => budget.EnsureAvailable(2));
        Assert.That(budget.Spent, Is.EqualTo(149_999_999));
        budget.Spend(1);
        Assert.Throws<ProbabilityCalculationLimitException>(() => budget.Spend(1));
        Assert.That(budget.Remaining, Is.Zero);
    }

    [Test]
    public void CustomStoragePolicyRemainsBoundedByArrayCapacity() {
        CalculationStoragePolicy storage = new(long.MaxValue, long.MaxValue);
        WorkBudget budget = new(CalculationWorkPolicy.Unlimited, storage);
        budget.CheckStorage(Array.MaxLength, 1);

        ProbabilityCalculationLimitException error = Assert.Throws<ProbabilityCalculationLimitException>(() =>
            budget.CheckStorage(Array.MaxLength + 1L, 2))!;

        Assert.Multiple(() => {
            Assert.That(error.Reason, Is.EqualTo(ProbabilityCalculationLimitReason.Storage));
            Assert.That(budget.StoragePolicy, Is.SameAs(storage));
            Assert.That(budget.PeakEntries, Is.EqualTo(Array.MaxLength + 1L));
            Assert.That(budget.PeakCells, Is.EqualTo(2));
            Assert.That(budget.Spent, Is.Zero);
        });
    }

    [TestCase(false), TestCase(true)]
    public void CancellationPreservesAccountingAndStoragePeaks(bool unlimited) {
        using CancellationTokenSource cancellation = new();
        WorkBudget budget = new(unlimited ? CalculationWorkPolicy.Unlimited : new(100), cancellationToken: cancellation.Token);
        budget.Spend(10);
        budget.CheckStorage(2, 3);
        cancellation.Cancel();

        Assert.Multiple(() => {
            Assert.Throws<OperationCanceledException>(() => budget.Spend(1));
            Assert.Throws<OperationCanceledException>(() => budget.EnsureAvailable(1));
            Assert.Throws<OperationCanceledException>(() => budget.CheckStorage(4, 5));
            Assert.That(budget.Spent, Is.EqualTo(10));
            Assert.That(budget.Remaining, Is.EqualTo(unlimited ? (long?)null : 90));
            Assert.That(budget.PeakEntries, Is.EqualTo(2));
            Assert.That(budget.PeakCells, Is.EqualTo(3));
        });
    }
}
