namespace YGOProbabilityCalculatorBlazor.Models;

// Workspace cards contain explicit assignments only. These projections are never
// written back into the workspace or serialized as session card memberships.
public static class CategoryMembership {
    public const int MaximumAlternatives = 16;
    public const int MaximumPropertiesPerAlternative = 16;

    public static void ValidateRule(CardSelector rule) {
        ArgumentNullException.ThrowIfNull(rule);
        if (rule.Kind == CardSelectorKind.Any
            || rule.CategoryIdentities.Any(identity => !identity.StartsWith("metadata:", StringComparison.Ordinal))
            || rule.Alternatives.Count() > MaximumAlternatives
            || rule.Alternatives.Any(alternative => alternative.CategoryIdentities.Count > MaximumPropertiesPerAlternative)) {
            throw new ArgumentException("A rule needs metadata properties only, with at most 16 alternatives and 16 properties per alternative.", nameof(rule));
        }
    }

    public static bool IsAutomatic(Card card, CategoryBase category) => category.Rule?.Matches(card.Categories) == true;

    public static IReadOnlyList<CategoryBase> EffectiveCategories(Card card, IEnumerable<CategoryBase> definitions) =>
        card.Categories.Concat(definitions.Where(category => IsAutomatic(card, category)))
            .DistinctBy(category => category.Identity).ToArray();

    public static IReadOnlyList<Card> Materialize(IEnumerable<Card> cards, IEnumerable<CategoryBase> definitions) {
        CategoryBase[] rules = definitions.Where(category => category.Rule is not null).ToArray();
        return cards.Select(card => rules.Length == 0 ? card : card.WithCategories(EffectiveCategories(card, rules))).ToArray();
    }
}
