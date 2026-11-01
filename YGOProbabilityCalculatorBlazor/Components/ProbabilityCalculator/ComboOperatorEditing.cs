using YGOProbabilityCalculatorBlazor.Localization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

public sealed record ComboGroupEdit(string Operator, IReadOnlyList<ComboAlternative> Operands);

// Conversions compose existing bounded requirements; the solver representation
// and separate-copy matching are unchanged.
public static class ComboOperatorEditing {
    // A separator between existing rows divides the OR at that exact boundary;
    // alternatives on either side keep their own Boolean grouping and bounds.
    public static Combo SplitAlternatives(Combo combo, int groupIndex, int boundary) {
        ComboAlternativeGroup group = combo.AlternativeGroups[groupIndex];
        if (boundary <= 0 || boundary >= group.Alternatives.Count) {
            throw new ArgumentOutOfRangeException(nameof(boundary));
        }
        ComboRequirementPosition position = new(ComboRequirementKind.Group, groupIndex);
        int authoredPosition = combo.RequirementOrder.ToList().IndexOf(position);
        Combo updated = combo.WithoutRequirement(position);
        foreach (ComboAlternative[] segment in new[] { group.Alternatives.Take(boundary).ToArray(), group.Alternatives.Skip(boundary).ToArray() }) {
            updated = segment.Length > 1
                ? ComboRequirementEditing.AppendGroup(updated, segment, authoredPosition++)
                : ComboRequirementEditing.Append(updated, segment[0], authoredPosition++);
        }
        return updated;
    }

    private static string? Validate(
        Combo combo,
        IReadOnlyList<ComboRequirementPosition> positions,
        ComboGroupEdit edit,
        out ComboCategory? sameCard
    ) {
        sameCard = null;
        if (positions.Count == 0 || positions.Distinct().Count() != positions.Count || positions.Any(position => !combo.RequirementOrder.Contains(position))) {
            return UiText.Get("OperatorEdit.ConditionsChanged");
        }
        if (edit.Operands.Count == 0 || edit.Operator is not ("+" or "AND" or "OR")) {
            return UiText.Get("OperatorEdit.CompleteConditionRequired");
        }

        if (edit.Operator == "AND") {
            ComboCategory[] categories = edit.Operands.Select(operand => operand.Category).OfType<ComboCategory>().ToArray();
            if (categories.Length != edit.Operands.Count) {
                return UiText.Get("OperatorEdit.CategoryOperandsRequired");
            }
            ComboCategory first = categories[0];
            if (categories.Any(category => category.MinCount != first.MinCount || category.MaxCount != first.MaxCount || category.MaximumMode != first.MaximumMode)) {
                if (categories.All(category => category.MaximumMode == RequirementMaximumMode.HandSize && category.MinCount == first.MinCount)) {
                    return UiText.Get("OperatorEdit.SavedAnyMaximaDiffer");
                }
                return UiText.Get("OperatorEdit.MatchingBoundsRequired");
            }
            if (positions.Count != 1 || positions[0].Kind != ComboRequirementKind.Group
                || categories.Any(category => category.CategoryBases.Count != 1)
                || categories.Select(category => category.BaseCategory.Identity).Distinct(StringComparer.Ordinal).Count() != categories.Length) {
                return UiText.Get("OperatorEdit.IrreversibleChange");
            }
            IReadOnlyList<ComboAlternative> original = combo.AlternativeGroups[positions[0].Index].Alternatives;
            if (original.Count != edit.Operands.Count
                || !original.Zip(edit.Operands).All(pair => ReferenceEquals(pair.First.Category, pair.Second.Category))) {
                return UiText.Get("OperatorEdit.ExistingAlternativesOnly");
            }
            sameCard = ComboCategory.SameCard(categories.SelectMany(category => category.CategoryBases), first.MinCount, first.MaxCount, first.MaximumMode);
        }

        return null;
    }

    public static bool CanApply(Combo combo, IReadOnlyList<ComboRequirementPosition> positions, ComboGroupEdit edit) {
        try {
            return Validate(combo, positions, edit, out _) is null;
        }
        catch (ArgumentException) {
            // The model constructor remains the authority for selector limits.
            // Ordinary unavailable operators return a validation message above.
            return false;
        }
    }

    public static Combo Apply(Combo combo, IReadOnlyList<ComboRequirementPosition> positions, ComboGroupEdit edit) {
        string? error = Validate(combo, positions, edit, out ComboCategory? sameCard);
        if (error is not null) {
            throw new ArgumentException(error);
        }
        int authoredPosition = positions.Min(position => combo.RequirementOrder.ToList().IndexOf(position));
        Combo updated = combo;
        foreach (ComboRequirementPosition position in positions.OrderByDescending(position => position.Index)) {
            updated = updated.WithoutRequirement(position);
        }
        if (edit.Operator == "OR") {
            return ComboRequirementEditing.AppendGroup(updated, edit.Operands, authoredPosition);
        }
        IReadOnlyList<ComboAlternative> requirements = sameCard is null ? edit.Operands : [ComboAlternative.For(sameCard)];
        foreach (ComboAlternative operand in requirements) {
            updated = ComboRequirementEditing.Append(updated, operand, authoredPosition++);
        }
        return updated;
    }
}
