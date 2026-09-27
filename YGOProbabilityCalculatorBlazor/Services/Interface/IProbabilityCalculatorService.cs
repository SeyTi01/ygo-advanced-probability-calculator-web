using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.Interface;

public interface IProbabilityCalculatorService {
    // Mask safety bound, not a performance guarantee: union work is exponential.
    public const int MaxComboCount = 30;

    // Callers select active inputs. The service evaluates every supplied entry.
    double CalculateProbabilityForCombos(List<Card> deck, List<Combo> combos, int handSize);

    ProbabilityCalculationResult CalculateProbabilityResults(
        List<Card> deck, List<Combo> combos, int handSize, IReadOnlyList<ComboGroup>? groups = null);
}
