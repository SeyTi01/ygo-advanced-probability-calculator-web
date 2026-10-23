using System.Numerics;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Search;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

// Bound retained entries/count-vector cells and cumulative work, rather than
// pretending the 30-combo compatibility ceiling is a runtime guarantee.
// Standard allows <= 32,768 entries and 262,144 cells (about 1 MiB of int payload) per live map.
// The request-local storage policy can raise both per-structure limits by bounded presets.
// Old/new DP maps, Hall subsets and union/change maps can coexist.
// DP/binomial integer payload is also checked; object overhead is extra.
// This is not a total-memory ceiling. Arithmetic work is charged separately.
internal sealed class WorkBudget(CalculationWorkPolicy policy, CalculationStoragePolicy? storagePolicy = null) {
    private long? _remaining = policy.IsUnlimited ? null : policy.WorkUnits;
    private long _spent;
    private readonly CalculationStoragePolicy _storagePolicy = storagePolicy ?? CalculationStoragePolicy.Standard;

    internal SearchCountingDiagnostics? SearchDiagnostics { get; init; }

    internal long PeakEntries { get; private set; }
    internal long PeakCells { get; private set; }

    internal long? Allowance => policy.IsUnlimited ? null : policy.WorkUnits;
    internal long? Remaining => _remaining;

    internal long Spent => _spent;
    internal CalculationStoragePolicy StoragePolicy => _storagePolicy;

    public void Spend(long units) {
        ArgumentOutOfRangeException.ThrowIfNegative(units);

        if (_remaining is long remaining && units > remaining) {
            throw new ProbabilityCalculationLimitException();
        }

        _remaining -= units;
        // Telemetry saturates; exhaustion is decided only by the finite remainder.
        _spent = units > long.MaxValue - _spent ? long.MaxValue : _spent + units;
    }

    internal void EnsureAvailable(long units) {
        ArgumentOutOfRangeException.ThrowIfNegative(units);
        if (_remaining is long remaining && units > remaining) {
            throw new ProbabilityCalculationLimitException();
        }
    }

    public void CheckStorage(long entries, long cells) {
        PeakEntries = Math.Max(PeakEntries, entries);
        PeakCells = Math.Max(PeakCells, cells);
        if (entries > _storagePolicy.EntryLimit || cells > _storagePolicy.CellLimit) {
            throw new ProbabilityCalculationLimitException(ProbabilityCalculationLimitReason.Storage);
        }
    }

    // Count retained BigInteger payload in 32-bit cells as well as count vectors.
    // This is still a per-structure bound, not a total process-byte ceiling.
    public static long IntegerCells(BigInteger value) => (BigInteger.Abs(value).GetBitLength() + 31) / 32;
}
