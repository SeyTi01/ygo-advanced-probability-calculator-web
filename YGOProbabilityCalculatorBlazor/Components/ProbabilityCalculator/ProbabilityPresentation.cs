using YGOProbabilityCalculatorBlazor.Localization;
using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

public enum ProbabilityRow { Total, Combo, Group }

public sealed record ProbabilityDisplay(
    string Value,
    string Accuracy,
    string Detail,
    bool Uninformative = false,
    string? Evidence = null,
    string? FocusDetail = null
) {
    public string Summary => UiText.Format(Uninformative ? "Probability.UninformativeSummary" : "Probability.Summary", Value, Accuracy);
}

// Presentation only. The optimistic primary can exceed the certified upper endpoint.
public static class ProbabilityPresentation {
    public static string? AutomaticExactDetail(AutomaticExactOutcome? outcome) => outcome switch {
        AutomaticExactOutcome.Completed => UiText.Get("Ui.FastCompletedAPhysicalExactCalculation"),
        AutomaticExactOutcome.Deadline => UiText.Get("Ui.TheAutomaticExactOpportunityEndedTheCompletedFastResultIsRetainedSelectExactOnlyToAllowM"),
        AutomaticExactOutcome.WorkLimit => UiText.Get("Ui.TheAutomaticExactAttemptReachedTheRemainingWorkAllowanceTheCompletedFastResultIsRetained"),
        AutomaticExactOutcome.StorageLimit => UiText.Get("Ui.TheAutomaticExactAttemptReachedTheStorageLimitTheCompletedFastResultIsRetained"),
        AutomaticExactOutcome.Unsupported => UiText.Get("Ui.TheAutomaticExactRouteWasUnsupportedTheCompletedFastResultIsRetained"),
        AutomaticExactOutcome.Error => UiText.Get("Ui.TheAutomaticExactAttemptCouldNotCompleteTheCompletedFastResultIsRetained"),
        AutomaticExactOutcome.NoTime => UiText.Get("Ui.FastCompletedAfterTheAutomaticExactOpportunityNoAdditionalAttemptWasStartedSelectExactOn"),
        _ => null
    };

    public static string Legend => UiText.Get("Ui.IsAnAbsoluteChangeOnThe0100ProbabilityScaleNotARelativePercentageBoundedValuesShowADispl");
    public static string AccuracyDetails => UiText.Get("Ui.FastMaySimplifyWhetherDeckSearchTargetsSurviveWhenExactCountingIsTooExpensiveCertifiedBo");

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
        string? focusDetail = raw is not null && Read(raw, out _, out _) ? UiText.Format("Ui.ModeledPrimaryFraction0", Ratio(raw)) : null;
        string value = primary.ToString("P2", culture);
        if (contract is { Completion: CalculationCompletion.Success, Accuracy: CalculationAccuracy.Exact }
            && certificate?.Status != SearchAvailabilityStatus.Exact) {
            return new(value, "±0%", WithPrimary(UiText.Get("Ui.ExactWithinTheAcceptedCalculationScope"), raw), FocusDetail: focusDetail);
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
            string evidence = UiText.Format("Ui.RationalEndpoints01ModeledOptimisticPrimary2", Ratio(interval.Lower), Ratio(interval.Upper), Ratio(raw ?? interval.Upper));
            string range = UiText.Format("Ui.CertifiedRange01RationalEndpoints23", Percent(ln, ld, false, culture), Percent(un, ud, true, culture), Ratio(interval.Lower), Ratio(interval.Upper));
            if (width.IsZero) {
                return new(Printed(Round(ln * 10000, ld), culture), "±0%", WithPrimary(range, raw), Evidence: evidence, FocusDetail: focusDetail);
            }

            // Round M to hundredths of a percent first. Then ceil BOTH exact
            // endpoint distances from that printed center, in the same units.
            BigInteger center = Round((ln * ud + un * ld) * 10000, 2 * ld * ud);
            BigInteger radius = BigInteger.Max(Ceiling(center * ld - ln * 10000, ld), Ceiling(un * 10000 - center * ud, ud));
            string detail = WithPrimary(UiText.Format("Probability.BoundedDetail", range), raw);
            if (ln.IsZero) { detail = UiText.Format("Probability.UninformativeDetail", detail); }
            return new(Printed(center, culture), "±" + Printed(radius, culture), detail, ln.IsZero, evidence, focusDetail);
        }

        string reasonKey = certificate?.Status switch {
            SearchAvailabilityStatus.Limited => "Probability.CertificationLimited",
            SearchAvailabilityStatus.Error => "Probability.CertificationError",
            _ => "Probability.NoInterval"
        };
        string reason = UiText.Format(reasonKey, certificate?.Detail ?? contract?.Reason ?? UiText.Get("Ui.AccuracyUnavailableForThisResult"));
        return new(value, UiText.Get("Ui.Uncertified"), WithPrimary(reason, raw),
            Evidence: raw is null ? null : UiText.Format("Ui.ModeledPrimaryFraction0.75f14642", Ratio(raw)), FocusDetail: focusDetail);
    }

    private static string WithPrimary(string detail, ProbabilityFraction? raw) => raw is null
        ? detail : UiText.Format("Probability.DetailWithPrimary", detail, Ratio(raw));

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

    private static string Percent(BigInteger n, BigInteger d, bool up, CultureInfo culture) =>
        RationalPercentage.FormatOutward(n, d, up, culture) + "%";
}
