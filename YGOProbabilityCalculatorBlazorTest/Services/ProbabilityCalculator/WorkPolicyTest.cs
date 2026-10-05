using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public class WorkPolicyTest {
    [Test]
    public void TinyPolicyStopsOnlyItsRequestAndSufficientPoliciesMatchPhysicalHands() {
        var a = new CategoryBase("A");
        var b = new CategoryBase("B");
        List<Card> deck = [new([a, b], 2), new([a]), new([b]), new([], 2)];
        List<Combo> combos = [new([new(a, 1, 2), new(b, 1, 2)], "Both", groupId: "g"),
            new([new(b, 0, 0)], "Without B", groupId: "g")];
        List<ComboGroup> groups = [new("g", "Group"), new("empty", "Empty")];
        var service = new ProbabilityCalculatorService();
        var error = Assert.Throws<ProbabilityCalculationLimitException>(() =>
            service.CalculateProbabilityResults(deck, combos, 2, groups, new(1)));
        Assert.That(error!.Reason, Is.EqualTo(ProbabilityCalculationLimitReason.Work));
        foreach (var policy in new[] { new CalculationWorkPolicy(100_000), CalculationWorkPolicy.Default, CalculationWorkPolicy.Interactive, new(long.MaxValue) }) {
            var result = service.CalculateProbabilityResults(deck, combos, 2, groups, policy);
            Assert.That(result.TotalProbability, Is.EqualTo(SmallDeckOracle.EnumerateProbability(deck, combos, 2)).Within(1e-12));
            Assert.That(result.GroupProbabilities![0].Probability, Is.EqualTo(result.TotalProbability));
            Assert.That(result.GroupProbabilities[1].Probability, Is.Zero);
            for (var i = 0; i < combos.Count; i++)
                Assert.That(result.ComboProbabilities[i].Probability,
                    Is.EqualTo(SmallDeckOracle.EnumerateProbability(deck, [combos[i]], 2)).Within(1e-12));
        }
        Assert.That(service.CalculateProbabilityResults(deck, combos, 2, groups).TotalProbability,
            Is.EqualTo(SmallDeckOracle.EnumerateProbability(deck, combos, 2)).Within(1e-12));
        Assert.Throws<ProbabilityCalculationLimitException>(() => service.CalculateProbabilityForCombos(deck, combos, 2, new(1)));
    }

    [Test]
    public void DefaultPolicyStopsARealisticMultiRouteRequestAtItsWorkLimit() {
        var categories = Enumerable.Range(0, 20).Select(i => new CategoryBase($"Role{i}")).ToArray();
        var deck = categories.Select((category, i) => new Card([category], 2, id: $"c{i}"))
            .Append(new Card([], 20, id: "blank")).ToList();
        var combos = categories.Select(category => new Combo([new(category, 1, 1)])).ToList();

        var error = Assert.Throws<ProbabilityCalculationLimitException>(() =>
            new ProbabilityCalculatorService().CalculateProbabilityResults(deck, combos, 5));

        Assert.That(error!.Reason, Is.EqualTo(ProbabilityCalculationLimitReason.Work));
    }

    [Test]
    public void RedundantZeroMaximumRoutesStopAtTheStorageLimit() {
        var categories = Enumerable.Range(0, 15).Select(i => new CategoryBase($"Excluded{i}")).ToArray();
        var deck = categories.SelectMany((category, i) =>
            Enumerable.Range(0, 4).Select(j => new Card([category], id: $"c{i}-{j}"))).ToList();
        var combos = categories.Select(category => new Combo([new(category, 0, 0)])).ToList();

        var error = Assert.Throws<ProbabilityCalculationLimitException>(() =>
            new ProbabilityCalculatorService().CalculateProbabilityResults(
                deck, combos, 9, null, CalculationWorkPolicy.Interactive));

        Assert.That(error!.Reason, Is.EqualTo(ProbabilityCalculationLimitReason.Storage));
    }

    [TestCase(0L)]
    [TestCase(-1L)]
    [TestCase(long.MinValue)]
    public void InvalidAllowancesAreRejected(long units) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new CalculationWorkPolicy(units));

    [Test]
    public void NullExplicitPolicyIsRejected() => Assert.Throws<ArgumentNullException>(() =>
        new ProbabilityCalculatorService().CalculateProbabilityForCombos([], [], 1, null!));

    [Test]
    public void HallSubsetStorageStopsBeforeWorkExhaustion() {
        // 16 independent positive roles generate 65,536 Hall subsets. The
        // unchanged 32,768-entry bound stops compilation with ample work left.
        var categories = Enumerable.Range(0, 16).Select(i => new CategoryBase($"R{i}")).ToArray();
        var deck = categories.Select(c => new Card([c], 2)).ToList();
        List<Combo> combos = [new(categories.Select(c => new ComboCategory(c, 1, 16)))];
        var error = Assert.Throws<ProbabilityCalculationLimitException>(() =>
            new ProbabilityCalculatorService().CalculateProbabilityResults(deck, combos, 16, null, new(5_000_000)));
        Assert.That(error!.Reason, Is.EqualTo(ProbabilityCalculationLimitReason.Storage));
    }

    [Test]
    public void ComboCompatibilityAndDeckOverflowDoNotBecomeResourceErrors() {
        var service = new ProbabilityCalculatorService();
        Assert.Throws<ArgumentOutOfRangeException>(() => service.CalculateProbabilityResults([], Enumerable.Range(0, 31)
            .Select(_ => new Combo([])).ToList(), 1, null, CalculationWorkPolicy.Interactive));
        Assert.Throws<OverflowException>(() => service.CalculateProbabilityForCombos(
            [new([], int.MaxValue), new([], 1)], [new([])], 1, CalculationWorkPolicy.Interactive));
    }

    [Test]
    public void DistributionIntegerPayloadStopsWithFewCountVectorCells() {
        // Count vectors alone fit, but the products retained by the distribution
        // exceed the existing cell bound. No physical deck is allocated.
        var role = new CategoryBase("Large multiplicity");
        var error = Assert.Throws<ProbabilityCalculationLimitException>(() =>
            new ProbabilityCalculatorService().CalculateProbabilityForCombos(
                [new([role], 100_000), new([], 100_000)], [new([new(role, 0, 999)])], 1000, new(5_000_000)));
        Assert.That(error!.Reason, Is.EqualTo(ProbabilityCalculationLimitReason.Storage));
    }

    [Test]
    public void BinomialRowPayloadStopsBeforeDistributionAllocation() {
        // Independently counted: this 2,001-entry row requires 490,231 cells,
        // including integer payload, exceeding 262,144 before DP transitions.
        var role = new CategoryBase("Large multiplicity");
        var error = Assert.Throws<ProbabilityCalculationLimitException>(() =>
            new ProbabilityCalculatorService().CalculateProbabilityForCombos(
                [new([role], 100_000), new([], 100_000)], [new([new(role, 0, 1999)])], 2000, new(5_000_000)));
        Assert.That(error!.Reason, Is.EqualTo(ProbabilityCalculationLimitReason.Storage));
    }
}
