using YGOProbabilityCalculatorBlazor.Localization;
using System.Globalization;
using System.Numerics;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

public static class SearchAvailabilityPresentation {
    public static string Summary(SearchAvailabilityCertificate certificate) => certificate.Status switch {
        SearchAvailabilityStatus.Exact => UiText.Get("Ui.ExactWithRespectToDeckSearchAvailability"),
        SearchAvailabilityStatus.NotApplicable => UiText.Get("Ui.NoActiveDeckSearches"),
        SearchAvailabilityStatus.Bounded => certificate.Total is { } total ? UiText.Format("Ui.0Width1", Range(total), Width(total)) : UiText.Get("Ui.NotCertified"),
        _ => UiText.Get("Ui.NotCertified")
    };

    public static string Range(SearchAvailabilityInterval interval) => interval.IsExact
        ? UiText.Format("Accuracy.ExactRange", Percent(interval.Lower, false))
        : $"[{Percent(interval.Lower, false)}%, {Percent(interval.Upper, true)}%]"
            + (interval.Informativeness == SearchAvailabilityRowStatus.Uninformative ? UiText.Get("Ui.UninformativeLowerBound") : "");

    public static string Width(SearchAvailabilityInterval interval) {
        BigInteger lowerN = BigInteger.Parse(interval.Lower.Numerator, CultureInfo.InvariantCulture);
        BigInteger lowerD = BigInteger.Parse(interval.Lower.Denominator, CultureInfo.InvariantCulture);
        BigInteger upperN = BigInteger.Parse(interval.Upper.Numerator, CultureInfo.InvariantCulture);
        BigInteger upperD = BigInteger.Parse(interval.Upper.Denominator, CultureInfo.InvariantCulture);
        return RationalPercentage.FormatOutward(upperN * lowerD - lowerN * upperD, upperD * lowerD, true, CultureInfo.CurrentCulture) + "%";
    }

    private static string Percent(ProbabilityFraction fraction, bool roundUp) {
        BigInteger numerator = BigInteger.Parse(fraction.Numerator, CultureInfo.InvariantCulture);
        BigInteger denominator = BigInteger.Parse(fraction.Denominator, CultureInfo.InvariantCulture);

        return RationalPercentage.FormatOutward(numerator, denominator, roundUp, CultureInfo.CurrentCulture);
    }

    private static IEnumerable<SearchAvailabilityInterval> Rows(SearchAvailabilityCertificate certificate) =>
        certificate.Total is { } total ? new[] { total }.Concat(certificate.Combos).Concat(certificate.Groups) : [];

    public static IEnumerable<string> Export(SearchAvailabilityCertificate certificate) {
        yield return certificate.Status == SearchAvailabilityStatus.Exact ? Summary(certificate) : UiText.Format("Ui.DeckSearchAvailability0", Summary(certificate));
        yield return certificate.Reference;
        yield return certificate.Detail;
        foreach (SearchAvailabilityInterval row in Rows(certificate)) {
            yield return UiText.Format("Ui.01234Width56", row.Label, row.Lower.Numerator, row.Lower.Denominator, row.Upper.Numerator, row.Upper.Denominator, Width(row), row.Informativeness.ToString().ToLowerInvariant());
        }
    }
}
