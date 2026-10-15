using System.Globalization;
using System.Numerics;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

public static class SearchAvailabilityPresentation {
    public static string Summary(SearchAvailabilityCertificate certificate) => certificate.Status switch {
        SearchAvailabilityStatus.Exact => "Exact with respect to Deck-search availability",
        SearchAvailabilityStatus.NotApplicable => "No active Deck searches",
        SearchAvailabilityStatus.Bounded => certificate.Total is { } total ? Range(total) : "Not certified",
        _ => "Not certified"
    };

    public static string Range(SearchAvailabilityInterval interval) => interval.IsExact
        ? $"≈ {Percent(interval.Lower, false)}% (exact)"
        : $"{Percent(interval.Lower, false)}%–{Percent(interval.Upper, true)}%"
            + (interval.Lower.Numerator == "0" ? " (uninformative lower bound)" : "");

    // Six decimal places, rounded OUTWARD using integers, never a rounded double.
    private static string Percent(ProbabilityFraction fraction, bool roundUp) {
        BigInteger numerator = BigInteger.Parse(fraction.Numerator, CultureInfo.InvariantCulture);
        BigInteger denominator = BigInteger.Parse(fraction.Denominator, CultureInfo.InvariantCulture);
        BigInteger scaled = BigInteger.DivRem(numerator * 100_000_000, denominator, out BigInteger remainder);
        if (roundUp && !remainder.IsZero) {
            scaled++;
        }
        return ((decimal)scaled / 1_000_000m).ToString("0.000000", CultureInfo.CurrentCulture);
    }

    public static IEnumerable<SearchAvailabilityInterval> Rows(SearchAvailabilityCertificate certificate) =>
        certificate.Total is { } total ? new[] { total }.Concat(certificate.Combos).Concat(certificate.Groups) : [];

    public static IEnumerable<string> Export(SearchAvailabilityCertificate certificate) {
        yield return certificate.Status == SearchAvailabilityStatus.Exact ? Summary(certificate) : $"Deck-search availability: {Summary(certificate)}";
        yield return certificate.Reference;
        yield return certificate.Detail;
        foreach (SearchAvailabilityInterval row in Rows(certificate)) {
            yield return $"- {row.Label}: [{row.Lower.Numerator}/{row.Lower.Denominator}, {row.Upper.Numerator}/{row.Upper.Denominator}]";
        }
    }
}
