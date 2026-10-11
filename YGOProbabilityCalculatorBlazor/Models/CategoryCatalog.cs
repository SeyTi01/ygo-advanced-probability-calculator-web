namespace YGOProbabilityCalculatorBlazor.Models;

using YGOProbabilityCalculatorBlazor.Services.DeckImport;

public static class CategoryCatalog {
    public static IReadOnlyList<CategoryBase> Build(
        IEnumerable<CategoryBase> categories,
        IEnumerable<Card> cards,
        IEnumerable<Combo> combos
    ) {
        CategoryBase[] input = [.. categories];
        CategoryBase[] built = [.. BuildOptions(input, cards, combos).All];

        return input
            .Concat(built)
            .DistinctBy(static category => category.Identity)
            .ToArray();
    }

    public static CategoryCatalogOptions BuildOptions(
        IEnumerable<CategoryBase> categories,
        IEnumerable<Card> cards,
        IEnumerable<Combo> combos,
        IEnumerable<string>? additionallyRelevantMetadataIdentities = null
    ) {
        CategoryBase[] categoryBases = [.. categories];
        Card[] cardArray = [.. cards];
        Combo[] comboArray = [.. combos];
        HashSet<string> commonIdentities = CardPropertyProvider.CommonCategories
            .Select(static category => category.Identity)
            .ToHashSet(StringComparer.Ordinal);

        CategoryBase[] referencedCategories = categoryBases
            .Where(category => category.Source == CategorySource.Metadata && !commonIdentities.Contains(category.Identity))
            .Concat(cardArray.SelectMany(static card => card.Categories)
                .Where(static category => category.Source == CategorySource.Metadata))
            .Concat(comboArray.SelectMany(static combo => combo.AllCategories)
                .SelectMany(static requirement => requirement.CategoryBases)
                .Where(static category => category.Source == CategorySource.Metadata))
            .DistinctBy(static category => category.Identity)
            .ToArray();

        HashSet<string> relevantIdentities = referencedCategories
            .Where(category => commonIdentities.Contains(category.Identity))
            .Select(static category => category.Identity)
            .ToHashSet(StringComparer.Ordinal);
        relevantIdentities.UnionWith(referencedCategories
            .Where(category => !commonIdentities.Contains(category.Identity))
            .Select(static category => category.Identity)
        );

        foreach (string identity in cardArray
            .SelectMany(static card => SelectorIdentities(card.PreDrawSelector).Concat(SelectorIdentities(card.PostDrawSelector)))
            .Concat(additionallyRelevantMetadataIdentities ?? [])) {
            relevantIdentities.Add(identity);
        }

        CategoryBase[] contextualMetadata = referencedCategories
            .Where(category => !commonIdentities.Contains(category.Identity))
            .OrderBy(static category => category.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static category => category.Identity, StringComparer.Ordinal)
            .ToArray();
        CategoryBase[] currentCommonMetadata = CardPropertyProvider.CommonCategories
            .Where(category => relevantIdentities.Contains(category.Identity))
            .ToArray();
        CategoryBase[] remainingCommonMetadata = CardPropertyProvider.CommonCategories
            .Where(category => !relevantIdentities.Contains(category.Identity))
            .ToArray();

        CategoryBase[] userCategories = categoryBases
            .Where(static category => category.Source == CategorySource.User)
            .ToArray();

        CategoryBase[] relevantMetadata = [.. contextualMetadata, .. currentCommonMetadata];
        return new(userCategories, relevantMetadata, remainingCommonMetadata);
    }

    public static bool IsCommonMetadata(CategoryBase category) =>
        category.Source == CategorySource.Metadata
        && CardPropertyProvider.CommonCategories.Any(common => common.Identity == category.Identity);

    private static IEnumerable<string> SelectorIdentities(CardSelector selector) =>
        selector.Alternatives.SelectMany(static alternative => alternative.CategoryIdentities);
}

public sealed class CategoryCatalogOptions(
    IReadOnlyList<CategoryBase> userCategories,
    IReadOnlyList<CategoryBase> relevantMetadata,
    IReadOnlyList<CategoryBase> remainingCommonMetadata
) {
    public static CategoryCatalogOptions Empty { get; } = new([], [], []);

    public IReadOnlyList<CategoryBase> UserCategories { get; } = userCategories;
    public IReadOnlyList<CategoryBase> RelevantMetadata { get; } = relevantMetadata;
    public IReadOnlyList<CategoryBase> RemainingCommonMetadata { get; } = remainingCommonMetadata;
    public IReadOnlyList<CategoryBase> All { get; } = [.. userCategories, .. relevantMetadata, .. remainingCommonMetadata];
    public IReadOnlySet<string> RelevantMetadataIdentities { get; } = relevantMetadata
        .Select(static category => category.Identity)
        .ToHashSet(StringComparer.Ordinal);
}
