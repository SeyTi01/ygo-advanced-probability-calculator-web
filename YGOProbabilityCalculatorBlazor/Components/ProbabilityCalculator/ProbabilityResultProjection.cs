using System.Collections.Immutable;
using YGOProbabilityCalculatorBlazor.Localization;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

public sealed record ProbabilityComboRow(int Index, string Label, double Probability, string? GroupId);

public sealed record ProbabilityGroupRow(
    int Index,
    string Label,
    double Probability,
    int ActiveCount,
    ImmutableArray<ProbabilityComboRow> Members
);

/// <summary>Display order and labels, retaining the original indices used by fractions and certificates.</summary>
public sealed record ProbabilityResultProjection(
    double Total,
    ImmutableArray<ProbabilityGroupRow> Groups,
    ImmutableArray<ProbabilityComboRow> Ungrouped
) {
    public static ProbabilityResultProjection Create(ProbabilityCalculationResult result) => Create(
        result.TotalProbability,
        result.ComboProbabilities.Select(combo => new ProbabilityComboRow(
            combo.ComboIndex,
            ComboLabel(combo.ComboIndex, combo.ComboName),
            combo.Probability,
            combo.GroupId)).ToImmutableArray(),
        result.GroupProbabilities ?? []
    );

    public static ProbabilityResultProjection Create(PinnedResultSnapshot snapshot) => Create(
        snapshot.Total,
        snapshot.Combos.Select(combo => new ProbabilityComboRow(combo.Index, combo.Label, combo.Probability, combo.GroupId)).ToImmutableArray(),
        snapshot.Groups.Select(group => new GroupProbabilityResult(group.Id, group.Label, group.Probability, group.ActiveCount)).ToArray()
    );

    internal static string ComboLabel(int index, string? name) =>
        string.IsNullOrWhiteSpace(name) ? UiText.Format("Ui.UnnamedCombo0", index + 1) : name;

    private static ProbabilityResultProjection Create(
        double total,
        ImmutableArray<ProbabilityComboRow> combos,
        IReadOnlyList<GroupProbabilityResult> groups
    ) {
        ImmutableArray<ProbabilityGroupRow>.Builder visibleGroups = ImmutableArray.CreateBuilder<ProbabilityGroupRow>();
        HashSet<string> visibleGroupIds = new(StringComparer.Ordinal);
        for (int index = 0; index < groups.Count; index++) {
            GroupProbabilityResult group = groups[index];
            if (group.ActiveComboCount <= 0) {
                continue;
            }

            visibleGroupIds.Add(group.GroupId);
            ImmutableArray<ProbabilityComboRow> members = combos
                .Where(combo => StringComparer.Ordinal.Equals(combo.GroupId, group.GroupId))
                .ToImmutableArray();
            visibleGroups.Add(new(index, group.GroupName, group.Probability, group.ActiveComboCount, members));
        }

        ImmutableArray<ProbabilityComboRow> ungrouped = combos
            .Where(combo => combo.GroupId is null || !visibleGroupIds.Contains(combo.GroupId))
            .ToImmutableArray();

        return new(total, visibleGroups.ToImmutable(), ungrouped);
    }
}
