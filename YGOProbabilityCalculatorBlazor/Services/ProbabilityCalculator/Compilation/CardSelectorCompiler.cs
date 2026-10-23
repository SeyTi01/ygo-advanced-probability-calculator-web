using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Compilation;

internal static class CardSelectorCompiler {
    internal static bool[] Compile(CardSelector selector, IReadOnlyList<Card> cards, WorkBudget budget) {
        long selectorCells = selector.IsGrouped ? selector.EvaluationSize
            : selector.CategoryIdentities.Count + selector.Children.Sum(child => (long)child.CategoryIdentities.Count);
        budget.CheckStorage(cards.Count, cards.Count + selectorCells);
        bool[] eligible = new bool[cards.Count];
        for (int i = 0; i < cards.Count; i++) {
            budget.Spend(1L + selectorCells * (cards[i].Categories.Count + 1L));
            eligible[i] = selector.Matches(cards[i]);
        }
        return eligible;
    }
}
