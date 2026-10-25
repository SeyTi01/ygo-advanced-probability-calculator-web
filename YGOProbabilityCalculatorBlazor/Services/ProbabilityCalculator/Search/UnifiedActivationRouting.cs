using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Search;

// Keep the established projections only when their draw-then-search phase order
// is equivalent to the selected chronological policy.
internal static class UnifiedActivationRouting {
    internal static bool Required(List<Card> deck, int handSize, DrawActivationPriority? priority, WorkBudget budget) {
        List<Card> cards = deck.Where(card => card.Active && card.Copies > 0).ToList();
        List<Card> searches = cards.Where(card => card.Search is not null).ToList();
        int copies = cards.Sum(card => card.Copies);
        budget.Spend(cards.Count + searches.Count + 1L);
        if (searches.Count == 0 || copies == handSize) {
            return false;
        }

        HashSet<Card> targets = [];
        foreach (Card source in searches) {
            foreach (Card target in cards) {
                budget.Spend((source.Search!.Selector.EvaluationSize + 1L) * (target.Categories.Count + 1L));
                if (source.Id != target.Id && source.Search.Matches(target)) {
                    targets.Add(target);
                }
            }
        }
        budget.CheckStorage(cards.Count + targets.Count, cards.Count + targets.Count);
        if (targets.Count == 0) {
            return false;
        }

        // One-for-one searches commute: original sources are not replenished,
        // target capacities only fall, and hand size stays fixed. Entry/mode
        // allowances and minimum-hand checks therefore preserve every feasible
        // assignment in any priority order. Hand payments/removals break this proof.
        if (searches.Any(card => card.EffectPlan.RequiresHandResolution)) {
            return true;
        }

        HashSet<Card>? inert = DrawEffectFeasibility.FindInert(cards, copies, handSize, budget);
        List<Card> draws = cards.Where(card => card.DrawCount is not null && inert?.Contains(card) != true).ToList();
        if (draws.Count == 0) {
            return false;
        }
        if (priority?.Mode != DrawActivationMode.CustomPriority
            || draws.Any(draw => searches.Any(search => priority.Rank(draw.Id) >= priority.Rank(search.Id)))) {
            return true;
        }

        // All draw ranks precede all search ranks. An unpaid draw may still be
        // revived by a fetched cost card, so exclude that interaction. Searches
        // cannot increase hand size, replenish Deck capacity, clear a draw lock,
        // or reactivate a searched source. All remaining draws finish first.
        foreach (Card draw in draws.Where(card => card.PreDrawRemovalCount > 0)) {
            foreach (Card target in targets) {
                budget.Spend((draw.PreDrawSelector.EvaluationSize + 1L) * (target.Categories.Count + 1L));
                if (draw.PreDrawSelector.Matches(target)) {
                    return true;
                }
            }
        }

        return false;
    }
}
