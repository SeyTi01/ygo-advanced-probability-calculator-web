using CardProbability;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Services.CalculationEngineAdapter;

internal static class CalculationResultAdapter {
    internal static ProbabilityCalculationResult Translate(
        CalculationResult result,
        IReadOnlyList<Combo> combos,
        IReadOnlyList<ComboGroup>? groups,
        CalculationModel mode
    ) {
        switch (result.Status) {
            case CalculationStatus.Cancelled:
                throw new OperationCanceledException(result.Detail);
            case CalculationStatus.WorkLimit:
                throw new ProbabilityCalculationLimitException(ProbabilityCalculationLimitReason.Work);
            case CalculationStatus.StorageLimit:
                throw new ProbabilityCalculationLimitException(ProbabilityCalculationLimitReason.Storage);
            case CalculationStatus.InvalidInput:
                throw new ArgumentException(result.Detail);
            case CalculationStatus.Unsupported:
                throw new UnsupportedExactCalculationException(CalculationResultContract.Failure(mode, CalculationCompletion.Unsupported, result.Detail));
        }
        if (result.Status != CalculationStatus.Completed || result.Total is null || result.Accuracy is null) {
            throw new InvalidOperationException("The calculation engine returned an incomplete terminal result.");
        }
        IReadOnlyList<ComboGroup> appGroups = groups ?? [];
        ComboProbabilityResult[] comboRows = result.Goals.Select((row, index) =>
            new ComboProbabilityResult(index, combos[index].Name, row.Value.Probability, combos[index].GroupId)).ToArray();
        GroupProbabilityResult[] groupRows = result.Groups.Select((row, index) =>
            new GroupProbabilityResult(appGroups[index].Id, appGroups[index].Name, row.Value.Probability, row.GoalCount)).ToArray();
        CalculationAccuracy accuracy = result.Accuracy.Value switch {
            ProbabilityAccuracy.Exact => CalculationAccuracy.Exact,
            ProbabilityAccuracy.Bounded => CalculationAccuracy.Bounded,
            _ => CalculationAccuracy.Uncertified
        };
        CalculationFractions fractions = new(
            Fraction(result.Total.Primary),
            [.. result.Goals.Select(row => Fraction(row.Value.Primary))],
            [.. result.Groups.Select(row => Fraction(row.Value.Primary))]);
        // A certificate can collapse to an exact physical answer below the
        // optimistic primary. Keep that evidence even when overall accuracy is Exact.
        bool primaryCertified = PrimaryCertified(result.Total)
            && result.Goals.All(row => PrimaryCertified(row.Value))
            && result.Groups.All(row => PrimaryCertified(row.Value));
        SearchAvailabilityCertificate? certificate = null;
        if (mode == CalculationModel.Fast && (!primaryCertified || result.Certification != CertificationStatus.NotRequired)) {
            SearchAvailabilityStatus status = result.Certification switch {
                CertificationStatus.Exact => SearchAvailabilityStatus.Exact,
                CertificationStatus.Bounded => SearchAvailabilityStatus.Bounded,
                CertificationStatus.NotApplicable => SearchAvailabilityStatus.NotApplicable,
                CertificationStatus.Limited => SearchAvailabilityStatus.Limited,
                CertificationStatus.Error => SearchAvailabilityStatus.Error,
                _ => SearchAvailabilityStatus.NotCertified
            };
            // An independent primary proof does not complete a failed optional
            // certificate attempt or authorize publishing its missing rows.
            bool certificateCompleted = status is SearchAvailabilityStatus.Exact or SearchAvailabilityStatus.Bounded or SearchAvailabilityStatus.NotApplicable;
            bool completeIntervals = certificateCompleted && result.Total.Certified is not null
                && result.Goals.All(row => row.Value.Certified is not null)
                && result.Groups.All(row => row.Value.Certified is not null);
            if (completeIntervals) {
                certificate = new(
                    status,
                    result.CertificationDetail ?? result.Detail,
                    Interval("Any combo", result.Total.Certified!),
                    [.. result.Goals.Select((row, index) => Interval(combos[index].Name ?? $"Combo {index + 1}", row.Value.Certified!))],
                    [.. result.Groups.Select((row, index) => Interval(appGroups[index].Name, row.Value.Certified!))]);
            }
            else {
                certificate = SearchAvailabilityCertificate.Unavailable(status, result.CertificationDetail ?? result.Detail);
            }
        }
        CalculationResultContract contract = new(
            CalculationResultContract.CurrentVersion,
            mode,
            mode,
            mode == CalculationModel.OpeningHand ? CalculationResultContract.OpeningReference : CalculationResultContract.EffectsReference,
            mode == CalculationModel.OpeningHand ? CalculationRuleScope.OpeningHand : CalculationRuleScope.SavedEffectsFixedPolicy,
            CalculationCompletion.Success,
            accuracy,
            result.Detail,
            Proof: primaryCertified ? "CertifiedPhysical" : null,
            Fractions: fractions);

        return new(result.Total.Probability, Array.AsReadOnly(comboRows), Array.AsReadOnly(groupRows), certificate, contract);
    }

    private static ProbabilityFraction Fraction(RationalProbability value) => new(value.Numerator, value.Denominator);

    private static bool PrimaryCertified(ProbabilityValue value) => value.Certified is { } interval
        && interval.Lower == value.Primary && interval.Upper == value.Primary;

    private static SearchAvailabilityInterval Interval(string label, CertifiedInterval value) =>
        new(label, Fraction(value.Lower), Fraction(value.Upper));
}
