using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Search;

// Terminal-only quotient for optional, inert one-card searches. A witness can
// fetch row i at most M_i times, where M_i sums the original held allowances of
// sources reaching i. Therefore only min(available_i, M_i) is observable. Rows
// whose capacity stays saturated after all H hidden losses, and unreachable
// rows, share one loss pool. Vandermonde sums its labels jointly, not as
// independent target survival probabilities.
internal sealed class PhysicalSearchLossCounter {
    private readonly WorkBudget _budget;
    private readonly (int Source, int Limit, int[] Targets)[] _sources;
    private readonly int[] _available;
    private readonly int[] _remaining;
    private readonly int[] _maximumFetches;
    private readonly int[] _vulnerable;
    private readonly int[] _suffix;
    private readonly BigInteger[] _poolChoose;
    private readonly long _entries;
    private long _cells;

    internal PhysicalSearchLossCounter(List<Card> cards, WorkBudget budget) {
        if (cards.Any(card => card.EffectPlan.RequiresSearchResolution)) {
            throw new ArgumentException("Hidden-loss capacity pooling requires simple optional searches.", nameof(cards));
        }

        _budget = budget;
        budget.Spend(cards.Sum(card => card.Categories.Count + 1L)
            * cards.Sum(card => (card.Search?.Selector.EvaluationSize ?? 0) + 1L));
        budget.CheckStorage(cards.Count, (long)cards.Count * cards.Count + 6L * cards.Count);
        _sources = cards.Select((card, row) => (Card: card, Row: row))
            .Where(entry => entry.Card.Search is not null)
            .Select(entry => (entry.Row, entry.Card.MaximumActivations,
                cards.Select((card, row) => (Card: card, Row: row))
                    .Where(target => target.Row != entry.Row && entry.Card.Search!.Matches(target.Card))
                    .Select(target => target.Row).ToArray())).ToArray();
        int n = cards.Count;
        int copies = cards.Sum(card => card.Copies);
        _entries = 5L * n + copies + 2 + _sources.Length;
        _cells = _entries + _sources.Sum(source => 3L + source.Targets.Length);
        budget.CheckStorage(_entries, _cells);
        _available = new int[n];
        _remaining = new int[n];
        _maximumFetches = new int[n];
        _vulnerable = new int[n];
        _suffix = new int[n + 1];
        _poolChoose = new BigInteger[copies + 1];
    }

    // Each callback borrows a capacity representative, not a physical Deck
    // vector: its sum need not equal the actual remaining Deck size. Use only
    // with PhysicalSearchHandCounter; bounded removal policies inspect capacity.
    // The weight counts labeled H-subsets. The caller supplies H!(R-H)! and
    // the visible-prefix weight, preserving failed traces in its denominator.
    internal void Enumerate(
        ReadOnlySpan<int> held,
        ReadOnlySpan<int> remaining,
        int hidden,
        Action<int[], BigInteger> terminal
    ) {
        _budget.Spend(4L * _available.Length + _sources.Length + 1);
        remaining.CopyTo(_remaining);
        Array.Clear(_maximumFetches);
        foreach ((int source, int limit, int[] targets) in _sources) {
            int allowance = Math.Min(held[source], limit);
            if (allowance == 0) {
                continue;
            }
            foreach (int target in targets) {
                _budget.Spend(1);
                _maximumFetches[target] += allowance;
            }
        }

        int pooled = 0;
        int vulnerable = 0;
        for (int row = 0; row < remaining.Length; row++) {
            int count = _remaining[row];
            int maximum = _maximumFetches[row];
            _available[row] = Math.Min(count, maximum);
            if (count == 0 || maximum == 0 || count - hidden >= maximum) {
                pooled += count;
            }
            else {
                _vulnerable[vulnerable++] = row;
            }
        }
        _budget.SearchDiagnostics?.Add("physical vulnerable target rows", vulnerable);
        _budget.SearchDiagnostics?.Add("physical pooled hidden copies", pooled);
        _suffix[vulnerable] = 0;
        for (int index = vulnerable - 1; index >= 0; index--) {
            _suffix[index] = _suffix[index + 1] + remaining[_vulnerable[index]];
        }

        SetChoose(0, BigInteger.One);
        for (int loss = 1; loss <= Math.Min(hidden, pooled); loss++) {
            BigInteger previous = _poolChoose[loss - 1];
            _budget.Spend(3 * WorkBudget.IntegerCells(previous) + 1);
            SetChoose(loss, previous * (pooled - loss + 1) / loss);
        }
        long stackCells = 0;
        Visit(0, hidden, BigInteger.One);

        void Visit(int index, int left, BigInteger mass) {
            _budget.SearchDiagnostics?.Add("physical quotient hidden allocation nodes");
            _budget.Spend(1 + WorkBudget.IntegerCells(mass));
            long frameCells = 9 + WorkBudget.IntegerCells(mass);
            stackCells += frameCells;
            _budget.CheckStorage(_entries + index + 1L, _cells + stackCells);
            if (index == vulnerable) {
                if (left >= 0 && left <= pooled) {
                    BigInteger poolWeight = _poolChoose[left];
                    _budget.Spend((WorkBudget.IntegerCells(mass) + 1) * (WorkBudget.IntegerCells(poolWeight) + 1));
                    BigInteger weight = mass * poolWeight;
                    _budget.CheckStorage(_entries + index + 2L, _cells + stackCells + WorkBudget.IntegerCells(weight));
                    _budget.SearchDiagnostics?.Add("physical hidden capacity representatives");
                    terminal(_available, weight);
                }
                stackCells -= frameCells;
                return;
            }

            int row = _vulnerable[index];
            int count = _remaining[row];
            int minimum = Math.Max(0, left - (_suffix[index + 1] + pooled));
            int maximum = Math.Min(count, left);
            BigInteger choose = BigInteger.One;
            stackCells += WorkBudget.IntegerCells(choose);
            for (int loss = 0; loss <= maximum; loss++) {
                _budget.Spend((WorkBudget.IntegerCells(choose) + 1) * (WorkBudget.IntegerCells(mass) + 1) + 1);
                if (loss >= minimum) {
                    _available[row] = Math.Min(count - loss, _maximumFetches[row]);
                    BigInteger weight = mass * choose;
                    _budget.CheckStorage(_entries + index + 2L, _cells + stackCells + WorkBudget.IntegerCells(weight));
                    Visit(index + 1, left - loss, weight);
                }
                if (loss < maximum) {
                    _budget.Spend(3 * WorkBudget.IntegerCells(choose) + 1);
                    BigInteger nextChoose = choose * (count - loss) / (loss + 1);
                    _budget.CheckStorage(_entries + index + 1L, _cells + stackCells + WorkBudget.IntegerCells(nextChoose));
                    stackCells += WorkBudget.IntegerCells(nextChoose) - WorkBudget.IntegerCells(choose);
                    choose = nextChoose;
                }
            }
            stackCells -= frameCells + WorkBudget.IntegerCells(choose);
        }
    }

    private void SetChoose(int index, BigInteger value) {
        _cells += WorkBudget.IntegerCells(value) - WorkBudget.IntegerCells(_poolChoose[index]);
        _budget.CheckStorage(_entries, _cells);
        _poolChoose[index] = value;
    }
}
