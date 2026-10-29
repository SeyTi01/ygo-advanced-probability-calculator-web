using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Compilation;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Counting;

// Enumerate optional single-pass search assignments for one fully known hand
// and physical Deck. A Boolean per combo merges all witnesses before weighting.
// Allowances come only from the original hand; fetched searchers stay inert.
internal sealed class PhysicalSearchHandCounter {
    private readonly List<Card> _cards;
    private readonly WorkBudget _budget;
    private readonly List<Combo> _combos;
    private readonly (int Source, int[] Targets)[] _sources;
    private readonly record struct HandBound(int[] Rows, int Minimum, int Maximum);
    private int _compiledHandSizeLimit = -1;
    private List<List<HandBound[]>> _events = [];
    private readonly int[] _hand;
    private readonly int[] _allowance;
    private readonly bool[] _success;
    private readonly Stack<(int Kind, int Index, int Start, int Left)> _stack = new();

    internal PhysicalSearchHandCounter(List<Card> cards, List<Combo> combos, WorkBudget budget) {
        _cards = cards;
        _combos = combos;
        _budget = budget;
        budget.Spend(cards.Sum(card => card.Categories.Count + 1L) * cards.Sum(card => (card.Search?.Selector.EvaluationSize ?? 0) + 1L));
        budget.CheckStorage(cards.Count, (long)cards.Count * cards.Count + 4L * cards.Count);
        _sources = cards.Select((card, row) => (Card: card, Row: row)).Where(entry => entry.Card.Search is not null)
            .Select(entry => (entry.Row, cards.Select((card, row) => (Card: card, Row: row))
                .Where(target => target.Row != entry.Row && entry.Card.Search!.Matches(target.Card)).Select(target => target.Row).ToArray())).ToArray();
        budget.CheckStorage(cards.Count + _sources.Length + combos.Count,
            cards.Count + (long)_sources.Length + combos.Count);
        _hand = new int[cards.Count];
        _allowance = new int[_sources.Length];
        _success = new bool[combos.Count];
    }

    // The caller consumes this borrowed row buffer before evaluating the next
    // terminal. No input, result, or mutable state is retained across requests.
    internal bool[] Evaluate(ReadOnlySpan<int> original, int[] available) {
        _budget.Spend(original.Length + _combos.Count + 1L);
        int[] hand = _hand;
        original.CopyTo(hand);
        int size = hand.Sum();
        // A compiled hand-size bound H is valid for every actual size h <= H.
        // Each witness replaces one source with one target, so its size stays h.
        // Counts already lie in [0,h]: clamping fixed maxima at H and using H
        // for unrestricted maxima cannot change their truth. Hall union minima
        // still enforce distinct copies, including total demand greater than h.
        // A route rejected as impossible at H is also impossible below H.
        // Retain one immutable compilation; only a larger hand needs rebuilding.
        if (size > _compiledHandSizeLimit) {
            _budget.SearchDiagnostics?.Add("physical event compilations");
            ComboEventCompiler compiler = new(_cards, size, _budget);
            List<List<HandBound[]>> events = [];
            long cells = _combos.Count;
            long entries = 0;
            foreach (Combo combo in _combos) {
                List<HandBound[]> routes = [];
                foreach (CompiledEvent? compiled in compiler.CompileCombo(combo)) {
                    if (compiled is null) {
                        continue;
                    }
                    entries++;
                    cells += compiled.Constraints.Length + 1L;
                    _budget.CheckStorage(entries, cells);
                    HandBound[] bounds = new HandBound[compiled.Constraints.Length];
                    for (int index = 0; index < bounds.Length; index++) {
                        CountBound bound = compiled.Constraints[index];
                        _budget.Spend(2L * _cards.Count * (WorkBudget.IntegerCells(bound.EligibleRows) + 1));
                        int count = 0;
                        for (int row = 0; row < _cards.Count; row++) {
                            if ((bound.EligibleRows & (BigInteger.One << row)) != 0) {
                                count++;
                            }
                        }
                        cells += count + 3L;
                        _budget.CheckStorage(entries, cells);
                        int[] rows = new int[count];
                        int next = 0;
                        for (int row = 0; row < _cards.Count; row++) {
                            if ((bound.EligibleRows & (BigInteger.One << row)) != 0) {
                                rows[next++] = row;
                            }
                        }
                        bounds[index] = new(rows, bound.MinCount, bound.MaxCount);
                    }
                    routes.Add(bounds);
                }
                events.Add(routes);
            }
            _events = events;
            _compiledHandSizeLimit = size;
        }
        int[] allowance = _allowance;
        for (int index = 0; index < _sources.Length; index++) {
            int source = _sources[index].Source;
            allowance[index] = Math.Min(hand[source], _cards[source].MaximumActivations);
        }
        bool[] success = _success;
        Array.Clear(success);
        Stack<(int Kind, int Index, int Start, int Left)> stack = _stack;
        stack.Clear();
        Push(0, 0, 0, 0);
        while (stack.Count > 0) {
            _budget.Spend(1);
            (int kind, int index, int start, int left) = stack.Pop();
            if (kind == 2) {
                int target = _sources[index].Targets[start];
                hand[target]--;
                hand[_sources[index].Source]++;
                available[target]++;
                continue;
            }
            if (kind == 1) {
                if (left == 0) {
                    continue;
                }
                (int source, int[] targets) = _sources[index];
                for (int next = start; next < targets.Length; next++) {
                    _budget.Spend(1);
                    int target = targets[next];
                    if (available[target] == 0) {
                        continue;
                    }
                    // Continuations enumerate nondecreasing target multisets,
                    // then restore the mutation before trying the next sibling.
                    Push(1, index, next + 1, left);
                    Push(2, index, next, 0);
                    Push(1, index, next, left - 1);
                    Push(0, index + 1, 0, 0);
                    available[target]--;
                    hand[source]--;
                    hand[target]++;
                    break;
                }
                continue;
            }
            if (index == _sources.Length) {
                _budget.SearchDiagnostics?.Add("physical search witnesses");
                for (int combo = 0; combo < success.Length; combo++) {
                    _budget.Spend(1);
                    if (success[combo]) {
                        continue;
                    }
                    foreach (HandBound[] route in _events[combo]) {
                        _budget.Spend(1);
                        bool matches = true;
                        foreach (HandBound bound in route) {
                            if (!Matches(bound)) {
                                matches = false;
                                break;
                            }
                        }
                        if (matches) {
                            success[combo] = true;
                            break;
                        }
                    }
                }
                continue;
            }
            Push(1, index, 0, allowance[index]);
            Push(0, index + 1, 0, 0);
        }
        return success;

        void Push(int kind, int index, int start, int left) {
            _budget.Spend(1);
            _budget.CheckStorage(stack.Count + 1L, 4L * (stack.Count + 1) + hand.Length + allowance.Length + success.Length);
            stack.Push((kind, index, start, left));
        }

        bool Matches(HandBound bound) {
            _budget.SearchDiagnostics?.Add("physical predicate evaluations");
            _budget.Spend(bound.Rows.Length + 1L);
            int count = 0;
            foreach (int row in bound.Rows) {
                count += hand[row];
            }
            return count >= bound.Minimum && count <= bound.Maximum;
        }
    }
}
