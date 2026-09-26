using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.Interface;

public interface IProbabilityCalculatorService {
    double CalculateProbabilityForCombos(List<Card> deck, List<Combo> combos, int handSize);

    ProbabilityCalculationResult CalculateProbabilityResults(List<Card> deck, List<Combo> combos, int handSize);
}
