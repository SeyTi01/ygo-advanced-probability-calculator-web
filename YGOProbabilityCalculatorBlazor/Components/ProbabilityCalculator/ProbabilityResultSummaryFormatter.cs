using System.Globalization;
using System.Text;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

/// <summary>Formats an accepted probability result as a readable, shareable Markdown summary.</summary>
public static class ProbabilityResultSummaryFormatter {
    private const string MarkdownPunctuation = """\`*_{}[]()<>#+-.!|~""";

    public static string Format(
        ProbabilityCalculationResult result,
        int handSize,
        CultureInfo culture) {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(culture);

        var groups = result.GroupProbabilities ?? Array.Empty<GroupProbabilityResult>();
        var knownGroupIds = groups.Select(group => group.GroupId).ToHashSet(StringComparer.Ordinal);
        var ungrouped = result.ComboProbabilities
            .Where(combo => combo.GroupId is null || !knownGroupIds.Contains(combo.GroupId))
            .ToList();

        var lines = new List<string> {
            "Probability results",
            $"Hand size: {handSize}",
            $"Any active combo: {FormatProbability(result.TotalProbability, culture)}"
        };

        if (groups.Count == 0) {
            lines.Add(string.Empty);
            lines.Add("Individual combos:");
            lines.AddRange(result.ComboProbabilities.Select(combo => FormatCombo(combo, culture)));
        }
        else {
            lines.Add(string.Empty);
            lines.Add("Group probabilities:");
            foreach (var group in groups) {
                lines.Add(
                    $"- **{EscapeMarkdown(group.GroupName)}** — {FormatProbability(group.Probability, culture)} ({group.ActiveComboCount} active)");

                foreach (var combo in result.ComboProbabilities.Where(combo =>
                             StringComparer.Ordinal.Equals(combo.GroupId, group.GroupId))) {
                    lines.Add(FormatCombo(combo, culture, indent: "  "));
                }
            }

            if (ungrouped.Count > 0) {
                lines.Add(string.Empty);
                lines.Add("Ungrouped combos:");
                lines.AddRange(ungrouped.Select(combo => FormatCombo(combo, culture)));
            }
        }

        return string.Join('\n', lines);
    }

    private static string FormatCombo(
        ComboProbabilityResult combo,
        CultureInfo culture,
        string indent = "") {
        var name = string.IsNullOrWhiteSpace(combo.ComboName)
            ? $"Unnamed combo {combo.ComboIndex + 1}"
            : combo.ComboName;

        return $"{indent}- **{EscapeMarkdown(name)}** — {FormatProbability(combo.Probability, culture)}";
    }

    private static string FormatProbability(double probability, CultureInfo culture) =>
        probability.ToString("P2", culture);

    private static string EscapeMarkdown(string? value) {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var normalized = new StringBuilder(value.Length);
        var hasPendingSpace = false;
        foreach (var character in value) {
            if (char.IsControl(character) || char.IsWhiteSpace(character)) {
                hasPendingSpace = normalized.Length > 0;
                continue;
            }

            if (hasPendingSpace) {
                normalized.Append(' ');
                hasPendingSpace = false;
            }

            if (MarkdownPunctuation.Contains(character)) normalized.Append('\\');
            normalized.Append(character);
        }

        return normalized.ToString();
    }
}
