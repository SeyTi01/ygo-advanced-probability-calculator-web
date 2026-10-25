using YGOProbabilityCalculatorBlazor.Localization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

internal static class RequirementRangeFormatter {
    public static string Format(int minimum, int maximum, RequirementMaximumMode maximumMode) {
        if (maximumMode == RequirementMaximumMode.HandSize) {
            return minimum == 0 ? UiText.Get("Ui.Any") : UiText.Format("Ui.0Min", minimum);
        }

        // Handle the empty requirement before the generic exact-value case.
        if (minimum == 0 && maximum == 0) {
            return UiText.Get("Ui.None");
        }

        if (minimum == maximum) {
            return minimum.ToString();
        }

        if (minimum == 0) {
            return UiText.Format("Ui.0Max", maximum);
        }

        return $"{minimum}–{maximum}";
    }
}
