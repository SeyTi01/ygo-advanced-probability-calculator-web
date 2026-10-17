namespace YGOProbabilityCalculatorBlazor.Models;

// Explicit workspace edits only. Empty eligibility predicates match no cards;
// empty combos are disabled rather than becoming unconditional successes.
public static class CategoryRemoval {
    public static bool IsUsed(CategoryBase definition, IEnumerable<Card> cards, IEnumerable<Combo> combos) =>
        cards.Any(card => CategoryMembership.IsAutomatic(card, definition)) || IsReferenced(definition.Identity, cards, combos);

    private static bool IsReferenced(string identity, IEnumerable<Card> cards, IEnumerable<Combo> combos) =>
        cards.Any(card => card.Categories.Any(category => category.Identity == identity)
            || card.PreDrawSelector.CategoryIdentities.Contains(identity)
            || card.PostDrawSelector.CategoryIdentities.Contains(identity)
            || (card.Search?.Selector.CategoryIdentities.Contains(identity) ?? false)
            || (card.Search?.Modes?.Any(mode => mode.Selector.CategoryIdentities.Contains(identity)) ?? false))
        || combos.Any(combo => combo.AllCategories.Any(category => category.Selector.CategoryIdentities.Contains(identity)));

    public static Card Remove(Card card, string identity) {
        bool membership = card.Categories.Any(category => category.Identity == identity);
        bool filter = card.PreDrawSelector.CategoryIdentities.Contains(identity)
            || card.PostDrawSelector.CategoryIdentities.Contains(identity)
            || (card.Search?.Selector.CategoryIdentities.Contains(identity) ?? false)
            || (card.Search?.Modes?.Any(mode => mode.Selector.CategoryIdentities.Contains(identity)) ?? false);
        if (!membership && !filter) {
            return card;
        }

        Card result = membership ? card.WithCategories(card.Categories.Where(category => category.Identity != identity)) : card;
        if (!filter) {
            return result;
        }

        SearchEffectPlan? search = card.Search;
        if (search is not null) {
            SearchEffectMode[]? modes = search.Modes?.Select(mode => mode with { Selector = RemoveFilter(mode.Selector, identity) }).ToArray();
            search = search with {
                Selector = modes is null ? RemoveFilter(search.Selector, identity) : CardSelector.AnyOfSelectors(modes.Select(mode => mode.Selector)),
                Modes = modes
            };
        }

        return result.WithEffect(card.EffectPlan with {
            HandCostSelector = RemoveFilter(card.PreDrawSelector, identity),
            RemovalSelector = RemoveFilter(card.PostDrawSelector, identity),
            Search = search
        }, card.EffectProfile.AsCustom());
    }

    public static Combo Remove(Combo combo, string identity) {
        if (!combo.AllCategories.Any(category => category.Selector.CategoryIdentities.Contains(identity))) {
            return combo;
        }

        Combo updated = combo;
        for (int index = combo.Categories.Count - 1; index >= 0; index--) {
            ComboCategory original = combo.Categories[index];
            ComboCategory? remaining = RemoveRequirement(original, identity);
            if (remaining is null) {
                updated = updated.WithoutRequirement(new(ComboRequirementKind.Category, index));
            }
            else if (!ReferenceEquals(original, remaining)) {
                List<ComboCategory> categories = updated.Categories.ToList();
                categories[index] = remaining;
                updated = updated.WithCategories(categories);
            }
        }
        for (int index = combo.AlternativeGroups.Count - 1; index >= 0; index--) {
            ComboAlternativeGroup group = combo.AlternativeGroups[index];
            List<ComboAlternative> alternatives = [];
            foreach (ComboAlternative alternative in group.Alternatives) {
                if (alternative.Category is not { } category) {
                    alternatives.Add(alternative);
                }
                else if (RemoveRequirement(category, identity) is { } remaining) {
                    alternatives.Add(ReferenceEquals(category, remaining) ? alternative : ComboAlternative.For(remaining));
                }
            }
            if (alternatives.Count == 0) {
                updated = updated.WithoutRequirement(new(ComboRequirementKind.Group, index));
            }
            else if (!alternatives.SequenceEqual(group.Alternatives)) {
                List<ComboAlternativeGroup> groups = updated.AlternativeGroups.ToList();
                groups[index] = new(alternatives);
                updated = updated.WithAlternativeGroups(groups);
            }
        }

        return updated.WithActive(combo.Active && updated.RequirementCount > 0);
    }

    private static ComboCategory? RemoveRequirement(ComboCategory requirement, string identity) {
        if (!requirement.Selector.CategoryIdentities.Contains(identity)) {
            return requirement;
        }
        CategoryBase[] remaining = requirement.CategoryBases.Where(category => category.Identity != identity).ToArray();
        return remaining.Length == 0 ? null : ComboCategory.SameCard(remaining, requirement.MinCount, requirement.MaxCount, requirement.MaximumMode);
    }

    private static CardSelector RemoveFilter(CardSelector selector, string identity) =>
        RemoveTerm(selector, identity) ?? CardSelector.Not(CardSelector.Any);

    private static CardSelector? RemoveTerm(CardSelector selector, string identity) {
        if (!selector.CategoryIdentities.Contains(identity)) {
            return selector;
        }
        if (selector.Kind == CardSelectorKind.Category) {
            return null;
        }
        CardSelector[] children = selector.AuthoredTerms.Where(child => !ReferenceEquals(child, selector))
            .Select(child => RemoveTerm(child, identity)).OfType<CardSelector>().ToArray();
        if (children.Length == 0) {
            return null;
        }
        return selector.Kind switch {
            CardSelectorKind.Objective => CardSelector.Objective(children[0]),
            CardSelectorKind.Not => CardSelector.Not(children[0]),
            CardSelectorKind.AllOf => CardSelector.AllOfSelectors(children),
            _ => CardSelector.AnyOfSelectors(children)
        };
    }
}
