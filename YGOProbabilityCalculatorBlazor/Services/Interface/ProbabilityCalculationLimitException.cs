namespace YGOProbabilityCalculatorBlazor.Services.Interface;

/// <summary>The request exceeded the engine's bounded work or storage budget; no result was produced.</summary>
public enum ProbabilityCalculationLimitReason { Work = 1, Storage = 2 }

public sealed class ProbabilityCalculationLimitException : Exception {
    public ProbabilityCalculationLimitReason Reason { get; }

    public ProbabilityCalculationLimitException(ProbabilityCalculationLimitReason reason = ProbabilityCalculationLimitReason.Work)
        : base(reason switch {
            ProbabilityCalculationLimitReason.Work => "Calculation stopped because it exceeded the work allowance. Simplify the combos or category constraints and try again.",
            ProbabilityCalculationLimitReason.Storage => "Calculation stopped because it exceeded the storage safety limit. Simplify the combos or category constraints and try again.",
            _ => throw new ArgumentOutOfRangeException(nameof(reason))
        }) => Reason = reason;
}
