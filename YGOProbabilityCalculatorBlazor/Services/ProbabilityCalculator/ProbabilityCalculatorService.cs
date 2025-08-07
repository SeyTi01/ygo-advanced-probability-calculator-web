using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

public class ProbabilityCalculatorService : IProbabilityCalculatorService {
    public double CalculateProbabilityForCombos(List<Card> deck, List<Combo> combos, int handSize) {
        double totalProbability = 0;

        for (var mask = 1; mask < 1 << combos.Count; mask++) {
            var selectedCombos = GetSelectedCombos(combos, mask);
            var mergedCategories = MergeComboCategories(selectedCombos);
            var subsetProbability = CalculateProbabilityForCategories(deck, mergedCategories, handSize);

            totalProbability += ApplyInclusionExclusionSign(subsetProbability, mask);
        }

        return totalProbability;
    }

    private static double CalculateProbabilityForCategories(List<Card> deckCards, List<Category> categories, int handSize) {
        var expandedDeck = ExpandDeck(deckCards);
        var minCounts = categories.Select(c => c.MinCount).ToArray();
        var maxCounts = categories.Select(c => c.MaxCount).ToArray();

        var cardMasks = expandedDeck
            .Select(card => BuildCardMask(card, categories))
            .GroupBy(mask => mask)
            .ToDictionary(g => g.Key, g => g.Count());

        var stateDistribution = ComputeDistribution(cardMasks, maxCounts, handSize);

        var successfulWays = stateDistribution
            .Where(kvp => kvp.Key.DrawnCards == handSize && WithinBounds(kvp.Key.CategoryCounts, minCounts, maxCounts))
            .Select(kvp => (BigInteger)kvp.Value)
            .Aggregate(BigInteger.Zero, (sum, ways) => sum + ways);

        var totalWays = ComputeBinomial(expandedDeck.Count, handSize);

        return (double)successfulWays / (double)totalWays;
    }

    private static List<Card> ExpandDeck(List<Card> deck) {
        return deck
            .SelectMany(card => Enumerable.Repeat(card, card.Copies))
            .ToList();
    }

    private static List<Combo> GetSelectedCombos(List<Combo> combos, int mask) {
        return combos.Where((_, i) => (mask & (1 << i)) != 0).ToList();
    }

    private static List<Category> MergeComboCategories(List<Combo> selectedCombos) {
        return selectedCombos
            .SelectMany(combo => combo.Categories)
            .GroupBy(cc => cc.BaseCategory.Name)
            .Select(g => new Category(
                g.Key,
                g.Max(cc => cc.MinCount),
                g.Min(cc => cc.MaxCount)
            ))
            .ToList();
    }

    private static int BuildCardMask(Card card, List<Category> categories) {
        var mask = 0;
        for (var i = 0; i < categories.Count; i++) {
            if (card.Categories.Any(c => c.Name == categories[i].Name))
                mask |= 1 << i;
        }

        return mask;
    }

    private static Dictionary<StateKey, double> ComputeDistribution(
        Dictionary<int, int> cardMasks,
        int[] maxCounts,
        int handSize) {
        var states = new Dictionary<StateKey, double> {
            [new StateKey(0, new int[maxCounts.Length])] = 1.0
        };

        foreach (var (patternMask, count) in cardMasks) {
            states = Convolve(states, patternMask, count, maxCounts, handSize);
        }

        return states;
    }

    private static Dictionary<StateKey, double> Convolve(
        Dictionary<StateKey, double> states,
        int patternMask,
        int groupSize,
        int[] maxCounts,
        int handSize) {
        var next = new Dictionary<StateKey, double>();

        foreach (var (state, ways) in states) {
            for (var draw = 0; draw <= groupSize && state.DrawnCards + draw <= handSize; draw++) {
                var binom = (double)ComputeBinomial(groupSize, draw);
                var newDrawn = state.DrawnCards + draw;
                var newCounts = (int[])state.CategoryCounts.Clone();

                if (draw > 0) {
                    for (var i = 0; i < maxCounts.Length; i++) {
                        if ((patternMask & (1 << i)) != 0) {
                            newCounts[i] = maxCounts[i] == 0
                                ? newCounts[i] + draw
                                : Math.Min(newCounts[i] + draw, maxCounts[i]);
                        }
                    }
                }

                var key = new StateKey(newDrawn, newCounts);
                var increment = ways * binom;

                if (!next.TryAdd(key, increment))
                    next[key] += increment;
            }
        }

        return next;
    }

    private static bool WithinBounds(int[] counts, int[] min, int[] max) {
        return !counts.Where((t, i) => t < min[i] || t > max[i]).Any();
    }

    private static BigInteger ComputeBinomial(int n, int k) {
        if (k < 0 || k > n) return 0;
        k = Math.Min(k, n - k);
        BigInteger result = 1;
        for (var i = 1; i <= k; i++)
            result = result * (n - (k - i)) / i;

        return result;
    }

    private static double ApplyInclusionExclusionSign(double probability, int subsetMask) {
        return BitOperations.PopCount((uint)subsetMask) % 2 == 1
            ? probability
            : -probability;
    }

    private sealed record StateKey(int DrawnCards, int[] CategoryCounts);
}
