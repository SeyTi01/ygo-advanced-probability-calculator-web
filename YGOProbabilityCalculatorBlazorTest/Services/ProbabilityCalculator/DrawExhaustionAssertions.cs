using System.Globalization;
using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.CalculationEngineAdapter;
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
        foreach (CalculationModel mode in new[] { CalculationModel.Fast, CalculationModel.Exact }) {
            ProbabilityCalculationResult result = new ProbabilityCalculatorService().CalculateProbabilityResults(
                deck, combos, handSize, groups, CalculationWorkPolicy.Interactive, new(Mode: mode));
            AssertRows(ApplicationResultFractions.From(result), oracle);
            AssertHandCompositions(deck, handSize, mode, oracle);
        }
    }

    internal static void AssertAdvancedMatchesOracle(List<Card> deck, int handSize, List<Combo> combos, List<ComboGroup> groups) {
        DrawSequenceOracle.Counts oracle = DrawSequenceOracle.Enumerate(deck, handSize, combos, groups);
        ProbabilityCalculationResult result = new ProbabilityCalculatorService().CalculateProbabilityResults(
            deck, combos, handSize, groups, CalculationWorkPolicy.Interactive, new(Mode: CalculationModel.Exact));
        AssertRows(ApplicationResultFractions.From(result), oracle);
    }

    private static void AssertRows(ApplicationResultFractions result, DrawSequenceOracle.Counts oracle) {
        ApplicationFraction[] rows = [result.Total, .. result.Combos, .. result.Groups];
        Assert.That(rows.Length, Is.EqualTo(oracle.Wins.Length));
        for (int i = 0; i < rows.Length; i++) {
            AssertProbability(rows[i], oracle.Wins[i], oracle.Total, $"Result row {i}");
        }
    }

    private static void AssertHandCompositions(List<Card> deck, int handSize, CalculationModel mode, DrawSequenceOracle.Counts oracle) {
        List<Combo> handCombos = CreateHandCombos(oracle);
        List<KeyValuePair<string, BigInteger>> expectedHands = oracle.Hands.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToList();
        CardProbability.CalculationRequest request = new CalculationRequestAdapter(deck, true).Create(
            handCombos, handSize, [new("all-hands", "All final hands")]);
        CardProbability.CalculationResult result = CardProbability.CalculationEngine.Calculate(request, new(
            mode == CalculationModel.Exact ? CardProbability.CalculationMode.Exact : CardProbability.CalculationMode.Fast));
        Assert.That(result.Status, Is.EqualTo(CardProbability.CalculationStatus.Completed));
        ApplicationResultFractions fractions = ApplicationResultFractions.From(result);
        Assert.That(fractions.Combos, Has.Length.EqualTo(expectedHands.Count));
        for (int i = 0; i < expectedHands.Count; i++) {
            AssertProbability(fractions.Combos[i], expectedHands[i].Value, oracle.Total, $"Final hand {expectedHands[i].Key}");
        }
        BigInteger successful = oracle.Total - oracle.Failed;
        AssertProbability(fractions.Total, successful, oracle.Total, "All successful final hands");
        AssertProbability(fractions.Groups.Single(), successful, oracle.Total, "Final hand group");
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

    private static void AssertProbability(ApplicationFraction actual, BigInteger numerator, BigInteger denominator, string message) =>
        Assert.That(actual.Numerator * denominator, Is.EqualTo(numerator * actual.Denominator), message);
}
