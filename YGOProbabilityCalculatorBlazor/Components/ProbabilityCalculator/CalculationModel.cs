using YGOProbabilityCalculatorBlazor.Localization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

public static class CalculationModelPresentation {
    public static CalculationModel FromDrawEffects(bool draws) =>
        draws ? CalculationModel.Fast : CalculationModel.OpeningHand;

    public static string Description(this CalculationModel model) => model switch {
        CalculationModel.Fast => UiText.Get("Ui.EffectsFast"),
        CalculationModel.Exact => UiText.Get("Ui.EffectsExact"),
        CalculationModel.OpeningHand => UiText.Get("Ui.NoEffectSimulation"),
        _ => throw new ArgumentOutOfRangeException(nameof(model))
    };
}
