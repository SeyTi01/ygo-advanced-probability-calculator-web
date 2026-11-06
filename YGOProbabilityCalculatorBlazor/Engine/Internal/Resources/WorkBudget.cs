using System.Numerics;
using CardProbability.Internal.Contracts;
using CardProbability.Internal.Search;

namespace CardProbability.Internal.Resources;

// Bound retained entries/count-vector cells and cumulative work.
// Standard allows <= 32,768 entries and 262,144 cells (about 1 MiB of int payload) per live map.
// The request-local storage policy controls both per-structure limits.
// Old/new DP maps, Hall subsets and union/change maps can coexist.
// DP/binomial integer payload is also checked; object overhead is extra.
// This is not a total-memory ceiling. Arithmetic work is charged separately.
internal sealed class WorkBudget(CalculationWorkPolicy policy, CalculationStoragePolicy? storagePolicy = null, CancellationToken cancellationToken = default) {
    private long? _remaining = policy.IsUnlimited ? null : policy.WorkUnits;
    private long _spent;
    private readonly CalculationStoragePolicy _storagePolicy = storagePolicy ?? CalculationStoragePolicy.Standard;
    private readonly long _entryLimit = Math.Min(storagePolicy?.EntryLimit ?? CalculationStoragePolicy.Standard.EntryLimit, Array.MaxLength);
    private readonly long _cellLimit = storagePolicy?.CellLimit ?? CalculationStoragePolicy.Standard.CellLimit;
    private readonly bool _canBeCanceled = cancellationToken.CanBeCanceled;

    internal SearchCountingDiagnostics? SearchDiagnostics { get; init; }

    internal long PeakEntries { get; private set; }
    internal long PeakCells { get; private set; }

    internal CancellationToken CancellationToken => cancellationToken;

    internal long? Allowance => policy.IsUnlimited ? null : policy.WorkUnits;
    internal long? Remaining => _remaining;

    internal long Spent => _spent;
    internal CalculationStoragePolicy StoragePolicy => _storagePolicy;

    public void Spend(long units) {
        if (_canBeCanceled) {
            cancellationToken.ThrowIfCancellationRequested();
        }
        // AOT leaves the generic throw helper as an indirect call; reserve it
        // for invalid input while retaining its exception details.
        if (units < 0) {
            ArgumentOutOfRangeException.ThrowIfNegative(units);
        }

        if (_remaining is long remaining) {
            if (units > remaining) {
                throw new ProbabilityCalculationLimitException();
            }

            _remaining = remaining - units;
        }

        // Telemetry saturates; exhaustion is decided only by the finite remainder.
        _spent = units > long.MaxValue - _spent ? long.MaxValue : _spent + units;
    }

    internal void EnsureAvailable(long units) {
        if (_canBeCanceled) {
            cancellationToken.ThrowIfCancellationRequested();
        }
        if (units < 0) {
            ArgumentOutOfRangeException.ThrowIfNegative(units);
        }

        if (_remaining is long remaining && units > remaining) {
            throw new ProbabilityCalculationLimitException();
        }
    }

    public void CheckStorage(long entries, long cells) {
        if (_canBeCanceled) {
            cancellationToken.ThrowIfCancellationRequested();
        }
        if (entries > PeakEntries) {
            PeakEntries = entries;
        }
        if (cells > PeakCells) {
            PeakCells = cells;
        }

        if (entries > _entryLimit || cells > _cellLimit) {
            throw new ProbabilityCalculationLimitException(ProbabilityCalculationLimitReason.Storage);
        }
    }

    // Count retained BigInteger payload in 32-bit cells as well as count vectors.
    // This is still a per-structure bound, not a total process-byte ceiling.
    public static long IntegerCells(BigInteger value) => (BigInteger.Abs(value).GetBitLength() + 31) / 32;
}
