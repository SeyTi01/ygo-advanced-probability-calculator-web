using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

// Projection/certificate fixtures explicitly place draws before searches. Their
// authored draw order remains intact; chronological interleaving has its own oracle.
internal static class SearchProjectionTestPolicy {
    internal static DrawActivationPriority DrawFirst(IEnumerable<Card> deck, IEnumerable<string>? authored = null) {
        Card[] cards = deck.ToArray();
        string[] previous = authored?.ToArray() ?? [];
        IEnumerable<string> Ordered(Func<Card, bool> predicate) {
            HashSet<string> ids = cards.Where(predicate).Select(card => card.Id).ToHashSet(StringComparer.Ordinal);
            return previous.Where(ids.Contains).Concat(ids.Except(previous).Order(StringComparer.Ordinal));
        }

        return new(DrawActivationMode.CustomPriority,
            Ordered(card => card.DrawCount is not null).Concat(Ordered(card => card.Search is not null)).ToArray());
    }

    internal static void AssertCausalRows(ProbabilityCalculationResult result, List<Card> deck, int hand,
        List<Combo> combos, IReadOnlyList<ComboGroup> groups, DrawActivationPriority priority) {
        UnifiedActivationOracle.Rational[] expected = UnifiedActivationOracle.Count(deck, hand, combos, groups.ToList(), priority);
        ProbabilityFraction[] actual = [result.Contract!.Fractions!.Total, .. result.Contract.Fractions.Combos,
            .. result.Contract.Fractions.Groups];
        Assert.That(actual, Has.Length.EqualTo(expected.Length));
        for (int row = 0; row < actual.Length; row++) {
            Assert.That(BigInteger.Parse(actual[row].Numerator) * expected[row].Denominator,
                Is.EqualTo(expected[row].Numerator * BigInteger.Parse(actual[row].Denominator)), $"causal row {row}");
        }
    }
}
