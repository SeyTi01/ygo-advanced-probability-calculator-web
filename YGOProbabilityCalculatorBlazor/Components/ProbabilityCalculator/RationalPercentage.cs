using System.Globalization;
using System.Numerics;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

// Certified endpoints and widths use six decimal percentage places. Keep the
// scaling and outward rounding in integers until the final culture-aware text.
internal static class RationalPercentage {
    public static string FormatOutward(BigInteger numerator, BigInteger denominator, bool roundUp, CultureInfo culture) {
        BigInteger scaled = BigInteger.DivRem(numerator * 100_000_000, denominator, out BigInteger remainder);
        if (roundUp && !remainder.IsZero) {
            scaled++;
        }

        return ((decimal)scaled / 1_000_000m).ToString("0.000000", culture);
    }
}
