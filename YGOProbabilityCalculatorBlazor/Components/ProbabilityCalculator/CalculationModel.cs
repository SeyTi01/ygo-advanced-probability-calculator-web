namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

public enum CalculationModel { Configured, OpeningHand }

public static class CalculationModelPresentation {
    public static CalculationModel FromDrawEffects(bool draws) =>
        draws ? CalculationModel.Configured : CalculationModel.OpeningHand;

    public static string Description(this CalculationModel model) => model switch {
        CalculationModel.Configured => "Configured effects",
        CalculationModel.OpeningHand => "Opening hand only — effects not applied",
        _ => throw new ArgumentOutOfRangeException(nameof(model))
    };
}
