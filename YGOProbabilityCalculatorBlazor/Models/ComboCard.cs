namespace YGOProbabilityCalculatorBlazor.Models;

public class ComboCard
{
    public string CardId { get; }
    public int MinCount { get; }
    public int MaxCount { get; }
    public RequirementMaximumMode MaximumMode { get; }

    public int GetEffectiveMaximum(int handSize) =>
        MaximumMode == RequirementMaximumMode.HandSize ? handSize : MaxCount;

    public ComboCard(
        string cardId,
        int minCount,
        int maxCount,
        RequirementMaximumMode maximumMode = RequirementMaximumMode.Fixed
    )
    {
        if (! Enum.IsDefined(maximumMode))
        {
            throw new ArgumentOutOfRangeException(nameof(maximumMode));
        }

        if (string.IsNullOrWhiteSpace(cardId))
        {
            throw new ArgumentException("Card ID is required.", nameof(cardId));
        }

        if (minCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minCount));
        }

        if (maxCount < 0 || (maximumMode == RequirementMaximumMode.Fixed && maxCount < minCount))
        {
            throw new ArgumentOutOfRangeException(nameof(maxCount));
        }

        CardId = cardId;
        MinCount = minCount;
        MaxCount = maxCount;
        MaximumMode = maximumMode;
    }
}
