using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

internal static class CardOrdering
{
    public static IEnumerable<Card> Alphabetize(IEnumerable<Card> cards) => cards
        .Select((card, deckIndex) => (Card: card, DeckIndex: deckIndex))
        .OrderBy(entry => string.IsNullOrWhiteSpace(entry.Card.Name))
        .ThenBy(entry => string.IsNullOrWhiteSpace(entry.Card.Name) ? string.Empty : entry.Card.Name,
            StringComparer.OrdinalIgnoreCase
        )
        .ThenBy(entry => entry.DeckIndex)
        .Select(entry => entry.Card);
}
