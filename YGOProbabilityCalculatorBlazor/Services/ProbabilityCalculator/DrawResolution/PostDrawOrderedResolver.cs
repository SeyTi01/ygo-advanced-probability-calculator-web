using System.Numerics;
using System.Runtime.CompilerServices;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.DrawResolution;

internal sealed partial class PostDrawCalculation {
    // Request-scoped policy and transition context. No per-state objects beyond the
    // existing owned vectors; selectors are compiled against this final row mapping.
    private sealed partial class OrderedResolver(
        PostDrawCalculation owner,
        WorkBudget budget,
        List<Card> cards,
        OrderedDrawStateLayout layout,
        bool customPriority,
        int[] ranks,
        bool[] relevant,
        bool[]?[] removalEligibility,
        bool[]?[]? costEligibility,
        SearchKeepPolicy? searchPolicy = null
    ) {
        private enum ResolutionOutcome { Ready, RemovalUnavailable }

        internal Dictionary<State, BigInteger> Resolve(
            int handSize,
            int copies,
            BigInteger[] factorial,
            int compactFrontierThreshold
        ) => Resolve<HandTerminal>(handSize, copies, factorial, compactFrontierThreshold);

        internal Dictionary<State, BigInteger> Resolve<TTerminal>(
            int handSize,
            int copies,
            BigInteger[] factorial,
            int compactFrontierThreshold,
            bool packTerminals = false
        ) where TTerminal : struct, ITerminalHand {
            int n = layout.RowCount;
            int[] initial = new int[layout.QueueStart];
            for (int i = 0; i < n; i++) {
                initial[i] = cards[i].Copies;
            }
            initial[layout.DrawsLeft] = handSize;
            Dictionary<State, BigInteger> current = new() { [new(initial)] = 1 };
            Dictionary<State, BigInteger> terminal = new();
            long currentCells = initial.Length + 1L;
            long terminalCells = 0;
            bool currentPacked = false;
            // Every count/phase is at most copies; source + 1 is at most n.
            // Keep small frontiers and the exchangeable/count-state fast paths unchanged.
            bool canPack = copies <= byte.MaxValue && n <= byte.MaxValue;
            for (int depth = 0; current.Count > 0; depth++) {
                Dictionary<State, BigInteger> next = new();
                long nextCells = 0;
                bool nextPacked = canPack && (currentPacked || currentCells >= compactFrontierThreshold);
                if (nextPacked) {
                    owner.PackedLayers++;
                }
                foreach ((State state, BigInteger weight) in current) {
                    // Initial state is ready; every later state was settled before
                    // insertion. Decode at most this one parent when packed. Only
                    // freshly allocated children may be changed by drawing or Settle.
                    budget.Spend(1);
                    int[] values = state.Values;
                    if (currentPacked) {
                        budget.Spend(values[0]);
                        values = DrawFrontierPacking.Unpack(values);
                    }
                    int remaining = copies - depth;
                    if (values[layout.DrawsLeft] == 0) {
                        budget.Spend(WorkBudget.IntegerCells(weight) * (WorkBudget.IntegerCells(factorial[remaining]) + 1));
                        Add(
                            terminal,
                            Terminal(values, remaining),
                            weight * factorial[remaining],
                            ref terminalCells,
                            "terminal hands"
                        );
                        continue;
                    }
                    for (int i = 0; i < n; i++) {
                        budget.Spend(1);
                        if (values[i] == 0) {
                            continue;
                        }
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
                        // Normalize immediately so batches differing only in discarded
                        // cards or obsolete candidates merge before the next layer.
                        if (Settle(ref drawn, remaining - 1) != ResolutionOutcome.RemovalUnavailable) {
                            if (owner.EagerTerminalAggregation && nextPacked && drawn[layout.DrawsLeft] == 0) {
                                // Settle has completed every pending operation. Nothing can
                                // observe this history again, so do not retain it in next.
                                // For any children merged there, sum(w * c) * r! equals
                                // sum(w * c * r!). Keep the original suffix and terminal
                                // key, including search capacity, for all counting passes.
                                BigInteger prefix = weight * values[i];
                                budget.Spend(WorkBudget.IntegerCells(prefix) * (WorkBudget.IntegerCells(factorial[remaining - 1]) + 1));
                                Add(
                                    terminal,
                                    Terminal(drawn, remaining - 1),
                                    prefix * factorial[remaining - 1],
                                    ref terminalCells,
                                    "terminal hands"
                                );

                                continue;
                            }

                            if (nextPacked) {
                                budget.Spend(drawn.Length);
                                drawn = DrawFrontierPacking.Pack(drawn);
                            }
                            Add(next, new(drawn), weight * values[i], ref nextCells, "draw frontier");
                        }
                    }
                }
                owner.PeakStates = Math.Max(owner.PeakStates, next.Count);
                owner.PeakConcurrentCells = Math.Max(owner.PeakConcurrentCells, currentCells + nextCells + terminalCells);
                owner.ObserveFrontier?.Invoke();
                GC.KeepAlive(current);
                current = next;
                currentCells = nextCells;
                currentPacked = nextPacked;
            }

            if (owner.ObserveTerminal is not null) {
                foreach ((State hand, BigInteger mass) in terminal) {
                    owner.ObserveTerminal(packTerminals ? DrawFrontierPacking.Unpack(hand.Values) : (int[])hand.Values.Clone(), mass);
                }
            }

            return terminal;

            State Terminal(int[] values, int remaining) {
                int[] hand = TTerminal.Create(values, layout, remaining);
                if (packTerminals) {
                    budget.Spend(hand.Length);
                    hand = DrawFrontierPacking.Pack(hand);
                }

                return new(hand);
            }
        }

        private bool Used(int[] values, int entry) => layout.OptSlot(entry) >= 0 && values[layout.OptSlot(entry)] >= cards[entry].MaximumActivations;

        private int RemainingAllowance(int[] values, int entry) => cards[entry].MaximumActivations
            - (layout.OptSlot(entry) < 0 ? 0 : values[layout.OptSlot(entry)]);

        private bool HandSizeAllows(int[] values, int entry, int reserved = 0) {
            int minimum = cards[entry].MinimumHandSize;
            if (minimum == 0) {
                return true;
            }
            budget.Spend(layout.RowCount);
            int count = -reserved;
            for (int row = 0; row < layout.RowCount; row++) {
                count += values[layout.HandStart + row];
            }

            return count >= minimum;
        }

        private bool TimingAllows(int[] values, int entry) => !layout.HasActivationState
            || (values[layout.ActivationState] != 2 && (!cards[entry].FirstActivationOnly || values[layout.ActivationState] == 0));

        private ResolutionOutcome Settle(ref int[] values, int remaining) {
            remaining -= layout.HasHiddenCount ? values[layout.HiddenCount] : 0;
            while (true) {
                if (values[layout.DrawsLeft] > remaining) {
                    throw new InvalidOperationException("An activated draw batch exceeds the remaining deck size.");
                }
                if (values[layout.DrawsLeft] > 0) {
                    return ResolutionOutcome.Ready;
                }

                if (CompleteBatch(ref values, ref remaining) == ResolutionOutcome.RemovalUnavailable) {
                    return ResolutionOutcome.RemovalUnavailable;
                }
                int candidate = SelectCandidate(ref values, remaining);
                if (candidate >= 0) {
                    Activate(ref values, candidate, ref remaining);
                }
                else {
                    if (values.Length > layout.QueueStart) {
                        // No pending draw/removal can change this hand. Retain the
                        // cards, but canonicalize the terminal queue so the next
                        // frontier visit cannot recheck the same failed costs.
                        budget.Spend(values.Length);
                        values = values.AsSpan(0, layout.QueueStart).ToArray();
                    }
                    return ResolutionOutcome.Ready;
                }
            }
        }

        // These stages have one hot caller. Keep the source boundaries without
        // adding call overhead to each settled child in the ordered frontier.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ResolutionOutcome CompleteBatch(ref int[] values, ref int remaining) {
            int removalSource = values[layout.CompletionSource] - 1;
            // The batch has completed. Mill before hand removal and any waiting activation.
            if (removalSource >= 0 && cards[removalSource].PostDrawDeckMillCount > 0) {
                int mill = cards[removalSource].PostDrawDeckMillCount;
                values[layout.HiddenCount] += mill;
                remaining -= mill;
            }
            if (removalSource >= 0 && cards[removalSource].BlocksLaterDraws) {
                values[layout.ActivationState] = 2;
            }
            int removalCount = removalSource < 0 ? 0 : cards[removalSource].PostDrawDiscardCount;
            bool[]? eligible = removalSource < 0 ? null : removalEligibility[removalSource];
            for (int discard = removalCount; discard > 0; discard--) {
                if (!RemoveCard(ref values, eligible, remaining)) {
                    return cards[removalSource].PostDrawFailureBehavior switch {
                        PostDrawFailureBehavior.FailBranch => ResolutionOutcome.RemovalUnavailable,
                        _ => throw new ArgumentOutOfRangeException(nameof(cards))
                    };
                }
            }
            values[layout.CompletionSource] = 0;
            return ResolutionOutcome.Ready;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int SelectCandidate(ref int[] values, int remaining) {
            int selectedCandidate = -1;
            int selectedRank = int.MaxValue;
            // Keep the physical FIFO intact, including for cost/removal choices. Custom
            // priority selects only among legal candidates; equal/unlisted ranks keep FIFO.
            // Deferred candidates retain FIFO position. Restart at the oldest
            // survivor after an activation's batch/removal. A pass with no
            // activation terminates: nothing changed that could help a retry.
            // Every successful pass consumes a source; draws advance depth.
            // This also bounds resolution when the deck is already empty.
            for (int candidate = layout.QueueStart; candidate < values.Length;) {
                budget.Spend(2L * values.Length + 1);
                int source = values[candidate];
                if (Used(values, source) || !TimingAllows(values, source)) {
                    values = RemoveCandidate(values, candidate);
                    continue;
                }
                if (cards[source].DrawCount!.Value + cards[source].PreDrawDeckRemovalCount + cards[source].PostDrawDeckMillCount > remaining) {
                    values = RemoveCandidate(values, candidate);
                    continue;
                }
                if (!HandSizeAllows(values, source)
                    || (cards[source].PreDrawRemovalCount > 0 && !CanPay(values, source))) {
                    candidate++;
                    continue;
                }
                if (selectedCandidate < 0 || ranks[source] < selectedRank) {
                    selectedCandidate = candidate;
                    if (!customPriority) {
                        break;
                    }
                    selectedRank = ranks[source];
                }
                candidate++;
            }
            return selectedCandidate;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Activate(ref int[] values, int selectedCandidate, ref int remaining) {
            int source = values[selectedCandidate];
            values = RemoveCandidate(values, selectedCandidate);
            // Check the whole cost before mutating activation state, and
            // reserve this source copy while selecting/removing material.
            for (int cost = 0; cost < cards[source].PreDrawRemovalCount; cost++) {
                if (!RemoveCard(ref values, costEligibility![source], remaining, source)) {
                    throw new InvalidOperationException("A validated activation cost became unavailable.");
                }
            }
            if (layout.HasHiddenCount) {
                values[layout.HiddenCount] += cards[source].PreDrawDeckRemovalCount;
                remaining -= cards[source].PreDrawDeckRemovalCount;
            }
            if (layout.HasActivationState) {
                values[layout.ActivationState] = 1;
            }
            values[layout.HandStart + source]--;
            if (layout.OptSlot(source) >= 0) {
                values[layout.OptSlot(source)]++;
                // Single-activation entries enqueue only one pending copy;
                // higher limits retain later copies until their allowance is spent.
            }
            values[layout.DrawsLeft] = cards[source].DrawCount!.Value;
            bool needsCompletion = cards[source].PostDrawDiscardCount > 0
                || cards[source].PostDrawDeckMillCount > 0 || cards[source].BlocksLaterDraws;
            values[layout.CompletionSource] = needsCompletion ? source + 1 : 0;
        }

        private bool CanPay(int[] values, int source, int reservedSource = -1) {
            int n = layout.RowCount;
            budget.Spend(n + 1L);
            bool[]? eligible = costEligibility![source];
            int available = 0;
            for (int i = 0; i < n; i++) {
                if (eligible is null || eligible[i]) {
                    available += values[layout.HandStart + i] - (i == source ? 1 : 0) - (i == reservedSource ? 1 : 0);
                }
            }
            return available >= cards[source].PreDrawRemovalCount;
        }

        private bool RemoveCard(ref int[] values, bool[]? eligible, int remaining, int reservedSource = -1) {
            int n = layout.RowCount;
            // Queue scan, activation classification, tier scan, removal scan and copying the surviving queue.
            budget.Spend(2L * values.Length + 2L * n + 1);
            int selected = -1;
            int worstTier = 0;
            int selectedQueue = -1;
            bool[] pending = new bool[n];
            for (int q = layout.QueueStart; q < values.Length; q++) {
                int entry = values[q];
                pending[entry] = true;
            }

            bool[] activatable = new bool[n];
            bool[] costDeferred = new bool[n];
            int minimumLegalDeckConsumption = int.MaxValue;
            if (reservedSource >= 0 && cards[reservedSource].DrawCount is int reservedDrawCount
                && reservedDrawCount + cards[reservedSource].PreDrawDeckRemovalCount + cards[reservedSource].PostDrawDeckMillCount <= remaining) {
                // A reserved source has already passed its activation checks and is finishing its cost.
                minimumLegalDeckConsumption = reservedDrawCount + cards[reservedSource].PreDrawDeckRemovalCount + cards[reservedSource].PostDrawDeckMillCount;
            }
            for (int i = 0; i < n; i++) {
                if (!TimingAllows(values, i)
                    || (reservedSource >= 0 && (cards[reservedSource].BlocksLaterDraws || cards[i].FirstActivationOnly))
                    || !pending[i] || values[layout.HandStart + i] - (i == reservedSource ? 1 : 0) == 0 || Used(values, i)
                    || cards[i].DrawCount is not int drawCount || drawCount + cards[i].PreDrawDeckRemovalCount + cards[i].PostDrawDeckMillCount > remaining
                    || (i == reservedSource && RemainingAllowance(values, i) <= 1)) {
                    continue;
                }

                bool canPay = HandSizeAllows(values, i, reservedSource < 0 ? 0 : 1)
                    && (cards[i].PreDrawRemovalCount == 0 || CanPay(values, i, reservedSource));
                if (canPay) {
                    activatable[i] = true;
                    minimumLegalDeckConsumption = Math.Min(minimumLegalDeckConsumption, drawCount + cards[i].PreDrawDeckRemovalCount + cards[i].PostDrawDeckMillCount);
                }
                else if (cards[i].PreDrawRemovalCount > 0 || cards[i].MinimumHandSize > 0) {
                    costDeferred[i] = true;
                }
            }

            // The smallest legal deck consumption leaves the most room for a deferred source.
            int remainingAfterAnotherDraw = minimumLegalDeckConsumption == int.MaxValue
                ? -1
                : remaining - minimumLegalDeckConsumption;
            for (int i = 0; i < n; i++) {
                if (values[layout.HandStart + i] - (i == reservedSource ? 1 : 0) == 0 || (eligible is not null && !eligible[i])) {
                    continue;
                }
                bool usable = activatable[i];
                bool redundant = values[layout.HandStart + i] - (i == reservedSource ? 1 : 0) > RemainingAllowance(values, i) - (i == reservedSource ? 1 : 0);
                bool deferred = costDeferred[i] && !redundant && !cards[i].FirstActivationOnly
                    && cards[i].DrawCount is int drawCount && drawCount + cards[i].PreDrawDeckRemovalCount + cards[i].PostDrawDeckMillCount <= remainingAfterAnotherDraw;
                int tier = (relevant[i], usable && !redundant, deferred) switch {
                    (true, _, _) => 1,
                    (_, true, _) => 2,
                    (_, _, true) => 3,
                    _ => 4
                };
                if (searchPolicy is not null) {
                    int searchCapacity = remaining - (reservedSource < 0 ? 0 : cards[reservedSource].DrawCount!.Value
                        + cards[reservedSource].PreDrawDeckRemovalCount + cards[reservedSource].PostDrawDeckMillCount);
                    tier = Math.Min(tier, searchPolicy.Tier(i, values, layout.HandStart, searchCapacity));
                }
                if (tier >= worstTier) {
                    worstTier = tier;
                    selected = i;
                }
            }
            if (selected < 0) {
                return false;
            }
            values[layout.HandStart + selected]--;
            if (layout.HasSearchQueue && cards[selected].Search is not null) {
                layout.RemoveSearch(values, selected);
            }
            // Remove the latest copy of this identity, retaining FIFO priority.
            for (int q = values.Length - 1; q >= layout.QueueStart; q--) {
                if (values[q] == selected) {
                    if (cards[selected].MaximumActivations != 1 || values[layout.HandStart + selected] == 0) {
                        selectedQueue = q;
                    }
                    break;
                }
            }
            if (selectedQueue >= 0) {
                values = RemoveCandidate(values, selectedQueue);
            }
            return true;
        }

        private void Add(
            Dictionary<State, BigInteger> map,
            State key,
            BigInteger increment,
            ref long cells,
            string structure
        ) {
            // Preserve the existing Work/Storage accounting for this update.
            budget.Spend(2L * key.Values.Length + WorkBudget.IntegerCells(increment) + 1);
            bool exists = map.TryGetValue(key, out BigInteger previous);
            budget.Spend(WorkBudget.IntegerCells(previous));
            BigInteger value = previous + increment;
            // Packed keys really own a shorter int[] (including its length word).
            // Charge that retained payload and the unchanged exact integer weight.
            cells += WorkBudget.IntegerCells(value) - WorkBudget.IntegerCells(previous) + (exists ? 0 : key.Values.Length);
            int count = map.Count + (exists ? 0 : 1);
            owner.PeakStates = Math.Max(owner.PeakStates, count);
            owner.CheckStorage(count, cells, structure);
            if (exists) {
                // BigInteger addition can collect. Mono WASM AOT can lose an
                // update through a dictionary value ref held across GC (#295).
                map[key] = value;
            }
            else {
                budget.Spend(2L * key.Values.Length);
                map.Add(key, value);
            }
        }

        private static int[] RemoveCandidate(int[] values, int index) {
            int[] result = new int[values.Length - 1];
            values.AsSpan(0, index).CopyTo(result);
            values.AsSpan(index + 1).CopyTo(result.AsSpan(index));
            return result;
        }
    }
}
