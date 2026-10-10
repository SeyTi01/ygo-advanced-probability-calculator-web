using System.Numerics;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

internal static class ProbabilityRatio {
    internal static double ToDouble(BigInteger successes, BigInteger denominator, WorkBudget budget) {
        double doubleDenominator = (double)denominator;

        // Preserve ordinary-sized conversion, including the existing invalid
        // empty sample-space behavior. Valid counts satisfy 0 <= successes <= denominator.
        if (double.IsFinite(doubleDenominator)) {
            return (double)successes / doubleDenominator;
        }

        if (successes.IsZero) {
            return 0;
        }

        // Neither infinity/infinity nor finite/infinity represents the exact
        // ratio. Locate its binary exponent using integers, then round once
        // to a 53-bit significand (or the fixed 2^-1074 subnormal grid).
        long numeratorBits = successes.GetBitLength();
        long denominatorBits = denominator.GetBitLength();
        budget.Spend(1 + (numeratorBits + denominatorBits) / 32);
        long exponent = numeratorBits - denominatorBits;

        if (exponent < -1075) {
            return 0;
        }

        if ((successes << (int)-exponent) < denominator) {
            exponent--;
        }

        int shift = (int)Math.Min(1074, 52 - exponent);
        WorkBudget.CheckStorage(1, (numeratorBits + shift + 31) / 32);
        BigInteger significand = BigInteger.DivRem(successes << shift, denominator, out BigInteger remainder);
        int rounding = (remainder << 1).CompareTo(denominator);

        if (rounding > 0 || (rounding == 0 && !significand.IsEven)) {
            significand++;
        }

        return Math.ScaleB((double)significand, -shift);
    }

}
