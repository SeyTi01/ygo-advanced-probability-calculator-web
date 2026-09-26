namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

public static class CategoryColorPalette {
    public const int PaletteSize = 12;

    public static string GetCssClass(string categoryName, IReadOnlyDictionary<string, int> colorIndices) {
        var colorIndex = colorIndices.TryGetValue(categoryName, out var assignedIndex) && assignedIndex >= 0
            ? assignedIndex
            : 0;

        return $"category-color-{colorIndex % PaletteSize}";
    }

    public static int FirstAvailableIndex(IReadOnlySet<int> assignedIndices) {
        var candidate = 0;
        while (assignedIndices.Contains(candidate)) candidate++;
        return candidate;
    }
}
