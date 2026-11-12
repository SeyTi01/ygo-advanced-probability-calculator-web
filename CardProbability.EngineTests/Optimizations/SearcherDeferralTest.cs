using CardProbability.EngineTests.Fixtures;
using CardProbability.Internal;
using CardProbability.Internal.Counting;
using CardProbability.Internal.DrawResolution;
using CardProbability.Internal.Resources;
using Core = CardProbability.Internal.Contracts;
using static CardProbability.EngineTests.Optimizations.DeferredTestCases;

namespace CardProbability.EngineTests.Optimizations;

[TestFixture, NonParallelizable]
public sealed class SearcherDeferralTest {
    [TestCase(false), TestCase(true)]
    public void ForcedPartitionsPreserveAllStagesAndChronologicalOracle(bool merge) {
        int smaller = 0;
        foreach (EngineCase fixture in SearchCertificateFixtures.OrderedCoverage()) {
            (ExactCalculationResult[] expected, int oldRows) = Calculate(fixture, false, merge);
            (ExactCalculationResult[] actual, int newRows) = Calculate(fixture, true, merge);
            if (newRows < oldRows) {
                smaller++;
            }
            AssertStages(expected, actual, fixture.Name);
            SearchCertificateOracle.Counts oracle = SearchCertificateOracle.Enumerate(fixture);
            for (int row = 0; row < actual[0].ToRows().Length; row++) {
                ExactProbability upper = actual[0].ToRows()[row];
                ExactProbability witness = actual[1].ToRows()[row];
                ExactProbability lower = actual[2].ToRows()[row];
                Assert.That(upper.Numerator * oracle.Outcomes, Is.EqualTo(oracle.Wins[2][row] * upper.Denominator), fixture.Name);
                Assert.That(witness.Numerator * oracle.Outcomes, Is.EqualTo(oracle.Wins[4][row] * witness.Denominator), fixture.Name);
                Assert.That(lower.Numerator * oracle.Outcomes, Is.LessThanOrEqualTo(oracle.Wins[1][row] * lower.Denominator), fixture.Name);
            }
        }
        Assert.That(smaller, Is.GreaterThan(0), "The search-source partition must actually be exercised.");
    }

    [TestCase(0), TestCase(1), TestCase(2), TestCase(6)]
    public void RemovableSearcherObservationsAndPoolBoundaries(int hand) {
        EngineCase fixture = Create("observed source", [Draw("draw", count: 1, removal: 1, removalSet: "material"),
            new("removable", 1, new(Search: new(new("target")), ActivationLimit: null)),
            new("observed-search", 1, new(Search: new(new("target")), ActivationLimit: null)),
            new("unobserved-search", 1, new(Search: new(new("target")), ActivationLimit: null)), new("target", 1), new("filler", 1)], hand,
            [Goal("target", Member("target", 1, 2)), Goal("observed-exclusion", Entry("observed-search", 0, 0)),
                Goal("unobserved", Entry("unobserved-search", 1, 1))],
            [new("material", ["removable"]), new("target", ["observed-search", "target"])]);
        (ExactCalculationResult[] expected, _) = Calculate(fixture, false, false);
        (ExactCalculationResult[] actual, _) = Calculate(fixture, true, false);
        AssertStages(expected, actual, fixture.Name);
        SearchCertificateOracle.Counts oracle = SearchCertificateOracle.Enumerate(fixture);
        for (int row = 0; row < actual[0].ToRows().Length; row++) {
            ExactProbability upper = actual[0].ToRows()[row];
            Assert.That(upper.Numerator * oracle.Outcomes, Is.EqualTo(oracle.Wins[2][row] * upper.Denominator));
        }
    }

    private static (ExactCalculationResult[] Stages, int Rows) Calculate(EngineCase fixture, bool defer, bool merge) {
        WorkBudget budget = new(Core.CalculationWorkPolicy.Interactive);
        PreparedCalculation input = fixture.Prepare(budget);
        PostDrawCalculation execution = new(input.Cards, fixture.Request.HandSize, budget, Priority(fixture), deferSearchers: defer);
        return ([execution.CalculateSearch(input.Combos, input.Groups, false, new(input.Cards, budget), coalesceOrdinary: merge),
            execution.CalculateSearchLower(input.Combos, input.Groups, true), execution.CalculateSearchLower(input.Combos, input.Groups, false)], execution.ReducedRows);
    }

    private static void AssertStages(ExactCalculationResult[] expected, ExactCalculationResult[] actual, string name) {
        for (int stage = 0; stage < expected.Length; stage++) {
            Assert.That(actual[stage].ToRows().Select(value => value.ToFraction()),
                Is.EqualTo(expected[stage].ToRows().Select(value => value.ToFraction())), $"{name}, stage {stage}");
        }
    }
}
