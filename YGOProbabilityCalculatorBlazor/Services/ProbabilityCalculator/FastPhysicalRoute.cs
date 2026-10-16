using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

// A measured, deliberately small domain; this is a cost estimate, never a
// semantic proof. SearchAccessCalculation owns applicability and target survival.
// No retry, speculative solver, timer, cache, or separate resource allowance.
internal static class FastPhysicalRoute {
    internal static bool IsCandidate(
        List<Card> cards,
        List<Card> effects,
        int handSize,
        List<Combo> combos,
        IReadOnlyList<ComboGroup>? groups,
        WorkBudget budget
    ) {
        // Bound physical multiplicities as well as entry count: eight rows in a
        // 40-card deck can already make explicit counting much slower than Fast.
        // Reduced user budgets retain the historical primary/certificate behavior.
        if (cards.Count > 6 || handSize is < 1 or > 3 || cards.Sum(card => card.Copies) > 7
            || effects.Count > 2 || combos.Count is < 1 or > 7 || (groups?.Count ?? 0) > 2
            || budget.Allowance < CalculationWorkPolicy.Default.WorkUnits
            || budget.StoragePolicy.EntryLimit < CalculationStoragePolicy.Standard.EntryLimit
            || budget.StoragePolicy.CellLimit < CalculationStoragePolicy.Standard.CellLimit) {
            return false;
        }

        int observations = handSize;
        int hidden = 0;
        int activations = 0;
        int searchCopies = 0;
        foreach (Card card in cards) {
            if (card.Id.Length > 128 || card.Categories.Count > 4
                || card.Categories.Any(category => category.Identity.Length > 128)
                || card.Search?.Selector.EvaluationSize > 8 || card.Search?.Selector.Signature.Length > 512) {
                return false;
            }
            if (card.Search is not null) {
                searchCopies += card.Copies;
            }
        }
        foreach (Card effect in effects) {
            int count = Math.Min(effect.MaximumActivations, effect.Copies);
            activations += count;
            observations += count * effect.DrawCount!.Value;
            hidden += count * (effect.PreDrawDeckRemovalCount + effect.PostDrawDeckMillCount);
            if (effect.PreDrawSelector.EvaluationSize > 8 || effect.PostDrawSelector.EvaluationSize > 8
                || effect.PreDrawSelector.Signature.Length > 512 || effect.PostDrawSelector.Signature.Length > 512) {
                return false;
            }
        }
        if (searchCopies > 1 || activations > 2 || observations > 5 || hidden > 2) {
            return false;
        }

        foreach (Combo combo in combos) {
            if (combo.RequirementCount > 2 || combo.AlternativeGroups.Count > 1
                || combo.AlternativeGroups.Any(group => group.Alternatives.Count > 2)
                || combo.AllCategories.Any(category => category.Selector.EvaluationSize > 4 || category.Selector.Signature.Length > 512)
                || combo.AllCards.Any(card => card.CardId.Length > 128)) {
                return false;
            }
        }

        return true;
    }
}
