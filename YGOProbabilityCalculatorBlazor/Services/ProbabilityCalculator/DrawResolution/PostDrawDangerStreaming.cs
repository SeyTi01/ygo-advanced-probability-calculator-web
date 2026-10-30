using System.Numerics;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.DrawResolution;

internal sealed partial class PostDrawCalculation {
    private sealed partial class OrderedResolver {
        // Skip partial batch states, but still merge endpoints by actual visible
        // Deck depth. Before a bucket fills, process its weighted prefixes and
        // release it. Later contributions to the same states are recomputed;
        // linearity preserves exact mass without retaining an unbounded frontier.
        internal BigInteger[] CountStreamedDanger(
            int handSize,
            int copies,
            BigInteger[] factorial,
            int rows,
            Action<int[], int, BigInteger, BigInteger[]> terminal
        ) {
            BigInteger scale = RandomScale(copies, handSize);
            BigInteger denominator = Denominator(factorial, handSize);
            BigInteger[] wins = new BigInteger[rows];
            int n = cards.Count;
            bool packed = copies <= byte.MaxValue && n <= byte.MaxValue;
            long targetCells = Math.Min(budget.StoragePolicy.CellLimit, owner.DangerFrontierCellTarget);
            int[] initial = new int[layout.QueueStart];
            for (int row = 0; row < n; row++) {
                initial[row] = cards[row].Copies;
            }
            initial[layout.DrawsLeft] = handSize;
            if (handSize == 0) {
                terminal(initial, copies, scale, wins);
                return wins;
            }
            if (packed) {
                budget.Spend(initial.Length);
                initial = DrawFrontierPacking.Pack(initial);
            }
            long liveCells = initial.Length + WorkBudget.IntegerCells(scale);
            owner.CheckStorage(1, liveCells, "draw frontier");
            long descriptorCells = 4L * (copies + 1);
            owner.CheckStorage(copies + 1L, descriptorCells, "streamed draw buckets");
            budget.Spend(2L * (copies + 1));
            Dictionary<State, BigInteger>?[] buckets = new Dictionary<State, BigInteger>?[copies + 1];
            long[] bucketCells = new long[copies + 1];
            buckets[copies] = new() { [new(initial)] = scale };
            bucketCells[copies] = liveCells;
            for (int remaining = copies; remaining > 0; remaining--) {
                Drain(remaining, 0, descriptorCells);
            }
            return wins;

            void Drain(int remaining, int depth, long parentCells) {
                Dictionary<State, BigInteger>? current = buckets[remaining];
                if (current is null) {
                    return;
                }
                long frameCells = parentCells + 4;
                owner.CheckStorage(depth + 1L, frameCells, "streamed draw stack");
                // Every draw decreases remaining. Descendants can therefore
                // share lower buckets without writing into an active ancestor.
                buckets[remaining] = null;
                bucketCells[remaining] = 0;
                foreach ((State state, BigInteger weight) in current) {
                    // net10 permits Remove during enumeration. Drop processed
                    // keys before recursive chunks so ancestors retain only
                    // genuinely pending work, never completed prefixes.
                    budget.Spend(2L * state.Values.Length + WorkBudget.IntegerCells(weight) + 1);
                    current.Remove(state);
                    liveCells -= state.Values.Length + WorkBudget.IntegerCells(weight);
                    int[] values = state.Values;
                    if (packed) {
                        budget.Spend(values[0]);
                        values = DrawFrontierPacking.Unpack(values);
                    }
                    long activeCells = frameCells + state.Values.Length + 2L * values.Length
                        + 3 * WorkBudget.IntegerCells(weight) + 8;
                    owner.CheckStorage(depth + 1L, activeCells, "streamed draw stack");
                    owner.PeakConcurrentCells = Math.Max(owner.PeakConcurrentCells, liveCells + activeCells);
                    if (!layout.HasSearchQueue && values[layout.DrawsLeft] > 1) {
                        EnumerateDangerBatch(values, remaining, weight, factorial, denominator, depth, activeCells, Accept);
                        continue;
                    }
                    for (int row = 0; row < n; row++) {
                        budget.Spend(1);
                        if (values[row] == 0) {
                            continue;
                        }
                        bool candidate = cards[row].HasDrawEffect && TimingAllows(values, row) && !Used(values, row);
                        budget.Spend(values.Length + WorkBudget.IntegerCells(weight) + 1L);
                        int[] drawn = new int[values.Length + (candidate ? 1 : 0)];
                        values.CopyTo(drawn, 0);
                        drawn[row]--;
                        drawn[layout.HandStart + row]++;
                        drawn[layout.DrawsLeft]--;
                        if (candidate) {
                            drawn[^1] = row;
                        }
                        if (layout.HasSearchQueue && cards[row].Search is not null) {
                            layout.EncounterSearch(drawn, row);
                        }
                        SettleDanger(drawn, remaining - 1, weight * values[row], depth, activeCells, Accept);
                    }
                }
                owner.ObserveFrontier?.Invoke();

                void Accept(int[] state, BigInteger weight, int settledDepth, long settledCells) {
                    budget.Spend(n);
                    int remaining = 0;
                    for (int row = 0; row < n; row++) {
                        remaining += state[row];
                    }
                    if (state[layout.DrawsLeft] == 0) {
                        terminal(state, remaining, weight, wins);
                        return;
                    }
                    int length = state.Length;
                    if (packed) {
                        budget.Spend(length);
                        state = DrawFrontierPacking.Pack(state);
                    }
                    State key = new(state);
                    Dictionary<State, BigInteger> target = buckets[remaining] ??= new();
                    budget.Spend(2L * state.Length + WorkBudget.IntegerCells(weight) + 1);
                    bool found = target.TryGetValue(key, out BigInteger previous);
                    budget.Spend(WorkBudget.IntegerCells(previous));
                    BigInteger combined = previous + weight;
                    long cells = bucketCells[remaining] + WorkBudget.IntegerCells(combined) - WorkBudget.IntegerCells(previous)
                        + (found ? 0 : state.Length);
                    int entries = target.Count + (found ? 0 : 1);
                    long pendingCells = settledCells + length + state.Length
                        + WorkBudget.IntegerCells(previous) + WorkBudget.IntegerCells(combined) + 8;
                    owner.CheckStorage(settledDepth + 1L, pendingCells, "streamed draw stack");
                    if (target.Count > 0 && (cells > targetCells || entries > budget.StoragePolicy.EntryLimit)) {
                        owner.DangerFrontierFlushes++;
                        Drain(remaining, settledDepth + 1, pendingCells);
                        target = new();
                        buckets[remaining] = target;
                        bucketCells[remaining] = 0;
                        combined = weight;
                        cells = state.Length + WorkBudget.IntegerCells(weight);
                        entries = 1;
                        found = false;
                    }
                    // Even a single oversized key/weight must fail normally.
                    owner.CheckStorage(entries, cells, "draw frontier");
                    budget.Spend(found ? state.Length : 2L * state.Length);
                    target[key] = combined;
                    liveCells += cells - bucketCells[remaining];
                    bucketCells[remaining] = cells;
                    owner.PeakStates = Math.Max(owner.PeakStates, entries);
                    owner.PeakConcurrentCells = Math.Max(owner.PeakConcurrentCells, liveCells + pendingCells);
                    if (cells > targetCells) {
                        owner.DangerFrontierFlushes++;
                        Drain(remaining, settledDepth + 1, pendingCells);
                        bucketCells[remaining] = 0;
                    }
                }
            }
        }

        // Keep equal batch count vectors together before visiting their distinct
        // effect words. This avoids retaining partial batch states. Ordinary interleavings are
        // unobservable; all physical effect tokens (including OPT copies) remain.
        private void EnumerateDangerBatch(
            int[] values,
            int remaining,
            BigInteger scale,
            BigInteger[] factorial,
            BigInteger denominator,
            int depth,
            long parentCells,
            Action<int[], BigInteger, int, long> settled
        ) {
            int n = cards.Count;
            int draws = values[layout.DrawsLeft];
            long cells = parentCells + values.Length + 4L * n + draws
                + (n + 1L) * (2 * WorkBudget.IntegerCells(denominator) + 6);
            int frames = depth + n + draws + 1;
            owner.CheckStorage(frames, cells, "random discard batch stack");
            int[] counts = new int[n];
            int[] used = new int[n];
            int[] word = new int[draws];
            bool[] effect = new bool[n];
            int[] suffix = new int[n + 1];
            for (int row = n - 1; row >= 0; row--) {
                budget.Spend(1);
                effect[row] = cards[row].HasDrawEffect && TimingAllows(values, row) && !Used(values, row);
                suffix[row] = suffix[row + 1] + values[row];
            }
            Count(0, draws, 0, scale);

            void Count(int row, int left, int effects, BigInteger mass) {
                budget.Spend(1);
                if (row == n) {
                    if (left == 0) {
                        budget.Spend((WorkBudget.IntegerCells(mass) + 1) * (WorkBudget.IntegerCells(factorial[draws]) + 1));
                        Permute(0, effects, mass * (factorial[draws] / factorial[effects]));
                    }
                    return;
                }
                BigInteger choose = BigInteger.One;
                for (int count = 0; count <= Math.Min(left, values[row]); count++) {
                    budget.Spend((WorkBudget.IntegerCells(choose) + 1) * (WorkBudget.IntegerCells(mass) + 2));
                    counts[row] = count;
                    if (left - count <= suffix[row + 1]) {
                        BigInteger factor = effect[row] ? choose * factorial[count] : choose;
                        Count(row + 1, left - count, effects + (effect[row] ? count : 0), mass * factor);
                    }
                    choose = choose * (values[row] - count) / (count + 1);
                }
            }

            void Permute(int position, int effects, BigInteger mass) {
                budget.Spend(1);
                if (position == effects) {
                    budget.Spend(values.Length + effects + n);
                    int[] drawn = new int[values.Length + effects];
                    values.CopyTo(drawn, 0);
                    for (int row = 0; row < n; row++) {
                        drawn[row] -= counts[row];
                        drawn[layout.HandStart + row] += counts[row];
                    }
                    drawn[layout.DrawsLeft] = 0;
                    word.AsSpan(0, effects).CopyTo(drawn.AsSpan(values.Length));
                    SettleDanger(drawn, remaining - draws, mass, frames, cells, settled);
                    return;
                }
                for (int row = 0; row < n; row++) {
                    budget.Spend(1);
                    if (!effect[row] || used[row] == counts[row]) {
                        continue;
                    }
                    used[row]++;
                    word[position] = row;
                    Permute(position + 1, effects, mass);
                    used[row]--;
                }
            }
        }
    }
}
