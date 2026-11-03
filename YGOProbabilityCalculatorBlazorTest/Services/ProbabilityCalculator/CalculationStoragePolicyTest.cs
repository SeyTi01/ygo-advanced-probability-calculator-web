using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public class CalculationStoragePolicyTest {
    [TestCaseSource(nameof(StoragePolicies))]
    public void PolicyLimitsAreAcceptedAndTheirSuccessorsAreRejected(CalculationStoragePolicy policy) {
        WorkBudget budget = new(CalculationWorkPolicy.Interactive, policy);

        Assert.DoesNotThrow(() => budget.CheckStorage(policy.EntryLimit, policy.CellLimit));
        AssertStorageLimit(() => budget.CheckStorage(policy.EntryLimit + 1, 1));
        AssertStorageLimit(() => budget.CheckStorage(1, policy.CellLimit + 1));
    }

    [Test]
    public void StoragePoliciesBelongToTheirOwnCalculationBudgets() {
        WorkBudget highBudget = new(CalculationWorkPolicy.Interactive, CalculationStoragePolicy.High);
        WorkBudget standardBudget = new(CalculationWorkPolicy.Interactive);

        Assert.DoesNotThrow(() => highBudget.CheckStorage(CalculationStoragePolicy.Standard.EntryLimit + 1, CalculationStoragePolicy.Standard.CellLimit + 1));
        AssertStorageLimit(() => standardBudget.CheckStorage(CalculationStoragePolicy.Standard.EntryLimit + 1, 1));
        Assert.DoesNotThrow(() => highBudget.CheckStorage(CalculationStoragePolicy.High.EntryLimit, CalculationStoragePolicy.High.CellLimit));
        AssertStorageLimit(() => standardBudget.CheckStorage(1, CalculationStoragePolicy.Standard.CellLimit + 1));
    }

    [TestCaseSource(nameof(StoragePolicies))]
    public void WorkAllowanceRemainsIndependentOfStorageTier(CalculationStoragePolicy storagePolicy) {
        const long spent = 1234;
        CalculationWorkPolicy workPolicy = CalculationWorkPolicy.Interactive;
        WorkBudget budget = new(workPolicy, storagePolicy);

        budget.Spend(spent);

        Assert.That(budget.Allowance, Is.EqualTo(workPolicy.WorkUnits));
        Assert.That(budget.Spent, Is.EqualTo(spent));
        Assert.That(budget.Remaining, Is.EqualTo(workPolicy.WorkUnits - spent));
        ProbabilityCalculationLimitException error = Assert.Throws<ProbabilityCalculationLimitException>(
            () => new WorkBudget(new(1), storagePolicy).Spend(2))!;
        Assert.That(error.Reason, Is.EqualTo(ProbabilityCalculationLimitReason.Work));
    }

    [Test]
    public void UnknownStorageTierIsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CalculationStoragePolicy.ForTier((CalculationStorageTier)99));

    private static IEnumerable<TestCaseData> StoragePolicies() {
        yield return new TestCaseData(CalculationStoragePolicy.Standard).SetName("{m}(Standard)");
        yield return new TestCaseData(CalculationStoragePolicy.High).SetName("{m}(High)");
        yield return new TestCaseData(CalculationStoragePolicy.VeryHigh).SetName("{m}(VeryHigh)");
    }

    private static void AssertStorageLimit(TestDelegate action) {
        ProbabilityCalculationLimitException error = Assert.Throws<ProbabilityCalculationLimitException>(action)!;
        Assert.That(error.Reason, Is.EqualTo(ProbabilityCalculationLimitReason.Storage));
    }
}
