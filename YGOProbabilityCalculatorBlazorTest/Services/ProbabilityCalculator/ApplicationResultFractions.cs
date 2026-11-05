using System.Numerics;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

// Exact arithmetic for independent oracle assertions over the public app result.
internal readonly record struct ApplicationFraction {
    internal BigInteger Numerator { get; }
    internal BigInteger Denominator { get; }
    internal static ApplicationFraction Zero => new(BigInteger.Zero, BigInteger.One);
    internal static ApplicationFraction One => new(BigInteger.One, BigInteger.One);

    internal ApplicationFraction(BigInteger numerator, BigInteger denominator) {
        if (denominator <= BigInteger.Zero) {
            throw new ArgumentOutOfRangeException(nameof(denominator));
        }
        BigInteger divisor = BigInteger.GreatestCommonDivisor(numerator, denominator);
        Numerator = numerator / divisor;
        Denominator = denominator / divisor;
    }

    internal static ApplicationFraction From(ProbabilityFraction value) =>
        new(BigInteger.Parse(value.Numerator), BigInteger.Parse(value.Denominator));

    internal static ApplicationFraction From(CardProbability.RationalProbability value) =>
        new(BigInteger.Parse(value.Numerator), BigInteger.Parse(value.Denominator));

    internal ProbabilityFraction ToAppFraction() => new(Numerator.ToString(), Denominator.ToString());
    internal ProbabilityFraction ToFraction() => ToAppFraction();
    internal double ToDouble() => (double)Numerator / (double)Denominator;
}

internal sealed record ApplicationResultFractions(
    ApplicationFraction Total,
    ApplicationFraction[] Combos,
    ApplicationFraction[] Groups,
    ProbabilityCalculationResult? Public
) {
    internal static ApplicationResultFractions From(CardProbability.CalculationResult result) => new(
        ApplicationFraction.From(result.Total!.Primary),
        result.Goals.Select(row => ApplicationFraction.From(row.Value.Primary)).ToArray(),
        result.Groups.Select(row => ApplicationFraction.From(row.Value.Primary)).ToArray(),
        null);

    internal static ApplicationResultFractions From(ProbabilityCalculationResult result) {
        CalculationFractions fractions = result.Contract!.Fractions!;
        return new(ApplicationFraction.From(fractions.Total), fractions.Combos.Select(ApplicationFraction.From).ToArray(),
            fractions.Groups.Select(ApplicationFraction.From).ToArray(), result);
    }
}
