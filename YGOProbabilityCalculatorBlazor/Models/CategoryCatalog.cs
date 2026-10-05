namespace YGOProbabilityCalculatorBlazor.Models;

public static class CategoryCatalog
{
    public static IReadOnlyList<CategoryBase> Build(
        IEnumerable<CategoryBase> categories,
        IEnumerable<Card> cards,
        IEnumerable<Combo> combos) => categories
        .Where(category => category.Source == CategorySource.User)
        .Concat(categories.Concat(cards.SelectMany(card => card.Categories))
            .Concat(combos.SelectMany(combo => combo.AllCategories).Select(requirement => requirement.BaseCategory))
            .Where(category => category.Source == CategorySource.Metadata)
            .DistinctBy(category => category.Identity)
            .OrderBy(category => category.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(category => category.Identity, StringComparer.Ordinal))
        .ToArray();
}
