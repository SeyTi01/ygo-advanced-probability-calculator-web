using CardProbability.EngineTests.Fixtures;
using static CardProbability.EngineTests.Optimizations.DeferredTestCases;

namespace CardProbability.EngineTests.Optimizations;

[TestFixture]
public sealed class PostDrawExchangeableTest {
    private static IEnumerable<TestCaseData> Cases() {
        foreach (int draw in new[] { 2, 3 }) {
            foreach (int filler in new[] { 0, 1, 2 }) {
                foreach (int effects in new[] { 1, 2, 3 }) {
                    foreach (int hand in new[] { 0, 1, 2, 4 }) {
                        yield return new(draw, filler, effects, hand);
                    }
                }
            }
        }
    }

    [TestCaseSource(nameof(Cases))]
    public void ExchangeableScheduleAndPriorityTruncationMatchPhysicalOracle(int draw, int filler, int effects, int hand) {
        EngineCase fixture = Create("exchangeable", [new("a", 2), new("b", 1), new("f", filler),
            Draw("g", effects, draw, removal: draw - 1)], hand,
            [new("distinct", [Member("a"), Member("b")], groupId: "g"),
                new("direct", [Entry("a", 1, 1)], groupId: "g"), Goal("bounded", Member("b", 0, 1)),
                Goal("source-exclusion", Entry("g", 0, 0)), new("alternative", [], [new([Member("a", 1, 1), Entry("b", 1, 1)])])],
            [new("a", ["a"]), new("b", ["a", "b"])], ["g", "empty"]);
        AssertDraw(fixture, peakStates: 200);
    }

    [TestCase(false), TestCase(true)]
    public void MaximumOnlySelectorsPreserveUnusableEffectVersusFillerTie(bool effectFirst) => AssertDraw(Create(
        "maximum-only tie", [new("r", 2), new("f", 1), Draw("g", 2, 3, removal: 2)], 2,
        [Goal("role-with-exclusion", Member("role", 1, 2), Member("excluded", 0, 0))],
        [new("role", ["r"]), new("excluded", effectFirst ? ["g"] : ["f"])]), peakStates: 200);
}
