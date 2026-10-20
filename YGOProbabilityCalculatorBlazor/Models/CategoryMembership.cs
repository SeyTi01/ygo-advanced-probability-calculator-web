namespace YGOProbabilityCalculatorBlazor.Models;

// Workspace cards contain explicit assignments only. These projections are never
// written back into the workspace or serialized as session card memberships.
public static class CategoryMembership {
    public const int MaximumAlternatives = 16;
    public const int MaximumPropertiesPerAlternative = 16;

    public static void ValidateRule(CardSelector rule) {
        ArgumentNullException.ThrowIfNull(rule);
        bool IsProperty(CardSelector selector) => selector.Kind is CardSelectorKind.NumericRange or CardSelectorKind.LevelAtLeast
            || selector.Kind == CardSelectorKind.Category
                && selector.CategoryIdentities[0].StartsWith("metadata:", StringComparison.Ordinal)
                && !selector.CategoryIdentities[0].StartsWith("metadata:numeric:", StringComparison.Ordinal);
        bool IsAlternative(CardSelector selector) => IsProperty(selector)
            || selector.Kind == CardSelectorKind.AllOf && selector.AuthoredTerms.Count() <= MaximumPropertiesPerAlternative
                && selector.AuthoredTerms.All(IsProperty);
        CardSelector[] alternatives = rule.Alternatives.ToArray();
        if (alternatives.Length > MaximumAlternatives || !alternatives.All(IsAlternative)) {
            throw new ArgumentException("A rule needs metadata or numeric properties only, with at most 16 alternatives and 16 properties per alternative.", nameof(rule));
        }
    }

    public static bool IsAutomatic(Card card, CategoryBase category) => category.Rule?.Matches(card) == true;

    public static IReadOnlyList<CategoryBase> EffectiveCategories(Card card, IEnumerable<CategoryBase> definitions) =>
        card.Categories.Concat(definitions.Where(category => IsAutomatic(card, category)))
            .DistinctBy(category => category.Identity).ToArray();

    public static IReadOnlyList<Card> Materialize(IEnumerable<Card> cards, IEnumerable<CategoryBase> definitions) {
        CategoryBase[] rules = definitions.Where(category => category.Rule is not null).ToArray();
        return cards.Select(card => rules.Length == 0 ? card : card.WithCategories(EffectiveCategories(card, rules))).ToArray();
    }
}
