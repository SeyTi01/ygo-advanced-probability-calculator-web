using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Compilation;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Activation;

internal sealed partial class UnifiedActivationCalculation {
    private List<Card> CompileCards(List<Card> cards, List<Combo> combos) {
        // Preserve the existing canonical removal tie order. Search entries are
        // in the ordinary-before-draw portion, retaining their workspace identity.
        CategoryBase[] categories = combos.SelectMany(combo => combo.AllCategories)
            .SelectMany(requirement => requirement.CategoryBases).Distinct()
            .OrderBy(category => category.Identity, StringComparer.Ordinal).ToArray();
        string[] direct = combos.SelectMany(combo => combo.AllCards).Select(requirement => requirement.CardId)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        List<CardSelector> selectors = combos.SelectMany(combo => combo.AllCategories).Select(requirement => requirement.Selector).ToList();
        foreach (Card card in cards) {
            if (card.PreDrawRemovalCount > 0) {
                selectors.Add(card.PreDrawSelector);
            }
            if (card.PostDrawDiscardCount > 0) {
                selectors.Add(card.PostDrawSelector);
            }
        }
        budget.CheckStorage(selectors.Count, (long)selectors.Count * cards.Count);
        bool[][] filters = selectors.Select(selector => CardSelectorCompiler.Compile(selector, cards, budget)).ToArray();
        List<bool[]> targetFilters = [];
        foreach (Card source in cards.Where(card => card.Search is not null)) {
            foreach (SearchEffectMode mode in source.Search!.Modes ?? [new(source.Search.Selector)]) {
                budget.Spend(cards.Sum(card => card.Categories.Count + 1L) * (mode.Selector.EvaluationSize + 1L));
                budget.CheckStorage(filters.Length + targetFilters.Count + 1L,
                    (filters.Length + targetFilters.Count + 1L) * cards.Count);
                targetFilters.Add(cards.Select(card => card.Id != source.Id && source.Search.Matches(card, mode.Selector)).ToArray());
            }
        }
        long signatureCells = ((2L * (categories.Length + direct.Length) + filters.Length + targetFilters.Count + 3) / 2) * cards.Count;
        budget.CheckStorage(cards.Count + filters.Length + targetFilters.Count,
            signatureCells + (filters.Length + (long)targetFilters.Count) * cards.Count);
        List<(Card Card, string Order, string Signature)> entries = [];
        for (int row = 0; row < cards.Count; row++) {
            Card card = cards[row];
            budget.Spend(categories.Length * (card.Categories.Count + 1L) + direct.Length + filters.Length + targetFilters.Count + 1L);
            string order = string.Concat(categories.Select(category => card.Categories.Contains(category) ? '1' : '0'))
                + string.Concat(direct.Select(id => card.Id == id ? '1' : '0'));
            string signature = order + ":" + string.Concat(filters.Select(filter => filter[row] ? '1' : '0'))
                + ":" + string.Concat(targetFilters.Select(filter => filter[row] ? '1' : '0'));
            entries.Add((card, order, signature));
        }
        budget.CheckStorage(cards.Count + filters.Length + targetFilters.Count,
            entries.Sum(entry => (entry.Order.Length + entry.Signature.Length + 1L) / 2)
            + (filters.Length + (long)targetFilters.Count) * cards.Count);
        List<Card> result = [];
        string? previous = null;
        foreach ((Card card, string _, string signature) in entries.OrderBy(entry => entry.Order, StringComparer.Ordinal)
            .ThenBy(entry => entry.Card.DrawCount is not null).ThenBy(entry => entry.Card.Id, StringComparer.Ordinal)) {
            // Only adjacent, entirely inert entries with identical observations
            // can merge. Never merge an effect identity, even after allowance use.
            if (!card.HasEffect && result.Count > 0 && !result[^1].HasEffect && previous == signature) {
                result[^1] = result[^1].WithCopies(checked(result[^1].Copies + card.Copies));
            }
            else {
                result.Add(card);
            }
            previous = signature;
        }
        return result;
    }

    private void CompilePolicy() {
        int rows = _cards.Count;
        // Both selector matrices and their row descriptors are retained together.
        // Reject an oversized descriptor set before any quadratic allocation.
        long policyCells = 2L * rows * rows + 4L * rows;
        budget.CheckStorage(3L * rows, policyCells);
        budget.Spend(_cards.Sum(card => card.Categories.Count + 1L)
            * (_combos.Sum(combo => combo.AllCategories.Sum(requirement => (long)requirement.Selector.EvaluationSize)
                + combo.AllCards.Count() + 1L) + 1));
        _relevant = _cards.Select(card => _combos.Any(combo =>
            combo.AllCards.Any(requirement => requirement.MinCount > 0 && requirement.CardId == card.Id)
            || combo.AllCategories.Any(requirement => requirement.MinCount > 0 && requirement.Selector.Matches(card)))).ToArray();
        _costEligible = _cards.Select(card => CardSelectorCompiler.Compile(card.PreDrawSelector, _cards, budget)).ToArray();
        _removalEligible = _cards.Select(card => CardSelectorCompiler.Compile(card.PostDrawSelector, _cards, budget)).ToArray();
        List<SearchMode> modes = [];
        long modeCells = 0;
        for (int source = 0; source < rows; source++) {
            if (_cards[source].Search is not { } search) {
                continue;
            }
            foreach (SearchEffectMode mode in search.Modes ?? [new(search.Selector)]) {
                long matchingWork = _cards.Sum(card => card.Categories.Count + 1L) * (mode.Selector.EvaluationSize + 1L);
                budget.Spend(matchingWork);
                int count = 0;
                for (int target = 0; target < rows; target++) {
                    if (target != source && search.Matches(_cards[target], mode.Selector)) {
                        count++;
                    }
                }
                // Include each mode's descriptor and its source-index mapping.
                long nextModeCells = modeCells + count + 4L;
                budget.CheckStorage(3L * rows + modes.Count + 1, policyCells + nextModeCells);
                int[] targets = new int[count];
                budget.Spend(matchingWork);
                int position = 0;
                for (int target = 0; target < rows; target++) {
                    if (target != source && search.Matches(_cards[target], mode.Selector)) {
                        targets[position++] = target;
                    }
                }
                modes.Add(new(source, mode.ActivationLimit ?? int.MaxValue, targets));
                modeCells = nextModeCells;
            }
        }
        _modes = modes.ToArray();
        _sourceModes = Enumerable.Range(0, rows).Select(source => Enumerable.Range(0, _modes.Length)
            .Where(mode => _modes[mode].Source == source).ToArray()).ToArray();
        budget.Spend(rows * (long)rows + _modes.Sum(mode => mode.Targets.Length + 1L));
        budget.CheckStorage(3L * rows + _modes.Length, policyCells + modeCells);
    }
}
