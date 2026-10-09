namespace YGOProbabilityCalculatorBlazor.Models;

public static class CategoryCatalog {
    public static IReadOnlyList<CategoryBase> Build(
        IEnumerable<CategoryBase> categories,
        IEnumerable<Card> cards,
        IEnumerable<Combo> combos
    ) {
        CategoryBase[] categoryBases = [.. categories];

        IEnumerable<CategoryBase> metadataCategories = categoryBases
            .Concat(cards.SelectMany(static card => card.Categories))
            .Concat(combos.SelectMany(static combo => combo.AllCategories)
                .Select(static requirement => requirement.BaseCategory))
            .Where(static category => category.Source == CategorySource.Metadata)
            .DistinctBy(static category => category.Identity)
            .OrderBy(static category => category.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static category => category.Identity, StringComparer.Ordinal);

        return categoryBases
            .Where(static category => category.Source == CategorySource.User)
            .Concat(metadataCategories)
            .ToArray();
    }
}
