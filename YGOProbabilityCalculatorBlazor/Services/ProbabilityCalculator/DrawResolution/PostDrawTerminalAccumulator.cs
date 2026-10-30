using System.Numerics;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.DrawResolution;

internal sealed partial class PostDrawCalculation {
    // Completed outcomes with the same success rows and unseen suffix length
    // share one factorial multiplier. Retain only these sums, never the hands.
    internal sealed class TerminalAccumulator {
        private readonly PostDrawCalculation _owner;
        private readonly WorkBudget _budget;
        private readonly BigInteger[] _factorial;
        private readonly BigInteger[] _masks;
        private readonly Dictionary<(BigInteger Mask, int Remaining), BigInteger> _mass = new();
        private long _cells;

        internal TerminalAccumulator(PostDrawCalculation owner, WorkBudget budget, BigInteger[] factorial,
            CalculationResultRows rows, bool totalOnly) {
            _owner = owner;
            _budget = budget;
            _factorial = factorial;
            _masks = new BigInteger[rows.Count];
            _masks[0] = (BigInteger.One << rows.ComboCount) - 1;
            if (!totalOnly) {
                for (int combo = 0; combo < rows.ComboCount; combo++) {
                    BigInteger bit = BigInteger.One << combo;
                    _masks[rows.ComboRow(combo)] = bit;
                    for (int group = 0; group < rows.GroupCount; group++) {
                        budget.Spend(WorkBudget.IntegerCells(bit) + 1);
                        if (rows.GroupContains(group, combo)) {
                            _masks[rows.GroupRow(group)] |= bit;
                        }
                    }
                }
            }
            budget.Spend(_masks.Length + _masks.Sum(WorkBudget.IntegerCells));
            owner.CheckStorage(_masks.Length, _masks.Length + _masks.Sum(WorkBudget.IntegerCells), "terminal row masks");
        }

        internal void Add(bool[] success, int remaining, BigInteger prefix, BigInteger[] wins) {
            BigInteger mask = BigInteger.Zero;
            for (int combo = 0; combo < success.Length; combo++) {
                _budget.Spend(WorkBudget.IntegerCells(mask) + 1);
                if (success[combo]) {
                    mask |= BigInteger.One << combo;
                }
            }
            if (mask.IsZero) {
                return;
            }
            (BigInteger, int) key = (mask, remaining);
            bool found = _mass.TryGetValue(key, out BigInteger previous);
            _budget.Spend(WorkBudget.IntegerCells(mask) + WorkBudget.IntegerCells(previous) + WorkBudget.IntegerCells(prefix) + 1);
            BigInteger value = previous + prefix;
            long cells = _cells + WorkBudget.IntegerCells(value) - WorkBudget.IntegerCells(previous)
                + (found ? 0 : WorkBudget.IntegerCells(mask) + 1);
            int entries = _mass.Count + (found ? 0 : 1);
            if (cells > _budget.StoragePolicy.CellLimit || entries > _budget.StoragePolicy.EntryLimit) {
                Flush(wins);
                value = prefix;
                cells = WorkBudget.IntegerCells(prefix) + WorkBudget.IntegerCells(mask) + 1;
                entries = 1;
            }
            _owner.CheckStorage(entries, cells, "terminal outcome sums");
            // Keep the by-value dictionary update across BigInteger arithmetic.
            _mass[key] = value;
            _cells = cells;
        }

        internal void Flush(BigInteger[] wins) {
            foreach (((BigInteger mask, int remaining), BigInteger prefix) in _mass) {
                _budget.Spend((WorkBudget.IntegerCells(prefix) + 1) * (WorkBudget.IntegerCells(_factorial[remaining]) + 1));
                BigInteger mass = prefix * _factorial[remaining];
                for (int row = 0; row < wins.Length; row++) {
                    _budget.Spend(WorkBudget.IntegerCells(mask) + WorkBudget.IntegerCells(_masks[row]) + 1);
                    if (!(mask & _masks[row]).IsZero) {
                        _budget.Spend(WorkBudget.IntegerCells(wins[row]) + WorkBudget.IntegerCells(mass));
                        wins[row] += mass;
                    }
                }
            }
            _budget.Spend(_mass.Count);
            _mass.Clear();
            _cells = 0;
        }
    }
}
