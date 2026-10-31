using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.DrawResolution;

internal sealed partial class PostDrawCalculation {
    private sealed partial class OrderedResolver {
        private readonly bool _hasDanger = cards.Any(card => card.IsDangerReveal);
        private BigInteger? _randomScale;
        private int?[]? _dangerIdentities;

        // Only settled hands can activate Danger. Ordinary activations add at
        // most their positive net hand gain; every Danger activation shrinks the
        // hand by one and removes a copy of an active Danger identity. With H the
        // opening hand plus all possible positive gains, and D those identity
        // copies, LCM(1..min(N,H))^min(D,H) covers every random denominator,
        // independently of visible deck depth. Ordinary requests retain N! units.
        private BigInteger RandomScale(int copies, int handSize) {
            if (_randomScale is { } existing) {
                return existing;
            }
            if (!_hasDanger) {
                return (_randomScale = BigInteger.One).Value;
            }
            _dangerIdentities ??= cards.Select(card => DangerCardIdentity.Resolve(card, owner.IdentityRegistry)).ToArray();
            HashSet<int> sourceIdentities = cards.Where(card => card.IsDangerReveal)
                .Select(card => DangerCardIdentity.Resolve(card, owner.IdentityRegistry)
                    ?? throw new ArgumentException("Danger! activation requires a verified canonical card identity.")).ToHashSet();
            int maximumActivations = Enumerable.Range(0, cards.Count)
                .Where(row => _dangerIdentities[row] is int identity && sourceIdentities.Contains(identity))
                .Sum(row => cards[row].Copies);
            long maximumHand = handSize;
            foreach (Card card in cards) {
                if (card.HasDrawEffect && !card.IsDangerReveal) {
                    long gain = Math.Max(0L, (long)card.DrawBatchSize - 1 - card.PreDrawRemovalCount - card.PostDrawDiscardCount);
                    maximumHand += gain * Math.Min(card.Copies, card.MaximumActivations);
                }
            }
            int bound = (int)Math.Min(copies, maximumHand);
            maximumActivations = (int)Math.Min(maximumActivations, maximumHand);
            budget.Spend(cards.Count * 3L);
            BigInteger lcm = BigInteger.One;
            for (int divisor = 2; divisor <= bound; divisor++) {
                budget.Spend(WorkBudget.IntegerCells(lcm) + 1);
                lcm = lcm / BigInteger.GreatestCommonDivisor(lcm, divisor) * divisor;
            }
            BigInteger scale = BigInteger.One;
            for (int activation = 0; activation < maximumActivations; activation++) {
                budget.Spend(WorkBudget.IntegerCells(scale) * (WorkBudget.IntegerCells(lcm) + 1));
                scale *= lcm;
                owner.CheckStorage(1, WorkBudget.IntegerCells(scale), "random discard unit");
            }
            _randomScale = scale;
            return scale;
        }

        internal BigInteger Denominator(BigInteger[] factorial, int handSize) {
            BigInteger scale = RandomScale(factorial.Length - 1, handSize);
            if (scale.IsOne) {
                return factorial[^1];
            }
            budget.Spend(WorkBudget.IntegerCells(scale) * (WorkBudget.IntegerCells(factorial[^1]) + 1));
            return factorial[^1] * scale;
        }

        private Dictionary<State, BigInteger> ResolveDanger<TTerminal>(
            int handSize,
            int copies,
            BigInteger[] factorial,
            int compactFrontierThreshold,
            bool packTerminals
        ) where TTerminal : struct, ITerminalHand {
            BigInteger scale = RandomScale(copies, handSize);
            for (int row = 0; row < cards.Count; row++) {
                if (cards[row].IsDangerReveal && _dangerIdentities[row] is null) {
                    throw new ArgumentException("Danger! activation requires a verified canonical card identity.");
                }
            }
            int[] initial = new int[layout.QueueStart];
            for (int row = 0; row < cards.Count; row++) {
                initial[row] = cards[row].Copies;
            }
            initial[layout.DrawsLeft] = handSize;
            Dictionary<State, BigInteger> current = new() { [new(initial)] = scale };
            Dictionary<State, BigInteger> terminal = new();
            long currentCells = initial.Length + WorkBudget.IntegerCells(scale);
            long terminalCells = 0;
            bool currentPacked = false;
            bool canPack = copies <= byte.MaxValue && cards.Count <= byte.MaxValue;
            for (int depth = 0; current.Count > 0; depth++) {
                Dictionary<State, BigInteger> next = new();
                long nextCells = 0;
                bool nextPacked = canPack && (currentPacked || currentCells >= compactFrontierThreshold);
                if (nextPacked) {
                    owner.PackedLayers++;
                }
                int remaining = copies - depth;
                foreach ((State state, BigInteger weight) in current) {
                    budget.Spend(1);
                    int[] values = state.Values;
                    if (currentPacked) {
                        budget.Spend(values[0]);
                        values = DrawFrontierPacking.Unpack(values);
                    }
                    if (values[layout.DrawsLeft] == 0) {
                        RecordTerminal(values, remaining, weight);
                        continue;
                    }
                    for (int row = 0; row < cards.Count; row++) {
                        budget.Spend(1);
                        if (values[row] == 0) {
                            continue;
                        }
                        bool candidate = cards[row].HasDrawEffect && TimingAllows(values, row) && !Used(values, row);
                        budget.Spend(values.Length + WorkBudget.IntegerCells(weight) + 1L);
                        int[] drawn = new int[values.Length + (candidate ? 1 : 0)];
                        values.CopyTo(drawn, 0);
                        drawn[row]--;
                        drawn[layout.HandStart + row]++;
                        drawn[layout.DrawsLeft]--;
                        if (candidate) {
                            drawn[^1] = row;
                        }
                        if (layout.HasSearchQueue && cards[row].Search is not null) {
                            layout.EncounterSearch(drawn, row);
                        }
                        SettleDanger(drawn, remaining - 1, weight * values[row], 0, 0, AddSettled);
                    }
                }
                owner.PeakStates = Math.Max(owner.PeakStates, next.Count);
                owner.PeakConcurrentCells = Math.Max(owner.PeakConcurrentCells, currentCells + nextCells + terminalCells);
                owner.ObserveFrontier?.Invoke();
                GC.KeepAlive(current);
                current = next;
                currentCells = nextCells;
                currentPacked = nextPacked;

                void AddSettled(int[] settled, BigInteger mass, int settledDepth, long settledCells) {
                    if (settled[layout.DrawsLeft] == 0) {
                        RecordTerminal(settled, remaining - 1, mass);
                        return;
                    }
                    if (nextPacked) {
                        budget.Spend(settled.Length);
                        settled = DrawFrontierPacking.Pack(settled);
                    }
                    Add(next, new(settled), mass, ref nextCells, "draw frontier");
                }
            }
            if (owner.ObserveTerminal is not null) {
                foreach ((State hand, BigInteger mass) in terminal) {
                    owner.ObserveTerminal(packTerminals ? DrawFrontierPacking.Unpack(hand.Values) : (int[])hand.Values.Clone(), mass);
                }
            }
            return terminal;

            void RecordTerminal(int[] values, int remaining, BigInteger weight) {
                budget.Spend(WorkBudget.IntegerCells(weight) * (WorkBudget.IntegerCells(factorial[remaining]) + 1));
                int[] hand = TTerminal.Create(values, layout, remaining);
                if (packTerminals) {
                    budget.Spend(hand.Length);
                    hand = DrawFrontierPacking.Pack(hand);
                }
                Add(terminal, new(hand), weight * factorial[remaining], ref terminalCells, "terminal hands");
            }
        }

        private void SettleDanger(
            int[] values,
            int poolRemaining,
            BigInteger weight,
            int depth,
            long parentCells,
            Action<int[], BigInteger, int, long> settled
        ) {
            long cells = parentCells + values.Length + WorkBudget.IntegerCells(weight) + 4;
            owner.CheckStorage(depth + 1L, cells, "random discard stack");
            int remaining = poolRemaining - (layout.HasHiddenCount ? values[layout.HiddenCount] : 0);
            while (true) {
                budget.Spend(1);
                if (values[layout.DrawsLeft] > remaining) {
                    throw new InvalidOperationException("An activated draw batch exceeds the remaining deck size.");
                }
                if (values[layout.DrawsLeft] > 0) {
                    settled(values, weight, depth, cells);
                    return;
                }
                if (CompleteBatch(ref values, ref remaining) == ResolutionOutcome.RemovalUnavailable) {
                    return;
                }
                int selected = SelectCandidate(ref values, remaining);
                if (selected < 0) {
                    if (values.Length > layout.QueueStart) {
                        budget.Spend(values.Length);
                        values = values.AsSpan(0, layout.QueueStart).ToArray();
                    }
                    settled(values, weight, depth, cells);
                    return;
                }
                if (!cards[values[selected]].IsDangerReveal) {
                    Activate(ref values, selected, ref remaining);
                    continue;
                }
                BranchDanger(values, selected, poolRemaining, weight, depth, cells, settled);
                return;
            }
        }

        private void BranchDanger(
            int[] values,
            int selected,
            int remaining,
            BigInteger weight,
            int depth,
            long parentCells,
            Action<int[], BigInteger, int, long> settled
        ) {
            int source = values[selected];
            int handSize = 0;
            for (int row = 0; row < cards.Count; row++) {
                handSize += values[layout.HandStart + row];
            }
            budget.Spend(cards.Count + WorkBudget.IntegerCells(weight));
            BigInteger unit = BigInteger.DivRem(weight, handSize, out BigInteger remainder);
            if (!remainder.IsZero) {
                throw new InvalidOperationException("Random discard branch mass must remain integral.");
            }
            for (int row = 0; row < cards.Count; row++) {
                int held = values[layout.HandStart + row];
                if (held == 0) {
                    continue;
                }
                int individual = 0;
                // Draw entries carry one physical FIFO token per
                // eligible copy, including once-per-turn entries. Removing different occurrences may change the
                // next activation, even when their card identity is identical.
                // Equal adjacent tokens alone are interchangeable: deleting any
                // token in their run leaves exactly the same encoded queue.
                if (cards[row].HasDrawEffect) {
                    for (int queue = layout.QueueStart; queue < values.Length; queue++) {
                        budget.Spend(1);
                        if (values[queue] == row) {
                            int start = queue;
                            while (queue + 1 < values.Length && values[queue + 1] == row) {
                                budget.Spend(1);
                                queue++;
                            }
                            int multiplicity = queue - start + 1;
                            Discard(row, start, -1, multiplicity);
                            individual += multiplicity;
                        }
                    }
                }
                if (layout.HasSearchQueue && cards[row].Search is not null) {
                    for (int queue = layout.SearchQueueStart; queue < layout.QueueStart; queue++) {
                        budget.Spend(1);
                        if (values[queue] == row + 1) {
                            int start = queue;
                            while (queue + 1 < layout.QueueStart && values[queue + 1] == row + 1) {
                                budget.Spend(1);
                                queue++;
                            }
                            int multiplicity = queue - start + 1;
                            Discard(row, -1, start, multiplicity);
                            individual += multiplicity;
                        }
                    }
                }
                if (held > individual) {
                    Discard(row, -1, -1, held - individual);
                }
            }

            void Discard(int row, int drawQueue, int searchQueue, int multiplicity) {
                budget.Spend(values.Length + WorkBudget.IntegerCells(unit) + 1L);
                int[] branch = (int[])values.Clone();
                branch[layout.HandStart + row]--;
                int revealQueue = selected;
                if (drawQueue >= 0) {
                    branch = RemoveCandidate(branch, drawQueue);
                    if (drawQueue < revealQueue) {
                        revealQueue--;
                    }
                }
                if (searchQueue >= 0) {
                    Array.Copy(branch, searchQueue + 1, branch, searchQueue, layout.QueueStart - searchQueue - 1);
                    branch[layout.QueueStart - 1] = 0;
                }
                if (layout.HasActivationState) {
                    branch[layout.ActivationState] = 1;
                }
                if (_dangerIdentities![row] != _dangerIdentities[source]) {
                    // Different identity: the revealed physical source leaves
                    // the hand and its own queue slot; only then draw one.
                    branch[layout.HandStart + source]--;
                    branch = RemoveCandidate(branch, revealQueue);
                    branch[layout.DrawsLeft] = 1;
                }
                // Same identity removes only the random copy. The unchanged
                // FIFO tokens preserve every surviving reveal opportunity.
                SettleDanger(branch, remaining, unit * multiplicity, depth + 1, parentCells + WorkBudget.IntegerCells(unit) + 4, settled);
            }
        }
    }
}
