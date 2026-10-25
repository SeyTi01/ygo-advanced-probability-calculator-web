using System.Numerics;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.DrawResolution;

internal sealed partial class PostDrawCalculation {
    private sealed partial class OrderedResolver {
        // A single source entry with at most one activation and no hand cost
        // makes no identity-sensitive decision between opening and drawn cards.
        // For L = h + d visible labels, k source copies and t = L - k ordinary
        // observations, the prefix mass is
        // (s)_[k] * (C(L,k) - C(d,k)) * t! * product C(c_i,k_i).
        // Choose and order k of the s labeled source copies; select their visible
        // positions with at least one in the opening hand; order the ordinary
        // labels. The first opening source activates exactly once. All other
        // source copies remain distinct held/unseen counts, never activations.
        // Hidden positions remain exchangeable until the terminal resolver.
        internal bool TryEnumerateSingleSource(
            int handSize,
            int copies,
            BigInteger[] factorial,
            Action<int[], int, BigInteger> terminal,
            bool? sourcePresent = null
        ) {
            int source = SingleSource(handSize, copies);
            if (source < 0) {
                return false;
            }
            int drawCount = cards[source].DrawCount!.Value;

            // Source absence and source presence partition every opening hand.
            // A hidden label is never inspected here; the existing terminal
            // resolver still jointly allocates its hidden positions afterward.
            if (sourcePresent != true) {
                Enumerate(0, BigInteger.One);
            }
            if (sourcePresent != false) {
                int visible = handSize + drawCount;
                BigInteger sourceLabels = BigInteger.One;
                BigInteger visiblePositions = BigInteger.One;
                BigInteger drawnPositions = BigInteger.One;
                for (int observedSources = 1; observedSources <= Math.Min(cards[source].Copies, visible); observedSources++) {
                    budget.Spend(3L * WorkBudget.IntegerCells(sourceLabels) + 3L * WorkBudget.IntegerCells(visiblePositions)
                        + 3L * WorkBudget.IntegerCells(drawnPositions) + 3);
                    sourceLabels *= cards[source].Copies - observedSources + 1;
                    visiblePositions = visiblePositions * (visible - observedSources + 1) / observedSources;
                    drawnPositions = observedSources <= drawCount
                        ? drawnPositions * (drawCount - observedSources + 1) / observedSources : BigInteger.Zero;
                    budget.Spend((WorkBudget.IntegerCells(sourceLabels) + 1)
                        * (WorkBudget.IntegerCells(visiblePositions) + WorkBudget.IntegerCells(drawnPositions) + 1));
                    BigInteger prefix = sourceLabels * (visiblePositions - drawnPositions);
                    long prefixCells = WorkBudget.IntegerCells(sourceLabels) + WorkBudget.IntegerCells(visiblePositions)
                        + WorkBudget.IntegerCells(drawnPositions) + WorkBudget.IntegerCells(prefix);
                    Enumerate(observedSources, prefix, prefixCells);
                }
            }
            return true;

            void Enumerate(int observedSources, BigInteger prefix, long prefixCells = 0) {
                bool present = observedSources > 0;
                int length = layout.QueueStart + (present ? 1 : 0);
                owner.CheckStorage(1, length + cards.Count + prefixCells + 1L, "single-source physical batch");
                budget.Spend(length + cards.Count);
                int[] values = new int[length];
                int[] rows = new int[cards.Count - 1];
                int next = 0;
                for (int row = 0; row < cards.Count; row++) {
                    values[row] = cards[row].Copies;
                    if (row != source) {
                        rows[next++] = row;
                    }
                }
                int remaining = copies;
                if (present) {
                    values[source] -= observedSources;
                    values[layout.HandStart + source] = observedSources;
                    values[layout.QueueStart] = source;
                    // Legality used the actual opening size above. With no hand
                    // cost, Activate only consumes the source and sets its normal
                    // OPT, hidden-count, activation and completion metadata.
                    int activationRemaining = copies - handSize;
                    Activate(ref values, layout.QueueStart, ref activationRemaining);
                    values[layout.DrawsLeft] = handSize - observedSources + drawCount;
                    remaining -= observedSources;
                }
                else {
                    values[layout.DrawsLeft] = handSize;
                }

                PhysicalBatchExpansion expansion = new(
                    owner,
                    budget,
                    cards,
                    layout,
                    values,
                    remaining,
                    prefix,
                    rows,
                    0,
                    factorial,
                    prefixCells,
                    0
                );
                budget.SearchDiagnostics?.Add("physical single-source frames");
                while (expansion.TryNext(prefixCells, expansion.Entries, out int[] drawn, out BigInteger weight)) {
                    budget.Spend(1);
                    budget.SearchDiagnostics?.Add("physical single-source children");
                    int unseen = remaining - expansion.Draws;
                    if (Settle(ref drawn, unseen) != ResolutionOutcome.RemovalUnavailable) {
                        terminal(drawn, unseen, weight);
                    }
                }
            }
        }

        internal int SingleSource(int handSize, int copies) {
            int source = -1;
            for (int row = 0; row < cards.Count; row++) {
                budget.Spend(1);
                if (cards[row].Copies <= 0 || cards[row].DrawCount is null) {
                    continue;
                }
                bool singleActivation = cards[row].Copies == 1 || cards[row].MaximumActivations == 1
                    || cards[row].FirstActivationOnly || cards[row].BlocksLaterDraws;
                if (source >= 0 || !singleActivation || cards[row].PreDrawRemovalCount != 0) {
                    return -1;
                }
                source = row;
            }
            if (source < 0 || handSize == 0 || handSize < cards[source].MinimumHandSize) {
                return -1;
            }
            int drawCount = cards[source].DrawCount!.Value;
            int consumption = drawCount + cards[source].PreDrawDeckRemovalCount + cards[source].PostDrawDeckMillCount;
            return consumption <= copies - handSize ? source : -1;
        }
    }
}
