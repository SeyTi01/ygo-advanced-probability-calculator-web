using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Search;

// A successful ordered terminal has D = N - held - remaining losses. Each
// activation contributes exactly 1 + cost + discard + banish + mill to D;
// failed mandatory removals never enter that terminal map. Of these losses,
// only hidden cards, searchable sources and selector-eligible searchable hand
// cards can belong to the union T of original search targets.
//
// Maximize that possible T loss over ALL activation-count vectors with total D,
// bounded only by original copies and per-entry allowances. This superset also
// contains every history merged into a terminal: timing, hand resources, ties
// and priority can only exclude vectors. Thus every target class simultaneously
// has r_i >= max(0, n_i - h_i - table[D]). No lost label is sampled, no held
// payment is restored, and no independence or alternative play policy is used.
internal static class OrderedSearchLossBudget {
    internal static int[] Create(List<Card> cards, WorkBudget budget) {
        budget.Spend(cards.Count + 1L);
        int copies = cards.Sum(card => card.Copies);
        budget.CheckStorage(copies + 1L, copies + 1L + cards.Count);
        bool[] target = new bool[cards.Count];
        foreach (Card source in cards) {
            budget.Spend(1);
            if (source.Copies <= 0 || source.Search is null) {
                continue;
            }
            for (int row = 0; row < cards.Count; row++) {
                budget.Spend((source.Search.Selector.EvaluationSize + 1L) * (cards[row].Categories.Count + 1L));
                target[row] |= cards[row].Copies > 0 && source.Id != cards[row].Id && source.Search.Matches(cards[row]);
            }
        }
        int[] losses = new int[copies + 1];
        budget.Spend(losses.Length);
        Array.Fill(losses, -1);
        losses[0] = 0;
        for (int row = 0; row < cards.Count; row++) {
            budget.Spend(1);
            Card source = cards[row];
            if (source.Copies <= 0 || source.DrawCount is null) {
                continue;
            }
            long hidden = (long)source.PreDrawDeckRemovalCount + source.PostDrawDeckMillCount;
            long total = 1 + hidden + source.PreDrawRemovalCount + source.PostDrawDiscardCount;
            if (total > copies) {
                continue;
            }
            int possible = (int)hidden + (target[row] ? 1 : 0)
                + PossiblePayment(source.PreDrawRemovalCount, source.PreDrawSelector)
                + PossiblePayment(source.PostDrawDiscardCount, source.PostDrawSelector);
            int amount = (int)total;
            int activations = Math.Min(Math.Min(source.Copies, source.MaximumActivations), copies / amount);
            for (int activation = 0; activation < activations; activation++) {
                budget.Spend(copies - amount + 1L);
                for (int lost = copies; lost >= amount; lost--) {
                    if (losses[lost - amount] >= 0) {
                        losses[lost] = Math.Max(losses[lost], losses[lost - amount] + possible);
                    }
                }
            }
        }
        // Unexplained totals have no proof; retain the original scalar bound.
        budget.Spend(losses.Length);
        for (int lost = 0; lost < losses.Length; lost++) {
            if (losses[lost] < 0) {
                losses[lost] = lost;
            }
        }
        return losses;

        int PossiblePayment(int count, CardSelector selector) {
            if (count == 0) {
                return 0;
            }
            for (int row = 0; row < cards.Count; row++) {
                budget.Spend(1);
                if (target[row]) {
                    budget.Spend((1L + selector.NodeCount + selector.CategoryReferenceCount) * (cards[row].Categories.Count + 1L));
                    if (selector.Matches(cards[row])) {
                        return count;
                    }
                }
            }
            return 0;
        }
    }
}
