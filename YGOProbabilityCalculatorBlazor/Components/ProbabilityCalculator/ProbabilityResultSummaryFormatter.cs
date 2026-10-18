using System.Globalization;
using System.Text;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

/// <summary>Formats an accepted probability result as a readable, shareable plain-text summary.</summary>
public static class ProbabilityResultSummaryFormatter {
    public static string Format(ProbabilityCalculationResult result, int handSize, CultureInfo culture) {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(culture);

        List<GroupProbabilityResult> groups = (result.GroupProbabilities ?? [])
            .Where(group => group.ActiveComboCount > 0).ToList();
        HashSet<string> knownGroupIds = groups.Select(group => group.GroupId).ToHashSet(StringComparer.Ordinal);
        List<ComboProbabilityResult> ungrouped = result.ComboProbabilities
            .Where(combo => combo.GroupId is null || !knownGroupIds.Contains(combo.GroupId))
            .ToList();

        List<string> lines = [
            "Probability results",
            $"Hand size: {handSize}",
            $"Any active combo: {Display(result.TotalProbability, ProbabilityRow.Total)}"
        ];

        if (result.Contract is { } contract) {
            lines.InsertRange(1, [
                $"Mode: {contract.RequestedMode.Description()}",
                $"Accuracy: {contract.Accuracy}; completion: {contract.Completion}; contract v{contract.Version}",
                $"Reference: {contract.Reference}",
                $"Reason: {contract.Reason}",
                "Effect scope: " + contract.Scope
            ]);
            if (ProbabilityPresentation.AutomaticExactDetail(contract.AutomaticExact) is { } automaticDetail) {
                lines.Add(automaticDetail);
            }
            if (contract.Accuracy != CalculationAccuracy.Exact) {
                lines.Add(ProbabilityPresentation.Legend);
            }
            if (contract.Accuracy != CalculationAccuracy.Bounded && contract.Fractions is { } fractions) {
                string fractionLabel = contract.Accuracy == CalculationAccuracy.Exact ? "Exact modeled fraction" : "Modeled primary fraction";
                lines.Add($"{fractionLabel}, any active combo: {Ratio(fractions.Total)}");
                for (int index = 0; index < fractions.Combos.Length; index++) {
                    lines.Add($"{fractionLabel}, combo {index + 1}: {Ratio(fractions.Combos[index])}");
                }
                for (int index = 0; index < fractions.Groups.Length; index++) {
                    if (index < (result.GroupProbabilities?.Count ?? 0) && result.GroupProbabilities![index].ActiveComboCount > 0) {
                        lines.Add($"{fractionLabel}, group {index + 1}: {Ratio(fractions.Groups[index])}");
                    }
                }
            }
        }

        if (groups.Count == 0 && ungrouped.Count > 0) {
            lines.Add(string.Empty);
            lines.Add("Individual combos:");
            lines.AddRange(result.ComboProbabilities.Select(combo => FormatCombo(combo)));
        }
        else if (groups.Count > 0) {
            lines.Add(string.Empty);
            lines.Add("Group probabilities:");

            foreach (GroupProbabilityResult group in groups) {
                lines.Add($"- **{NormalizeLabel(group.GroupName)}** — {Display(group.Probability, ProbabilityRow.Group, ProbabilityPresentation.GroupIndex(result.GroupProbabilities, group.GroupId))} ({group.ActiveComboCount} active)");
                IEnumerable<ComboProbabilityResult> groupCombos = result.ComboProbabilities.Where(combo => StringComparer.Ordinal.Equals(combo.GroupId, group.GroupId));

                foreach (ComboProbabilityResult combo in groupCombos) {
                    lines.Add(FormatCombo(combo, "  "));
                }
            }

            if (ungrouped.Count > 0) {
                lines.Add(string.Empty);
                lines.Add("Ungrouped combos:");
                lines.AddRange(ungrouped.Select(combo => FormatCombo(combo)));
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

        string FormatCombo(ComboProbabilityResult combo, string indent = "") {
            string name = string.IsNullOrWhiteSpace(combo.ComboName) ? $"Unnamed combo {combo.ComboIndex + 1}" : combo.ComboName;

            return $"{indent}- **{NormalizeLabel(name)}** — {Display(combo.Probability, ProbabilityRow.Combo, combo.ComboIndex)}";
        }
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
