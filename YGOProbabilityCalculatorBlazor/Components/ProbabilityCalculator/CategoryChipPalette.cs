using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

public static class CategoryChipPalette {
    public static string GetCssClass(CategoryBase category, IReadOnlyDictionary<string, int> categoryColorIndices) =>
        category.Source == CategorySource.Metadata
            ? $"card-property-tag {CardPropertyColorPalette.GetCssClass(category)}"
            : CategoryColorPalette.GetCssClass(category.Name, categoryColorIndices);

    public static string GetFallbackCssClass(string identity) =>
        identity.StartsWith("user:", StringComparison.Ordinal)
            ? CategoryColorPalette.Options[0].CssClass
            : $"card-property-tag {CardPropertyColorPalette.GenericMetadataClass}";
}
