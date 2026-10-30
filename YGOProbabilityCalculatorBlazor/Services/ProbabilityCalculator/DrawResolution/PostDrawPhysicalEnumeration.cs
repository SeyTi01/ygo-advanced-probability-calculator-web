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
        CalculationResultRows resultRows = new(combos, groups, totalOnly);
        BigInteger[] wins = new BigInteger[resultRows.Count];
        TerminalAccumulator? accumulator = cards.Any(card => card.IsDangerReveal) && ObservePhysicalOutcome is null
            ? new(this, budget, factorial, resultRows, totalOnly) : null;
        BigInteger denominator = resolver.Denominator(factorial, handSize);
        CheckStorage(wins.Length, wins.Length * (WorkBudget.IntegerCells(denominator) + 1), "physical results");
        BoundedSearchHandCounter? bounded = cards.Any(card => card.EffectPlan.RequiresSearchResolution)
            ? new(cards, combos, budget, priority) : null;
        PhysicalSearchHandCounter? counter = bounded is null ? new(cards, combos, budget, memoize: cards.Any(card => card.IsDangerReveal)) : null;
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
        bool frontier = cards.Any(card => card.IsDangerReveal) || method == PhysicalEnumerationMethod.Frontier || (method == PhysicalEnumerationMethod.Auto
            && n <= 8 && handSize <= 5 && activations <= 4 && observations <= 12);
        Route = frontier ? SolverRoute.SearchPhysicalFrontier : SolverRoute.SearchPhysicalEnumeration;
        if (StreamDanger && cards.Any(card => card.IsDangerReveal)
            && (method == PhysicalEnumerationMethod.Streaming || method != PhysicalEnumerationMethod.Frontier && PreferStreamedDanger(cards))
            && ObservePhysicalOutcome is null && ObserveTerminal is null) {
            wins = resolver.CountStreamedDanger(handSize, copies, factorial, resultRows.Count,
                (state, remaining, weight, destination) => CountTerminalInto(state, remaining, weight, destination, sums: accumulator));
        }
        else if (!layout.HasSearchQueue && cards.All(card => !card.HasDrawEffect)) {
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
        else if (!layout.HasSearchQueue
            && method is PhysicalEnumerationMethod.BatchStreaming or PhysicalEnumerationMethod.SingleSource or PhysicalEnumerationMethod.Auto) {
            resolver.EnumerateBatches(handSize, copies, factorial, CountTerminal);
        }
        else {
            resolver.Enumerate(handSize, copies, CountTerminal);
        }
        accumulator?.Flush(wins);
        ExactProbability[] result = wins.Select(win => new ExactProbability(win, denominator)).ToArray();

        return resultRows.ToResult(result);

        void CountTerminal(int[] state, int remaining, BigInteger weight) => CountTerminalInto(state, remaining, weight, wins, sums: accumulator);

        void CountTerminalInto(int[] state, int remaining, BigInteger weight, BigInteger[] destination,
            TerminalAccumulator? sums = null) {
            using IDisposable? terminalMeasurement = budget.SearchDiagnostics?.Measure("physical terminal resolution");
            budget.SearchDiagnostics?.Add("physical visible terminals");
            int hidden = layout.HasHiddenCount ? state[layout.HiddenCount] : 0;
            // Conditional on this complete VISIBLE trace, all unobserved labels
            // are exchangeable. The policy inspected only hand identities and
            // scalar capacity. Hidden positions never affect a decision by identity.
            // For a hidden vector l: product C(r_i,l_i) * H! * (R-H)!
            // counts its labeled suffixes. This is joint, not independent losses.
            // Removed/consumed observed cards are already absent from state[0..n].
            BigInteger suffixWeight = weight;
            if (sums is not null) {
                budget.Spend((WorkBudget.IntegerCells(weight) + 1) * (WorkBudget.IntegerCells(factorial[hidden]) + 1));
                suffixWeight *= factorial[hidden];
            }
            else {
                budget.Spend(WorkBudget.IntegerCells(weight) * (WorkBudget.IntegerCells(factorial[hidden])
                    + WorkBudget.IntegerCells(factorial[remaining - hidden]) + 2));
                suffixWeight *= factorial[hidden] * factorial[remaining - hidden];
            }
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
                if (sums is not null) {
                    sums.Add(success, remaining - hidden, mass, destination);
                    return;
                }
                if (success.Any(value => value)) {
                    destination[0] += mass;
                }
                if (!totalOnly) {
                    for (int combo = 0; combo < combos.Count; combo++) {
                        budget.Spend(WorkBudget.IntegerCells(mass) + 1);
                        if (success[combo]) {
                            destination[resultRows.ComboRow(combo)] += mass;
                        }
                    }
                    for (int group = 0; group < (groups?.Count ?? 0); group++) {
                        budget.Spend(combos.Count + WorkBudget.IntegerCells(mass));
                        if (resultRows.GroupMatches(group, success)) {
                            destination[resultRows.GroupRow(group)] += mass;
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
