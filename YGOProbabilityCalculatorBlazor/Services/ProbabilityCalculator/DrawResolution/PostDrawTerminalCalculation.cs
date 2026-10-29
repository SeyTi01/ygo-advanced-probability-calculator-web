using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Compilation;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Counting;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.DrawResolution;

internal sealed partial class PostDrawCalculation {
    // General ordered fallback. Deferred identities use their own conditional
    // counter; both consume the same terminal hand weights under the original N!.
    private ExactCalculationResult CountTerminalHands(
        List<Card> cards,
        Dictionary<State, BigInteger> terminal,
        BigInteger denominator,
        List<Combo> combos,
        IReadOnlyList<ComboGroup>? groups,
        bool totalOnly
    ) {
        int n = cards.Count;
        CalculationResultRows resultRows = new(combos, groups, totalOnly);
        int resultCount = resultRows.Count;
        long resultIntegerCells = WorkBudget.IntegerCells(denominator);
        CheckStorage(resultCount, resultCount * (resultIntegerCells + 1), "result accumulators");
        BigInteger[] wins = new BigInteger[resultCount];
        // Compile once per final hand size. All result rows see the same hands.
        budget.Spend(terminal.Count * (n + 1L));
        foreach (IGrouping<int, KeyValuePair<State, BigInteger>> size in terminal.GroupBy(pair => pair.Key.Values.Sum())) {
            ComboEventCompiler compiler = new(cards, size.Key, budget);
            List<List<CompiledEvent?>> events = combos.Select(compiler.CompileCombo).ToList();
            foreach ((State hand, BigInteger weight) in size) {
                budget.Spend((wins.Length + (long)combos.Count * (groups?.Count ?? 0)) * (resultIntegerCells + 1));
                bool[] matches = events.Select(routes => routes.Any(route => route is not null && Matches(route, hand.Values))).ToArray();
                if (matches.Any(match => match)) {
                    wins[0] += weight;
                }
                if (!totalOnly) {
                    for (int i = 0; i < combos.Count; i++) {
                        if (matches[i]) {
                            wins[resultRows.ComboRow(i)] += weight;
                        }
                    }
                    for (int g = 0; g < (groups?.Count ?? 0); g++) {
                        if (resultRows.GroupMatches(g, matches)) {
                            wins[resultRows.GroupRow(g)] += weight;
                        }
                    }
                }
            }
        }
        ExactProbability[] results = wins.Select(win => new ExactProbability(win, denominator)).ToArray();
        return resultRows.ToResult(results);

        bool Matches(CompiledEvent route, int[] hand) {
            foreach (CountBound bound in route.Constraints) {
                budget.Spend(n * (WorkBudget.IntegerCells(bound.EligibleRows) + 1));
                int count = 0;
                for (int i = 0; i < n; i++) {
                    if ((bound.EligibleRows & (BigInteger.One << i)) != 0) {
                        count += hand[i];
                    }
                }
                if (count < bound.MinCount || count > bound.MaxCount) {
                    return false;
                }
            }
            return true;
        }
    }
}
