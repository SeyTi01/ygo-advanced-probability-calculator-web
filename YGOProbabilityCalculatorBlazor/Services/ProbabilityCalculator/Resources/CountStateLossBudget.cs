using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

// Only for completed count-state draws: every batch fits, there are no hand
// operations, and D = sum a_j (1 + hidden_j). For target i, bound its own
// contribution by cap_i (1 + hidden_i). Each OTHER activation contributes at
// most max_j!=i (1 + hidden_j), and consumes one copy outside identity i.
// Thus at least ceil((D - cap_i (1 + hidden_i)) / max_j!=i (1 + hidden_j))
// losses are outside i. Retained OPT duplicates independently prove an
// activation. Subtract only these guaranteed OTHER consumed copies from D.
// Hidden identities are never inferred; a searchable draw source keeps its
// own possible consumption in its loss budget. These inequalities hold jointly
// for every physical outcome represented by the unchanged terminal state.
internal static class CountStateLossBudget {
    internal static int[] For(
        List<Card> cards,
        List<Card> effects,
        DrawComposition composition,
        int lost,
        WorkBudget budget
    ) {
        budget.CheckStorage(cards.Count, cards.Count);
        int[] losses = new int[cards.Count];
        for (int row = 0; row < cards.Count; row++) {
            long ownContribution = 0;
            long maximumOtherContribution = 0;
            int observedOther = 0;
            budget.Spend(effects.Count + 1L);
            for (int effect = 0; effect < effects.Count; effect++) {
                Card source = effects[effect];
                long contribution = 1L + source.PreDrawDeckRemovalCount + source.PostDrawDeckMillCount;
                if (source.Id == cards[row].Id) {
                    int cap = Math.Min(source.MaximumActivations, source.Copies);
                    ownContribution = cap * contribution;
                }
                else {
                    maximumOtherContribution = Math.Max(maximumOtherContribution, contribution);
                    if (composition.EffectCounts[effect] > 0) {
                        observedOther++;
                    }
                }
            }
            long unexplained = Math.Max(0, lost - ownContribution);
            int requiredOther = maximumOtherContribution == 0 ? 0
                : (int)((unexplained + maximumOtherContribution - 1) / maximumOtherContribution);
            losses[row] = lost - Math.Max(observedOther, requiredOther);
        }

        return losses;
    }
}
