using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Compilation;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Counting;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.DrawResolution;

internal sealed partial class PostDrawCalculation {
    private ExactCalculationResult CalculateExchangeable(
        List<Card> cards,
        bool[] relevant,
        int effect,
        int filler,
        BigInteger[] factorial,
        List<Combo> combos,
        IReadOnlyList<ComboGroup>? groups,
        bool totalOnly
    ) {
        int[] rows = Enumerable.Range(0, cards.Count).Where(index => relevant[index]).ToArray();
        int ordinary = rows.Sum(index => cards[index].Copies);
        int fillers = filler < 0 ? 0 : cards[filler].Copies;
        int effects = cards[effect].Copies;
        int copies = ordinary + fillers + effects;
        int draws = cards[effect].DrawCount!.Value;
        int discards = cards[effect].PostDrawDiscardCount;
        // Only three exchangeable classes: relevant, filler and effect. Each
        // vector owns remaining/hand counts plus the current batch phase. All
        // pending sources have the same identity, so hand effect count is FIFO.
        Dictionary<State, BigInteger> current = new() { [new([ordinary, fillers, effects, 0, 0, 0, handSize, 0])] = 1 };
        PeakStates = 1;
        CheckStorage(1, 9, "exchangeable draw frontier");
        BigInteger[] schedules = new BigInteger[ordinary + 1];
        long scheduleCells = schedules.Length;
        CheckStorage(schedules.Length, scheduleCells, "effect schedules");
        for (int depth = 0; current.Count > 0; depth++) {
            Dictionary<State, BigInteger> next = new();
            long cells = 0;
            foreach ((State state, BigInteger weight) in current) {
                int[] values = state.Values;
                if (values[6] == 0) {
                    int seen = ordinary - values[0];
                    BigInteger mass = weight * factorial[copies - depth];
                    budget.Spend(WorkBudget.IntegerCells(weight) * (WorkBudget.IntegerCells(factorial[copies - depth]) + 1));
                    scheduleCells -= WorkBudget.IntegerCells(schedules[seen]);
                    schedules[seen] += mass;
                    scheduleCells += WorkBudget.IntegerCells(schedules[seen]);
                    CheckStorage(schedules.Length, scheduleCells, "effect schedules");
                    continue;
                }
                for (int i = 0; i < 3; i++) {
                    budget.Spend(1);
                    if (values[i] == 0) {
                        continue;
                    }
                    int[] drawn = (int[])values.Clone();
                    budget.Spend(9 + WorkBudget.IntegerCells(weight));
                    drawn[i]--;
                    drawn[i + 3]++;
                    drawn[6]--;
                    int remaining = copies - depth - 1;
                    if (!Settle(drawn, remaining)) {
                        continue;
                    }
                    State key = new(drawn);
                    budget.Spend(33 + WorkBudget.IntegerCells(weight));
                    bool exists = next.TryGetValue(key, out BigInteger previous);
                    BigInteger value = previous + weight * values[i];
                    budget.Spend(WorkBudget.IntegerCells(previous));
                    cells += WorkBudget.IntegerCells(value) - WorkBudget.IntegerCells(previous) + (exists ? 0 : 8);
                    PeakStates = Math.Max(PeakStates, next.Count + (exists ? 0 : 1));
                    CheckStorage(next.Count + (exists ? 0 : 1), cells, "exchangeable draw frontier");
                    next[key] = value;
                }
            }
            current = next;
        }

        // Within every aggregate schedule, the seen relevant subset is uniform.
        // Divide out its binomial multiplicity, retaining integer permutation mass.
        BigInteger[][] choose = new BigInteger[copies + 1][];
        long chooseCells = 0;
        for (int n = 0; n <= copies; n++) {
            choose[n] = new BigInteger[n + 1];
            choose[n][0] = choose[n][n] = 1;
            for (int k = 1; k < n; k++) {
                budget.Spend(WorkBudget.IntegerCells(choose[n - 1][k - 1]) + WorkBudget.IntegerCells(choose[n - 1][k]));
                choose[n][k] = choose[n - 1][k - 1] + choose[n - 1][k];
            }
            chooseCells += choose[n].Sum(value => WorkBudget.IntegerCells(value));
            CheckStorage((n + 1L) * (n + 2) / 2, chooseCells, "binomial coefficients");
        }
        for (int seen = 0; seen < schedules.Length; seen++) {
            budget.Spend(WorkBudget.IntegerCells(schedules[seen]) * (WorkBudget.IntegerCells(choose[ordinary][seen]) + 1));
            schedules[seen] = BigInteger.DivRem(schedules[seen], choose[ordinary][seen], out BigInteger remainder);
            if (!remainder.IsZero) {
                throw new InvalidOperationException("Exchangeable schedule mass must contain every relevant subset equally.");
            }
        }

        ComboEventCompiler compiler = new(cards, handSize, budget);
        List<List<CompiledEvent?>> events = combos.Select(compiler.CompileCombo).ToList();
        // Precompute row membership once. Streaming terminal hands retain no
        // dictionary, and each bound visits only the occupied hand rows.
        List<List<(bool[] Eligible, int Min, int Max)[]>> tests = [];
        long testCells = 0;
        long boundCount = 0;
        foreach (List<CompiledEvent?> routes in events) {
            List<(bool[] Eligible, int Min, int Max)[]> compiled = [];
            foreach (CompiledEvent? route in routes) {
                if (route is null) {
                    continue;
                }
                List<(bool[] Eligible, int Min, int Max)> bounds = [];
                foreach (CountBound bound in route.Constraints) {
                    budget.Spend(cards.Count * (WorkBudget.IntegerCells(bound.EligibleRows) + 1));
                    testCells += cards.Count + 2L;
                    CheckStorage(++boundCount, testCells, "terminal predicates");
                    bool[] eligible = Enumerable.Range(0, cards.Count)
                        .Select(index => (bound.EligibleRows & (BigInteger.One << index)) != 0).ToArray();
                    bounds.Add((eligible, bound.MinCount, bound.MaxCount));
                }
                compiled.Add(bounds.ToArray());
            }
            tests.Add(compiled);
        }
        CalculationResultRows resultRows = new(combos, groups, totalOnly);
        BigInteger[] wins = new BigInteger[resultRows.Count];
        CheckStorage(wins.Length, wins.Length * (WorkBudget.IntegerCells(factorial[copies]) + 1), "result accumulators");
        int[] hand = new int[cards.Count];
        List<int> occupied = [];
        int[] suffix = new int[rows.Length + 1];
        for (int i = rows.Length - 1; i >= 0; i--) {
            suffix[i] = suffix[i + 1] + cards[rows[i]].Copies;
        }
        for (int count = 0; count <= Math.Min(handSize, ordinary); count++) {
            if (count < handSize && (schedules[count].IsZero || filler < 0 || handSize - count > fillers)) {
                continue;
            }
            if (filler >= 0) {
                hand[filler] = handSize - count;
                if (hand[filler] > 0) {
                    occupied.Add(filler);
                }
            }
            Enumerate(0, count, -1, BigInteger.One, count);
            occupied.Clear();
        }
        ExactProbability[] results = wins.Select(win => new ExactProbability(win, factorial[copies])).ToArray();
        return resultRows.ToResult(results);

        bool Settle(int[] values, int remaining) {
            if (values[6] > 0) {
                return values[6] <= remaining;
            }
            for (int discard = values[7]; discard > 0; discard--) {
                budget.Spend(4);
                bool unusable = draws > remaining;
                bool discardEffect = values[5] > 0 && (values[4] == 0 || (unusable && effect > filler));
                int selected = 3;
                if (discardEffect) {
                    selected = 5;
                }
                else if (values[4] > 0) {
                    selected = 4;
                }
                if (values[selected] == 0) {
                    return false;
                }
                values[selected]--;
            }
            values[7] = 0;
            if (values[5] > 0) {
                values[5]--;
                values[6] = draws;
                values[7] = discards;
                return draws <= remaining;
            }
            return true;
        }

        BigInteger Binomial(int n, int k) => k < 0 || k > n ? BigInteger.Zero : choose[n][k];

        void Enumerate(int position, int left, int last, BigInteger prefix, int retainedCount) {
            budget.Spend(1);
            if (left == 0) {
                BigInteger mass;
                if (last < 0 || retainedCount < handSize) {
                    mass = prefix * schedules[retainedCount];
                }
                else {
                    // All rows before the last retained row must be drawn exactly
                    // as retained. At the boundary, extra copies and any worse
                    // rows may have been seen and discarded. Vandermonde sums
                    // count those unseen identities without retaining them.
                    int boundary = rows[last];
                    int retained = hand[boundary];
                    BigInteger tail = 0;
                    for (int seen = handSize; seen <= ordinary; seen++) {
                        if (schedules[seen].IsZero) {
                            continue;
                        }
                        for (int extra = retained; extra <= cards[boundary].Copies; extra++) {
                            budget.Spend(WorkBudget.IntegerCells(schedules[seen]) + 3);
                            tail += schedules[seen] * Binomial(cards[boundary].Copies, extra)
                                * Binomial(suffix[last + 1], seen - handSize + retained - extra);
                        }
                    }
                    mass = prefix / Binomial(cards[boundary].Copies, retained) * tail;
                }
                if (!mass.IsZero) {
                    TerminalStates++;
                    Evaluate(mass);
                }
                return;
            }
            if (position == rows.Length || suffix[position] < left) {
                return;
            }
            int row = rows[position];
            for (int count = 0; count <= Math.Min(cards[row].Copies, left); count++) {
                hand[row] = count;
                if (count > 0) {
                    occupied.Add(row);
                }
                budget.Spend(WorkBudget.IntegerCells(prefix) + 1);
                Enumerate(position + 1, left - count, count > 0 ? position : last, prefix * Binomial(cards[row].Copies, count), retainedCount);
                if (count > 0) {
                    occupied.RemoveAt(occupied.Count - 1);
                }
            }
            hand[row] = 0;
        }

        void Evaluate(BigInteger mass) {
            bool[] matches = new bool[combos.Count];
            for (int i = 0; i < tests.Count; i++) {
                matches[i] = tests[i].Any(route => route.All(bound => {
                    budget.Spend(occupied.Count + 1L);
                    int count = occupied.Sum(index => bound.Eligible[index] ? hand[index] : 0);
                    return count >= bound.Min && count <= bound.Max;
                }));
                budget.Spend(WorkBudget.IntegerCells(mass) + 1);
                if (!totalOnly && matches[i]) {
                    wins[resultRows.ComboRow(i)] += mass;
                }
            }
            budget.Spend(WorkBudget.IntegerCells(mass) + matches.Length + 1);
            if (matches.Any(match => match)) {
                wins[0] += mass;
            }
            for (int g = 0; !totalOnly && g < (groups?.Count ?? 0); g++) {
                budget.Spend(matches.Length + WorkBudget.IntegerCells(mass) + 1);
                if (resultRows.GroupMatches(g, matches)) {
                    wins[resultRows.GroupRow(g)] += mass;
                }
            }
        }
    }
}
