// Research only; compiled solely through the opt-in Research.targets.
using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Counting;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.DrawResolution;

internal sealed partial class PostDrawCalculation {
    internal sealed record AnonymousResult(BigInteger[] Wins, BigInteger Resolved, BigInteger Denominator,
        long Expanded, long Evicted, long Reveals, long TerminalSplits, long TerminalHits,
        long SkippedReveals, long RetainedDiscards, long RetainedReveals,
        long JointStratumReveals, long EmptyRetainedReveals, long RetainedObserverReveals,
        long FinalizationWork, long PriorityWork, long ScaleWork, string Stop);

    internal AnonymousResult ResearchAnonymous(List<Combo> combos, IReadOnlyList<ComboGroup> groups, int capacity,
        bool wholeBatches = false, bool retainedStratum = false) {
        if (capacity < 1 || (wholeBatches && retainedStratum) || deck.Any(card => !card.Active || card.Copies < 0)
            || deck.Sum(card => card.Copies) > byte.MaxValue || deck.Count > byte.MaxValue
            || deck.Any(card => card.PreDrawDeckRemovalCount > 0 || card.PostDrawDeckMillCount > 0
                || card.PreDrawRemovalCount > 0 || card.MinimumHandSize > 0 || card.EffectPlan.RequiresSearchResolution)) {
            throw new NotSupportedException("Anonymous research excludes hidden operations, bounded searches, costs, hand-size gates and nonactive/non-byte-packable inputs.");
        }
        List<Card> cards = ReduceSearchDeck(combos, out _, merge: false);
        cards = CoalescePhysicalSearchDeck(cards, combos);
        OrderedDrawPreparation preparation = PrepareOrderedDraw(cards, combos);
        (bool[]?[] removal, bool[]?[]? cost) = CompileHandOperations(cards);
        HashSet<int> dangerIdentities = cards.Where(card => card.IsDangerReveal)
            .Select(card => DangerCardIdentity.Resolve(card, IdentityRegistry)
                ?? throw new ArgumentException("Danger source requires canonical identity.")).ToHashSet();
        bool[] pooled = cards.Select(card => !card.HasDrawEffect && card.Search is null
            && !(DangerCardIdentity.Resolve(card, IdentityRegistry) is int identity && dangerIdentities.Contains(identity))).ToArray();
        budget.Spend(cards.Count * 4L);
        OrderedResolver resolver = new(this, budget, cards, preparation.Layout, preparation.CustomPriority,
            preparation.Ranks, preparation.Relevant, removal, cost, new SearchKeepPolicy(cards, preparation.Relevant, budget));
        PhysicalSearchHandCounter counter = new(cards, combos, budget, memoize: true);
        return resolver.Anonymous(handSize, preparation.Copies, preparation.Factorial, new(combos, groups), counter, capacity,
            pooled, wholeBatches, retainedStratum);
    }

    private sealed partial class OrderedResolver {
        private sealed record AnonymousEntry(State State, BigInteger Weight, int Priority) {
            internal LinkedListNode<AnonymousEntry>? Node { get; set; }
        }
        private sealed class AnonymousFrontier(int bucketCount) {
            internal Dictionary<State, AnonymousEntry> Entries { get; } = new();
            internal LinkedList<AnonymousEntry>?[] Buckets { get; } = new LinkedList<AnonymousEntry>?[bucketCount];
            internal int Minimum { get; set; } = bucketCount;
            internal int Maximum { get; set; } = -1;
            internal long Cells { get; set; }
            // Bucket descriptor references plus priority number/node pointers and
            // linked-node references; entry state/weight belongs to the map.
            internal long PriorityCells => 2L * Buckets.Length + 11L * Entries.Count;
        }
        private sealed record AnonymousTerminal(BigInteger[] Wins, BigInteger Denominator);

        internal AnonymousResult Anonymous(int handSize, int copies, BigInteger[] factorial,
            CalculationResultRows rows, PhysicalSearchHandCounter counter, int capacity, bool[] pooled,
            bool wholeBatches, bool retainedStratum) {
            BigInteger denominator = Denominator(factorial, handSize);
            int n = cards.Count;
            int[] poolRows = Enumerable.Range(0, n).Where(row => pooled[row]).ToArray();
            int[] searchRows = Enumerable.Range(0, n).Where(row => cards[row].Search is not null).ToArray();
            budget.Spend(cards.Sum(card => card.Categories.Count + 1L)
                * searchRows.Sum(row => cards[row].Search!.Selector.EvaluationSize + 1L) + n);
            bool[] target = Enumerable.Range(0, n).Select(row => searchRows.Any(source => source != row && cards[source].Search!.Matches(cards[row]))).ToArray();
            budget.Spend(n * (long)(searchRows.Length + 1) + poolRows.Length);
            BigInteger[][] choose = new BigInteger[copies + 1][];
            long chooseCells = copies + 1;
            for (int total = 0; total <= copies; total++) {
                choose[total] = new BigInteger[total + 1];
                choose[total][0] = choose[total][total] = 1;
                for (int selected = 1; selected < total; selected++) {
                    budget.Spend(WorkBudget.IntegerCells(choose[total - 1][selected - 1]) + WorkBudget.IntegerCells(choose[total - 1][selected]) + 1);
                    choose[total][selected] = choose[total - 1][selected - 1] + choose[total - 1][selected];
                }
                chooseCells += choose[total].Sum(WorkBudget.IntegerCells);
                owner.CheckStorage((total + 1L) * (total + 2) / 2, chooseCells, "anonymous binomials");
            }
            BigInteger[] wins = new BigInteger[rows.Count];
            BigInteger resolved = 0;
            long expanded = 0, evicted = 0, reveals = 0, terminalSplits = 0, terminalHits = 0, skippedReveals = 0;
            long retainedDiscards = 0, retainedReveals = 0;
            long jointStratumReveals = 0, emptyRetainedReveals = 0, retainedObserverReveals = 0;
            long priorityWork = 0, scaleWork = 0;
            long finalization = (rows.Count + 2L) * (WorkBudget.IntegerCells(denominator) + 2) * 8;
            budget.Spend(finalization);
            owner.CheckStorage(rows.Count + 2L, (rows.Count + 2L) * (WorkBudget.IntegerCells(denominator) + 1), "anonymous result accumulators");
            Dictionary<State, AnonymousTerminal> terminalCache = new();
            long terminalCells = 0;
            long conditioningCells = 0, conditioningFrames = 0;
            string stop = "exhausted retained frontier";
            int bucketCount = checked(((int)denominator.GetBitLength() + 1) * 16 + 16);
            AnonymousFrontier current = NewFrontier();
            AnonymousFrontier next = current;
            int[] initial = new int[layout.QueueStart];
            int ordinary = 0;
            for (int row = 0; row < n; row++) {
                initial[row] = cards[row].Copies;
                if (pooled[row]) {
                    ordinary += cards[row].Copies;
                }
            }
            initial[layout.DrawsLeft] = handSize;
            try {
                if (handSize == 0) {
                    Terminal(initial, 0, ordinary, denominator, retainedKnown: retainedStratum ? 0 : -1);
                }
                else {
                    Insert(initial, 0, ordinary, denominator, 0, retainedStratum ? 0 : -1);
                }
                while (current.Entries.Count > 0) {
                    next = NewFrontier();
                    while (current.Entries.Count > 0) {
                        AnonymousEntry entry = TakeLargest(current);
                        expanded++;
                        budget.Spend(entry.State.Values[0] + 2L);
                        int[] unpacked = DrawFrontierPacking.Unpack(entry.State.Values);
                        int metadata = retainedStratum ? 3 : 2;
                        int unknown = unpacked[unpacked.Length - metadata], physical = unpacked[unpacked.Length - metadata + 1];
                        int retainedKnown = retainedStratum ? unpacked[^1] : -1;
                        int[] values = unpacked.AsSpan(0, unpacked.Length - metadata).ToArray();
                        int remaining = Remaining(values, physical);
                        if (values[layout.DrawsLeft] <= 0 || remaining <= 0) {
                            throw new InvalidOperationException("Anonymous frontier must await a legal draw.");
                        }
                        if (wholeBatches && values[layout.DrawsLeft] > 1) {
                            EnumerateAnonymousBatch(values, unknown, physical, entry.Weight, remaining, pooled, choose,
                                factorial, denominator, Scale, (state, held, pool, weight, depth, cells) =>
                                    SettleAnonymous(state, held, pool, weight, depth, cells));
                            continue;
                        }
                        if (physical > 0) {
                            budget.Spend(values.Length);
                            int[] drawn = (int[])values.Clone();
                            drawn[layout.DrawsLeft]--;
                            SettleAnonymous(drawn, unknown + 1, physical - 1, Scale(entry.Weight, physical, remaining), 0, 0, retainedKnown);
                        }
                        for (int row = 0; row < n; row++) {
                            budget.Spend(1);
                            if (pooled[row] || values[row] == 0) {
                                continue;
                            }
                            bool candidate = cards[row].HasDrawEffect && TimingAllows(values, row) && !Used(values, row);
                            budget.Spend(values.Length + 1L);
                            int[] drawn = new int[values.Length + (candidate ? 1 : 0)];
                            values.CopyTo(drawn, 0);
                            drawn[row]--;
                            drawn[layout.HandStart + row]++;
                            drawn[layout.DrawsLeft]--;
                            if (candidate) {
                                drawn[^1] = row;
                            }
                            SettleAnonymous(drawn, unknown, physical, Scale(entry.Weight, values[row], remaining), 0, 0, retainedKnown);
                        }
                    }
                    current = next;
                }
            }
            catch (ProbabilityCalculationLimitException error) {
                stop = error.Reason.ToString();
            }
            return new(wins, resolved, denominator, expanded, evicted, reveals, terminalSplits, terminalHits,
                skippedReveals, retainedDiscards, retainedReveals, jointStratumReveals, emptyRetainedReveals,
                retainedObserverReveals, finalization, priorityWork, scaleWork, stop);

            int Remaining(int[] values, int physical) {
                budget.Spend(n);
                int count = physical;
                for (int row = 0; row < n; row++) {
                    if (!pooled[row]) {
                        count += values[row];
                    }
                }
                return count;
            }

            BigInteger Scale(BigInteger mass, BigInteger numerator, BigInteger divisor) {
                long comparison = WorkBudget.IntegerCells(numerator) + WorkBudget.IntegerCells(divisor) + 1;
                budget.Spend(comparison);
                scaleWork += comparison;
                if (numerator.IsZero) {
                    return BigInteger.Zero;
                }
                if (numerator == divisor) {
                    return mass;
                }
                long arithmetic = (WorkBudget.IntegerCells(mass) + WorkBudget.IntegerCells(divisor) + 2)
                    * (WorkBudget.IntegerCells(numerator) + WorkBudget.IntegerCells(divisor) + 2);
                budget.Spend(arithmetic);
                scaleWork += arithmetic;
                BigInteger value = BigInteger.DivRem(mass * numerator, divisor, out BigInteger remainder);
                if (!remainder.IsZero) {
                    throw new InvalidOperationException("Anonymous conditioning lost integral physical mass.");
                }
                return value;
            }

            State Key(int[] values, int unknown, int physical, int retainedKnown = -1) {
                budget.Spend(values.Length * 2L + 4);
                if (retainedStratum && retainedKnown < 0) {
                    throw new InvalidOperationException("Missing retained ordinary stratum count.");
                }
                int[] complete = new int[values.Length + (retainedStratum ? 3 : 2)];
                values.CopyTo(complete, 0);
                complete[values.Length] = unknown;
                complete[values.Length + 1] = physical;
                if (retainedStratum) {
                    complete[^1] = retainedKnown;
                }
                return new(DrawFrontierPacking.Pack(complete));
            }

            void PrioritySpend(long units) {
                budget.Spend(units);
                priorityWork += units;
            }

            AnonymousFrontier NewFrontier() {
                PrioritySpend(bucketCount + 1L);
                owner.CheckStorage(bucketCount, 2L * bucketCount, "anonymous priority descriptors");
                return new(bucketCount);
            }

            int Priority(BigInteger mass) {
                PrioritySpend(WorkBudget.IntegerCells(mass) * 2L + 3);
                int bits = (int)mass.GetBitLength();
                // A relative mass bin: top four significant bits plus exponent.
                // The ranking is approximate; FIFO ties are deterministic. This
                // only selects refinements and never supplies numeric endpoints.
                int leading = bits <= 4 ? (int)mass << (4 - bits) : (int)(mass >> (bits - 4));
                return bits * 16 + leading;
            }

            void Remove(AnonymousFrontier frontier, AnonymousEntry entry) {
                PrioritySpend(entry.State.Values.Length + 6L);
                frontier.Buckets[entry.Priority]!.Remove(entry.Node!);
                entry.Node = null;
                frontier.Entries.Remove(entry.State);
                frontier.Cells -= EntryCells(entry);
            }

            AnonymousEntry TakeLargest(AnonymousFrontier frontier) {
                while (frontier.Maximum >= 0 && frontier.Buckets[frontier.Maximum]?.Count is not > 0) {
                    PrioritySpend(1);
                    frontier.Maximum--;
                }
                AnonymousEntry entry = frontier.Buckets[frontier.Maximum]!.First!.Value;
                Remove(frontier, entry);
                return entry;
            }

            long EntryCells(AnonymousEntry entry) => entry.State.Values.Length + WorkBudget.IntegerCells(entry.Weight);

            void Insert(int[] values, int unknown, int physical, BigInteger mass, long stackCells, int retainedKnown = -1) {
                State key = Key(values, unknown, physical, retainedKnown);
                budget.Spend(key.Values.Length + WorkBudget.IntegerCells(mass) + 1);
                bool found = next.Entries.TryGetValue(key, out AnonymousEntry? previous);
                budget.Spend(previous is null ? 1 : WorkBudget.IntegerCells(previous.Weight) + key.Values.Length);
                BigInteger combined = mass + (previous?.Weight ?? 0);
                if (found) {
                    Remove(next, previous!);
                }
                AnonymousEntry added = new(key, combined, Priority(combined));
                long cells = EntryCells(added);
                while (next.Entries.Count > 0 && (next.Entries.Count >= capacity
                    || bucketCount + next.Entries.Count >= budget.StoragePolicy.EntryLimit
                    || next.Cells + cells > budget.StoragePolicy.CellLimit
                    || next.PriorityCells + 11 > budget.StoragePolicy.CellLimit)) {
                    while (next.Minimum < bucketCount && next.Buckets[next.Minimum]?.Count is not > 0) {
                        PrioritySpend(1);
                        next.Minimum++;
                    }
                    AnonymousEntry least = next.Buckets[next.Minimum]!.First!.Value;
                    PrioritySpend(1);
                    if (least.Priority >= added.Priority) {
                        evicted++;
                        return;
                    }
                    Remove(next, least);
                    evicted++;
                }
                owner.CheckStorage(next.Entries.Count + 1L, next.Cells + cells, "anonymous frontier");
                owner.CheckStorage(bucketCount + next.Entries.Count + 1L, next.PriorityCells + 11, "anonymous priority references");
                PrioritySpend(8);
                next.Entries.Add(key, added);
                LinkedList<AnonymousEntry> bucket = next.Buckets[added.Priority] ??= new();
                added.Node = bucket.AddLast(added);
                next.Minimum = Math.Min(next.Minimum, added.Priority);
                next.Maximum = Math.Max(next.Maximum, added.Priority);
                next.Cells += cells;
                owner.PeakConcurrentCells = Math.Max(owner.PeakConcurrentCells,
                    (ReferenceEquals(current, next) ? 0 : current.Cells + current.PriorityCells)
                    + next.Cells + next.PriorityCells + terminalCells + chooseCells + stackCells + conditioningCells);
            }

            void SettleAnonymous(int[] values, int unknown, int physical, BigInteger mass, int depth, long parentCells, int retainedKnown = -1) {
                long cells = parentCells + values.Length + WorkBudget.IntegerCells(mass) + 5;
                owner.CheckStorage(depth + 1L + conditioningFrames, cells + conditioningCells, "anonymous resolution and conditioning stack");
                int remaining = Remaining(values, physical);
                while (true) {
                    budget.Spend(1);
                    if (values[layout.DrawsLeft] > remaining) {
                        throw new InvalidOperationException("Activated anonymous draw exceeds physical deck.");
                    }
                    if (values[layout.DrawsLeft] > 0) {
                        Insert(values, unknown, physical, mass, cells, retainedKnown);
                        return;
                    }
                    int completion = values[layout.CompletionSource] - 1;
                    bool knownExact = !retainedStratum || retainedKnown == KnownInventory(values);
                    bool unobserved = unknown > 0 || !knownExact;
                    if (unobserved && completion >= 0 && cards[completion].PostDrawDiscardCount > 0
                        && !SafeUnrevealedRemoval(values, completion)) {
                        if (retainedStratum && !knownExact) {
                            retainedObserverReveals++;
                        }
                        RevealBoth(values, unknown, retainedKnown, mass, (known, conditioned) =>
                            SettleAnonymous(known, 0, physical, conditioned, depth + 1, cells,
                                retainedStratum ? KnownInventory(known) : -1), cells);
                        return;
                    }
                    if (unobserved && completion >= 0 && cards[completion].PostDrawDiscardCount > 0) {
                        skippedReveals++;
                    }
                    if (CompleteBatch(ref values, ref remaining) == ResolutionOutcome.RemovalUnavailable) {
                        budget.Spend(WorkBudget.IntegerCells(resolved) + WorkBudget.IntegerCells(mass) + 1);
                        resolved += mass;
                        return;
                    }
                    if (retainedStratum && knownExact) {
                        // A fully revealed pool may just have lost known cards to
                        // policy removal. Its remaining multiset becomes the new
                        // observed-hand stratum, with every member initially held.
                        retainedKnown = KnownInventory(values);
                    }
                    int selected = SelectCandidate(ref values, remaining);
                    if (selected < 0) {
                        Terminal(values.AsSpan(0, layout.QueueStart).ToArray(), unknown, physical, mass, cells, retainedKnown);
                        return;
                    }
                    if (!cards[values[selected]].IsDangerReveal) {
                        Activate(ref values, selected, ref remaining);
                        continue;
                    }
                    int source = values[selected];
                    int heldSize = unknown + (retainedStratum ? retainedKnown : 0);
                    for (int row = 0; row < n; row++) {
                        if (!retainedStratum || !pooled[row]) {
                            heldSize += values[layout.HandStart + row];
                        }
                    }
                    budget.Spend(n);
                    for (int row = 0; row < n; row++) {
                        int held = values[layout.HandStart + row];
                        if (held == 0 || (retainedStratum && pooled[row])) {
                            continue;
                        }
                        int individual = 0;
                        if (cards[row].HasDrawEffect) {
                            for (int queue = layout.QueueStart; queue < values.Length; queue++) {
                                budget.Spend(1);
                                if (values[queue] == row) {
                                    int first = queue;
                                    while (queue + 1 < values.Length && values[queue + 1] == row) {
                                        budget.Spend(1);
                                        queue++;
                                    }
                                    int multiplicity = queue - first + 1;
                                    Discard(row, first, multiplicity);
                                    individual += multiplicity;
                                }
                            }
                        }
                        if (held > individual) {
                            Discard(row, -1, held - individual);
                        }
                    }
                    if (unknown > 0) {
                        budget.Spend(values.Length * 2L);
                        int[] child = (int[])values.Clone();
                        child[layout.HandStart + source]--;
                        child = RemoveCandidate(child, selected);
                        child[layout.DrawsLeft] = 1;
                        if (layout.HasActivationState) {
                            child[layout.ActivationState] = 1;
                        }
                        SettleAnonymous(child, unknown - 1, physical, Scale(mass, unknown, heldSize), depth + 1, cells, retainedKnown);
                    }
                    if (retainedStratum && retainedKnown > 0) {
                        retainedDiscards++;
                        budget.Spend(values.Length * 2L);
                        int[] child = (int[])values.Clone();
                        child[layout.HandStart + source]--;
                        child = RemoveCandidate(child, selected);
                        child[layout.DrawsLeft] = 1;
                        if (layout.HasActivationState) {
                            child[layout.ActivationState] = 1;
                        }
                        // Do not decrement a category count in K. Only the number
                        // of surviving slots changes until the next observation.
                        SettleAnonymous(child, unknown, physical, Scale(mass, retainedKnown, heldSize), depth + 1, cells, retainedKnown - 1);
                    }
                    return;

                    void Discard(int row, int queue, int multiplicity) {
                        budget.Spend(values.Length * 2L);
                        int[] child = (int[])values.Clone();
                        child[layout.HandStart + row]--;
                        int reveal = selected;
                        if (queue >= 0) {
                            child = RemoveCandidate(child, queue);
                            if (queue < reveal) {
                                reveal--;
                            }
                        }
                        if (layout.HasActivationState) {
                            child[layout.ActivationState] = 1;
                        }
                        if (_dangerIdentities![row] != _dangerIdentities[source]) {
                            child[layout.HandStart + source]--;
                            child = RemoveCandidate(child, reveal);
                            child[layout.DrawsLeft] = 1;
                        }
                        SettleAnonymous(child, unknown, physical, Scale(mass, multiplicity, heldSize), depth + 1, cells, retainedKnown);
                    }
                }
            }

            int KnownInventory(int[] values) {
                budget.Spend(poolRows.Length);
                return poolRows.Sum(row => values[layout.HandStart + row]);
            }

            void RevealBoth(int[] values, int unknown, int retainedKnown, BigInteger mass,
                Action<int[], BigInteger> accept, long activeCells = 0) {
                if (!retainedStratum || retainedKnown == KnownInventory(values)) {
                    Reveal(values, unknown, mass, accept, activeCells);
                    return;
                }
                int inventory = KnownInventory(values);
                reveals++;
                if (retainedKnown > 0 && retainedKnown < inventory) {
                    retainedReveals++;
                    if (unknown > 0) {
                        jointStratumReveals++;
                    }
                }
                if (retainedKnown == 0 && inventory > 0) {
                    emptyRetainedReveals++;
                }
                // K labels were previously observed in hand and never belong to
                // the physical Deck or global unobserved inventory I.
                EnumeratePool(values, retainedKnown, mass, choose[inventory][retainedKnown], (counts, conditioned) => {
                    budget.Spend(values.Length + poolRows.Length);
                    int[] child = (int[])values.Clone();
                    for (int index = 0; index < poolRows.Length; index++) {
                        child[layout.HandStart + poolRows[index]] = counts[index];
                    }
                    Reveal(child, unknown, conditioned, accept, activeCells + values.Length);
                }, activeCells, layout.HandStart);
            }

            bool SafeUnrevealedRemoval(int[] values, int source) {
                if (cards[source].PostDrawDiscardCount != 1) {
                    return false;
                }
                bool[]? eligible = removalEligibility[source];
                bool dominant = false;
                for (int row = 0; row < n; row++) {
                    budget.Spend(1);
                    if (eligible is not null && !eligible[row]) {
                        continue;
                    }
                    if (pooled[row]) {
                        if (!relevant[row]) {
                            return false;
                        }
                    }
                    else {
                        // A removable search source's keep tier could observe U.
                        if (cards[row].Search is not null) {
                            return false;
                        }
                        dominant |= !relevant[row] && values[layout.HandStart + row] > 0;
                    }
                }
                // Every hidden eligible card is tier1; an explicit nonrelevant,
                // nonsearch card is tier2/3/4. No guarded policy test observes U.
                return dominant;
            }

            void Reveal(int[] values, int unknown, BigInteger mass, Action<int[], BigInteger> accept, long activeCells = 0) {
                if (unknown == 0) {
                    accept(values, mass);
                    return;
                }
                reveals++;
                int inventory = poolRows.Sum(row => values[row]);
                budget.Spend(poolRows.Length);
                EnumeratePool(values, unknown, mass, choose[inventory][unknown], (counts, conditioned) => {
                    budget.Spend(values.Length + poolRows.Length * 2L);
                    int[] child = (int[])values.Clone();
                    for (int index = 0; index < poolRows.Length; index++) {
                        int row = poolRows[index];
                        child[row] -= counts[index];
                        child[layout.HandStart + row] += counts[index];
                    }
                    accept(child, conditioned);
                }, activeCells);
            }

            void EnumeratePool(int[] values, int count, BigInteger mass, BigInteger divisor, Action<int[], BigInteger> accept,
                long activeCells = 0, int offset = 0) {
                int[] counts = new int[poolRows.Length];
                int[] suffix = new int[poolRows.Length + 1];
                for (int index = poolRows.Length - 1; index >= 0; index--) {
                    suffix[index] = suffix[index + 1] + values[offset + poolRows[index]];
                }
                budget.Spend(poolRows.Length * 2L);
                long localCells = 2L * poolRows.Length + (poolRows.Length + 1L) * (WorkBudget.IntegerCells(denominator) + 3);
                conditioningCells += localCells;
                conditioningFrames += poolRows.Length + 1L;
                try {
                    owner.CheckStorage(conditioningFrames, conditioningCells + activeCells, "anonymous conditioning and resolution stack");
                    VisitPool(0, count, BigInteger.One);
                }
                finally {
                    conditioningCells -= localCells;
                    conditioningFrames -= poolRows.Length + 1L;
                }
                void VisitPool(int index, int left, BigInteger weight) {
                    budget.Spend(1);
                    if (index == poolRows.Length) {
                        if (left == 0) {
                            accept(counts, Scale(mass, weight, divisor));
                        }
                        return;
                    }
                    int available = values[offset + poolRows[index]];
                    for (int selected = Math.Max(0, left - suffix[index + 1]); selected <= Math.Min(left, available); selected++) {
                        counts[index] = selected;
                        budget.Spend((WorkBudget.IntegerCells(weight) + 1) * (WorkBudget.IntegerCells(choose[available][selected]) + 1));
                        VisitPool(index + 1, left - selected, weight * choose[available][selected]);
                    }
                }
            }

            void Terminal(int[] values, int unknown, int physical, BigInteger mass, long activeCells = 0, int retainedKnown = -1) {
                int inventory = poolRows.Sum(row => values[row]);
                int maximumSearch = searchRows.Sum(row => Math.Min(values[layout.HandStart + row], cards[row].MaximumActivations));
                int[] summary = values.AsSpan(0, 2 * n).ToArray();
                for (int row = 0; row < n; row++) {
                    if (!pooled[row]) {
                        summary[row] = target[row] ? Math.Min(summary[row], maximumSearch) : 0;
                    }
                }
                budget.Spend(n * 2L + searchRows.Length + poolRows.Length);
                State key = Key(summary, unknown, physical, retainedKnown);
                budget.Spend(key.Values.Length);
                if (!terminalCache.TryGetValue(key, out AnonymousTerminal? conditional)) {
                    BigInteger unit = factorial[inventory];
                    if (retainedStratum) {
                        int knownInventory = KnownInventory(values);
                        budget.Spend((WorkBudget.IntegerCells(unit) + 1) * (WorkBudget.IntegerCells(factorial[knownInventory]) + 1));
                        unit *= factorial[knownInventory];
                    }
                    BigInteger[] conditionalWins = new BigInteger[rows.Count];
                    RevealBoth(values, unknown, retainedKnown, unit, (known, conditioned) => CountKnown(known, conditioned, conditionalWins),
                        activeCells + values.Length + (rows.Count + 1L) * (WorkBudget.IntegerCells(denominator) + 1));
                    conditional = new(conditionalWins, unit);
                    long cells = key.Values.Length + WorkBudget.IntegerCells(unit) + conditionalWins.Sum(WorkBudget.IntegerCells) + rows.Count;
                    budget.Spend(cells);
                    if (terminalCache.Count < budget.StoragePolicy.EntryLimit && terminalCells + cells <= budget.StoragePolicy.CellLimit) {
                        owner.CheckStorage(terminalCache.Count + 1L, terminalCells + cells, "anonymous terminal cache");
                        terminalCache.Add(key, conditional);
                        terminalCells += cells;
                    }
                }
                else {
                    terminalHits++;
                }
                BigInteger[] contribution = new BigInteger[rows.Count];
                for (int row = 0; row < rows.Count; row++) {
                    contribution[row] = Scale(mass, conditional.Wins[row], conditional.Denominator);
                }
                budget.Spend((rows.Count + 1L) * (WorkBudget.IntegerCells(denominator) + 1) + rows.Count);
                // Atomically classify the entire abstract terminal only after its
                // complete conditional row calculation and all budget calls.
                for (int row = 0; row < rows.Count; row++) {
                    wins[row] += contribution[row];
                }
                resolved += mass;

                void CountKnown(int[] known, BigInteger conditioned, BigInteger[] destination) {
                    int availableInventory = poolRows.Sum(row => known[row]);
                    int hidden = availableInventory - physical;
                    if (hidden < 0) {
                        throw new InvalidOperationException("Anonymous inventory conservation failed.");
                    }
                    int[] lowerCapacity = known.AsSpan(0, n).ToArray();
                    int[] upperCapacity = (int[])lowerCapacity.Clone();
                    foreach (int row in poolRows) {
                        lowerCapacity[row] = Math.Max(0, known[row] - hidden);
                        upperCapacity[row] = Math.Min(known[row], physical);
                    }
                    budget.Spend(n * 3L + poolRows.Length * 2L);
                    bool[] low = counter.Evaluate(known.AsSpan(layout.HandStart, n), lowerCapacity).ToArray();
                    budget.Spend(low.Length);
                    bool[] high = maximumSearch == 0 ? low : counter.Evaluate(known.AsSpan(layout.HandStart, n), upperCapacity);
                    budget.Spend(low.Length);
                    if (low.AsSpan().SequenceEqual(high)) {
                        Accumulate(low, conditioned, destination);
                        return;
                    }
                    EnumeratePool(known, physical, conditioned, choose[availableInventory][physical], (counts, allocated) => {
                        int[] available = known.AsSpan(0, n).ToArray();
                        for (int index = 0; index < poolRows.Length; index++) {
                            available[poolRows[index]] = counts[index];
                        }
                        budget.Spend(n + poolRows.Length);
                        bool[] success = counter.Evaluate(known.AsSpan(layout.HandStart, n), available);
                        Accumulate(success, allocated, destination);
                    }, activeCells + known.Length + 3L * n + (rows.Count + 1L) * (WorkBudget.IntegerCells(denominator) + 1));
                }
            }

            void Accumulate(bool[] success, BigInteger mass, BigInteger[] destination) {
                terminalSplits++;
                bool[] matches = new bool[rows.Count];
                matches[0] = success.Any(value => value);
                for (int combo = 0; combo < rows.ComboCount; combo++) {
                    matches[rows.ComboRow(combo)] = success[combo];
                }
                for (int group = 0; group < rows.GroupCount; group++) {
                    matches[rows.GroupRow(group)] = rows.GroupMatches(group, success);
                }
                budget.Spend((rows.Count + 1L) * (WorkBudget.IntegerCells(denominator) + 1) + (long)rows.GroupCount * rows.ComboCount);
                for (int row = 0; row < rows.Count; row++) {
                    if (matches[row]) {
                        destination[row] += mass;
                    }
                }
            }
        }
    }
}
