using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

public sealed record ComboGroupEdit(string Operator, IReadOnlyList<ComboAlternative> Operands);

// Conversions compose existing bounded requirements; the solver representation
// and separate-copy matching are unchanged.
public static class ComboOperatorEditing {
    private static ComboCategory? Validate(Combo combo, IReadOnlyList<ComboRequirementPosition> positions, ComboGroupEdit edit) {
        if (positions.Count == 0 || positions.Distinct().Count() != positions.Count || positions.Any(position => !combo.RequirementOrder.Contains(position))) {
            throw new ArgumentException("The edited conditions changed. Reopen the group.");
        }
        if (edit.Operands.Count == 0 || edit.Operator is not ("+" or "AND" or "OR")) {
            throw new ArgumentException("Choose an operator and at least one complete condition.");
        }

        ComboCategory? sameCard = null;
        if (edit.Operator == "AND") {
            ComboCategory[] categories = edit.Operands.Select(operand => operand.Category).OfType<ComboCategory>().ToArray();
            if (categories.Length != edit.Operands.Count) {
                throw new ArgumentException("Same-card AND requires category/property operands. Direct-card conditions can use + or OR.");
            }
            ComboCategory first = categories[0];
            if (categories.Any(category => category.MinCount != first.MinCount || category.MaxCount != first.MaxCount || category.MaximumMode != first.MaximumMode)) {
                if (categories.All(category => category.MaximumMode == RequirementMaximumMode.HandSize && category.MinCount == first.MinCount)) {
                    throw new ArgumentException("The saved Any maxima differ. Choose one shared fixed maximum to combine without discarding a saved bound.");
                }
                throw new ArgumentException("Same-card AND requires matching bounds. Set every operand to the same minimum and fixed/Any maximum, then save.");
            }
            sameCard = ComboCategory.SameCard(categories.SelectMany(category => category.CategoryBases), first.MinCount, first.MaxCount, first.MaximumMode);
        }

        return sameCard;
    }

    public static bool CanApply(Combo combo, IReadOnlyList<ComboRequirementPosition> positions, ComboGroupEdit edit) {
        try {
            Validate(combo, positions, edit);
            return true;
        }
        catch (ArgumentException) {
            return false;
        }
    }

    public static Combo Apply(Combo combo, IReadOnlyList<ComboRequirementPosition> positions, ComboGroupEdit edit) {
        ComboCategory? sameCard = Validate(combo, positions, edit);
        int authoredPosition = positions.Min(position => combo.RequirementOrder.ToList().IndexOf(position));
        Combo updated = combo;
        foreach (ComboRequirementPosition position in positions.OrderByDescending(position => position.Index)) {
            updated = updated.WithoutRequirement(position);
        }
        if (edit.Operator == "OR") {
            updated = updated.WithAlternativeGroups(updated.AlternativeGroups.Append(new(edit.Operands.ToArray())));
            return updated.WithRequirementAt(new(ComboRequirementKind.Group, updated.AlternativeGroups.Count - 1), authoredPosition);
        }
        IReadOnlyList<ComboAlternative> requirements = sameCard is null ? edit.Operands : [ComboAlternative.For(sameCard)];
        foreach (ComboAlternative operand in requirements) {
            ComboRequirementPosition added;
            if (operand.Category is { } category) {
                updated = updated.WithCategories(updated.Categories.Append(category));
                added = new(ComboRequirementKind.Category, updated.Categories.Count - 1);
            }
            else {
                updated = updated.WithCards(updated.Cards.Append(operand.Card!));
                added = new(ComboRequirementKind.Card, updated.Cards.Count - 1);
            }
            updated = updated.WithRequirementAt(added, authoredPosition++);
        }
        return updated;
    }
}
