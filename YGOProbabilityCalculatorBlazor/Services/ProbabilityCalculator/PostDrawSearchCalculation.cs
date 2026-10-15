using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

internal sealed partial class PostDrawCalculation {
    // Search-only hands are unordered compositions weighted by products of
    // binomials. Stream them instead of retaining a permutation frontier.
    private ExactCalculationResult CountSearchOpeningHands(
        List<Card> cards,
        int copies,
        BigInteger[] factorial,
        List<Combo> combos,
        IReadOnlyList<ComboGroup>? groups,
        bool totalOnly
    ) {
        int n = cards.Count;
        int[] state = new int[2 * n];
        int[] suffix = new int[n + 1];
        for (int i = n - 1; i >= 0; i--) {
            suffix[i] = suffix[i + 1] + cards[i].Copies;
        }
        BigInteger denominator = Choose(copies, handSize);
        SearchHandEvaluator evaluator = new(cards, handSize, combos, budget);
        SearchResultAccumulator results = new(combos, groups, totalOnly, denominator, budget);
        CheckStorage(n + 1L, 4L * n + (n + 1L) * WorkBudget.IntegerCells(denominator), "opening composition stack");
        Visit(0, handSize, BigInteger.One);

        return results.Result();

        void Visit(int row, int left, BigInteger weight) {
            budget.Spend(WorkBudget.IntegerCells(weight) + 1);
            if (row == n) {
                TerminalStates++;
                results.Add(evaluator.Evaluate(state), weight);
                return;
            }
            int minimum = Math.Max(0, left - suffix[row + 1]);
            int maximum = Math.Min(left, cards[row].Copies);
            for (int count = minimum; count <= maximum; count++) {
                state[row] = cards[row].Copies - count;
                state[n + row] = count;
                BigInteger combinations = Choose(cards[row].Copies, count);
                budget.Spend(WorkBudget.IntegerCells(weight) * (WorkBudget.IntegerCells(combinations) + 1));
                Visit(row + 1, left - count, weight * combinations);
            }
        }

        BigInteger Choose(int count, int take) {
            budget.Spend(WorkBudget.IntegerCells(factorial[count]) * 3 + 1);
            return factorial[count] / (factorial[take] * factorial[count - take]);
        }
    }

    private ExactCalculationResult CountSearchTerminalHands(
        List<Card> cards,
        Dictionary<State, BigInteger> terminal,
        BigInteger denominator,
        List<Combo> combos,
        IReadOnlyList<ComboGroup>? groups,
        bool totalOnly
    ) {
        SearchResultAccumulator results = new(combos, groups, totalOnly, denominator, budget);
        // Search preserves hand size. Compile once for each size produced by draws.
        budget.Spend(terminal.Count * (cards.Count + 1L));
        foreach (IGrouping<int, KeyValuePair<State, BigInteger>> size in terminal.GroupBy(pair => pair.Key.Values.Skip(cards.Count).Sum())) {
            SearchHandEvaluator evaluator = new(cards, size.Key, combos, budget);
            foreach ((State state, BigInteger weight) in size) {
                results.Add(evaluator.Evaluate(state.Values), weight);
            }
        }

        return results.Result();
    }

    private sealed class SearchResultAccumulator {
        private readonly List<Combo> _combos;
        private readonly IReadOnlyList<ComboGroup> _groups;
        private readonly bool _totalOnly;
        private readonly BigInteger _denominator;
        private readonly WorkBudget _budget;
        private readonly BigInteger[] _wins;

        internal SearchResultAccumulator(
            List<Combo> combos,
            IReadOnlyList<ComboGroup>? groups,
            bool totalOnly,
            BigInteger denominator,
            WorkBudget budget
        ) {
            _combos = combos;
            _groups = groups ?? [];
            _totalOnly = totalOnly;
            _denominator = denominator;
            _budget = budget;
            int count = 1 + (totalOnly ? 0 : combos.Count + _groups.Count);
            budget.CheckStorage(count, count * (WorkBudget.IntegerCells(denominator) + 1));
            _wins = new BigInteger[count];
        }

        internal void Add(bool[] matches, BigInteger weight) {
            _budget.Spend((_wins.Length + (long)_combos.Count * _groups.Count + 1) * (WorkBudget.IntegerCells(_denominator) + 1));
            // Choices are existential within each row. Every realized random
            // outcome contributes its mass once to the total/group union.
            if (matches.Any(match => match)) {
                _wins[0] += weight;
            }
            if (_totalOnly) {
                return;
            }
            for (int i = 0; i < matches.Length; i++) {
                if (matches[i]) {
                    _wins[1 + i] += weight;
                }
            }
            for (int g = 0; g < _groups.Count; g++) {
                if (_combos.Where((combo, i) => matches[i] && combo.GroupId == _groups[g].Id).Any()) {
                    _wins[1 + _combos.Count + g] += weight;
                }
            }
        }

        internal ExactCalculationResult Result() {
            ExactProbability[] results = _wins.Select(win => new ExactProbability(win, _denominator)).ToArray();
            return new(
                results[0],
                _totalOnly ? [] : results.Skip(1).Take(_combos.Count).ToArray(),
                _totalOnly ? [] : results.Skip(1 + _combos.Count).ToArray()
            );
        }
    }

    // Complete bounded reachability over (remaining copies, held copies). Every
    // edge consumes a held source and transfers one eligible remaining copy;
    // remaining deck size strictly decreases, even for self-search/cyclic chains.
    // Stopping at EVERY node makes activation optional, including under exclusions.
    // Hall constraints apply only to each resulting physical hand, never to a
    // proxy union of all targets. Maxima and source consumption are thus preserved.
    private sealed class SearchHandEvaluator {
        private readonly int _n;
        private readonly WorkBudget _budget;
        private readonly (int Source, int[] Targets)[] _searches;
        private readonly Bound[][][] _events;
        private readonly record struct Bound(int[] Rows, int Min, int Max);

        internal SearchHandEvaluator(List<Card> cards, int size, List<Combo> combos, WorkBudget budget) {
            _n = cards.Count;
            _budget = budget;
            budget.CheckStorage(_n, (long)_n * _n);
            budget.Spend(cards.Sum(card => card.Categories.Count + 1L)
                * cards.Sum(card => (long)(card.Search?.Selector.CategoryIdentities.Count ?? 0) + 1));
            _searches = cards.Select((card, index) => (Card: card, Index: index))
                .Where(entry => entry.Card.Search is not null)
                .Select(entry => (entry.Index, cards.Select((card, index) => (Card: card, Index: index))
                    .Where(target => entry.Card.Search!.Matches(target.Card)).Select(target => target.Index).ToArray())).ToArray();
            ComboEventCompiler compiler = new(cards, size, budget);
            long cells = 0;
            _events = combos.Select(combo => compiler.CompileCombo(combo).Where(route => route is not null)
                .Select(route => route!.Constraints.Select(bound => {
                    budget.Spend(_n * (WorkBudget.IntegerCells(bound.EligibleRows) + 1));
                    int[] rows = Enumerable.Range(0, _n).Where(i => (bound.EligibleRows & (BigInteger.One << i)) != 0).ToArray();
                    cells += rows.Length + 2;
                    budget.CheckStorage(cells, cells);
                    return new Bound(rows, bound.MinCount, bound.MaxCount);
                }).ToArray()).ToArray()).ToArray();
        }

        internal bool[] Evaluate(int[] outcome) {
            bool[] wins = new bool[_events.Length];
            HashSet<State> visited = [];
            int[] state = (int[])outcome.Clone();
            long cells = 0;
            Visit();

            return wins;

            void Visit() {
                _budget.Spend(4L * _n + 1);
                State key = new((int[])state.Clone());
                if (visited.Contains(key)) {
                    return;
                }
                cells += key.Values.Length;
                _budget.CheckStorage(visited.Count + 1L, cells);
                visited.Add(key);
                for (int i = 0; i < _events.Length; i++) {
                    _budget.Spend(1);
                    if (!wins[i]) {
                        wins[i] = _events[i].Any(route => route.All(Matches));
                    }
                }
                if (wins.All(win => win)) {
                    return;
                }
                foreach ((int source, int[] targets) in _searches) {
                    _budget.Spend(1);
                    if (state[_n + source] == 0) {
                        continue;
                    }
                    foreach (int target in targets) {
                        _budget.Spend(1);
                        if (state[target] == 0) {
                            continue;
                        }
                        state[_n + source]--;
                        state[target]--;
                        state[_n + target]++;
                        Visit();
                        state[_n + target]--;
                        state[target]++;
                        state[_n + source]++;
                    }
                }
            }

            bool Matches(Bound bound) {
                _budget.Spend(bound.Rows.Length + 1L);
                int count = 0;
                foreach (int row in bound.Rows) {
                    count += state[_n + row];
                }
                return count >= bound.Min && count <= bound.Max;
            }
        }
    }
}
