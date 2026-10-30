// Research-only whole-batch quotient for the anonymous-card prototype.
using System.Numerics;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.DrawResolution;

internal sealed partial class PostDrawCalculation {
    private sealed partial class OrderedResolver {
        private void EnumerateAnonymousBatch(
            int[] values,
            int unknown,
            int physical,
            BigInteger mass,
            int remaining,
            bool[] pooled,
            BigInteger[][] choose,
            BigInteger[] factorial,
            BigInteger denominator,
            Func<BigInteger, BigInteger, BigInteger, BigInteger> scale,
            Action<int[], int, int, BigInteger, int, long> settled
        ) {
            if (layout.HasSearchQueue) {
                throw new NotSupportedException("Anonymous batches require unordered simple terminal searches.");
            }
            int n = cards.Count;
            int draws = values[layout.DrawsLeft];
            int[] available = new int[n + 1];
            int[] counts = new int[n + 1];
            int[] suffix = new int[n + 2];
            int[] used = new int[n];
            int[] word = new int[draws];
            bool[] effect = new bool[n];
            available[n] = physical;
            for (int row = 0; row < n; row++) {
                available[row] = pooled[row] ? 0 : values[row];
                effect[row] = !pooled[row] && cards[row].HasDrawEffect && TimingAllows(values, row) && !Used(values, row);
            }
            for (int row = n; row >= 0; row--) {
                suffix[row] = suffix[row + 1] + available[row];
            }
            int frames = n + draws + 2;
            long cells = values.Length + 5L * n + draws + 5 + frames * (2 * WorkBudget.IntegerCells(denominator) + 6);
            owner.CheckStorage(frames, cells, "anonymous whole-batch stack");
            budget.Spend(3L * n + draws + 3);
            budget.Spend((WorkBudget.IntegerCells(factorial[remaining]) + 1)
                * (WorkBudget.IntegerCells(factorial[remaining - draws]) + 1));
            BigInteger divisor = factorial[remaining] / factorial[remaining - draws];
            Count(0, draws, 0, BigInteger.One);

            void Count(int row, int left, int effects, BigInteger coefficient) {
                budget.Spend(1);
                if (row == n + 1) {
                    if (left == 0) {
                        budget.Spend((WorkBudget.IntegerCells(factorial[draws]) + 1)
                            * (WorkBudget.IntegerCells(factorial[effects]) + 1));
                        BigInteger interleavings = factorial[draws] / factorial[effects];
                        budget.Spend((WorkBudget.IntegerCells(coefficient) + 1) * (WorkBudget.IntegerCells(interleavings) + 1));
                        BigInteger wordMass = scale(mass, coefficient * interleavings, divisor);
                        Permute(0, effects, wordMass);
                    }
                    return;
                }
                for (int count = Math.Max(0, left - suffix[row + 1]); count <= Math.Min(left, available[row]); count++) {
                    counts[row] = count;
                    bool candidate = row < n && effect[row];
                    BigInteger factor = choose[available[row]][count];
                    if (candidate) {
                        budget.Spend((WorkBudget.IntegerCells(factor) + 1) * (WorkBudget.IntegerCells(factorial[count]) + 1));
                        factor *= factorial[count];
                    }
                    budget.Spend((WorkBudget.IntegerCells(coefficient) + 1) * (WorkBudget.IntegerCells(factor) + 1));
                    Count(row + 1, left - count, effects + (candidate ? count : 0), coefficient * factor);
                }
            }

            void Permute(int position, int effects, BigInteger wordMass) {
                budget.Spend(1);
                if (position == effects) {
                    budget.Spend(values.Length + effects + n * 2L + 3);
                    int[] drawn = new int[values.Length + effects];
                    values.CopyTo(drawn, 0);
                    for (int row = 0; row < n; row++) {
                        // Pooled slots contain inventory, not physical deck
                        // capacity; their labels remain unobserved through draws.
                        if (!pooled[row]) {
                            drawn[row] -= counts[row];
                            drawn[layout.HandStart + row] += counts[row];
                        }
                    }
                    drawn[layout.DrawsLeft] = 0;
                    word.AsSpan(0, effects).CopyTo(drawn.AsSpan(values.Length));
                    settled(drawn, unknown + counts[n], physical - counts[n], wordMass, frames, cells);
                    return;
                }
                for (int row = 0; row < n; row++) {
                    budget.Spend(1);
                    if (!effect[row] || used[row] == counts[row]) {
                        continue;
                    }
                    used[row]++;
                    word[position] = row;
                    Permute(position + 1, effects, wordMass);
                    used[row]--;
                }
            }
        }
    }
}
