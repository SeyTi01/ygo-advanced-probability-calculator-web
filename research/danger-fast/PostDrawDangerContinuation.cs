// Research only: opt-in compilation through Research.targets.
using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Counting;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.DrawResolution;

internal sealed partial class PostDrawCalculation {
    internal sealed record ContinuationResult(ulong[] Lower, ulong[] Upper, ulong Scale,
        long Expanded, long Hits, long Evicted, string Stop, long FinalizationWork, long CacheCells);

    internal ContinuationResult ResearchContinuation(List<Combo> combos, IReadOnlyList<ComboGroup> groups, int capacity, int horizon = 8) {
        if (capacity < 1 || horizon < 0 || deck.Any(card => !card.Active || card.Copies < 0)
            || deck.Sum(card => card.Copies) > byte.MaxValue || deck.Count > byte.MaxValue
            || deck.Any(card => card.PreDrawDeckRemovalCount > 0 || card.PostDrawDeckMillCount > 0
                || card.EffectPlan.RequiresSearchResolution)) {
            throw new NotSupportedException("Research continuation supports active, byte-packable inputs without hidden losses or bounded search plans.");
        }
        List<Card> cards = ReduceSearchDeck(combos, out _, merge: false);
        cards = CoalescePhysicalSearchDeck(cards, combos);
        OrderedDrawPreparation preparation = PrepareOrderedDraw(cards, combos);
        (bool[]?[] removal, bool[]?[]? cost) = CompileHandOperations(cards);
        OrderedResolver resolver = new(this, budget, cards, preparation.Layout, preparation.CustomPriority,
            preparation.Ranks, preparation.Relevant, removal, cost, new SearchKeepPolicy(cards, preparation.Relevant, budget));
        PhysicalSearchHandCounter counter = new(cards, combos, budget, memoize: true);
        return resolver.Continuation(handSize, preparation.Copies, new(combos, groups), counter, capacity, horizon);
    }

    private sealed partial class OrderedResolver {
        private sealed record ContinuationBounds(ulong[] Lower, ulong[] Upper);
        private sealed record ContinuationEdge(State State, BigInteger Weight, long Order);
        private sealed record ContinuationCache(ContinuationBounds Bounds, LinkedListNode<State> Node, long Cells);

        internal ContinuationResult Continuation(int handSize, int copies,
            CalculationResultRows rows, PhysicalSearchHandCounter counter, int capacity, int horizon) {
            const ulong precision = 1UL << 48;
            BigInteger randomScale = RandomScale(copies, handSize);
            Dictionary<State, ContinuationCache> cache = new();
            LinkedList<State> recent = new();
            long cacheCells = 0;
            long peakCacheCells = 0;
            long expanded = 0;
            long hits = 0;
            long evicted = 0;
            long finalization = 0;
            long retainedFrames = 0;
            bool stopped = false;
            string stop = "completed traversal";
            int n = cards.Count;
            int[] initial = new int[layout.QueueStart];
            for (int row = 0; row < n; row++) {
                initial[row] = cards[row].Copies;
            }
            initial[layout.DrawsLeft] = handSize;
            ContinuationBounds result = Visit(new(DrawFrontierPacking.Pack(initial)), copies);
            return new(result.Lower, result.Upper, precision, expanded, hits, evicted, stop, finalization, peakCacheCells);

            ContinuationBounds Unknown() => new(new ulong[rows.Count], Enumerable.Repeat(precision, rows.Count).ToArray());

            ContinuationBounds Visit(State key, int remaining) {
                // All cache values are probabilities conditional on this full state.
                // They never carry another caller's incoming probability mass.
                ContinuationBounds answer = Unknown();
                BigInteger[] lower = new BigInteger[rows.Count];
                BigInteger[] upper = new BigInteger[rows.Count];
                BigInteger pending = 0;
                BigInteger denominator = remaining * randomScale;
                bool expandedCompletely = false;
                long frameCells = 0;
                try {
                    budget.Spend(key.Values.Length + 6L * rows.Count + 1);
                    long finalCharge = rows.Count * (WorkBudget.IntegerCells(denominator * precision) + 2L) * 8;
                    budget.Spend(finalCharge);
                    finalization += finalCharge;
                    if (cache.TryGetValue(key, out ContinuationCache? known)) {
                        hits++;
                        recent.Remove(known.Node);
                        recent.AddLast(known.Node);
                        return known.Bounds;
                    }
                    budget.Spend(key.Values[0]);
                    int[] values = DrawFrontierPacking.Unpack(key.Values);
                    if (values[layout.DrawsLeft] == 0) {
                        int[] available = values.AsSpan(0, n).ToArray();
                        bool[] success = counter.Evaluate(values.AsSpan(layout.HandStart, n), available);
                        ulong[] exact = new ulong[rows.Count];
                        exact[0] = success.Any(value => value) ? precision : 0;
                        for (int combo = 0; combo < rows.ComboCount; combo++) {
                            exact[rows.ComboRow(combo)] = success[combo] ? precision : 0;
                        }
                        for (int group = 0; group < rows.GroupCount; group++) {
                            exact[rows.GroupRow(group)] = rows.GroupMatches(group, success) ? precision : 0;
                        }
                        budget.Spend(rows.Count + (long)rows.GroupCount * rows.ComboCount);
                        answer = new(exact, exact);
                        // The physical counter already memoizes terminal predicates
                        // with a smaller key. Reserve this cache for continuations.
                        return answer;
                    }
                    if (copies - remaining >= horizon) {
                        // The fixed horizon belongs to this invocation, hence to
                        // every memoized value. A larger horizon is a new request.
                        return answer;
                    }
                    expanded++;
                    Dictionary<State, ContinuationEdge> children = new();
                    long childCells = 0;
                    long order = 0;
                    // One visible draw followed by exactly the existing deterministic
                    // and random-discard settling. Every edge has probability w/(r*S).
                    for (int row = 0; row < n; row++) {
                        budget.Spend(1);
                        if (values[row] == 0) {
                            continue;
                        }
                        bool candidate = cards[row].HasDrawEffect && TimingAllows(values, row) && !Used(values, row);
                        budget.Spend(values.Length + WorkBudget.IntegerCells(randomScale) + 1);
                        int[] drawn = new int[values.Length + (candidate ? 1 : 0)];
                        values.CopyTo(drawn, 0);
                        drawn[row]--;
                        drawn[layout.HandStart + row]++;
                        drawn[layout.DrawsLeft]--;
                        if (candidate) {
                            drawn[^1] = row;
                        }
                        SettleDanger(drawn, remaining - 1, randomScale * values[row], 0, 0, Add);
                    }
                    // No callback means an unavailable mandatory postdraw removal:
                    // these outcomes are failures for every row. Only after the entire
                    // expansion completes may their mass be omitted from the upper sum.
                    pending = children.Values.Aggregate(BigInteger.Zero, (sum, child) => sum + child.Weight);
                    if (pending > denominator) {
                        throw new InvalidOperationException("Conditional child mass exceeds one.");
                    }
                    expandedCompletely = true;
                    frameCells = values.Length + childCells + rows.Count * (2L * WorkBudget.IntegerCells(denominator * precision) + 4);
                    retainedFrames += frameCells;
                    owner.CheckStorage(remaining + 1L, retainedFrames, "continuation recursive frames");
                    owner.PeakConcurrentCells = Math.Max(owner.PeakConcurrentCells, retainedFrames + cacheCells);
                    budget.Spend(children.Count * (1L + (long)Math.Ceiling(Math.Log2(children.Count + 1)))
                        * (WorkBudget.IntegerCells(denominator) + 1));
                    List<ContinuationEdge> ordered = children.Values.OrderByDescending(child => child.Weight).ThenBy(child => child.Order).ToList();
                    foreach (ContinuationEdge edge in ordered) {
                        long arithmetic = rows.Count * (WorkBudget.IntegerCells(denominator * precision) + 3L) * 8;
                        budget.Spend(arithmetic);
                        finalization += arithmetic;
                        ContinuationBounds child = Visit(edge.State, remaining - 1);
                        // Arithmetic was reserved before descending, so unwinding
                        // after a Work limit cannot discard a completed refinement.
                        for (int row = 0; row < rows.Count; row++) {
                            lower[row] += edge.Weight * child.Lower[row];
                            upper[row] += edge.Weight * child.Upper[row];
                        }
                        pending -= edge.Weight;
                        if (stopped) {
                            break;
                        }
                    }

                    void Add(int[] state, BigInteger weight, int depth, long cells) {
                        budget.Spend(state.Length * 3L + WorkBudget.IntegerCells(weight) + 1);
                        State childKey = new(DrawFrontierPacking.Pack(state));
                        bool found = children.TryGetValue(childKey, out ContinuationEdge? prior);
                        BigInteger combined = weight + (prior?.Weight ?? 0);
                        long updatedCells = childCells + WorkBudget.IntegerCells(combined)
                            - (prior is null ? 0 : WorkBudget.IntegerCells(prior.Weight))
                            + (found ? 0 : childKey.Values.Length + 6);
                        owner.CheckStorage(children.Count + (found ? 0 : 1), updatedCells, "continuation children");
                        children[childKey] = new(childKey, combined, prior?.Order ?? order++);
                        childCells = updatedCells;
                    }
                }
                catch (ProbabilityCalculationLimitException error) {
                    stopped = true;
                    stop = error.Reason.ToString();
                }
                finally {
                    retainedFrames -= frameCells;
                }
                if (expandedCompletely) {
                    for (int row = 0; row < rows.Count; row++) {
                        answer.Lower[row] = (ulong)(lower[row] / denominator);
                        BigInteger numerator = upper[row] + pending * precision;
                        answer.Upper[row] = (ulong)((numerator + denominator - 1) / denominator);
                    }
                }
                if (!stopped) {
                    try {
                        Remember(key, answer);
                    }
                    catch (ProbabilityCalculationLimitException error) {
                        stopped = true;
                        stop = error.Reason.ToString();
                    }
                }
                return answer;
            }

            void Remember(State key, ContinuationBounds bounds) {
                long cells = key.Values.Length + 4L * rows.Count + 10;
                budget.Spend(cells + 1);
                while (cache.Count > 0 && (cache.Count >= capacity || cache.Count >= budget.StoragePolicy.EntryLimit
                    || cacheCells + cells > budget.StoragePolicy.CellLimit)) {
                    State oldest = recent.First!.Value;
                    cacheCells -= cache[oldest].Cells;
                    cache.Remove(oldest);
                    recent.RemoveFirst();
                    evicted++;
                }
                owner.CheckStorage(cache.Count + 1L, cacheCells + cells, "continuation cache including LRU");
                LinkedListNode<State> node = recent.AddLast(key);
                cache.Add(key, new(bounds, node, cells));
                cacheCells += cells;
                peakCacheCells = Math.Max(peakCacheCells, cacheCells);
            }
        }
    }
}
