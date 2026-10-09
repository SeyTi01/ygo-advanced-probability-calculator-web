using System.Numerics;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

// Bound retained entries/count-vector cells and cumulative work, rather than
// pretending the 30-combo compatibility ceiling is a runtime guarantee.
// Each live map: <= 32K entries and 256K cells (about 1 MiB of int payload).
// Old/new DP maps, Hall subsets and union/change maps can coexist.
// DP/binomial integer payload is also checked; object overhead is extra.
// This is not a total-memory ceiling. Arithmetic work is charged separately.
internal sealed class WorkBudget(CalculationWorkPolicy policy) {
    private long _remaining = policy.WorkUnits;

    public void Spend(long units) {
        ArgumentOutOfRangeException.ThrowIfNegative(units);

        if (units > _remaining) {
            throw new ProbabilityCalculationLimitException();
        }

        _remaining -= units;
    }

    public static void CheckStorage(long entries, long cells) {
        if (entries > 32768 || cells > 262144) {
            throw new ProbabilityCalculationLimitException(ProbabilityCalculationLimitReason.Storage);
        }
    }

    // Count retained BigInteger payload in 32-bit cells as well as count vectors.
    // This is still a per-structure bound, not a total process-byte ceiling.
    public static long IntegerCells(BigInteger value) => (BigInteger.Abs(value).GetBitLength() + 31) / 32;
}
