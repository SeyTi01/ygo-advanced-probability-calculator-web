// Research only: included explicitly by Research.targets, never by normal builds.
using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Counting;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.DrawResolution;

internal sealed partial class PostDrawCalculation {
    internal sealed record BeamResult(BigInteger[] Wins, BigInteger Resolved, BigInteger Denominator,
        long Expanded, long Evicted, string Stop, long FinalizationWork);

    internal BeamResult ResearchBeam(List<Combo> combos, IReadOnlyList<ComboGroup> groups, int capacity) {
        if (deck.Any(card => card.PreDrawDeckRemovalCount > 0 || card.PostDrawDeckMillCount > 0
            || card.EffectPlan.RequiresSearchResolution)) {
            throw new NotSupportedException("Research beam excludes hidden losses and bounded search plans.");
        }
        List<Card> cards = ReduceSearchDeck(combos, out _, merge: false);
        cards = CoalescePhysicalSearchDeck(cards, combos);
        OrderedDrawPreparation preparation = PrepareOrderedDraw(cards, combos);
        (bool[]?[] removal, bool[]?[]? cost) = CompileHandOperations(cards);
        OrderedResolver resolver = new(this, budget, cards, preparation.Layout, preparation.CustomPriority,
            preparation.Ranks, preparation.Relevant, removal, cost, new SearchKeepPolicy(cards, preparation.Relevant, budget));
        PhysicalSearchHandCounter counter = new(cards, combos, budget, memoize: true);
        CalculationResultRows rows = new(combos, groups);
        return resolver.Beam(handSize, preparation.Copies, preparation.Factorial, rows, counter, capacity);
    }

    private sealed partial class OrderedResolver {
        private sealed record BeamEntry(State State, BigInteger Weight, long Serial);

        internal BeamResult Beam(int handSize, int copies, BigInteger[] factorial,
            CalculationResultRows rows, PhysicalSearchHandCounter counter, int capacity) {
            BigInteger scale = RandomScale(copies, handSize);
            BigInteger denominator = Denominator(factorial, handSize);
            BigInteger[] wins = new BigInteger[rows.Count];
            BigInteger resolved = 0;
            long expanded = 0;
            long evicted = 0;
            long serial = 0;
            string stop = "exhausted retained frontier";
            int n = cards.Count;
            int[] initial = new int[layout.QueueStart];
            for (int row = 0; row < n; row++) {
                initial[row] = cards[row].Copies;
            }
            initial[layout.DrawsLeft] = handSize;
            Dictionary<State, BeamEntry> current = new();
            State first = new(DrawFrontierPacking.Pack(initial));
            current.Add(first, new(first, scale, serial++));
            long currentCells = first.Values.Length + WorkBudget.IntegerCells(scale) + 4;
            owner.CheckStorage(1, currentCells, "beam current");
            try {
                for (int remaining = copies; remaining >= 0 && current.Count > 0; remaining--) {
                    Dictionary<State, BeamEntry> next = new();
                    SortedSet<BeamEntry> ranking = new(Comparer<BeamEntry>.Create((a, b) => {
                        int comparison = a.Weight.CompareTo(b.Weight);
                        return comparison != 0 ? comparison : a.Serial.CompareTo(b.Serial);
                    }));
                    long nextCells = 0;
                    foreach (BeamEntry entry in current.Values) {
                        budget.Spend(entry.State.Values[0] + 1L);
                        int[] values = DrawFrontierPacking.Unpack(entry.State.Values);
                        if (values[layout.DrawsLeft] == 0) {
                            Terminal(values, remaining, entry.Weight);
                            continue;
                        }
                        expanded++;
                        for (int row = 0; row < n; row++) {
                            budget.Spend(1);
                            if (values[row] == 0) {
                                continue;
                            }
                            bool candidate = cards[row].HasDrawEffect && TimingAllows(values, row) && !Used(values, row);
                            budget.Spend(values.Length + WorkBudget.IntegerCells(entry.Weight) + 1L);
                            int[] drawn = new int[values.Length + (candidate ? 1 : 0)];
                            values.CopyTo(drawn, 0);
                            drawn[row]--;
                            drawn[layout.HandStart + row]++;
                            drawn[layout.DrawsLeft]--;
                            if (candidate) {
                                drawn[^1] = row;
                            }
                            SettleDanger(drawn, remaining - 1, entry.Weight * values[row], 0, 0, Accept);
                        }
                    }
                    current = next;
                    currentCells = nextCells;

                    void Accept(int[] values, BigInteger weight, int depth, long cells) {
                        if (values[layout.DrawsLeft] == 0) {
                            Terminal(values, remaining - 1, weight);
                            return;
                        }
                        budget.Spend(3L * values.Length + WorkBudget.IntegerCells(weight) + 1);
                        State key = new(DrawFrontierPacking.Pack(values));
                        bool found = next.TryGetValue(key, out BeamEntry? previous);
                        // Charge comparisons and both retained indexes before mutating them.
                        long levels = 1 + (long)Math.Log2(next.Count + 2);
                        budget.Spend(levels * (2L * WorkBudget.IntegerCells(weight) + 8)
                            + (previous is null ? 0 : WorkBudget.IntegerCells(previous.Weight)));
                        BigInteger combined = weight + (previous?.Weight ?? BigInteger.Zero);
                        if (found) {
                            ranking.Remove(previous!);
                            next.Remove(key);
                            nextCells -= EntryCells(previous!);
                        }
                        BeamEntry added = new(key, combined, previous?.Serial ?? serial++);
                        long addedCells = EntryCells(added);
                        while (next.Count > 0 && (next.Count >= capacity || next.Count >= budget.StoragePolicy.EntryLimit
                            || nextCells + addedCells > budget.StoragePolicy.CellLimit)) {
                            BeamEntry smallest = ranking.Min!;
                            if (smallest.Weight >= combined) {
                                evicted++;
                                return;
                            }
                            budget.Spend(levels * (2L * WorkBudget.IntegerCells(smallest.Weight) + 8) + key.Values.Length);
                            ranking.Remove(smallest);
                            next.Remove(smallest.State);
                            nextCells -= EntryCells(smallest);
                            evicted++;
                        }
                        owner.CheckStorage(next.Count + 1L, nextCells + addedCells, "beam next including priority index");
                        next.Add(key, added);
                        ranking.Add(added);
                        nextCells += addedCells;
                        owner.PeakConcurrentCells = Math.Max(owner.PeakConcurrentCells, currentCells + nextCells + cells);
                    }
                }
            }
            catch (ProbabilityCalculationLimitException error) {
                // Every committed terminal is disjoint. Stop the entire traversal;
                // incomplete branches stay in D-resolved, never retry their parent.
                stop = error.Reason.ToString();
            }
            // Research reports a separate conservative finalization charge; this
            // prototype does NOT yet reserve it in the production Work contract.
            long finalization = (wins.Length + 2L) * (WorkBudget.IntegerCells(denominator) + 2) * 4;
            return new(wins, resolved, denominator, expanded, evicted, stop, finalization);

            long EntryCells(BeamEntry entry) => entry.State.Values.Length + WorkBudget.IntegerCells(entry.Weight) + 8;

            void Terminal(int[] values, int remaining, BigInteger weight) {
                budget.Spend(n + (WorkBudget.IntegerCells(weight) + 1) * (WorkBudget.IntegerCells(factorial[remaining]) + 1));
                int[] available = values.AsSpan(0, n).ToArray();
                bool[] success = counter.Evaluate(values.AsSpan(layout.HandStart, n), available);
                bool[] matches = new bool[rows.Count];
                matches[0] = success.Any(value => value);
                for (int combo = 0; combo < rows.ComboCount; combo++) {
                    matches[rows.ComboRow(combo)] = success[combo];
                }
                for (int group = 0; group < rows.GroupCount; group++) {
                    matches[rows.GroupRow(group)] = rows.GroupMatches(group, success);
                }
                BigInteger mass = weight * factorial[remaining];
                budget.Spend((rows.Count + 2L) * (WorkBudget.IntegerCells(denominator) + 2) + (long)rows.GroupCount * rows.ComboCount);
                // No throwing budget calls after the first accumulator mutation.
                for (int row = 0; row < wins.Length; row++) {
                    if (matches[row]) {
                        wins[row] += mass;
                    }
                }
                resolved += mass;
            }
        }
    }
}
