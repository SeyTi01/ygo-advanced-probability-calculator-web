using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public sealed class ExactOneUnionOracleTest {
    [TestCase(3, 5)]
    [TestCase(6, 5)]
    [TestCase(6, 6)]
    [TestCase(6, 12)]
    [TestCase(6, 60)]
    [TestCase(10, 5)]
    public void ExactlyOneFromAnyTwoCopyRoleMatchesIndependentComplement(int roles, int handSize) {
        CategoryBase[] categories = Enumerable.Range(0, roles).Select(index => new CategoryBase($"Role {index}")).ToArray();
        List<Card> deck = categories.Select(category => new Card([category], 2)).Append(new([], 60 - roles * 2)).ToList();
        List<Combo> combos = categories.Select(category => new Combo([new(category, 1, 1)])).ToList();
        BigInteger denominator = Choose(60, handSize);
        BigInteger failed = BigInteger.Zero;

        // No combo succeeds exactly when each role contributes zero or both
        // copies, and every remaining drawn copy is blank.
        for (int pairs = 0; pairs <= Math.Min(roles, handSize / 2); pairs++) {
            failed += Choose(roles, pairs) * Choose(60 - roles * 2, handSize - 2 * pairs);
        }

        double expected = 1d - (double)failed / (double)denominator;
        double actual = new ProbabilityCalculatorService().CalculateProbabilityResults(deck, combos, handSize).TotalProbability;

        Assert.That(actual, Is.EqualTo(expected).Within(1e-12));
    }

    private static BigInteger Choose(int total, int count) {
        if (count < 0 || count > total) {
            return BigInteger.Zero;
        }

        BigInteger result = BigInteger.One;
        for (int index = 1; index <= count; index++) {
            result = result * (total - index + 1) / index;
        }

        return result;
    }
}
