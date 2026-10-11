using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

internal static class CardSelectorCompiler {
    internal static bool[] Compile(CardSelector selector, IReadOnlyList<Card> cards, WorkBudget budget) {
        budget.CheckStorage(cards.Count, cards.Count + (long)selector.CategoryIdentities.Count);
        bool[] eligible = new bool[cards.Count];
        for (int i = 0; i < cards.Count; i++) {
            budget.Spend(1L + selector.CategoryIdentities.Count * (cards[i].Categories.Count + 1L));
            eligible[i] = selector.Kind switch {
                CardSelectorKind.Any => true,
                CardSelectorKind.Category or CardSelectorKind.AnyOf => selector.CategoryIdentities.Any(identity =>
                    cards[i].Categories.Any(category => category.Identity == identity)),
                _ => throw new ArgumentOutOfRangeException(nameof(selector))
            };
        }
        return eligible;
    }
}
