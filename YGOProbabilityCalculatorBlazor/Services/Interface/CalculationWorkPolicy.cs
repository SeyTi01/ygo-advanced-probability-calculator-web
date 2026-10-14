namespace YGOProbabilityCalculatorBlazor.Services.Interface;

/// <summary>Immutable, request-local cumulative work allowance. Storage limits are independent.</summary>
public sealed class CalculationWorkPolicy {
    public static CalculationWorkPolicy Default { get; } = new(10_000_000);
    // The complete 3 Trade-In + 3 Allure example charges 90.77M units at High
    // storage; retain finite headroom without promoting the selected storage tier.
    public static CalculationWorkPolicy Interactive { get; } = new(150_000_000);

    public long WorkUnits { get; }

    public CalculationWorkPolicy(long workUnits) {
        if (workUnits <= 0) {
            throw new ArgumentOutOfRangeException(nameof(workUnits), "Work allowance must be positive.");
        }

        WorkUnits = workUnits;
    }
}
