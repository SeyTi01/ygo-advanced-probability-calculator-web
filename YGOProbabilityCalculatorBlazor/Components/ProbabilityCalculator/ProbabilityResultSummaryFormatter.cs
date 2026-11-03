using YGOProbabilityCalculatorBlazor.Localization;
using System.Globalization;
using System.Text;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

/// <summary>Formats an accepted probability result as a readable, shareable plain-text summary.</summary>
internal static class ProbabilityResultSummaryFormatter {
    public static string Format(ProbabilityCalculationResult result, int handSize, CultureInfo culture, string? modeDescription = null) {
        ProbabilityResultProjection projection = ProbabilityResultProjection.Create(result);

        List<string> lines = [
            UiText.Get("Ui.ProbabilityResults"),
            UiText.Format("Ui.HandSize0", handSize),
            UiText.Format("Ui.AnyActiveCombo0", Display(result.TotalProbability, ProbabilityRow.Total))
        ];

        if (result.Contract is { } contract) {
            lines.InsertRange(1, [
                $"Mode: {modeDescription ?? contract.RequestedMode.Description()}",
                UiText.Format("Ui.Accuracy0Completion1ContractV2", contract.Accuracy, contract.Completion, contract.Version),
                $"Reference: {contract.Reference}",
                $"Reason: {contract.Reason}",
                UiText.Get("Ui.EffectScope") + contract.Scope
            ]);
            if (ProbabilityPresentation.AutomaticExactDetail(contract.AutomaticExact) is { } automaticDetail) {
                lines.Add(automaticDetail);
            }
            if (contract.Accuracy != CalculationAccuracy.Exact) {
                lines.Add(ProbabilityPresentation.Legend);
            }
            if (contract.Accuracy != CalculationAccuracy.Bounded && contract.Fractions is { } fractions) {
                string fractionLabel = contract.Accuracy == CalculationAccuracy.Exact ? UiText.Get("Ui.ExactModeledFraction") : UiText.Get("Ui.ModeledPrimaryFraction");
                lines.Add(UiText.Format("Ui.0AnyActiveCombo1", fractionLabel, Ratio(fractions.Total)));
                for (int index = 0; index < fractions.Combos.Length; index++) {
                    lines.Add(UiText.Format("Ui.0Combo12", fractionLabel, index + 1, Ratio(fractions.Combos[index])));
                }
                foreach (ProbabilityGroupRow group in projection.Groups) {
                    if (group.Index < fractions.Groups.Length) {
                        lines.Add(UiText.Format("Ui.0Group12", fractionLabel, group.Index + 1, Ratio(fractions.Groups[group.Index])));
                    }
                }
            }
        }

        if (projection.Groups.Length == 0 && projection.Ungrouped.Length > 0) {
            lines.Add(string.Empty);
            lines.Add(UiText.Get("Ui.IndividualCombos.dc8d9abb"));
            lines.AddRange(projection.Ungrouped.Select(combo => FormatCombo(combo)));
        }
        else if (projection.Groups.Length > 0) {
            lines.Add(string.Empty);
            lines.Add(UiText.Get("Ui.GroupProbabilities"));

            foreach (ProbabilityGroupRow group in projection.Groups) {
                lines.Add(UiText.Format("Ui.012Active", NormalizeLabel(group.Label), Display(group.Probability, ProbabilityRow.Group, group.Index), group.ActiveCount));

                foreach (ProbabilityComboRow combo in group.Members) {
                    lines.Add(FormatCombo(combo, "  "));
                }
            }

            if (projection.Ungrouped.Length > 0) {
                lines.Add(string.Empty);
                lines.Add(UiText.Get("Ui.UngroupedCombos.ec9c0ff4"));
                lines.AddRange(projection.Ungrouped.Select(combo => FormatCombo(combo)));
            }
        }

        if (result.SearchAvailability is { } certificate) {
            lines.Add(string.Empty);
            lines.Add($"Certification: {certificate.Status}. {certificate.Detail}");
        }

        return string.Join('\n', lines);

        string Display(double primary, ProbabilityRow row, int index = 0) {
            ProbabilityDisplay display = ProbabilityPresentation.Create(primary, result.Contract, result.SearchAvailability, row, index, culture);

            return display.Summary + (display.Evidence is { } evidence ? $" ({evidence})" : "");
        }

        string FormatCombo(ProbabilityComboRow combo, string indent = "") =>
            $"{indent}- **{NormalizeLabel(combo.Label)}** — {Display(combo.Probability, ProbabilityRow.Combo, combo.Index)}";
    }

    private static string Ratio(ProbabilityFraction fraction) => $"{fraction.Numerator}/{fraction.Denominator}";

    private static string NormalizeLabel(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return string.Empty;
        }

        StringBuilder normalized = new(value.Length);
        bool hasPendingSpace = false;

        foreach (char character in value) {
            if (char.IsControl(character) || char.IsWhiteSpace(character)) {
                hasPendingSpace = normalized.Length > 0;
                continue;
            }

            if (hasPendingSpace) {
                normalized.Append(' ');
                hasPendingSpace = false;
            }

            normalized.Append(character);
        }

        return normalized.ToString();
    }
}
