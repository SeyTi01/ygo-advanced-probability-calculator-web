using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.Interface;

public interface IProbabilityCalculatorService {
    // Compatibility ceiling, not a performance guarantee. Work/storage budgets
    // can throw ProbabilityCalculationLimitException even below this count.
    public const int MaxComboCount = 30;

    // Callers select active inputs. The service evaluates every supplied entry.
    double CalculateProbabilityForCombos(List<Card> deck, List<Combo> combos, int handSize);

    ProbabilityCalculationResult CalculateProbabilityResults(
        List<Card> deck, List<Combo> combos, int handSize, IReadOnlyList<ComboGroup>? groups = null);
}
