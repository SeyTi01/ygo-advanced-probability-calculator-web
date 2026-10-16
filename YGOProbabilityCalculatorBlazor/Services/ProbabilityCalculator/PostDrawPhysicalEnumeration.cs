using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

internal enum PhysicalEnumerationMethod { Auto, Streaming, Frontier }

internal sealed partial class PostDrawCalculation {
    private sealed partial class OrderedResolver {
        private readonly struct PhysicalTerminal : ITerminalHand {
            public static int[] Create(int[] state, OrderedDrawStateLayout layout, int remaining) => (int[])state.Clone();
        }

        internal void EnumerateFrontier(int handSize, int copies, BigInteger[] factorial, Action<int[], int, BigInteger> terminal) {
            Dictionary<State, BigInteger> states = Resolve<PhysicalTerminal>(handSize, copies, factorial, DefaultCompactFrontierThreshold);
            foreach ((State state, BigInteger mass) in states) {
                budget.Spend(layout.RowCount + WorkBudget.IntegerCells(mass));
                int remaining = 0;
                for (int row = 0; row < layout.RowCount; row++) {
                    remaining += state.Values[row];
                }
                // Resolve already multiplied each terminal prefix by its unseen
                // suffix factorial. Remove that factor before jointly splitting
                // hidden positions; equal explicit states have the same suffix.
                budget.Spend(WorkBudget.IntegerCells(mass) * (WorkBudget.IntegerCells(factorial[remaining]) + 1));
                terminal(state.Values, remaining, mass / factorial[remaining]);
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
            ? new(cards, combos, budget) : null;
        PhysicalSearchHandCounter? counter = bounded is null ? new(cards, combos, budget) : null;
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
        if (cards.All(card => card.DrawCount is null)) {
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
        else {
            resolver.Enumerate(handSize, copies, CountTerminal);
        }
        ExactProbability[] result = wins.Select(win => new ExactProbability(win, factorial[copies])).ToArray();

        return new(result[0], totalOnly ? [] : result.Skip(1).Take(combos.Count).ToArray(),
            totalOnly ? [] : result.Skip(1 + combos.Count).ToArray());

        void CountTerminal(int[] state, int remaining, BigInteger weight) {
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
            else {
                VisitLoss(0, hidden, remaining, suffixWeight);
            }

            void Record(BigInteger mass) {
                bool[] success = bounded is null ? counter!.Evaluate(state.AsSpan(layout.HandStart, n), available)
                    : bounded.Evaluate(state.AsSpan(layout.HandStart, n), available);
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
                        if (success.Where((_, index) => combos[index].GroupId == groups![group].Id).Any(value => value)) {
                            wins[1 + combos.Count + group] += mass;
                        }
                    }
                }
            }

            void VisitLoss(int row, int left, int pool, BigInteger mass) {
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
    private int _compiledSize = -1;
    private List<List<CompiledEvent?>> _events = [];

    internal PhysicalSearchHandCounter(List<Card> cards, List<Combo> combos, WorkBudget budget) {
        _cards = cards;
        _combos = combos;
        _budget = budget;
        budget.Spend(cards.Sum(card => card.Categories.Count + 1L) * cards.Sum(card => (card.Search?.Selector.EvaluationSize ?? 0) + 1L));
        budget.CheckStorage(cards.Count, (long)cards.Count * cards.Count + 4L * cards.Count);
        _sources = cards.Select((card, row) => (Card: card, Row: row)).Where(entry => entry.Card.Search is not null)
            .Select(entry => (entry.Row, cards.Select((card, row) => (Card: card, Row: row))
                .Where(target => target.Row != entry.Row && entry.Card.Search!.Matches(target.Card)).Select(target => target.Row).ToArray())).ToArray();
    }

    internal bool[] Evaluate(ReadOnlySpan<int> original, int[] available) {
        _budget.Spend(original.Length + _combos.Count + 1L);
        int[] hand = original.ToArray();
        int size = hand.Sum();
        if (size != _compiledSize) {
            ComboEventCompiler compiler = new(_cards, size, _budget);
            _events = _combos.Select(compiler.CompileCombo).ToList();
            _compiledSize = size;
        }
        int[] allowance = _sources.Select(source => Math.Min(hand[source.Source], _cards[source.Source].MaximumActivations)).ToArray();
        bool[] success = new bool[_combos.Count];
        Stack<(int Kind, int Index, int Start, int Left)> stack = new();
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
                for (int combo = 0; combo < success.Length; combo++) {
                    _budget.Spend(1);
                    if (success[combo]) {
                        continue;
                    }
                    foreach (CompiledEvent? route in _events[combo]) {
                        _budget.Spend(1);
                        if (route is not null && route.Constraints.All(Matches)) {
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

        bool Matches(CountBound bound) {
            _budget.Spend(_cards.Count * (WorkBudget.IntegerCells(bound.EligibleRows) + 1));
            int count = 0;
            for (int row = 0; row < hand.Length; row++) {
                if ((bound.EligibleRows & (BigInteger.One << row)) != 0) {
                    count += hand[row];
                }
            }
            return count >= bound.MinCount && count <= bound.MaxCount;
        }
    }
}
