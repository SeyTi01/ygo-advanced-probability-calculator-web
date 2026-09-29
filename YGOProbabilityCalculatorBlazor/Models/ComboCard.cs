namespace YGOProbabilityCalculatorBlazor.Models;

public class ComboCard {
    public string CardId { get; }
    public int MinCount { get; }
    public int MaxCount { get; }

    public ComboCard(string cardId, int minCount, int maxCount) {
        if (string.IsNullOrWhiteSpace(cardId)) throw new ArgumentException("Card ID is required.", nameof(cardId));
        if (minCount < 0) throw new ArgumentOutOfRangeException(nameof(minCount));
        if (maxCount < minCount) throw new ArgumentOutOfRangeException(nameof(maxCount));
        CardId = cardId;
        MinCount = minCount;
        MaxCount = maxCount;
    }
}
