using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public sealed class UnlimitedWorkPolicyTest {
    [TestCase(CalculationStorageTier.Standard), TestCase(CalculationStorageTier.High), TestCase(CalculationStorageTier.VeryHigh)]
    public void UnlimitedWorkSaturatesAccountingAndPreservesEveryStorageBoundary(CalculationStorageTier tier) {
        CalculationStoragePolicy storage = CalculationStoragePolicy.ForTier(tier);
        WorkBudget budget = new(CalculationWorkPolicy.Unlimited, storage);
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
        Assert.Throws<ArgumentOutOfRangeException>(() => budget.Spend(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => budget.EnsureAvailable(-1));
    }

    [Test]
    public void FiniteBudgetStillPreflightsAndChargesExactly() {
        WorkBudget budget = new(new(150_000_000));
        budget.Spend(149_999_999);
        budget.EnsureAvailable(1);
        Assert.Throws<ProbabilityCalculationLimitException>(() => budget.EnsureAvailable(2));
        Assert.That(budget.Spent, Is.EqualTo(149_999_999));
        budget.Spend(1);
        Assert.Throws<ProbabilityCalculationLimitException>(() => budget.Spend(1));
        Assert.That(budget.Remaining, Is.Zero);
    }
}
