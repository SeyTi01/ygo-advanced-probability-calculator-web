using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

internal static class RequirementRangeFormatter {
    public static string Format(int minimum, int maximum, RequirementMaximumMode maximumMode) {
        if (maximumMode == RequirementMaximumMode.HandSize) {
            return minimum == 0 ? "Any" : $"{minimum} Min";
        }

        // Handle the empty requirement before the generic exact-value case.
        if (minimum == 0 && maximum == 0) {
            return "None";
        }

        if (minimum == maximum) {
            return minimum.ToString();
        }

        if (minimum == 0) {
            return $"{maximum} Max";
        }

        return $"{minimum}–{maximum}";
    }
}
