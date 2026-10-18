using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

// Fixed-policy coupling: n_i=h_i+r_i+d_i, D=sum d_i=N-H-R, hence
// max(0,n_i-h_i-D) <= r_i <= n_i-h_i simultaneously for all original rows.
// Every optional lower-capacity assignment is a legal physical assignment with
// the identical final hand (including maxima/exclusions), and likewise for the
// original optimistic U. A separately proved necessary event may then reduce U.
// Each row/group/total is an existential union over the SAME weighted outcomes.
// This proof says nothing about a different draw/discard policy or full gameplay.
internal static class SearchAvailabilityCertification {
    internal static SearchAvailabilityCertificate TryCalculate(
        List<Combo> combos,
        IReadOnlyList<ComboGroup>? groups,
        ExactCalculationResult upper,
        WorkBudget budget,
        DrawEffectCalculation? execution
    ) {
        try {
            return Calculate(combos, groups, upper, budget, execution);
        }
        catch (ProbabilityCalculationLimitException exception) {
            return SearchAvailabilityCertificate.Unavailable(SearchAvailabilityStatus.Limited,
                $"Not certified: optional calculation reached the {exception.Reason} limit. Modeled probabilities are complete.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException) {
            // A secondary defect must be visible, but cannot erase the completed
            // primary answer or be mislabeled as a resource limit / exact result.
            Console.Error.WriteLine($"Search-availability certification failed: {exception}");
            return SearchAvailabilityCertificate.Unavailable(SearchAvailabilityStatus.Error,
                $"Not certified: certificate calculation failed ({exception.GetType().Name}). Modeled probabilities are complete.");
        }
    }

    private static SearchAvailabilityCertificate Calculate(
        List<Combo> combos,
        IReadOnlyList<ComboGroup>? groups,
        ExactCalculationResult upper,
        WorkBudget budget,
        DrawEffectCalculation? execution
    ) {
        CalculationCapability capability = execution?.DescribeCapability() ?? ProbabilityCalculation.Capability;
        if (capability.Reason == CapabilityReason.NoActiveSearch) {
            return Complete(upper, SearchAvailabilityStatus.NotApplicable, "No active Deck searches in the executed model.");
        }
        if (capability.Certification == CertificationSupport.ReusePrimary) {
            return Complete(upper, SearchAvailabilityStatus.Exact,
                capability.Explanation);
        }
        if (capability.Certification == CertificationSupport.Unsupported) {
            return SearchAvailabilityCertificate.Unavailable(SearchAvailabilityStatus.NotCertified,
                "Not certified: " + capability.Explanation + " The executed route has no established target-survival coupling.");
        }

        SearchAccessCalculation searchExecution = execution!.SearchExecution!;
        ExactCalculationResult zero = new(ExactProbability.Zero,
            Enumerable.Repeat(ExactProbability.Zero, combos.Count).ToArray(),
            Enumerable.Repeat(ExactProbability.Zero, groups?.Count ?? 0).ToArray());
        SearchAvailabilityCertificate completed = Complete(zero, SearchAvailabilityStatus.Bounded,
            "Uninformative: only the fixed-policy optimistic upper bound is available.");
        ExactCalculationResult completedLower = zero;
        if (completed.Status == SearchAvailabilityStatus.Exact) {
            return completed with { Detail = "All optimistic endpoints are zero, so physical success is also zero under the fixed policy." };
        }
        try {
            if (capability.Certification == CertificationSupport.OrderedTerminalLowerPass) {
                // Finish a cheap witness before attempting the stronger family.
                // A failed stronger pass cannot publish its partial sums or erase
                // this completed same-policy empty-assignment lower bound.
                ExactCalculationResult witness = searchExecution.CalculateLower(combos, groups, declineSearch: true);
                completed = Complete(witness, SearchAvailabilityStatus.Bounded,
                    "Guaranteed interval from declining optional searches after the same draw/removal policy.");
                completedLower = witness;
                if (completed.Status == SearchAvailabilityStatus.Exact) {
                    return completed;
                }
            }
            ExactCalculationResult bound = searchExecution.CalculateLower(combos, groups, declineSearch: false);
            completed = Complete(bound, SearchAvailabilityStatus.Bounded,
                "Guaranteed interval under the fixed modeled policy; only Deck-search target survival varies.");
            completedLower = bound;
            if (completed.Status != SearchAvailabilityStatus.Exact && searchExecution.CanRefineKnownSourceLoss()) {
                ExactCalculationResult refined = searchExecution.CalculateLower(combos, groups, declineSearch: false, improve: bound);
                completed = Complete(refined, SearchAvailabilityStatus.Bounded,
                    "Guaranteed interval under the fixed modeled policy; consumed draw sources tighten Deck-search target loss bounds.");
                completedLower = refined;
            }
        }
        catch (ProbabilityCalculationLimitException exception) {
            return completed with { Detail = completed.Detail
                + $" Limited refinement: optional calculation reached the {exception.Reason} limit; the completed interval is retained." };
        }
        catch (Exception exception) when (exception is not OperationCanceledException) {
            Console.Error.WriteLine($"Search lower refinement failed: {exception}");
            return completed with { Detail = completed.Detail
                + $" Lower refinement failed ({exception.GetType().Name}); the completed interval is retained." };
        }

        // The optional upper proof has its own transaction. Neither a resource
        // limit nor a defect may erase any previously completed lower certificate.
        try {
            if (completed.Status != SearchAvailabilityStatus.Exact
                && searchExecution.TightenSurvivalUpper(combos, groups, upper) is { } tightened) {
                completed = Complete(completedLower, SearchAvailabilityStatus.Bounded,
                    completed.Detail + " Mandatory original-card survival tightens upper endpoints; the optimistic primary fractions are unchanged.", tightened);
            }
        }
        catch (ProbabilityCalculationLimitException exception) {
            return completed with { Detail = completed.Detail
                + $" Limited survival refinement: optional calculation reached the {exception.Reason} limit; the completed interval is retained." };
        }
        catch (Exception exception) when (exception is not OperationCanceledException) {
            Console.Error.WriteLine($"Target-survival refinement failed: {exception}");
            return completed with { Detail = completed.Detail + $" Survival refinement failed ({exception.GetType().Name}); the completed interval is retained." };
        }

        return completed;

        SearchAvailabilityCertificate Complete(
            ExactCalculationResult lower,
            SearchAvailabilityStatus status,
            string detail,
            ExactCalculationResult? tightened = null
        ) {
            ExactCalculationResult endpoint = tightened ?? upper;
            budget.Spend(1L + combos.Count + (groups?.Count ?? 0));
            SearchAvailabilityInterval total = Interval("Any active combo", lower.Total, endpoint.Total);
            ImmutableArray<SearchAvailabilityInterval> individual = combos.Select((combo, index) =>
                Interval(string.IsNullOrWhiteSpace(combo.Name) ? $"Unnamed combo {index + 1}" : combo.Name,
                    lower.Combos[index], endpoint.Combos[index])).ToImmutableArray();
            ImmutableArray<SearchAvailabilityInterval> grouped = (groups ?? []).Select((group, index) =>
                Interval(group.Name, lower.Groups[index], endpoint.Groups[index])).ToImmutableArray();
            if (status == SearchAvailabilityStatus.Bounded && total.IsExact && individual.All(row => row.IsExact) && grouped.All(row => row.IsExact)) {
                status = SearchAvailabilityStatus.Exact;
            }
            if (status == SearchAvailabilityStatus.Bounded && (total.Informativeness == SearchAvailabilityRowStatus.Uninformative
                || individual.Any(row => row.Informativeness == SearchAvailabilityRowStatus.Uninformative)
                || grouped.Any(row => row.Informativeness == SearchAvailabilityRowStatus.Uninformative))) {
                detail += " Rows marked uninformative have no positive lower bound from the completed conservative proof.";
            }
            return new(status, detail, total, individual, grouped);
        }

        SearchAvailabilityInterval Interval(string label, ExactProbability lower, ExactProbability upperValue) {
            long cells = lower.Cells + upperValue.Cells + 1;
            budget.Spend(cells * cells + label.Length + 1);
            budget.CheckStorage(1 + combos.Count + (groups?.Count ?? 0), cells * (1 + combos.Count + (groups?.Count ?? 0)) * 4);
            if (lower.Denominator <= 0 || upperValue.Denominator <= 0 || lower.Numerator < 0
                || upperValue.Numerator > upperValue.Denominator
                || lower.Numerator * upperValue.Denominator > upperValue.Numerator * lower.Denominator) {
                throw new InvalidOperationException("Search certificate interval invariant failed.");
            }
            return new(label, Fraction(lower), Fraction(upperValue));
        }

        static ProbabilityFraction Fraction(ExactProbability value) {
            BigInteger divisor = BigInteger.GreatestCommonDivisor(value.Numerator, value.Denominator);
            return new((value.Numerator / divisor).ToString(CultureInfo.InvariantCulture),
                (value.Denominator / divisor).ToString(CultureInfo.InvariantCulture));
        }

    }
}
