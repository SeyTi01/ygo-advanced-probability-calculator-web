using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

public enum ProbabilityRow { Total, Combo, Group }

public sealed record ProbabilityDisplay(string Value, string Accuracy, string Detail, bool Uninformative = false, string? Evidence = null) {
    public string Summary => $"{Value} {Accuracy}" + (Uninformative ? " · Uninformative" : "");
}

// Presentation only. The accepted primary fraction remains the solver's optimistic U.
public static class ProbabilityPresentation {
    public const string Legend = "± is an absolute change on the 0%–100% probability scale, not a relative percentage. Bounded values show a display midpoint with an outward-rounded radius covering the certified range; no value within that range is claimed more likely. This is not a statistical confidence interval.";
    public const string AccuracyDetails = "Some calculations simplify whether cards remain available as Deck-search targets after draws or removals. For Bounded results, the displayed percentage is the midpoint of a guaranteed range; ± is a conservative upper bound on the absolute error from that midpoint on the 0%–100% scale, and the actual error may be smaller. This is model uncertainty, not statistical uncertainty, and excludes mechanics outside the calculator's assumptions. ±0% is exact within those assumptions; Uncertified means no guaranteed error bound is available.";

    public static ProbabilityDisplay Create(
        double primary,
        CalculationResultContract? contract,
        SearchAvailabilityCertificate? certificate,
        ProbabilityRow row = ProbabilityRow.Total,
        int index = 0,
        CultureInfo? culture = null
    ) {
        culture ??= CultureInfo.CurrentCulture;
        ProbabilityFraction? raw = contract?.Fractions is { } fractions ? row switch {
            ProbabilityRow.Total => fractions.Total,
            ProbabilityRow.Combo => At(fractions.Combos, index),
            _ => At(fractions.Groups, index)
        } : null;
        string value = primary.ToString("P2", culture);
        string modeled = raw is null ? "" : $" Modeled primary fraction: {Ratio(raw)}.";
        if (contract is { Completion: CalculationCompletion.Success, Accuracy: CalculationAccuracy.Exact }) {
            return new(value, "±0%", "Exact within the accepted calculation scope." + modeled);
        }

        SearchAvailabilityInterval? interval = certificate is null ? null : row switch {
            ProbabilityRow.Total => certificate.Total,
            ProbabilityRow.Combo => At(certificate.Combos, index),
            _ => At(certificate.Groups, index)
        };
        bool hasProof = contract is { Completion: CalculationCompletion.Success, Accuracy: CalculationAccuracy.Bounded }
            && certificate?.Status is SearchAvailabilityStatus.Bounded or SearchAvailabilityStatus.Exact or SearchAvailabilityStatus.Limited;
        if (hasProof && interval is not null && Read(interval.Lower, out BigInteger ln, out BigInteger ld)
            && Read(interval.Upper, out BigInteger un, out BigInteger ud) && ln * ud <= un * ld) {
            BigInteger width = un * ld - ln * ud;
            string evidence = $"rational endpoints [{Ratio(interval.Lower)}, {Ratio(interval.Upper)}]; modeled optimistic U: {Ratio(raw ?? interval.Upper)}";
            string range = $"Certified range: [{Percent(ln, ld, false, culture)}, {Percent(un, ud, true, culture)}]; rational endpoints [{Ratio(interval.Lower)}, {Ratio(interval.Upper)}].";
            if (width.IsZero) {
                return new(Printed(Round(ln * 10000, ld), culture), "±0%", range + modeled, Evidence: evidence);
            }

            // Round M to hundredths of a percent first. Then ceil BOTH exact
            // endpoint distances from that printed center, in the same units.
            BigInteger center = Round((ln * ud + un * ld) * 10000, 2 * ld * ud);
            BigInteger radius = BigInteger.Max(Ceiling(center * ld - ln * 10000, ld), Ceiling(un * 10000 - center * ud, ud));
            string detail = range + " Display midpoint ± conservative absolute uncertainty; modeled optimistic U is retained." + modeled;
            return new(Printed(center, culture), "±" + Printed(radius, culture), detail + (ln.IsZero ? " Uninformative: the true probability could be zero." : ""), ln.IsZero, evidence);
        }

        string reason = certificate?.Status switch {
            SearchAvailabilityStatus.Limited => "Certification limited: ",
            SearchAvailabilityStatus.Error => "Certification error: ",
            _ => "No complete valid interval: "
        };
        return new(value, "⚠ Uncertified", reason + (certificate?.Detail ?? contract?.Reason ?? "Accuracy unavailable for this result.") + modeled,
            Evidence: raw is null ? null : $"modeled primary fraction: {Ratio(raw)}");
    }

    public static int GroupIndex(IReadOnlyList<GroupProbabilityResult>? groups, string id) {
        for (int index = 0; index < (groups?.Count ?? 0); index++) {
            if (groups![index].GroupId == id) {
                return index;
            }
        }

        return -1;
    }

    private static T? At<T>(ImmutableArray<T> values, int index) where T : class =>
        !values.IsDefault && index >= 0 && index < values.Length ? values[index] : null;

    private static bool Read(ProbabilityFraction? fraction, out BigInteger n, out BigInteger d) {
        n = d = 0;
        return fraction is not null && BigInteger.TryParse(fraction.Numerator, NumberStyles.None, CultureInfo.InvariantCulture, out n)
            && BigInteger.TryParse(fraction.Denominator, NumberStyles.None, CultureInfo.InvariantCulture, out d)
            && d > 0 && n >= 0 && n <= d;
    }

    private static BigInteger Round(BigInteger n, BigInteger d) => (2 * n + d) / (2 * d);
    private static BigInteger Ceiling(BigInteger n, BigInteger d) => n <= 0 ? BigInteger.Zero : (n + d - 1) / d;
    private static string Printed(BigInteger hundredths, CultureInfo culture) => ((decimal)hundredths / 100m).ToString("0.00", culture) + "%";
    private static string Ratio(ProbabilityFraction fraction) => $"{fraction.Numerator}/{fraction.Denominator}";

    private static string Percent(BigInteger n, BigInteger d, bool up, CultureInfo culture) {
        BigInteger units = up ? Ceiling(n * 100_000_000, d) : n * 100_000_000 / d;

        return ((decimal)units / 1_000_000m).ToString("0.000000", culture) + "%";
    }
}
