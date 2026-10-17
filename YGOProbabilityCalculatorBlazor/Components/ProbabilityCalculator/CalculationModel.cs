using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

public static class CalculationModelPresentation {
    public static CalculationModel FromDrawEffects(bool draws) =>
        draws ? CalculationModel.Fast : CalculationModel.OpeningHand;

    public static string Description(this CalculationModel model) => model switch {
        CalculationModel.Fast => "Effects — fast",
        CalculationModel.Exact => "Effects — exact",
        CalculationModel.OpeningHand => "No effect simulation",
        _ => throw new ArgumentOutOfRangeException(nameof(model))
    };
}
