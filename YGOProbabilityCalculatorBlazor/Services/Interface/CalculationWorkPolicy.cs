namespace YGOProbabilityCalculatorBlazor.Services.Interface;

/// <summary>Immutable, request-local cumulative work allowance. Storage limits are independent.</summary>
public sealed class CalculationWorkPolicy
{
    public static CalculationWorkPolicy Default { get; } = new(10_000_000);

    // Reviewed against the opt-in WorkPolicyBenchmark and published worker probes.
    public static CalculationWorkPolicy Interactive { get; } = new(50_000_000);

    public long WorkUnits { get; }

    public CalculationWorkPolicy(long workUnits)
    {
        if (workUnits <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(workUnits), "Work allowance must be positive.");
        }

        WorkUnits = workUnits;
    }
}
