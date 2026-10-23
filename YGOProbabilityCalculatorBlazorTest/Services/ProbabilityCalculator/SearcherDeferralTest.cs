using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.Session;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Counting;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.DrawResolution;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture, NonParallelizable]
public sealed class SearcherDeferralTest {
    [TestCase(false), TestCase(true)]
    public void ForcedPartitionsPreserveAllStagesAndChronologicalOracle(bool merge) {
        int smaller = 0;
        foreach (SearchCertificateFixtures.Fixture fixture in SearchCertificateFixtures.OrderedCoverage()) {
            DrawActivationPriority? priority = fixture.Priority is null ? null : new(DrawActivationMode.CustomPriority, fixture.Priority);
            (ExactCalculationResult[] expected, int oldRows) = Calculate(fixture.Deck, fixture.Hand, fixture.Combos, fixture.Groups, priority, false, merge);
            (ExactCalculationResult[] actual, int newRows) = Calculate(fixture.Deck, fixture.Hand, fixture.Combos, fixture.Groups, priority, true, merge);
            if (newRows < oldRows) {
                smaller++;
            }
            AssertStages(expected, actual, fixture.Name);
            SearchCertificateOracle.Counts oracle = SearchCertificateOracle.Enumerate(fixture.Deck, fixture.Hand, fixture.Combos, fixture.Groups, fixture.Priority);
            for (int row = 0; row < Rows(actual[0]).Length; row++) {
                ExactProbability upper = Rows(actual[0])[row];
                ExactProbability witness = Rows(actual[1])[row];
                ExactProbability lower = Rows(actual[2])[row];
                Assert.That(upper.Numerator * oracle.Outcomes, Is.EqualTo(oracle.Wins[2][row] * upper.Denominator), fixture.Name);
                Assert.That(witness.Numerator * oracle.Outcomes, Is.EqualTo(oracle.Wins[4][row] * witness.Denominator), fixture.Name);
                Assert.That(lower.Numerator * oracle.Outcomes, Is.LessThanOrEqualTo(oracle.Wins[1][row] * lower.Denominator), fixture.Name);
            }
        }
        Assert.That(smaller, Is.GreaterThan(0), "The new search-source partition must actually be exercised.");
    }

    [TestCase(0), TestCase(1), TestCase(2), TestCase(6)]
    public void RemovableSearcherObservationsAndPoolBoundaries(int hand) {
        CategoryBase material = new("Material");
        CategoryBase target = new("Target");
        List<Card> cards = [
            new([], 1, id: "draw", drawCount: 1, postDrawDiscardCount: 1, postDrawSelector: CardSelector.For(material)),
            new([material], 1, id: "removable", search: new(CardSelector.For(target), false)),
            new([target], 1, id: "observed-search", search: new(CardSelector.For(target), false)),
            new([], 1, id: "unobserved-search", search: new(CardSelector.For(target), false)),
            new([target], 1, id: "target"), new([], 1, id: "filler")
        ];
        List<Combo> combos = [new([new(target, 1, 2)]), new([], cards: [new("observed-search", 0, 0)]),
            new([], cards: [new("unobserved-search", 1, 1)])];
        (ExactCalculationResult[] expected, int _) = Calculate(cards, hand, combos, [], null, false, false);
        (ExactCalculationResult[] actual, int _) = Calculate(cards, hand, combos, [], null, true, false);
        AssertStages(expected, actual, "observed source");
        SearchCertificateOracle.Counts oracle = SearchCertificateOracle.Enumerate(cards, hand, combos, []);
        for (int row = 0; row < Rows(actual[0]).Length; row++) {
            ExactProbability upper = Rows(actual[0])[row];
            Assert.That(upper.Numerator * oracle.Outcomes, Is.EqualTo(oracle.Wins[2][row] * upper.Denominator));
        }
    }

    [Test]
    public async Task OriginalSessionPreservesEveryCompletedStage() {
        SessionState state = await SelectiveSurvivorTest.Source();
        (ExactCalculationResult[] expected, int oldRows) = Calculate(state.Cards, state.HandSize, state.Combos, state.ComboGroups, state.DrawActivationPriority, false, true);
        (ExactCalculationResult[] actual, int newRows) = Calculate(state.Cards, state.HandSize, state.Combos, state.ComboGroups, state.DrawActivationPriority, true, true);
        AssertStages(expected, actual, "original session");
        Assert.That(newRows, Is.LessThan(oldRows));
        Assert.That(actual[0].Total.ToFraction(), Is.EqualTo(new ProbabilityFraction("63931285207", "68906597760")));
        Assert.That(actual[2].Total.ToFraction(), Is.EqualTo(new ProbabilityFraction("61422732523", "68906597760")));
    }

    private static (ExactCalculationResult[] Stages, int Rows) Calculate(
        List<Card> cards,
        int hand,
        List<Combo> combos,
        IReadOnlyList<ComboGroup> groups,
        DrawActivationPriority? priority,
        bool defer,
        bool merge
    ) {
        WorkBudget budget = new(CalculationWorkPolicy.Interactive);
        PostDrawCalculation execution = new(cards, hand, budget, priority, deferSearchers: defer);
        ExactCalculationResult primary = execution.CalculateSearch(combos, groups, false, new(cards, budget), coalesceOrdinary: merge);
        ExactCalculationResult witness = execution.CalculateSearchLower(combos, groups, true);
        ExactCalculationResult lower = execution.CalculateSearchLower(combos, groups, false);
        return ([primary, witness, lower], execution.ReducedRows);
    }

    private static void AssertStages(ExactCalculationResult[] expected, ExactCalculationResult[] actual, string name) {
        for (int stage = 0; stage < expected.Length; stage++) {
            Assert.That(Rows(actual[stage]).Select(value => value.ToFraction()),
                Is.EqualTo(Rows(expected[stage]).Select(value => value.ToFraction())), $"{name}, stage {stage}");
        }
    }

    private static ExactProbability[] Rows(ExactCalculationResult result) => [result.Total, .. result.Combos, .. result.Groups];
}
