using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public class CalculationStoragePolicyTest {
    [Test]
    public void StandardUsesTheExistingPerStructureLimitsAndRejectsBoundaryPlusOne() {
        CalculationStoragePolicy policy = CalculationStoragePolicy.Standard;
        WorkBudget entryBudget = new(CalculationWorkPolicy.Interactive, policy);
        WorkBudget cellBudget = new(CalculationWorkPolicy.Interactive, policy);

        Assert.That(policy.EntryLimit, Is.EqualTo(32_768));
        Assert.That(policy.CellLimit, Is.EqualTo(262_144));
        Assert.DoesNotThrow(() => entryBudget.CheckStorage(32_768, 262_144));
        AssertStorageLimit(() => entryBudget.CheckStorage(32_769, 1));
        AssertStorageLimit(() => cellBudget.CheckStorage(1, 262_145));
    }

    [TestCase(CalculationStorageTier.High, 65_536, 524_288)]
    [TestCase(CalculationStorageTier.VeryHigh, 131_072, 1_048_576)]
    public void HigherTiersScaleBothBoundedPerStructureLimits(
        CalculationStorageTier tier,
        long entries,
        long cells
    ) {
        CalculationStoragePolicy policy = CalculationStoragePolicy.ForTier(tier);
        WorkBudget budget = new(CalculationWorkPolicy.Interactive, policy);

        Assert.That(policy.EntryLimit, Is.EqualTo(entries));
        Assert.That(policy.CellLimit, Is.EqualTo(cells));
        Assert.DoesNotThrow(() => budget.CheckStorage(entries, cells));
        AssertStorageLimit(() => budget.CheckStorage(entries + 1, 1));
        AssertStorageLimit(() => budget.CheckStorage(1, cells + 1));
    }

    [Test]
    public void StoragePoliciesBelongToTheirOwnCalculationBudgets() {
        WorkBudget highBudget = new(CalculationWorkPolicy.Interactive, CalculationStoragePolicy.High);
        WorkBudget standardBudget = new(CalculationWorkPolicy.Interactive);

        Assert.DoesNotThrow(() => highBudget.CheckStorage(32_769, 262_145));
        AssertStorageLimit(() => standardBudget.CheckStorage(32_769, 1));
        Assert.DoesNotThrow(() => highBudget.CheckStorage(65_536, 524_288));
        AssertStorageLimit(() => standardBudget.CheckStorage(1, 262_145));
    }

    [Test]
    public void WorkAllowanceRemainsIndependentOfStorageTier() {
        CalculationWorkPolicy workPolicy = CalculationWorkPolicy.Interactive;
        WorkBudget standardBudget = new(workPolicy, CalculationStoragePolicy.Standard);
        WorkBudget highBudget = new(workPolicy, CalculationStoragePolicy.High);
        WorkBudget veryHighBudget = new(workPolicy, CalculationStoragePolicy.VeryHigh);

        standardBudget.Spend(1234);
        highBudget.Spend(1234);
        veryHighBudget.Spend(1234);

        Assert.That(workPolicy.WorkUnits, Is.EqualTo(150_000_000));
        Assert.That(standardBudget.Spent, Is.EqualTo(1234));
        Assert.That(highBudget.Spent, Is.EqualTo(1234));
        Assert.That(veryHighBudget.Spent, Is.EqualTo(1234));
        ProbabilityCalculationLimitException error = Assert.Throws<ProbabilityCalculationLimitException>(
            () => new WorkBudget(new(1), CalculationStoragePolicy.VeryHigh).Spend(2))!;
        Assert.That(error.Reason, Is.EqualTo(ProbabilityCalculationLimitReason.Work));
    }

    [Test]
    public void UnknownStorageTierIsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CalculationStoragePolicy.ForTier((CalculationStorageTier)99));

    private static void AssertStorageLimit(TestDelegate action) {
        ProbabilityCalculationLimitException error = Assert.Throws<ProbabilityCalculationLimitException>(action)!;
        Assert.That(error.Reason, Is.EqualTo(ProbabilityCalculationLimitReason.Storage));
    }
}
