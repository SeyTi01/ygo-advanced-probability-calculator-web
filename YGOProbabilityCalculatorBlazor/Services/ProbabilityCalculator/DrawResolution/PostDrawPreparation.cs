using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Compilation;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.DrawResolution;

internal sealed partial class PostDrawCalculation {
    // Request preparation owns canonical policy inputs, never per-state data.
    // Both ordered routes charge the same work in the same sequence. Hand
    // operation tables are deliberately prepared separately, after the
    // no-search exchangeable route has had its opportunity to finish.
    private readonly record struct OrderedDrawPreparation(
        int Copies,
        OrderedDrawStateLayout Layout,
        bool CustomPriority,
        int[] Ranks,
        bool[] Relevant,
        BigInteger[] Factorial
    );

    private OrderedDrawPreparation PrepareOrderedDraw(List<Card> cards, List<Combo> combos, bool trackSearchOrder = false) {
        int n = cards.Count;
        bool customPriority = priority?.Mode == DrawActivationMode.CustomPriority;
        if (customPriority) {
            budget.Spend(n + (long)priority!.CardIds.Count);
            budget.CheckStorage(n, n);
        }
        int[] ranks = customPriority ? cards.Select(card => priority!.Rank(card.Id)).ToArray() : [];
        ReducedRows = n;
        OrderedDrawStateLayout layout = new(cards, trackSearchOrder);
        int copies = 0;
        foreach (Card card in cards) {
            budget.Spend(1);
            copies = checked(copies + card.Copies);
        }
        if (handSize < 0 || handSize > copies) {
            throw new ArgumentOutOfRangeException(nameof(handSize));
        }

        CheckStorage(1, layout.QueueStart + (long)copies + 1, "policy descriptors");
        foreach (Combo combo in combos) {
            long categoryAtoms = combo.AllCategories.Sum(requirement => (long)requirement.Selector.CategoryIdentities.Count);
            long deckMemberships = n + cards.Sum(card => (long)card.Categories.Count);
            budget.Spend((combo.AllCards.Count() + categoryAtoms + 1L) * deckMemberships);
        }
        bool[] relevant = cards.Select(card => combos.Any(combo =>
            combo.AllCards.Any(requirement => requirement.MinCount > 0 && requirement.CardId == card.Id)
            || combo.AllCategories.Any(requirement => requirement.MinCount > 0 && requirement.Selector.Matches(card)))).ToArray();

        CheckStorage(copies + 1L, copies + 1L, "factorials");
        BigInteger[] factorial = new BigInteger[copies + 1];
        factorial[0] = 1;
        long factorialCells = 1;
        for (int i = 1; i <= copies; i++) {
            budget.Spend(WorkBudget.IntegerCells(factorial[i - 1]) + 1);
            factorial[i] = factorial[i - 1] * i;
            factorialCells += WorkBudget.IntegerCells(factorial[i]);
            CheckStorage(copies + 1L, factorialCells, "factorials");
        }

        return new(copies, layout, customPriority, ranks, relevant, factorial);
    }

    private (bool[]?[] Removal, bool[]?[]? Cost) CompileHandOperations(List<Card> cards) {
        int n = cards.Count;
        CheckStorage(n, n, "removal selector table");
        bool[]?[] removalEligibility = new bool[n][];
        long eligibilityCells = n;
        for (int i = 0; i < n; i++) {
            if (cards[i].PostDrawDiscardCount > 0 && cards[i].PostDrawSelector.Kind != CardSelectorKind.Any) {
                eligibilityCells += n;
                CheckStorage(n, eligibilityCells, "removal selector table");
                removalEligibility[i] = CardSelectorCompiler.Compile(cards[i].PostDrawSelector, cards, budget);
            }
        }

        // Keep post-draw-only decks free of cost tables.
        bool[]?[]? costEligibility = null;
        if (cards.Any(card => card.PreDrawRemovalCount > 0)) {
            eligibilityCells += n;
            CheckStorage(n, eligibilityCells, "removal selector table");
            costEligibility = new bool[n][];
            for (int i = 0; i < n; i++) {
                if (cards[i].PreDrawRemovalCount > 0 && cards[i].PreDrawSelector.Kind != CardSelectorKind.Any) {
                    eligibilityCells += n;
                    CheckStorage(n, eligibilityCells, "removal selector table");
                    costEligibility[i] = CardSelectorCompiler.Compile(cards[i].PreDrawSelector, cards, budget);
                }
            }
        }

        return (removalEligibility, costEligibility);
    }

    private readonly record struct ReductionSelectors(CategoryBase[] Categories, string[] Direct, List<bool[]> Filters);

    private ReductionSelectors PrepareReductionSelectors(List<Combo> combos) {
        // Canonical tie order: membership bit string for ordinal category identities,
        // then ordinal direct-card IDs (including maximum-only selectors), ordinary
        // before effect, then ordinal Card.Id, then earlier encounter within an ID.
        // Ordinary cards with equal signatures are indistinguishable to every
        // requirement and to the policy. Their counts can therefore be merged.
        long selectors = 0;
        foreach (Combo combo in combos) {
            selectors += combo.Categories.Sum(requirement => (long)requirement.Selector.CategoryIdentities.Count) + combo.Cards.Count;
            foreach (ComboAlternativeGroup group in combo.AlternativeGroups) {
                selectors += group.Alternatives.Sum(alternative => alternative.Category is { } category
                    ? (long)category.Selector.CategoryIdentities.Count : 1L);
            }
            CheckStorage(selectors, selectors, "policy selectors");
            budget.Spend(combo.RequirementCount + 1L);
        }
        CheckStorage(deck.Count, (selectors + 1) * deck.Count, "policy descriptors");
        budget.Spend(selectors * (selectors + 1) + deck.Count * (long)(deck.Count + 1) * (selectors + 1));
        CategoryBase[] categories = combos.SelectMany(combo => combo.AllCategories)
            .SelectMany(requirement => requirement.CategoryBases).Distinct().OrderBy(category => category.Identity, StringComparer.Ordinal).ToArray();
        string[] direct = combos.SelectMany(combo => combo.AllCards).Select(requirement => requirement.CardId)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        List<bool[]> filters = [];
        foreach (Card source in deck) {
            if (source.Copies > 0 && source.PostDrawDiscardCount > 0 && source.PostDrawSelector.Kind != CardSelectorKind.Any) {
                CheckStorage(filters.Count + 1L, (filters.Count + 1L) * deck.Count, "reduction selectors");
                filters.Add(CardSelectorCompiler.Compile(source.PostDrawSelector, deck, budget));
            }
        }
        foreach (Card source in deck) {
            if (source.Copies > 0 && source.PreDrawRemovalCount > 0 && source.PreDrawSelector.Kind != CardSelectorKind.Any) {
                CheckStorage(filters.Count + 1L, (filters.Count + 1L) * deck.Count, "reduction selectors");
                filters.Add(CardSelectorCompiler.Compile(source.PreDrawSelector, deck, budget));
            }
        }
        return new(categories, direct, filters);
    }

    private List<(Card Card, string Signature, string Eligibility)> CreateReductionEntries(ReductionSelectors selectors) {
        CategoryBase[] categories = selectors.Categories;
        string[] direct = selectors.Direct;
        List<bool[]> filters = selectors.Filters;
        List<(Card Card, string Signature, string Eligibility)> entries = [];
        int row = 0;
        foreach (Card card in deck) {
            ArgumentOutOfRangeException.ThrowIfNegative(card.Copies);
            budget.Spend((categories.Length + 1L) * (card.Categories.Count + 1L) + direct.Length);
            CheckStorage(deck.Count, (categories.Length + (long)direct.Length + 1) * deck.Count, "policy descriptors");
            string signature = string.Concat(categories.Select(category => card.Categories.Contains(category) ? '1' : '0'))
                + string.Concat(direct.Select(id => card.Id == id ? '1' : '0'));
            budget.Spend(filters.Count);
            CheckStorage(deck.Count, (categories.Length + (long)direct.Length + filters.Count + 1) * deck.Count, "policy descriptors");
            entries.Add((card, signature, string.Concat(filters.Select(filter => filter[row] ? '1' : '0'))));
            row++;
        }
        return entries;
    }
}
