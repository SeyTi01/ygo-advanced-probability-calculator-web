namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

public enum CalculationModel { Configured, OpeningHand, Simplified }

public static class CalculationModelPresentation {
    public static CalculationModel FromFlags(bool draws, bool conditions) =>
        !draws ? CalculationModel.OpeningHand : conditions ? CalculationModel.Configured : CalculationModel.Simplified;

    public static string Description(this CalculationModel model) => model switch {
        CalculationModel.Configured => "Configured draw effects",
        CalculationModel.OpeningHand => "Opening hand only — draw effects not applied",
        CalculationModel.Simplified => "Simplified draws — pre-draw costs and post-draw removals ignored",
        _ => throw new ArgumentOutOfRangeException(nameof(model))
    };
}
