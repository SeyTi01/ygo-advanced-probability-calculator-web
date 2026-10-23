using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Counting;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public class ConvolutionBoundsTest {
    [Test]
    public void FeasibleDrawIntervalsMatchPhysicalHandsAcrossBoundsAndDeckOrders() {
        CategoryBase a = new("A");
        CategoryBase b = new("B");
        List<Card> deck = [new([a, b], 2, id: "ab"), new([a], 2, id: "a"), new([b], id: "b"), new([], 2, id: "blank")];
        foreach (int hand in new[] { 0, 1, 3, 5, 6, 7 }) {
            foreach (int minimum in new[] { 0, 1, 2, 3 }) {
                foreach (int maximum in new[] { minimum, Math.Max(minimum, 5) }) {
                    List<Combo> combos = [
                        new([new(a, minimum, maximum), new(b, 1, 0, RequirementMaximumMode.HandSize)], groupId: "g"),
                        new([new(a, 0, 0)], cards: [new("blank", 1, 0, RequirementMaximumMode.HandSize)], groupId: "g"),
                        new(
                            [ComboCategory.SameCard([a, b], 1, 0, RequirementMaximumMode.HandSize)],
                            alternativeGroups: [new([ComboAlternative.For(new ComboCategory(a, 1, 2)), ComboAlternative.For(new ComboCard("b", 1, 1))])]
                        )
                    ];
                    foreach (List<Card> order in new[] { deck, deck.AsEnumerable().Reverse().ToList() }) {
                        ExactCalculationResult actual = new ProbabilityCalculation(order, hand, CalculationWorkPolicy.Interactive)
                            .CalculateExactResults(combos, [new("g", "Group")]);
                        AssertFraction(actual.Total, SmallDeckOracle.EnumerateCounts(deck, combos, hand));
                        AssertFraction(actual.Groups[0], SmallDeckOracle.EnumerateCounts(deck, combos.Take(2).ToList(), hand));
                        for (int i = 0; i < combos.Count; i++) {
                            AssertFraction(actual.Combos[i], SmallDeckOracle.EnumerateCounts(deck, [combos[i]], hand));
                        }
                    }
                }
            }
        }
    }

    private static void AssertFraction(ExactProbability actual, (int Successes, int Total) expected) {
        Assert.That(actual.Numerator * expected.Total, Is.EqualTo(new BigInteger(expected.Successes) * actual.Denominator));
    }
}
