namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

public readonly record struct CategoryColorOption(int Index, string Name)
{
    public string CssClass => $"category-color-{Index}";
}

public static class CategoryColorPalette
{
    public const int PaletteSize = 12;

    private static readonly IReadOnlyList<CategoryColorOption> Palette = Array.AsReadOnly<CategoryColorOption>([
            new(0, "Blue"),
            new(1, "Orange"),
            new(2, "Green"),
            new(3, "Purple"),
            new(4, "Red"),
            new(5, "Cyan"),
            new(6, "Gold"),
            new(7, "Pink"),
            new(8, "Lime"),
            new(9, "Slate"),
            new(10, "Teal"),
            new(11, "Magenta")
        ]
    );

    public static IReadOnlyList<CategoryColorOption> Options => Palette;

    public static bool IsValidIndex(int colorIndex) => colorIndex >= 0 && colorIndex < PaletteSize;

    public static string GetCssClass(string categoryName, IReadOnlyDictionary<string, int> colorIndices)
    {
        int colorIndex = colorIndices.TryGetValue(categoryName, out int assignedIndex) && IsValidIndex(assignedIndex)
            ? assignedIndex
            : 0;

        return Palette[colorIndex].CssClass;
    }

    public static int FirstAvailableIndex(IReadOnlySet<int> assignedIndices, int assignedCategoryCount = 0)
    {
        for (int candidate = 0; candidate < PaletteSize; candidate++)
        {
            if (! assignedIndices.Contains(candidate))
            {
                return candidate;
            }
        }

        // Once every palette slot is used, repeat deterministically while keeping saved indices in range.
        return Math.Max(0, assignedCategoryCount) % PaletteSize;
    }
}
