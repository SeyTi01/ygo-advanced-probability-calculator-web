using CardProbability;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.CalculationEngineAdapter;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

// Host guidance contains no solver route or implementation details.
public readonly record struct DrawExecutionAssessment(bool ElevatedRisk) {
    public static DrawExecutionAssessment Assess(IReadOnlyList<Card> cards, IReadOnlyList<Combo> combos, int handSize, bool drawsEnabled) {
        if (!drawsEnabled) {
            return new(false);
        }
        try {
            CalculationRequest request = new CalculationRequestAdapter(cards.Where(card => card.Active && card.Copies > 0), true)
                .Create(combos.Where(combo => combo.Active).ToArray(), handSize);

            return new(CalculationEngine.Assess(request).ElevatedRisk);
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException) {
            // Advisory rendering must remain available for invalid or unsupported
            // workspaces; the execution boundary supplies the actual input error.
            return new(false);
        }
    }
}
