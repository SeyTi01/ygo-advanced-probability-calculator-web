using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

public abstract class SmallDeckOracleTestBase {

    protected static readonly CategoryBase A = new("A");

    protected static readonly CategoryBase B = new("B");

    protected static readonly CategoryBase C = new("C");

    protected static void AssertTotalAndStandaloneResultsMatchEveryPhysicalHand(List<Card> deck, List<Combo> combos, int handSize) {
        var expectedTotal = SmallDeckOracle.EnumerateProbability(deck, combos, handSize);
        var result = new ProbabilityCalculatorService().CalculateProbabilityResults(deck, combos, handSize);

        Assert.That(result.TotalProbability, Is.EqualTo(expectedTotal).Within(1e-12));
        Assert.That(result.ComboProbabilities, Has.Count.EqualTo(combos.Count));
        for (var index = 0; index < combos.Count; index++) {
            var comboResult = result.ComboProbabilities[index];
            Assert.That(comboResult.ComboIndex, Is.EqualTo(index));
            Assert.That(comboResult.ComboName, Is.EqualTo(combos[index].Name));
            Assert.That(comboResult.Probability,
                Is.EqualTo(SmallDeckOracle.EnumerateProbability(deck, [combos[index]], handSize)).Within(1e-12),
                $"Standalone result for combo at index {index}");
        }
    }

}
