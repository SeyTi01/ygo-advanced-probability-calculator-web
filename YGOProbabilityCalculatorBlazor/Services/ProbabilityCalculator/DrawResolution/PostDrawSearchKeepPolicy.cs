using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.DrawResolution;

internal sealed partial class PostDrawCalculation {
    private HashSet<string> SearchPolicyObservedRows(List<Card> cards, bool deferUnremovableSearchers = false) {
        HashSet<string> observed = new(StringComparer.Ordinal);
        Card[] effects = cards.Where(card => card.Copies > 0 && card.DrawCount is not null).ToArray();
        foreach (Card source in cards.Where(card => card.Copies > 0 && card.Search is not null)) {
            // An unremovable searcher cannot act during draws. Its label may be
            // pooled unless another removable searcher's retention tier observes
            // it as a target; the target loop below retains those observations.
            if (!deferUnremovableSearchers) {
                observed.Add(source.Id);
            }
            budget.Spend(effects.Sum(effect => effect.PreDrawSelector.CategoryIdentities.Count
                + effect.PostDrawSelector.CategoryIdentities.Count + 1L) * (source.Categories.Count + 1L));
            bool removable = effects.Any(effect => (effect.PreDrawRemovalCount > 0 && effect.PreDrawSelector.Matches(source))
                || (effect.PostDrawDiscardCount > 0 && effect.PostDrawSelector.Matches(source)));
            if (!removable) {
                continue;
            }
            budget.Spend(cards.Sum(card => card.Categories.Count + 1L) * (source.Search!.Selector.CategoryIdentities.Count + 1L));
            foreach (Card target in cards) {
                if (source.Id != target.Id && source.Search!.Matches(target)) {
                    observed.Add(target.Id);
                }
            }
        }
        CheckStorage(observed.Count, observed.Count, "search policy observations");

        return observed;
    }

    // Request-local target descriptors over existing hand rows, never remaining
    // target identities. Reduction preserves search membership AND relevance.
    // Sources stay separate; a target row can still represent equivalent labels.
    private sealed class SearchKeepPolicy {
        private readonly (int Row, int Copies, bool Relevant)[][] _targets;
        private readonly WorkBudget _budget;

        internal SearchKeepPolicy(List<Card> cards, bool[] relevant, WorkBudget budget) {
            _budget = budget;
            budget.CheckStorage(cards.Count, 3L * cards.Count * cards.Count + cards.Count);
            _targets = new (int, int, bool)[cards.Count][];
            for (int source = 0; source < cards.Count; source++) {
                if (cards[source].Copies <= 0 || cards[source].Search is not { } search) {
                    _targets[source] = [];
                    continue;
                }
                budget.Spend(cards.Sum(card => card.Categories.Count + 1L) * (search.Selector.CategoryIdentities.Count + 1L));
                _targets[source] = cards.Select((card, row) => (Card: card, Row: row))
                    .Where(target => target.Row != source && search.Matches(target.Card))
                    .Select(target => (target.Row, target.Card.Copies, relevant[target.Row])).ToArray();
            }
        }

        internal int Tier(int source, int[] values, int handStart, int capacity) {
            if (capacity <= 0) {
                return 4;
            }
            int tier = 4;
            foreach ((int row, int copies, bool relevant) in _targets[source]) {
                _budget.Spend(1);
                if (values[handStart + row] < copies) {
                    if (relevant) {
                        return 1;
                    }
                    tier = 2;
                }
            }

            // Individual plausibility, not a joint search assignment. Competing
            // sources retain canonical ties; final counting enforces shared copies
            // and capacity. A held target is unavailable until it leaves the hand.
            return tier;
        }
    }
}
