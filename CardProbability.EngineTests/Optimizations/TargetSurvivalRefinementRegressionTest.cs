using System.Numerics;
using CardProbability.EngineTests.Fixtures;
using CardProbability.Internal;
using CardProbability.Internal.Certification;
using CardProbability.Internal.Counting;
using CardProbability.Internal.Resources;
using Core = CardProbability.Internal.Contracts;

namespace CardProbability.EngineTests.Optimizations;

[TestFixture, NonParallelizable]
internal sealed class TargetSurvivalRefinementRegressionTest {
    [TestCase(0), TestCase(1), TestCase(2), TestCase(3)]
    [TestCase(4), TestCase(5), TestCase(6), TestCase(7)]
    [TestCase(8), TestCase(9), TestCase(10), TestCase(11)]
    public void LabeledCopiesPreserveOldLowerAndEveryNestedInterval(int variant) {
        EngineCase fixture = Variant(variant);
        PreparedCalculation input = fixture.Prepare();
        WorkBudget budget = new(Core.CalculationWorkPolicy.Interactive);
        DrawEffectCalculation execution = new(input.Cards, 2, budget);
        ExactCalculationResult primary = execution.Calculate(input.Combos, input.Groups);
        ExactCalculationResult baseline = execution.SearchExecution!.CalculateLower(input.Combos, input.Groups, false);
        if (execution.SearchExecution.CanRefineKnownSourceLoss()) {
            baseline = execution.SearchExecution.CalculateLower(input.Combos, input.Groups, false, baseline);
        }
        Core.SearchAvailabilityCertificate certificate = SearchAvailabilityCertification.TryCalculate(input.Combos, input.Groups, primary, budget, execution);
        SearchCertificateOracle.Counts physical = SearchCertificateOracle.Enumerate(fixture);
        Assert.That(certificate.Status, Is.AnyOf(Core.SearchAvailabilityStatus.Bounded, Core.SearchAvailabilityStatus.Exact), certificate.Detail);
        Assert.That(certificate.Detail, Does.Not.Contain("refinement failed"));
        Core.SearchAvailabilityInterval[] intervals = [certificate.Total!, .. certificate.Combos, .. certificate.Groups];
        for (int row = 0; row < intervals.Length; row++) {
            ExactProbability lower = Parse(intervals[row].Lower);
            ExactProbability upper = Parse(intervals[row].Upper);
            ExactProbability truth = new(physical.Wins[1][row], physical.Outcomes);
            using (Assert.EnterMultipleScope()) {
                LessOrEqual(baseline.ToRows()[row], lower, "old lower " + row);
                LessOrEqual(lower, truth, "physical lower " + row);
                LessOrEqual(truth, upper, "physical upper " + row);
                LessOrEqual(upper, primary.ToRows()[row], "old upper " + row);
            }
        }
    }

    [TestCase(64), TestCase(65), TestCase(255), TestCase(256), TestCase(257)]
    public void CopyCapDoesNotTruncateCountsOrChangePrimary(int copies) {
        EngineCase fixture = new("copy-cap", new([
            new("desires", 3, new(DrawCount: 2, ActivationLimit: 1, HiddenDeckCost: 10)),
            new("target", 5), new("search", copies - 8, new(ActivationLimit: null, Search: new(new("target"))))
        ], 5, [new("goal", [Requirement.OfEntry("target", 5)])], [new("target", ["target"])]), new());
        PreparedCalculation input = fixture.Prepare();
        WorkBudget budget = new(Core.CalculationWorkPolicy.Default);
        DrawEffectCalculation execution = new(input.Cards, 5, budget);
        ExactCalculationResult primary = execution.Calculate(input.Combos, []);
        Core.SearchAvailabilityCertificate certificate = SearchAvailabilityCertification.TryCalculate(input.Combos, [], primary, budget, execution);
        Assert.That(certificate.Total, Is.Not.Null);
        if (copies > 64) {
            Assert.That(certificate.Total!.Upper, Is.EqualTo(primary.Total.ToFraction()));
        }
        Assert.That(budget.PeakEntries, Is.LessThanOrEqualTo(Core.CalculationStoragePolicy.Standard.EntryLimit));
        Assert.That(budget.PeakCells, Is.LessThanOrEqualTo(Core.CalculationStoragePolicy.Standard.CellLimit));
    }

    private static EngineCase Variant(int variant) {
        List<DeckEntry> entries = [new("t", 2), new("b", 1),
            new("s", 2, new(ActivationLimit: variant % 3 == 0 ? 1 : null, Search: new(new("a")))), new("f", 1),
            new("d", 2, new(DrawCount: 2, ActivationLimit: variant % 2 == 0 ? 1 : null,
                HiddenDeckCost: variant % 3 != 1 ? 1 : 0, HiddenPostDrawMill: variant % 3 != 0 ? 1 : 0))];
        if (variant >= 8) {
            entries[4] = new("d", 1, new(DrawCount: 1, ActivationLimit: 1, HiddenDeckCost: 1));
            entries.Add(new("e", 1, new(DrawCount: 1, ActivationLimit: 1, HiddenPostDrawMill: 1)));
        }
        int sourceCopies = entries.Single(entry => entry.Id == "d").Copies;
        Goal[] goals = [new("all-t", [Requirement.OfEntry("t", 2, 2)], groupId: "all"),
            new("identities", [Requirement.OfEntry("t", 2, 5), Requirement.OfEntry("b", 1, 5)], groupId: "all"),
            new("extra-role", [Requirement.InMembership("a", 1, 5), Requirement.OfEntry("t", 2, 5)], groupId: "all"),
            new("redundant", [Requirement.OfEntry("t", 1, 5)], groupId: "mixed"),
            new("zero", [Requirement.OfEntry("t", 0, 0)], groupId: "mixed"),
            new("alternative", [], [new([Requirement.OfEntry("t", 2, 5), Requirement.OfEntry("f", 1, 5)])], "mixed"),
            new("maximum", [Requirement.InMembership("a", 0, 2), Requirement.OfEntry("t", 2, 5)]),
            new("shared-alternative", [Requirement.OfEntry("t", 2, 5)], [new([Requirement.OfEntry("b", 1, 5), Requirement.OfEntry("f", 1, 5)])]),
            new("repeated", [Requirement.OfEntry("t", 2, 5), Requirement.OfEntry("t", 2, 5)]),
            new("source", [Requirement.OfEntry("d", sourceCopies, 5)])];
        if (variant % 2 != 0) {
            entries.Reverse();
        }
        return new("nested-" + variant, new(entries, 2, goals, [new("a", ["t", "b"])], ["all", "mixed", "empty"]), new());
    }

    private static ExactProbability Parse(Core.ProbabilityFraction value) => new(BigInteger.Parse(value.Numerator), BigInteger.Parse(value.Denominator));
    private static void LessOrEqual(ExactProbability left, ExactProbability right, string label) =>
        Assert.That(left.Numerator * right.Denominator, Is.LessThanOrEqualTo(right.Numerator * left.Denominator), label);
}
