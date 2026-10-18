using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

public enum ProbabilityRow { Total, Combo, Group }

public sealed record ProbabilityDisplay(string Value, string Accuracy, string Detail, bool Uninformative = false, string? Evidence = null) {
    public string Summary => $"{Value} {Accuracy}" + (Uninformative ? " · Uninformative" : "");
}

// Presentation only. The optimistic primary can exceed the certified upper endpoint.
public static class ProbabilityPresentation {
    public static string? AutomaticExactDetail(AutomaticExactOutcome? outcome) => outcome switch {
        AutomaticExactOutcome.Completed => "Fast completed a physical Exact calculation.",
        AutomaticExactOutcome.Deadline => "The automatic Exact opportunity ended; the completed Fast result is retained. Select Exact only to allow more time.",
        AutomaticExactOutcome.WorkLimit => "The automatic Exact attempt reached the remaining Work allowance; the completed Fast result is retained.",
        AutomaticExactOutcome.StorageLimit => "The automatic Exact attempt reached the Storage limit; the completed Fast result is retained.",
        AutomaticExactOutcome.Unsupported => "The automatic Exact route was unsupported; the completed Fast result is retained.",
        AutomaticExactOutcome.Error => "The automatic Exact attempt could not complete; the completed Fast result is retained.",
        AutomaticExactOutcome.NoTime => "Fast completed after the automatic Exact opportunity; no additional attempt was started. Select Exact only to allow more time.",
        _ => null
    };

    public const string Legend = "± is an absolute change on the 0%–100% probability scale, not a relative percentage. Bounded values show a display midpoint with an outward-rounded radius covering the certified range; no value within that range is claimed more likely. This is not a statistical confidence interval.";
    public const string AccuracyDetails = "Fast may simplify whether Deck-search targets survive when Exact counting is too expensive. Certified ± bounds describe model uncertainty, not a statistical confidence interval; Uncertified has no guaranteed bound. Exact (±0%) resolves this uncertainty within the modeled assumptions.";

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
        if (contract is { Completion: CalculationCompletion.Success, Accuracy: CalculationAccuracy.Exact }
            && certificate?.Status != SearchAvailabilityStatus.Exact) {
            return new(value, "±0%", "Exact within the accepted calculation scope." + modeled);
        }

        SearchAvailabilityInterval? interval = certificate is null ? null : row switch {
            ProbabilityRow.Total => certificate.Total,
            ProbabilityRow.Combo => At(certificate.Combos, index),
            _ => At(certificate.Groups, index)
        };
        bool hasProof = contract is { Completion: CalculationCompletion.Success, Accuracy: CalculationAccuracy.Bounded or CalculationAccuracy.Exact }
            && certificate?.Status is SearchAvailabilityStatus.Bounded or SearchAvailabilityStatus.Exact or SearchAvailabilityStatus.Limited;
        if (hasProof && interval is not null && Read(interval.Lower, out BigInteger ln, out BigInteger ld)
            && Read(interval.Upper, out BigInteger un, out BigInteger ud) && ln * ud <= un * ld) {
            BigInteger width = un * ld - ln * ud;
            string evidence = $"rational endpoints [{Ratio(interval.Lower)}, {Ratio(interval.Upper)}]; modeled optimistic primary: {Ratio(raw ?? interval.Upper)}";
            string range = $"Certified range: [{Percent(ln, ld, false, culture)}, {Percent(un, ud, true, culture)}]; rational endpoints [{Ratio(interval.Lower)}, {Ratio(interval.Upper)}].";
            if (width.IsZero) {
                return new(Printed(Round(ln * 10000, ld), culture), "±0%", range + modeled, Evidence: evidence);
            }

            // Round M to hundredths of a percent first. Then ceil BOTH exact
            // endpoint distances from that printed center, in the same units.
            BigInteger center = Round((ln * ud + un * ld) * 10000, 2 * ld * ud);
            BigInteger radius = BigInteger.Max(Ceiling(center * ld - ln * 10000, ld), Ceiling(un * 10000 - center * ud, ud));
            string detail = range + " Display midpoint ± conservative absolute uncertainty; the retained optimistic primary may exceed the certified upper endpoint." + modeled;
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
