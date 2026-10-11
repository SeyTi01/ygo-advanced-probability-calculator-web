using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

public class ProbabilityCalculatorService : IProbabilityCalculatorService {
    public double CalculateProbabilityForCombos(List<Card> deck, List<Combo> combos, int handSize) =>
        CalculateProbabilityForCombos(deck, combos, handSize, CalculationWorkPolicy.Default, CalculationStoragePolicy.Standard);

    public double CalculateProbabilityForCombos(List<Card> deck, List<Combo> combos, int handSize, CalculationWorkPolicy workPolicy, CalculationOptions? options = null) =>
        CalculateProbabilityForCombos(deck, combos, handSize, workPolicy, CalculationStoragePolicy.Standard, options);

    public double CalculateProbabilityForCombos(
        List<Card> deck,
        List<Combo> combos,
        int handSize,
        CalculationWorkPolicy workPolicy,
        CalculationStoragePolicy storagePolicy,
        CalculationOptions? options = null
    ) {
        ArgumentNullException.ThrowIfNull(workPolicy);
        ArgumentNullException.ThrowIfNull(storagePolicy);
        ValidateComboCount(combos);
        ValidateCardIds(deck);

        if (!deck.Any(card => card.DrawCount is not null)) {
            return new ProbabilityCalculation(deck, handSize, workPolicy, storagePolicy).CalculateUnion(combos);
        }

        WorkBudget budget = new(workPolicy, storagePolicy);
        return new DrawEffectCalculation(deck, handSize, budget, options?.ConditionalEffectsEnabled ?? false).Calculate(combos, null, totalOnly: true).Total.ToDouble(budget);
    }

    public ProbabilityCalculationResult CalculateProbabilityResults(
        List<Card> deck,
        List<Combo> combos,
        int handSize,
        IReadOnlyList<ComboGroup>? groups = null
    ) => CalculateProbabilityResults(deck, combos, handSize, groups, CalculationWorkPolicy.Default, CalculationStoragePolicy.Standard);

    public ProbabilityCalculationResult CalculateProbabilityResults(
        List<Card> deck,
        List<Combo> combos,
        int handSize,
        IReadOnlyList<ComboGroup>? groups,
        CalculationWorkPolicy workPolicy,
        CalculationOptions? options = null
    ) => CalculateProbabilityResults(deck, combos, handSize, groups, workPolicy, CalculationStoragePolicy.Standard, options);

    public ProbabilityCalculationResult CalculateProbabilityResults(
        List<Card> deck,
        List<Combo> combos,
        int handSize,
        IReadOnlyList<ComboGroup>? groups,
        CalculationWorkPolicy workPolicy,
        CalculationStoragePolicy storagePolicy,
        CalculationOptions? options = null
    ) {
        ArgumentNullException.ThrowIfNull(workPolicy);
        ArgumentNullException.ThrowIfNull(storagePolicy);
        ValidateComboCount(combos);
        ValidateCardIds(deck);

        if (!deck.Any(card => card.DrawCount is not null)) {
            return new ProbabilityCalculation(deck, handSize, workPolicy, storagePolicy).CalculateResults(combos, groups);
        }

        WorkBudget budget = new(workPolicy, storagePolicy);
        return new DrawEffectCalculation(deck, handSize, budget, options?.ConditionalEffectsEnabled ?? false).Calculate(combos, groups).ToPublic(combos, groups, budget);
    }

    private static void ValidateComboCount(List<Combo> combos) {
        if (combos.Count > IProbabilityCalculatorService.MaxComboCount) {
            throw new ArgumentOutOfRangeException(nameof(combos), $"Calculation supports at most {IProbabilityCalculatorService.MaxComboCount} combos.");
        }
    }

    private static void ValidateCardIds(List<Card> deck) {
        bool hasDuplicateCardIds = deck.Select(static card => card.Id).Distinct(StringComparer.Ordinal).Count() != deck.Count;

        if (hasDuplicateCardIds) {
            throw new ArgumentException("Deck card IDs must be unique.", nameof(deck));
        }
    }
}
