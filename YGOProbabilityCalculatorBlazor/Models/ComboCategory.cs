namespace YGOProbabilityCalculatorBlazor.Models;

public class ComboCategory {
    public CategoryBase BaseCategory { get; }
    public int MinCount { get; }
    public int MaxCount { get; }

    public ComboCategory(CategoryBase baseCategory, int minCount, int maxCount) {
        if (minCount < 0)
            throw new ArgumentOutOfRangeException(nameof(minCount), "Minimum count cannot be negative.");
        if (maxCount < minCount)
            throw new ArgumentOutOfRangeException(nameof(maxCount), "Maximum count cannot be less than minimum count.");

        BaseCategory = baseCategory;
        MinCount = minCount;
        MaxCount = maxCount;
    }
}
