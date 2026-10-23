using System.Numerics;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.DrawResolution;

internal sealed partial class PostDrawCalculation {
    private sealed partial class OrderedResolver {
        // One physical draw source without a hand cost makes no identity-sensitive
        // decision between the ordinary opening cards and its drawn cards. Given
        // that source in the opening hand, enumerate their combined sample once.
        // For t = h - 1 + d ordinary observations, its labeled prefix mass is
        // h * t! * product C(c_i, k_i): choose the source's opening position and
        // any ordering of the t other labels. The opening/draw split is invisible
        // until the unchanged completion policy sees the complete combined hand.
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
                Enumerate(present: false);
            }
            if (sourcePresent != false) {
                Enumerate(present: true);
            }
            return true;

            void Enumerate(bool present) {
                int length = layout.QueueStart + (present ? 1 : 0);
                owner.CheckStorage(1, length + cards.Count + 1L, "single-source physical batch");
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
                BigInteger prefix = BigInteger.One;
                if (present) {
                    values[source] = 0;
                    values[layout.HandStart + source] = 1;
                    values[layout.QueueStart] = source;
                    // Legality used the actual opening size above. With no hand
                    // cost, Activate only consumes the source and sets its normal
                    // OPT, hidden-count, activation and completion metadata.
                    int activationRemaining = copies - handSize;
                    Activate(ref values, layout.QueueStart, ref activationRemaining);
                    values[layout.DrawsLeft] = handSize - 1 + drawCount;
                    remaining--;
                    prefix = handSize;
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
                    0,
                    0
                );
                budget.SearchDiagnostics?.Add("physical single-source frames");
                while (expansion.TryNext(0, expansion.Entries, out int[] drawn, out BigInteger weight)) {
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
                if (source >= 0 || cards[row].Copies != 1 || cards[row].PreDrawRemovalCount != 0) {
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
