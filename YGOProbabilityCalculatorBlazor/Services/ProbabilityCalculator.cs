using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services;

public static class ProbabilityCalculator {
    public static double CalculateProbabilityRange(
        IEnumerable<Card> deck,
        IEnumerable<Category> categories,
        int handSize,
        CancellationToken cancellationToken = default) {
        var deckCards = deck.SelectMany(card => Enumerable.Repeat(card, card.Copies)).ToList();
        var categoryList = categories.ToList();

        var (minCounts, maxCounts) = ExtractCategoryArrays(categoryList);
        var membershipMap = BuildCardCategoryMap(deckCards, categoryList);

        var stateDistribution = ComputeStateDistributions(
            membershipMap,
            maxCounts,
            handSize,
            cancellationToken);

        var successfulWays = CountSuccessfulOutcomes(
            stateDistribution,
            minCounts,
            maxCounts,
            handSize);

        var totalWays = (double)ComputeBinomial(deckCards.Count, handSize);

        return successfulWays / totalWays;
    }

    public static double CalculateProbabilityForCombos(
        IEnumerable<Card> deck,
        IEnumerable<Combo> combos,
        int handSize,
        CancellationToken cancellationToken = default) {
        var deckList = deck.ToList();
        var comboList = combos.ToList();
        double result = 0;

        for (var mask = 1; mask < 1 << comboList.Count; mask++) {
            cancellationToken.ThrowIfCancellationRequested();

            var mergedCategories =
                CategoryMerger.MergeComboCategories(comboList, mask);

            var p = CalculateProbabilityRange(
                deckList,
                mergedCategories,
                handSize,
                cancellationToken);

            result += ApplyInclusionExclusionSign(p, mask);
        }

        return result;
    }

    private static (int[] MinCounts, int[] MaxCounts) ExtractCategoryArrays(
        IEnumerable<Category> categories) {
        var list = categories.ToList();
        var minCounts = list.Select(c => c.MinCount).ToArray();
        var maxCounts = list.Select(c => c.MaxCount).ToArray();

        return (minCounts, maxCounts);
    }

    private static List<bool[]> BuildCardCategoryMap(List<Card> deckCards, List<Category> categories) {
        return deckCards
            .Select(card => categories
                .Select(category => card.Categories.Any(c => c.Name == category.Name))
                .ToArray())
            .ToList();
    }

    private static Dictionary<StateKey, double> ComputeStateDistributions(
        List<bool[]> membershipMap,
        int[] maxCounts,
        int handSize,
        CancellationToken cancellationToken) {
        var patternCounts = BuildPatternCounts(membershipMap, cancellationToken);
        var states = new Dictionary<StateKey, double> {
            [new StateKey(0, new int[maxCounts.Length])] = 1.0
        };

        foreach (var (pattern, count) in patternCounts) {
            cancellationToken.ThrowIfCancellationRequested();
            states = ConvolvePatternGroup(
                states,
                pattern,
                count,
                maxCounts,
                handSize,
                cancellationToken);
        }

        return states;
    }

    private static Dictionary<string, int> BuildPatternCounts(
        List<bool[]> membershipMap,
        CancellationToken cancellationToken) {
        var counts = new Dictionary<string, int>();

        foreach (var pattern in membershipMap) {
            cancellationToken.ThrowIfCancellationRequested();

            var key = string.Join(",", pattern.Select(b => b ? "1" : "0"));
            if (!counts.TryAdd(key, 1))
                counts[key]++;
        }

        return counts;
    }

    private static Dictionary<StateKey, double> ConvolvePatternGroup(
        Dictionary<StateKey, double> states,
        string patternKey,
        int groupSize,
        int[] maxCounts,
        int handSize,
        CancellationToken cancellationToken) {
        var pattern = patternKey
            .Split(',')
            .Select(s => s == "1")
            .ToArray();

        var nextStates = new Dictionary<StateKey, double>();

        foreach (var (state, ways) in states) {
            cancellationToken.ThrowIfCancellationRequested();

            for (var drawCount = 0;
                 drawCount <= groupSize && state.DrawnCards + drawCount <= handSize;
                 drawCount++) {
                cancellationToken.ThrowIfCancellationRequested();

                var binomial = (double)ComputeBinomial(groupSize, drawCount);
                var newDrawn = state.DrawnCards + drawCount;

                var newCounts = (int[])state.CategoryCounts.Clone();
                if (drawCount > 0) {
                    for (var i = 0; i < pattern.Length; i++) {
                        if (!pattern[i]) continue;

                        newCounts[i] = maxCounts[i] == 0
                            ? newCounts[i] + drawCount
                            : Math.Min(newCounts[i] + drawCount, maxCounts[i]);
                    }
                }

                var key = new StateKey(newDrawn, newCounts);
                var increment = ways * binomial;

                if (!nextStates.TryAdd(key, increment))
                    nextStates[key] += increment;
            }
        }

        return nextStates;
    }

    private static double CountSuccessfulOutcomes(
        Dictionary<StateKey, double> states,
        int[] minCounts,
        int[] maxCounts,
        int handSize) {
        double total = 0;

        foreach (var (state, ways) in states) {
            if (state.DrawnCards != handSize)
                continue;

            var valid = true;
            for (var i = 0; i < state.CategoryCounts.Length; i++) {
                if (state.CategoryCounts[i] >= minCounts[i] && state.CategoryCounts[i] <= maxCounts[i])
                    continue;

                valid = false;
                break;
            }

            if (valid)
                total += ways;
        }

        return total;
    }

    private static BigInteger ComputeBinomial(int n, int k) {
        if (k < 0 || k > n)
            return 0;
        k = Math.Min(k, n - k);
        BigInteger result = 1;

        for (var i = 1; i <= k; i++) {
            result *= n - (k - i);
            result /= i;
        }

        return result;
    }

    private static double ApplyInclusionExclusionSign(
        double probability,
        int subsetMask) {
        var hasOdd = BitOperations.PopCount((uint)subsetMask) % 2 == 1;

        return hasOdd ? probability : -probability;
    }

    private sealed record StateKey(int DrawnCards, int[] CategoryCounts);
}
