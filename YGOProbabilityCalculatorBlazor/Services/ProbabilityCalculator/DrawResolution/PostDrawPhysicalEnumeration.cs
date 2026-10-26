using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Certification;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Compilation;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Counting;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Search;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.DrawResolution;

internal enum PhysicalEnumerationMethod { Auto, Streaming, Frontier, BatchStreaming, SingleSource }

internal sealed partial class PostDrawCalculation {
    private sealed partial class OrderedResolver {
        private readonly struct PhysicalTerminal : ITerminalHand {
            public static int[] Create(int[] state, OrderedDrawStateLayout layout, int remaining) => (int[])state.Clone();
        }

        internal void EnumerateFrontier(int handSize, int copies, BigInteger[] factorial, Action<int[], int, BigInteger> terminal) {
            bool packed = owner.CompactPhysicalTerminals && copies <= byte.MaxValue && layout.RowCount <= byte.MaxValue;
            Dictionary<State, BigInteger> states = Resolve<PhysicalTerminal>(handSize, copies, factorial, DefaultCompactFrontierThreshold, packed);
            foreach ((State state, BigInteger mass) in states) {
                budget.Spend(layout.RowCount + WorkBudget.IntegerCells(mass));
                int[] values = state.Values;
                if (packed) {
                    budget.Spend(values[0]);
                    values = DrawFrontierPacking.Unpack(values);
                }
                int remaining = 0;
                for (int row = 0; row < layout.RowCount; row++) {
                    remaining += values[row];
                }
                // Resolve already multiplied each terminal prefix by its unseen
                // suffix factorial. Remove that factor before jointly splitting
                // hidden positions; equal explicit states have the same suffix.
                budget.Spend(WorkBudget.IntegerCells(mass) * (WorkBudget.IntegerCells(factorial[remaining]) + 1));
                terminal(values, remaining, mass / factorial[remaining]);
            }
        }

        // Every distinct opening entry word must be visited before any effect can
        // fail. Count these words by interleaving each entry's indistinct copies.
        // This is a LOWER work bound, so rejection cannot discard a request that
        // would fit. It charges no predicted work; actual work is charged below.
        private void PreflightOpeningPrefixes(int handSize) {
            budget.CheckStorage(handSize + 1L, 2L * (handSize + 1));
            BigInteger[] words = new BigInteger[handSize + 1];
            words[0] = 1;
            foreach (Card card in cards) {
                for (int length = handSize; length > 0; length--) {
                    BigInteger choose = BigInteger.One;
                    for (int count = 1; count <= Math.Min(card.Copies, length); count++) {
                        budget.Spend(WorkBudget.IntegerCells(words[length - count]) + WorkBudget.IntegerCells(words[length])
                            + 3 * WorkBudget.IntegerCells(choose) + 1);
                        choose = choose * (length - count + 1) / count;
                        words[length] += words[length - count] * choose;
                        budget.Spend(words.Length);
                        budget.CheckStorage(words.Length, words.Sum(WorkBudget.IntegerCells));
                        budget.EnsureAvailable(words[length] > long.MaxValue ? long.MaxValue : (long)words[length]);
                    }
                }
            }
        }

        // Stream entry-ID prefixes, multiplying by the number of remaining labels.
        // Only the active DFS path is retained. Settle is the SAME policy transition
        // as the ordered frontier; no terminal pooling or identity averaging occurs.
        internal void Enumerate(int handSize, int copies, Action<int[], int, BigInteger> terminal) {
            PreflightOpeningPrefixes(handSize);
            int[] initial = new int[layout.QueueStart];
            for (int i = 0; i < cards.Count; i++) {
                initial[i] = cards[i].Copies;
            }
            initial[layout.DrawsLeft] = handSize;
            Stack<(int[] Values, int Remaining, BigInteger Weight, int Next)> stack = new();
            stack.Push((initial, copies, BigInteger.One, 0));
            long cells = initial.Length + 5L;
            while (stack.Count > 0) {
                budget.Spend(1);
                (int[] values, int remaining, BigInteger weight, int next) = stack.Pop();
                cells -= values.Length + WorkBudget.IntegerCells(weight) + 4;
                if (values[layout.DrawsLeft] == 0) {
                    terminal(values, remaining, weight);
                    continue;
                }
                for (int i = next; i < cards.Count; i++) {
                    budget.Spend(1);
                    if (values[i] == 0) {
                        continue;
                    }
                    Push(values, remaining, weight, i + 1);
                    bool candidate = cards[i].DrawCount is not null && TimingAllows(values, i) && !Used(values, i)
                        && (cards[i].MaximumActivations != 1 || !values.AsSpan(layout.QueueStart).Contains(i));
                    budget.Spend(values.Length + WorkBudget.IntegerCells(weight) + 1L);
                    int[] drawn = new int[values.Length + (candidate ? 1 : 0)];
                    values.CopyTo(drawn, 0);
                    drawn[i]--;
                    drawn[layout.HandStart + i]++;
                    if (layout.HasSearchQueue && cards[i].Search is not null) {
                        layout.EncounterSearch(drawn, i);
                    }
                    drawn[layout.DrawsLeft]--;
                    if (candidate) {
                        drawn[^1] = i;
                    }
                    if (Settle(ref drawn, remaining - 1) != ResolutionOutcome.RemovalUnavailable) {
                        Push(drawn, remaining - 1, weight * values[i], 0);
                    }
                    break;
                }
            }

            void Push(int[] values, int remaining, BigInteger weight, int next) {
                cells += values.Length + WorkBudget.IntegerCells(weight) + 4;
                owner.CheckStorage(stack.Count + 1L, cells, "physical enumeration stack");
                stack.Push((values, remaining, weight, next));
            }
        }
    }

    private ExactCalculationResult CountPhysicalSearch(
        List<Card> cards,
        OrderedDrawStateLayout layout,
        OrderedResolver resolver,
        BigInteger[] factorial,
        List<Combo> combos,
        IReadOnlyList<ComboGroup>? groups,
        bool totalOnly,
        PhysicalEnumerationMethod method
    ) {
        int n = cards.Count;
        int copies = cards.Sum(card => card.Copies);
        BigInteger[] wins = new BigInteger[1 + (totalOnly ? 0 : combos.Count + (groups?.Count ?? 0))];
        CheckStorage(wins.Length, wins.Length * (WorkBudget.IntegerCells(factorial[copies]) + 1), "physical results");
        BoundedSearchHandCounter? bounded = cards.Any(card => card.EffectPlan.RequiresSearchResolution || (card.Search is not null && card.MinimumHandSize > 0))
            ? new(cards, combos, budget, priority) : null;
        PhysicalSearchHandCounter? counter = bounded is null ? new(cards, combos, budget) : null;
        PhysicalSearchLossCounter? lossCounter = bounded is null && ObservePhysicalOutcome is null
            && cards.Any(card => card.PreDrawDeckRemovalCount > 0 || card.PostDrawDeckMillCount > 0)
            ? new(cards, budget) : null;
        int[] available = new int[n];
        long activations = cards.Where(card => card.DrawCount is not null).Sum(card => Math.Min(card.MaximumActivations, card.Copies));
        long observations = handSize + cards.Sum(card => (long)(card.DrawCount ?? 0) * (Math.Min(card.MaximumActivations, card.Copies)));
        // Measured small-frontier domain: avoid repeating ordered prefixes when
        // only a few source/identity rows can distinguish them. Outside this
        // conservative structural domain, stream rather than speculate/ retry.
        // This is a selection estimate, not a promise to fit: both routes charge
        // actual work and storage and report typed failure at unchanged limits.
        bool frontier = method == PhysicalEnumerationMethod.Frontier || (method == PhysicalEnumerationMethod.Auto
            && n <= 8 && handSize <= 5 && activations <= 4 && observations <= 12);
        Route = frontier ? SolverRoute.SearchPhysicalFrontier : SolverRoute.SearchPhysicalEnumeration;
        if (!layout.HasSearchQueue && cards.All(card => card.DrawCount is null)) {
            // With no draw phase the order of the opening hand is unobservable.
            // Enumerate count vectors with physical binomial mass, not permutations.
            Route = SolverRoute.SearchOnly;
            int[] state = new int[layout.QueueStart];
            VisitOpening(0, handSize, BigInteger.One);

            void VisitOpening(int row, int left, BigInteger mass) {
                budget.Spend(1 + WorkBudget.IntegerCells(mass));
                CheckStorage(n + 1L, (n + 1L) * (WorkBudget.IntegerCells(factorial[copies]) + 3), "opening counts");
                if (row == n) {
                    if (left == 0) {
                        CountTerminal(state, copies - handSize, mass * factorial[handSize]);
                    }
                    return;
                }
                BigInteger choose = BigInteger.One;
                for (int held = 0; held <= Math.Min(left, cards[row].Copies); held++) {
                    state[layout.HandStart + row] = held;
                    state[row] = cards[row].Copies - held;
                    VisitOpening(row + 1, left - held, mass * choose);
                    choose = choose * (cards[row].Copies - held) / (held + 1);
                }
            }
        }
        else if (frontier) {
            resolver.EnumerateFrontier(handSize, copies, factorial, CountTerminal);
        }
        else if (!layout.HasSearchQueue && (method == PhysicalEnumerationMethod.Auto || method == PhysicalEnumerationMethod.SingleSource)
            && resolver.TryEnumerateSingleSource(handSize, copies, factorial, CountTerminal)) {
            // The source-conditioned quotient has already counted both opening
            // partitions through the same physical terminal/search boundary.
        }
        else if (!layout.HasSearchQueue && (method is PhysicalEnumerationMethod.BatchStreaming or PhysicalEnumerationMethod.SingleSource
            || (method == PhysicalEnumerationMethod.Auto && AggregatePhysicalBatches))) {
            resolver.EnumerateBatches(handSize, copies, factorial, CountTerminal);
        }
        else {
            resolver.Enumerate(handSize, copies, CountTerminal);
        }
        ExactProbability[] result = wins.Select(win => new ExactProbability(win, factorial[copies])).ToArray();

        return new(result[0], totalOnly ? [] : result.Skip(1).Take(combos.Count).ToArray(),
            totalOnly ? [] : result.Skip(1 + combos.Count).ToArray());

        void CountTerminal(int[] state, int remaining, BigInteger weight) {
            using IDisposable? terminalMeasurement = budget.SearchDiagnostics?.Measure("physical terminal resolution");
            budget.SearchDiagnostics?.Add("physical visible terminals");
            int hidden = layout.HasHiddenCount ? state[layout.HiddenCount] : 0;
            // Conditional on this complete VISIBLE trace, all unobserved labels
            // are exchangeable. The policy inspected only hand identities and
            // scalar capacity. Hidden positions never affect a decision by identity.
            // For a hidden vector l: product C(r_i,l_i) * H! * (R-H)!
            // counts its labeled suffixes. This is joint, not independent losses.
            // Removed/consumed observed cards are already absent from state[0..n].
            budget.Spend(WorkBudget.IntegerCells(weight) * (WorkBudget.IntegerCells(factorial[hidden])
                + WorkBudget.IntegerCells(factorial[remaining - hidden]) + 2));
            BigInteger suffixWeight = weight * factorial[hidden] * factorial[remaining - hidden];
            if (hidden == 0) {
                budget.Spend(n);
                state.AsSpan(0, n).CopyTo(available);
                Record(suffixWeight);
            }
            else if (lossCounter is not null) {
                lossCounter.Enumerate(state.AsSpan(layout.HandStart, n), state.AsSpan(0, n), hidden, (capacity, mass) => {
                    budget.Spend((WorkBudget.IntegerCells(suffixWeight) + 1) * (WorkBudget.IntegerCells(mass) + 1));
                    Record(suffixWeight * mass, capacity);
                });
            }
            else {
                VisitLoss(0, hidden, remaining, suffixWeight);
            }

            void Record(BigInteger mass, int[]? capacity = null) {
                using IDisposable? searchMeasurement = budget.SearchDiagnostics?.Measure("physical hand search");
                budget.SearchDiagnostics?.Add("physical search evaluations");
                ObservePhysicalOutcome?.Invoke(cards, (int[])state.Clone(), (int[])available.Clone(), mass);
                bool[] success = bounded is null ? counter!.Evaluate(state.AsSpan(layout.HandStart, n), capacity ?? available)
                    : bounded.Evaluate(state.AsSpan(layout.HandStart, n), available,
                        layout.HasSearchQueue ? state.AsSpan(layout.SearchQueueStart, layout.SearchQueueCapacity).ToArray()
                            .Where(value => value > 0).Select(value => value - 1).ToArray() : null);
                if (success.Any(value => value)) {
                    wins[0] += mass;
                }
                if (!totalOnly) {
                    for (int combo = 0; combo < combos.Count; combo++) {
                        budget.Spend(WorkBudget.IntegerCells(mass) + 1);
                        if (success[combo]) {
                            wins[1 + combo] += mass;
                        }
                    }
                    for (int group = 0; group < (groups?.Count ?? 0); group++) {
                        budget.Spend(combos.Count + WorkBudget.IntegerCells(mass));
                        bool matches = false;
                        for (int combo = 0; combo < combos.Count; combo++) {
                            if (success[combo] && combos[combo].GroupId == groups![group].Id) {
                                matches = true;
                                break;
                            }
                        }
                        if (matches) {
                            wins[1 + combos.Count + group] += mass;
                        }
                    }
                }
            }

            void VisitLoss(int row, int left, int pool, BigInteger mass) {
                budget.SearchDiagnostics?.Add("physical hidden allocation nodes");
                budget.Spend(1 + WorkBudget.IntegerCells(mass));
                CheckStorage(n + 1L, (n + 1L) * (WorkBudget.IntegerCells(factorial[copies]) + 5), "hidden loss stack");
                if (row == n) {
                    if (left != 0) {
                        return;
                    }
                    Record(mass);
                    return;
                }
                int count = state[row];
                int minimum = Math.Max(0, left - (pool - count));
                int maximum = Math.Min(count, left);
                BigInteger choose = BigInteger.One;
                for (int loss = 0; loss <= maximum; loss++) {
                    budget.Spend(WorkBudget.IntegerCells(choose) * (WorkBudget.IntegerCells(mass) + 3) + 1);
                    if (loss >= minimum) {
                        available[row] = count - loss;
                        VisitLoss(row + 1, left - loss, pool - count, mass * choose);
                    }
                    choose = choose * (count - loss) / (loss + 1);
                }
            }
        }
    }
}

// Enumerate optional single-pass search assignments for one fully known hand
// and physical Deck. A Boolean per combo merges all witnesses before weighting.
// Allowances come only from the original hand; fetched searchers stay inert.
internal sealed class PhysicalSearchHandCounter {
    private readonly List<Card> _cards;
    private readonly WorkBudget _budget;
    private readonly List<Combo> _combos;
    private readonly (int Source, int[] Targets)[] _sources;
    private readonly record struct HandBound(int[] Rows, int Minimum, int Maximum);
    private int _compiledSize = -1;
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
        if (size != _compiledSize) {
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
            _compiledSize = size;
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
