namespace CardProbability.Internal.Contracts;

/// <summary>Immutable, explicit bounded or unlimited request-local work policy. Storage limits are independent.</summary>
internal sealed class CalculationWorkPolicy {
    public static CalculationWorkPolicy Default { get; } = new(10_000_000);
    public static CalculationWorkPolicy Interactive { get; } = new(150_000_000);
    public static CalculationWorkPolicy Unlimited { get; } = new();
    public bool IsUnlimited => _workUnits is null;
    // Finite wire contracts must explicitly reject Unlimited, never serialize a sentinel.
    public long WorkUnits => _workUnits ?? throw new InvalidOperationException("Unlimited work has no finite allowance.");
    public CalculationWorkPolicy(long workUnits) {
        if (workUnits <= 0) {
            throw new ArgumentOutOfRangeException(nameof(workUnits), "Work allowance must be positive.");
        }

        _workUnits = workUnits;
    }

    private CalculationWorkPolicy() { }

    private readonly long? _workUnits;
}
