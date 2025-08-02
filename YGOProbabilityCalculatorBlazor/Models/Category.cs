namespace YGOProbabilityCalculatorBlazor.Models;

public record Category {
    public string Name { get; }
    public int MinCount { get; }
    public int MaxCount { get; }

    public Category(string name, int minCount, int maxCount) {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Category cannot be empty.", nameof(name));
        if (minCount < 0)
            throw new ArgumentOutOfRangeException(nameof(minCount), "Minimum count cannot be negative.");
        if (maxCount < minCount)
            throw new ArgumentOutOfRangeException(nameof(maxCount), "Maximum count cannot be less than minimum count.");

        Name = name;
        MinCount = minCount;
        MaxCount = maxCount;
    }
}
