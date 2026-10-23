using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Certification;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Compilation;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Counting;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.DrawResolution;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture, NonParallelizable]
public sealed class OrderedThroughputTest {
    [TestCase(false), TestCase(true)]
    public void PackedKeysAndTemplatesPreserveCompleteDistributionAndCertificateRows(bool defer) {
        foreach (SearchCertificateFixtures.Fixture fixture in SearchCertificateFixtures.Fixtures()) {
            List<Card> deck = fixture.Deck.Where(card => card.Active && card.Copies > 0).ToList();
            DrawActivationPriority? priority = fixture.Priority is null ? null : new(DrawActivationMode.CustomPriority, fixture.Priority);
            Dictionary<string, BigInteger>[] distributions = [new(), new()];
            ExactCalculationResult[][] results = [new ExactCalculationResult[3], new ExactCalculationResult[3]];
            for (int side = 0; side < 2; side++) {
                int index = side;
                WorkBudget budget = new(CalculationWorkPolicy.Interactive);
                PostDrawCalculation calculation = new(deck, fixture.Hand, budget, priority, defer,
                    compactFrontierThreshold: 0, reuseSearchTemplates: side == 1,
                    compactSearchTerminals: side == 1) {
                    ObserveTerminal = (hand, mass) => {
                        string key = string.Join(',', hand);
                        distributions[index][key] = distributions[index].GetValueOrDefault(key) + mass;
                        Array.Fill(hand, int.MinValue);
                    }
                };
                results[side][0] = calculation.CalculateSearch(fixture.Combos, fixture.Groups, false,
                    new(deck, budget), coalesceOrdinary: defer);
                results[side][1] = calculation.CalculateSearchLower(fixture.Combos, fixture.Groups, true);
                results[side][2] = calculation.CalculateSearchLower(fixture.Combos, fixture.Groups, false);
            }
            Assert.That(distributions[1], Is.EquivalentTo(distributions[0]), fixture.Name);
            for (int pass = 0; pass < 3; pass++) {
                AssertRows(results[1][pass], results[0][pass], fixture.Name);
            }
        }
    }

    [Test]
    public void ElevenRoleStandardPrimaryAndCertificateMatchWideHighReference() {
        SessionState state = OrderedTerminalAggregationTest.Pressure(roles: 11);
        WorkBudget oldBudget = new(CalculationWorkPolicy.Interactive);
        PostDrawCalculation old = new(state.Cards, state.HandSize, oldBudget,
            reuseSearchTemplates: false, compactSearchTerminals: false);
        Assert.That(Assert.Throws<ProbabilityCalculationLimitException>(() => old.CalculateSearch(state.Combos,
            state.ComboGroups, false, new(state.Cards, oldBudget)))!.Reason, Is.EqualTo(ProbabilityCalculationLimitReason.Storage));
        WorkBudget referenceBudget = new(new(1_000_000_000), CalculationStoragePolicy.High);
        PostDrawCalculation reference = new(state.Cards, state.HandSize, referenceBudget,
            reuseSearchTemplates: false, compactSearchTerminals: false);
        ExactCalculationResult expected = reference.CalculateSearch(state.Combos, state.ComboGroups, false, new(state.Cards, referenceBudget));
        WorkBudget budget = new(CalculationWorkPolicy.Interactive);
        PostDrawCalculation candidate = new(state.Cards, state.HandSize, budget);
        ExactCalculationResult actual = candidate.CalculateSearch(state.Combos, state.ComboGroups, false, new(state.Cards, budget));
        AssertRows(actual, expected, "eleven roles");
        foreach (bool decline in new[] { true, false }) {
            AssertRows(candidate.CalculateSearchLower(state.Combos, state.ComboGroups, decline),
                reference.CalculateSearchLower(state.Combos, state.ComboGroups, decline), "eleven-role certificate");
        }
        Assert.That(budget.Spent, Is.LessThan(CalculationWorkPolicy.Interactive.WorkUnits));
        Assert.That(candidate.PeakCells, Is.LessThan(reference.PeakCells));
        Assert.That(budget.PeakCells, Is.LessThanOrEqualTo(CalculationStoragePolicy.Standard.CellLimit));
    }

    [TestCase(255), TestCase(256)]
    public void TerminalEncodingHonorsByteCapacityAndWideFallback(int copies) {
        CategoryBase target = new("Target");
        List<Card> deck = [new([], 1, id: "d", drawCount: 1, postDrawDiscardCount: 1),
            new([], 1, id: "s", search: new(CardSelector.For(target), false)),
            new([target], 3, id: "t"), new([], copies - 5, id: "f")];
        List<Combo> combos = [new([new(target, 0, 0)]), new([new(target, 1, 2)])];
        WorkBudget[] budgets = [new(CalculationWorkPolicy.Interactive), new(CalculationWorkPolicy.Interactive)];
        ExactCalculationResult[] results = new ExactCalculationResult[2];
        for (int side = 0; side < 2; side++) {
            PostDrawCalculation calculation = new(deck, 2, budgets[side], compactSearchTerminals: side == 1);
            results[side] = calculation.CalculateSearch(combos, null, false, new(deck, budgets[side]));
        }
        AssertRows(results[1], results[0], "byte capacity");
        if (copies > byte.MaxValue) {
            Assert.That(budgets[1].Spent, Is.EqualTo(budgets[0].Spent));
            Assert.That(budgets[1].PeakCells, Is.EqualTo(budgets[0].PeakCells));
        }
    }

    [TestCase(0), TestCase(1), TestCase(31), TestCase(32), TestCase(63), TestCase(64), TestCase(65), TestCase(96)]
    public void SparseProjectionMatchesFullScanAcrossMachineWordBoundary(int retainedRows) {
        List<Card> retained = Enumerable.Range(0, retainedRows).Select(index => new Card([], index % 3, id: $"r{index}")).ToList();
        BigInteger all = (BigInteger.One << (retainedRows + 2)) - 1;
        foreach (int maximum in new[] { 0, 1, 3, 200 }) {
            CompiledEvent compiled = new([new(all, 1, maximum), new(all ^ 1, 0, 200)]);
            ScenarioConditioner full = new(2, 3, retained, new(CalculationWorkPolicy.Interactive));
            ScenarioConditioner sparse = new(2, 3, retained, new(CalculationWorkPolicy.Interactive), sparseFixedRows: true);
            Assert.That(sparse.Project(compiled), Is.EqualTo(full.Project(compiled)));
        }
    }

    [Test]
    public void LimitedRefinementPreservesCompletedPrimaryAndWitnessOnPackedRescue() {
        SessionState state = OrderedTerminalAggregationTest.Pressure(roles: 11);
        WorkBudget measure = new(new(1_000_000_000));
        DrawEffectCalculation reference = new(state.Cards, state.HandSize, measure);
        ExactCalculationResult upper = reference.Calculate(state.Combos, state.ComboGroups);
        reference.DescribeCapability();
        ExactCalculationResult witness = reference.SearchExecution!.CalculateLower(state.Combos, state.ComboGroups, true);
        WorkBudget limited = new(new(measure.Spent + 10_000));
        DrawEffectCalculation execution = new(state.Cards, state.HandSize, limited);
        ExactCalculationResult actual = execution.Calculate(state.Combos, state.ComboGroups);
        SearchAvailabilityCertificate certificate = SearchAvailabilityCertification.TryCalculate(state.Combos,
            state.ComboGroups, actual, limited, execution);
        AssertRows(actual, upper, "primary after refinement limit");
        Assert.That(certificate.Status, Is.EqualTo(SearchAvailabilityStatus.Bounded));
        Assert.That(certificate.Detail, Does.Contain("Limited refinement"));
        SearchAvailabilityInterval[] intervals = [certificate.Total!, .. certificate.Combos, .. certificate.Groups];
        ExactProbability[] expected = [witness.Total, .. witness.Combos, .. witness.Groups];
        for (int row = 0; row < expected.Length; row++) {
            Assert.That(intervals[row].Lower, Is.EqualTo(expected[row].ToFraction()));
        }
    }

    private static void AssertRows(ExactCalculationResult actual, ExactCalculationResult expected, string label) {
        ExactProbability[] left = [actual.Total, .. actual.Combos, .. actual.Groups];
        ExactProbability[] right = [expected.Total, .. expected.Combos, .. expected.Groups];
        Assert.That(left.Length, Is.EqualTo(right.Length), label);
        for (int row = 0; row < left.Length; row++) {
            Assert.That(left[row].Numerator * right[row].Denominator,
                Is.EqualTo(right[row].Numerator * left[row].Denominator), label);
        }
    }
}
