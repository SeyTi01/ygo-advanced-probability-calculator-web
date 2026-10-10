using System.Numerics;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

// Count pairs need not be reduced: preserve the existing no-effect conversion.
// Scenario arithmetic cancels factors before multiplying, and never uses double.
internal readonly record struct ExactProbability(BigInteger Numerator, BigInteger Denominator) {
    internal static ExactProbability Zero => new(0, 1);
    internal static ExactProbability One => new(1, 1);
    internal long Cells => WorkBudget.IntegerCells(Numerator) + WorkBudget.IntegerCells(Denominator);

    internal double ToDouble(WorkBudget budget) => ProbabilityRatio.ToDouble(Numerator, Denominator, budget);

    internal ExactProbability Add(ExactProbability other, WorkBudget budget) {
        Charge(other, budget);
        BigInteger common = BigInteger.GreatestCommonDivisor(Denominator, other.Denominator);
        BigInteger leftScale = other.Denominator / common;
        BigInteger rightScale = Denominator / common;
        BigInteger numerator = Numerator * leftScale + other.Numerator * rightScale;
        BigInteger denominator = Denominator * leftScale;
        return Reduce(numerator, denominator, budget);
    }

    internal ExactProbability Multiply(ExactProbability other, WorkBudget budget) {
        Charge(other, budget);
        BigInteger first = BigInteger.GreatestCommonDivisor(Numerator, other.Denominator);
        BigInteger second = BigInteger.GreatestCommonDivisor(other.Numerator, Denominator);
        return Reduce((Numerator / first) * (other.Numerator / second),
            (Denominator / second) * (other.Denominator / first), budget);
    }

    private void Charge(ExactProbability other, WorkBudget budget) {
        // Conservative limb-product charge covers multiplication and Euclidean division.
        long cells = Cells + other.Cells + 1;
        WorkBudget.CheckStorage(1, cells * 2);
        budget.Spend(cells * cells);
    }

    private static ExactProbability Reduce(BigInteger numerator, BigInteger denominator, WorkBudget budget) {
        long cells = WorkBudget.IntegerCells(numerator) + WorkBudget.IntegerCells(denominator) + 1;
        WorkBudget.CheckStorage(1, cells);
        budget.Spend(cells * cells);
        BigInteger divisor = BigInteger.GreatestCommonDivisor(numerator, denominator);
        return new(numerator / divisor, denominator / divisor);
    }
}
