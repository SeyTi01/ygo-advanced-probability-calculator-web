using System.Globalization;
using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

internal static class DrawExhaustionAssertions {
    private static readonly CategoryBase A = new("A");
    private static readonly CategoryBase B = new("B");

    internal static void AssertBasicAndAdvancedMatchOracle(List<Card> deck, int handSize) {
        List<Combo> combos = [
            new([new(A, 1, 0, RequirementMaximumMode.HandSize), new(B, 1, 0, RequirementMaximumMode.HandSize)], groupId: "routes"),
            new([new(A, 1, 1)], cards: [new("reward", 1, 2)], groupId: "routes"),
            new([new(A, 0, 0)]),
            new([new(B, 0, 1)]),
            new([], cards: [new("source", 1, 2)])
        ];
        List<ComboGroup> groups = [new("routes", "Routes"), new("empty", "Empty")];
        DrawSequenceOracle.Counts oracle = DrawSequenceOracle.Enumerate(deck, handSize, combos, groups);

        ExactCalculationResult basic = new DrawEffectCalculation(deck, handSize, new(CalculationWorkPolicy.Interactive))
            .Calculate(combos, groups);
        ExactCalculationResult advanced = new PostDrawCalculation(deck, handSize, new(CalculationWorkPolicy.Interactive))
            .Calculate(combos, groups);

        AssertRows(basic, oracle);
        AssertRows(advanced, oracle);
        AssertHandCompositions(new DrawEffectCalculation(deck, handSize, new(CalculationWorkPolicy.Interactive)), oracle);
        AssertHandCompositions(new PostDrawCalculation(deck, handSize, new(CalculationWorkPolicy.Interactive)), oracle);
    }

    internal static void AssertAdvancedMatchesOracle(List<Card> deck, int handSize, List<Combo> combos, List<ComboGroup> groups) {
        DrawSequenceOracle.Counts oracle = DrawSequenceOracle.Enumerate(deck, handSize, combos, groups);
        ExactCalculationResult result = new PostDrawCalculation(deck, handSize, new(CalculationWorkPolicy.Interactive))
            .Calculate(combos, groups);

        AssertRows(result, oracle);
    }

    private static void AssertRows(ExactCalculationResult result, DrawSequenceOracle.Counts oracle) {
        ExactProbability[] rows = [result.Total, .. result.Combos, .. result.Groups];
        Assert.That(rows.Length, Is.EqualTo(oracle.Wins.Length));
        for (int i = 0; i < rows.Length; i++) {
            AssertProbability(rows[i], oracle.Wins[i], oracle.Total, $"Result row {i}");
        }
    }

    private static void AssertHandCompositions(
        DrawEffectCalculation calculation,
        DrawSequenceOracle.Counts oracle
    ) {
        List<Combo> handCombos = CreateHandCombos(oracle);
        ExactCalculationResult result = calculation.Calculate(handCombos, [new("all-hands", "All final hands")]);
        AssertHandRows(result, oracle);
    }

    private static void AssertHandCompositions(
        PostDrawCalculation calculation,
        DrawSequenceOracle.Counts oracle
    ) {
        List<Combo> handCombos = CreateHandCombos(oracle);
        ExactCalculationResult result = calculation.Calculate(handCombos, [new("all-hands", "All final hands")]);
        AssertHandRows(result, oracle);
    }

    private static List<Combo> CreateHandCombos(DrawSequenceOracle.Counts oracle) {
        List<Combo> combos = [];
        foreach (string handKey in oracle.Hands.Keys.Order(StringComparer.Ordinal)) {
            string[] countTexts = handKey.Split(',');
            List<ComboCard> exactCounts = [];
            for (int i = 0; i < oracle.CardIds.Length; i++) {
                int count = int.Parse(countTexts[i], CultureInfo.InvariantCulture);
                exactCounts.Add(new(oracle.CardIds[i], count, count));
            }
            combos.Add(new([], $"Hand {combos.Count}", groupId: "all-hands", cards: exactCounts));
        }
        return combos;
    }

    private static void AssertHandRows(ExactCalculationResult result, DrawSequenceOracle.Counts oracle) {
        List<KeyValuePair<string, BigInteger>> expectedHands = oracle.Hands.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToList();
        Assert.That(result.Combos.Length, Is.EqualTo(expectedHands.Count));
        for (int i = 0; i < expectedHands.Count; i++) {
            AssertProbability(result.Combos[i], expectedHands[i].Value, oracle.Total, $"Final hand {expectedHands[i].Key}");
        }

        BigInteger successfulOrders = oracle.Total - oracle.Failed;
        AssertProbability(result.Total, successfulOrders, oracle.Total, "All successful final hands");
        AssertProbability(result.Groups.Single(), successfulOrders, oracle.Total, "Final hand group");
    }

    private static void AssertProbability(ExactProbability actual, BigInteger expectedNumerator, BigInteger expectedDenominator, string message) {
        Assert.That(actual.Numerator * expectedDenominator, Is.EqualTo(expectedNumerator * actual.Denominator), message);
    }
}
