namespace YGOProbabilityCalculatorBlazor.Services.Interface;

public enum CalculationStorageTier {
    Standard,
    High,
    VeryHigh
}

public sealed class CalculationStoragePolicy {
    public static CalculationStoragePolicy Standard { get; } = new(CalculationStorageTier.Standard, 32_768, 262_144);
    public static CalculationStoragePolicy High { get; } = new(CalculationStorageTier.High, 65_536, 524_288);
    public static CalculationStoragePolicy VeryHigh { get; } = new(CalculationStorageTier.VeryHigh, 131_072, 1_048_576);

    private CalculationStoragePolicy(CalculationStorageTier tier, long entryLimit, long cellLimit) {
        Tier = tier;
        EntryLimit = entryLimit;
        CellLimit = cellLimit;
    }

    public CalculationStorageTier Tier { get; }
    public long EntryLimit { get; }
    public long CellLimit { get; }

    public static CalculationStoragePolicy ForTier(CalculationStorageTier tier) => tier switch {
        CalculationStorageTier.Standard => Standard,
        CalculationStorageTier.High => High,
        CalculationStorageTier.VeryHigh => VeryHigh,
        _ => throw new ArgumentOutOfRangeException(nameof(tier), "Unknown calculation storage tier.")
    };
}
