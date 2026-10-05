namespace YGOProbabilityCalculatorBlazor.Models;

public record Category : CategoryBase
{
    public int MinCount { get; }
    public int MaxCount { get; }

    public Category(string name, int minCount, int maxCount) : base(name)
    {
        if (minCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minCount), "Minimum count cannot be negative.");
        }

        if (maxCount < minCount)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCount), "Maximum count cannot be less than minimum count.");
        }

        MinCount = minCount;
        MaxCount = maxCount;
    }
}
