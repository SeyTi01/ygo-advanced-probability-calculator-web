using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

public class ProbabilityCalculatorService : IProbabilityCalculatorService
{
    public double CalculateProbabilityForCombos(
        List<Card> deck,
        List<Combo> combos,
        int handSize
    ) => CalculateProbabilityForCombos(deck, combos, handSize, CalculationWorkPolicy.Default);

    public double CalculateProbabilityForCombos(
        List<Card> deck,
        List<Combo> combos,
        int handSize,
        CalculationWorkPolicy workPolicy
    )
    {
        ArgumentNullException.ThrowIfNull(workPolicy);
        ValidateComboCount(combos);
        ValidateCardIds(deck);

        return new ProbabilityCalculation(deck, handSize, workPolicy).CalculateUnion(combos);
    }

    public ProbabilityCalculationResult CalculateProbabilityResults(
        List<Card> deck,
        List<Combo> combos,
        int handSize,
        IReadOnlyList<ComboGroup>? groups = null
    ) => CalculateProbabilityResults(deck, combos, handSize, groups, CalculationWorkPolicy.Default);

    public ProbabilityCalculationResult CalculateProbabilityResults(
        List<Card> deck,
        List<Combo> combos,
        int handSize,
        IReadOnlyList<ComboGroup>? groups,
        CalculationWorkPolicy workPolicy
    )
    {
        ArgumentNullException.ThrowIfNull(workPolicy);
        ValidateComboCount(combos);
        ValidateCardIds(deck);

        return new ProbabilityCalculation(deck, handSize, workPolicy).CalculateResults(
            combos,
            groups
        );
    }

    private static void ValidateComboCount(List<Combo> combos)
    {
        if (combos.Count > IProbabilityCalculatorService.MaxComboCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(combos),
                $"Calculation supports at most {IProbabilityCalculatorService.MaxComboCount} combos."
            );
        }
    }

    private static void ValidateCardIds(List<Card> deck)
    {
        if (deck.Select(card => card.Id).Distinct(StringComparer.Ordinal).Count() != deck.Count)
        {
            throw new ArgumentException("Deck card IDs must be unique.", nameof(deck));
        }
    }
}
