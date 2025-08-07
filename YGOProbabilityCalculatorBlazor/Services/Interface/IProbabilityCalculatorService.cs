using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.Interface;

public interface IProbabilityCalculatorService {
    double CalculateProbabilityForCombos(
        IEnumerable<Card> deck,
        IEnumerable<Combo> combos,
        int handSize,
        CancellationToken cancellationToken = default
    );
}
