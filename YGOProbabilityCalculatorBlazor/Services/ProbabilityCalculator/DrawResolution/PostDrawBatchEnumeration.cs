using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.DrawResolution;

internal sealed partial class PostDrawCalculation {
    private sealed partial class OrderedResolver {
        // Within-batch permutations preserve later policy when the effect-entry
        // word is fixed. E is chosen at batch start; its word includes every drawn
        // E copy, even repeated OPT copies suppressed when constructing the FIFO.
        // Thus m counts E draws, not appended queue entries. For ordinary counts k,
        // the labeled mass is d!/m! * product C(r_f,k_f) * product (r_e)_[k_e].
        // Enumerate that quotient lazily, then run the unchanged settlement policy.
        internal void EnumerateBatches(
            int handSize,
            int copies,
            BigInteger[] factorial,
            Action<int[], int, BigInteger> terminal
        ) {
            owner.CheckStorage(1, layout.QueueStart + 1L, "physical batch enumeration stack");
            int[] initial = new int[layout.QueueStart];
            for (int row = 0; row < cards.Count; row++) {
                initial[row] = cards[row].Copies;
            }
            initial[layout.DrawsLeft] = handSize;
            if (handSize == 0) {
                terminal(initial, copies, BigInteger.One);
                return;
            }

            Stack<PhysicalBatchExpansion> stack = new();
            long cells = 0;
            long entries = 0;
            Push(initial, copies, BigInteger.One);
            while (stack.Count > 0) {
                budget.Spend(1);
                PhysicalBatchExpansion expansion = stack.Peek();
                long previousCells = expansion.Cells;
                bool found = expansion.TryNext(cells - previousCells, entries, out int[] drawn, out BigInteger weight);
                cells += expansion.Cells - previousCells;
                if (!found) {
                    stack.Pop();
                    cells -= expansion.Cells;
                    entries -= expansion.Entries;
                    continue;
                }

                int remaining = expansion.Remaining - expansion.Draws;
                budget.SearchDiagnostics?.Add("physical batch children");
                if (Settle(ref drawn, remaining) == ResolutionOutcome.RemovalUnavailable) {
                    continue;
                }
                if (drawn[layout.DrawsLeft] == 0) {
                    terminal(drawn, remaining, weight);
                }
                else {
                    Push(drawn, remaining, weight);
                }
            }

            void Push(int[] values, int remaining, BigInteger weight) {
                budget.SearchDiagnostics?.Add("physical batch frames");
                int n = layout.RowCount;
                owner.CheckStorage(
                    entries + 1,
                    cells + values.Length + n + WorkBudget.IntegerCells(weight),
                    "physical batch enumeration stack"
                );
                int[] rows = new int[n];
                int effects = 0;
                int ordinary = n;
                for (int row = 0; row < n; row++) {
                    budget.Spend(values.Length + 1L);
                    // Costs and minimum hand sizes may become payable within this
                    // batch. They must never be used to classify a source as inert.
                    bool candidate = cards[row].DrawCount is not null && TimingAllows(values, row) && !Used(values, row)
                        && (cards[row].MaximumActivations != 1 || !values.AsSpan(layout.QueueStart).Contains(row));
                    if (candidate) {
                        rows[effects++] = row;
                    }
                    else {
                        rows[--ordinary] = row;
                    }
                }

                PhysicalBatchExpansion expansion = new(
                    owner,
                    budget,
                    cards,
                    layout,
                    values,
                    remaining,
                    weight,
                    rows,
                    effects,
                    factorial,
                    cells,
                    entries
                );
                stack.Push(expansion);
                cells += expansion.Cells;
                entries += expansion.Entries;
            }
        }

        // One frame owns only the source state and resumable effect-word/count
        // cursors. Children are produced one at a time, never retained as a batch.
        private sealed class PhysicalBatchExpansion {
            private readonly PostDrawCalculation _owner;
            private readonly WorkBudget _budget;
            private readonly List<Card> _cards;
            private readonly OrderedDrawStateLayout _layout;
            private readonly int[] _values;
            private readonly BigInteger _prefix;
            private readonly int[] _rows;
            private readonly int _effectCount;
            private readonly BigInteger[] _factorial;
            private readonly int[] _effectCounts;
            private readonly int[] _effectWord;
            private readonly int[] _effectNext;
            private readonly BigInteger[] _effectWeights;
            private readonly int[] _ordinaryCounts;
            private readonly int[] _ordinaryNext;
            private readonly int[] _ordinaryLeft;
            private readonly int[] _ordinarySuffix;
            private readonly BigInteger[] _ordinaryWeights;
            private readonly BigInteger[] _ordinaryChoose;
            private int _effectDepth;
            private int _ordinaryDepth;
            private long _otherCells;
            private long _entries;

            internal int Remaining { get; }
            internal int Draws { get; }
            internal long Cells { get; private set; }
            internal long Entries { get; }

            internal PhysicalBatchExpansion(
                PostDrawCalculation owner,
                WorkBudget budget,
                List<Card> cards,
                OrderedDrawStateLayout layout,
                int[] values,
                int remaining,
                BigInteger prefix,
                int[] rows,
                int effectCount,
                BigInteger[] factorial,
                long otherCells,
                long otherEntries
            ) {
                _owner = owner;
                _budget = budget;
                _cards = cards;
                _layout = layout;
                _values = values;
                _prefix = prefix;
                _rows = rows;
                _effectCount = effectCount;
                _factorial = factorial;
                Remaining = remaining;
                Draws = values[layout.DrawsLeft];
                int ordinaryCount = rows.Length - effectCount;
                Entries = Draws + ordinaryCount + 1L;
                _otherCells = otherCells;
                _entries = otherEntries + Entries;
                // Count all owned array slots, cursor fields and integer payload.
                // Card/selector descriptors and factorials are shared with the request.
                Cells = values.Length + rows.Length + effectCount + 3L * Draws + 2
                    + 6L * ordinaryCount + 3 + 16 + WorkBudget.IntegerCells(prefix);
                CheckStorage();
                _budget.Spend(Cells - WorkBudget.IntegerCells(prefix));
                _effectCounts = new int[effectCount];
                _effectWord = new int[Draws];
                _effectNext = new int[Draws + 1];
                _effectWeights = new BigInteger[Draws + 1];
                _ordinaryCounts = new int[ordinaryCount];
                _ordinaryNext = new int[ordinaryCount];
                _ordinaryLeft = new int[ordinaryCount + 1];
                _ordinarySuffix = new int[ordinaryCount + 1];
                _ordinaryWeights = new BigInteger[ordinaryCount + 1];
                _ordinaryChoose = new BigInteger[ordinaryCount];
                for (int index = ordinaryCount - 1; index >= 0; index--) {
                    _budget.Spend(1);
                    _ordinarySuffix[index] = _ordinarySuffix[index + 1] + values[rows[effectCount + index]];
                }
                SetInteger(_effectWeights, 0, BigInteger.One);
                StartOrdinary();
            }

            internal bool TryNext(long otherCells, long entries, out int[] drawn, out BigInteger weight) {
                _otherCells = otherCells;
                _entries = entries;
                while (true) {
                    _budget.Spend(1);
                    if (_ordinaryDepth < 0) {
                        if (!NextEffectWord()) {
                            drawn = [];
                            weight = BigInteger.Zero;
                            return false;
                        }
                        StartOrdinary();
                        continue;
                    }
                    if (_ordinaryDepth == _ordinaryCounts.Length) {
                        int leaf = _ordinaryDepth--;
                        if (_ordinaryLeft[leaf] == 0) {
                            weight = _ordinaryWeights[leaf];
                            drawn = CreateChild(weight);
                            return true;
                        }
                        continue;
                    }

                    int index = _ordinaryDepth;
                    int row = _rows[_effectCount + index];
                    int available = _values[row];
                    int left = _ordinaryLeft[index];
                    int count = _ordinaryNext[index];
                    int maximum = Math.Min(left, available);
                    if (count > maximum) {
                        _ordinaryDepth--;
                        continue;
                    }

                    BigInteger choose = _ordinaryChoose[index];
                    _ordinaryNext[index] = count + 1;
                    if (count < maximum) {
                        _budget.Spend(3 * WorkBudget.IntegerCells(choose) + 1);
                        SetInteger(_ordinaryChoose, index, choose * (available - count) / (count + 1));
                    }
                    if (count < Math.Max(0, left - _ordinarySuffix[index + 1])) {
                        continue;
                    }

                    _ordinaryCounts[index] = count;
                    _ordinaryLeft[index + 1] = left - count;
                    _budget.Spend((WorkBudget.IntegerCells(_ordinaryWeights[index]) + 1)
                        * (WorkBudget.IntegerCells(choose) + 1));
                    SetInteger(_ordinaryWeights, index + 1, _ordinaryWeights[index] * choose);
                    _ordinaryDepth++;
                    if (_ordinaryDepth < _ordinaryCounts.Length) {
                        _ordinaryNext[_ordinaryDepth] = 0;
                        SetInteger(_ordinaryChoose, _ordinaryDepth, BigInteger.One);
                    }
                }
            }

            private void StartOrdinary() {
                int left = Draws - _effectDepth;
                if (left > _ordinarySuffix[0]) {
                    _ordinaryDepth = -1;
                    return;
                }
                _ordinaryDepth = 0;
                _ordinaryLeft[0] = left;
                _budget.Spend((WorkBudget.IntegerCells(_factorial[Draws]) + 1)
                    * (WorkBudget.IntegerCells(_factorial[_effectDepth]) + 1));
                BigInteger shuffles = _factorial[Draws] / _factorial[_effectDepth];
                _budget.Spend((WorkBudget.IntegerCells(_prefix) + 1)
                    * (WorkBudget.IntegerCells(_effectWeights[_effectDepth]) + 1)
                    + (WorkBudget.IntegerCells(_prefix) + WorkBudget.IntegerCells(_effectWeights[_effectDepth]) + 1)
                    * (WorkBudget.IntegerCells(shuffles) + 1));
                SetInteger(_ordinaryWeights, 0, _prefix * _effectWeights[_effectDepth] * shuffles);
                if (_ordinaryCounts.Length > 0) {
                    _ordinaryNext[0] = 0;
                    SetInteger(_ordinaryChoose, 0, BigInteger.One);
                }
            }

            private bool NextEffectWord() {
                while (true) {
                    _budget.Spend(1);
                    if (_effectDepth < Draws) {
                        while (_effectNext[_effectDepth] < _effectCount) {
                            int index = _effectNext[_effectDepth]++;
                            int available = _values[_rows[index]] - _effectCounts[index];
                            _budget.Spend(1);
                            if (available == 0) {
                                continue;
                            }
                            _effectWord[_effectDepth] = index;
                            _effectCounts[index]++;
                            _budget.Spend(WorkBudget.IntegerCells(_effectWeights[_effectDepth]) + 1);
                            SetInteger(_effectWeights, _effectDepth + 1, _effectWeights[_effectDepth] * available);
                            _effectDepth++;
                            _effectNext[_effectDepth] = 0;
                            return true;
                        }
                    }
                    if (_effectDepth == 0) {
                        return false;
                    }
                    _effectDepth--;
                    _effectCounts[_effectWord[_effectDepth]]--;
                }
            }

            private int[] CreateChild(BigInteger weight) {
                int appended = 0;
                for (int index = 0; index < _effectCount; index++) {
                    _budget.Spend(1);
                    appended += _cards[_rows[index]].MaximumActivations == 1
                        ? Math.Min(1, _effectCounts[index]) : _effectCounts[index];
                }
                int length = _values.Length + appended;
                _owner.CheckStorage(
                    _entries + 1,
                    _otherCells + Cells + length + WorkBudget.IntegerCells(weight),
                    "physical batch enumeration stack"
                );
                _budget.Spend(length + 2L * _rows.Length);
                int[] drawn = new int[length];
                _values.CopyTo(drawn, 0);
                for (int index = 0; index < _rows.Length; index++) {
                    int row = _rows[index];
                    int count = index < _effectCount ? _effectCounts[index] : _ordinaryCounts[index - _effectCount];
                    drawn[row] -= count;
                    drawn[_layout.HandStart + row] += count;
                }
                drawn[_layout.DrawsLeft] = 0;
                int next = _values.Length;
                for (int position = 0; position < _effectDepth; position++) {
                    int row = _rows[_effectWord[position]];
                    _budget.Spend(next - _layout.QueueStart + 1L);
                    if (_cards[row].MaximumActivations != 1 || !drawn.AsSpan(_layout.QueueStart, next - _layout.QueueStart).Contains(row)) {
                        drawn[next++] = row;
                    }
                }
                return drawn;
            }

            private void SetInteger(BigInteger[] values, int index, BigInteger value) {
                Cells += WorkBudget.IntegerCells(value) - WorkBudget.IntegerCells(values[index]);
                CheckStorage();
                values[index] = value;
            }

            private void CheckStorage() => _owner.CheckStorage(_entries, _otherCells + Cells, "physical batch enumeration stack");
        }
    }
}
