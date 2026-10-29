using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

// Editor transformations address occurrences, not equal values: legacy sessions
// can contain the same requirement object more than once. Typed list positions
// remain stable unless an operation removes an occurrence or changes its kind.
internal static class ComboRequirementEditing {
    public static Combo Append(Combo combo, ComboAlternative requirement, int authoredPosition) {
        Combo updated;
        ComboRequirementPosition added;
        if (requirement.Category is { } category) {
            updated = combo.WithCategories(combo.Categories.Append(category));
            added = new(ComboRequirementKind.Category, updated.Categories.Count - 1);
        }
        else {
            updated = combo.WithCards(combo.Cards.Append(requirement.Card!));
            added = new(ComboRequirementKind.Card, updated.Cards.Count - 1);
        }

        return updated.WithRequirementAt(added, authoredPosition);
    }

    public static Combo AppendGroup(Combo combo, IReadOnlyList<ComboAlternative> alternatives, int authoredPosition) {
        Combo updated = combo.WithAlternativeGroups(combo.AlternativeGroups.Append(new(alternatives)));

        return updated.WithRequirementAt(new(ComboRequirementKind.Group, updated.AlternativeGroups.Count - 1), authoredPosition);
    }

    public static Combo Replace(Combo combo, ComboRequirementPosition position, ComboAlternative replacement) {
        if (position.Kind == ComboRequirementKind.Category && replacement.Category is { } category) {
            List<ComboCategory> categories = combo.Categories.ToList();
            categories[position.Index] = category;

            return combo.WithCategories(categories);
        }
        if (position.Kind == ComboRequirementKind.Card && replacement.Card is { } card) {
            List<ComboCard> cards = combo.Cards.ToList();
            cards[position.Index] = card;

            return combo.WithCards(cards);
        }

        int authoredPosition = combo.RequirementOrder.ToList().IndexOf(position);

        return Append(combo.WithoutRequirement(position), replacement, authoredPosition);
    }

    public static Combo ReplaceAlternative(Combo combo, int groupIndex, int alternativeIndex, ComboAlternative replacement) {
        List<ComboAlternative> alternatives = combo.AlternativeGroups[groupIndex].Alternatives.ToList();
        alternatives[alternativeIndex] = replacement;

        return WithAlternatives(combo, groupIndex, alternatives);
    }

    public static Combo WithAlternatives(Combo combo, int groupIndex, IReadOnlyList<ComboAlternative> alternatives) {
        List<ComboAlternativeGroup> groups = combo.AlternativeGroups.ToList();
        groups[groupIndex] = new(alternatives);

        return combo.WithAlternativeGroups(groups);
    }

    public static Combo RemoveAlternative(Combo combo, int groupIndex, int alternativeIndex) {
        ComboAlternative[] remaining = combo.AlternativeGroups[groupIndex].Alternatives
            .Where((alternative, index) => index != alternativeIndex).ToArray();
        if (remaining.Length > 1) {
            return WithAlternatives(combo, groupIndex, remaining);
        }

        ComboRequirementPosition position = new(ComboRequirementKind.Group, groupIndex);
        if (remaining.Length == 1) {
            return Replace(combo, position, remaining[0]);
        }

        return combo.WithoutRequirement(position);
    }
}
