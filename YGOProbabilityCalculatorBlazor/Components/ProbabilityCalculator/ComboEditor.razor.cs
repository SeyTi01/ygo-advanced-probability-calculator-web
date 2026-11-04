using YGOProbabilityCalculatorBlazor.Localization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

public partial class ComboEditor {
    private readonly Dictionary<string, ExpressionRow> _rowButtons = [];
    private readonly Dictionary<string, ExpressionMember> _alternativeButtons = [];
    private ExpressionSeparator? _newRequirementButton;
    private ElementReference _minimumInput;
    private bool _focusMinimum;
    private string? _returnRowKey;

    private bool RowExists(string key) => Combo.Categories.Select((category, index) => RowKey(ComboAlternative.For(category), index, null))
        .Concat(Combo.Cards.Select((card, index) => RowKey(ComboAlternative.For(card), index, null)))
        .Concat(Combo.AlternativeGroups.SelectMany((group, groupIndex) => group.Alternatives.Select((leaf, index) => RowKey(leaf, index, groupIndex))))
        .Contains(key, StringComparer.Ordinal);

    private bool EditingHasChanges => _draft.Editing?.Requirement is { } original && (
        _draft.Values.Target.Kind != (original.Category is not null ? ComboDraftTargetKind.Category : ComboDraftTargetKind.Card)
        || _draft.Values.Minimum != (original.Category?.MinCount ?? original.Card!.MinCount).ToString()
        || _draft.Values.Maximum != ((original.Category?.MaximumMode ?? original.Card!.MaximumMode) == RequirementMaximumMode.HandSize
            ? string.Empty : (original.Category?.MaxCount ?? original.Card!.MaxCount).ToString())
        || (_draft.Values.Target.Kind == ComboDraftTargetKind.Card ? _draft.Values.Target.CardId != original.Card?.CardId
            : original.Category is null || !_draft.Values.Properties.Select(category => category.Identity).SequenceEqual(original.Category.CategoryBases.Select(category => category.Identity))));

    private string SelectedTarget => _draft.Values.Target.PickerValue;

    private static string RowKey(ComboAlternative requirement, int index, int? groupIndex) => groupIndex is int group
        ? $"alternative:{group}:{index}"
        : $"{requirement.Kind.ToLowerInvariant()}:{index}";

    private bool IsOpenRow(ComboAlternative requirement, int index, int? groupIndex) {
        if (_draft.Editing is { } editing) {
            return editing.Location.Index == index && editing.Location.GroupIndex == groupIndex && SameRequirement(editing.Requirement, requirement);
        }
        if (_draft.Property is { } propertyDraft) {
            return groupIndex == propertyDraft.Location.GroupIndex && index == propertyDraft.Location.Index
                && ReferenceEquals(requirement.Category, propertyDraft.Seed);
        }
        if (_draft.Alternative?.Anchor is ComboAlternativeGroupAnchor groupAnchor) {
            return groupIndex == groupAnchor.GroupIndex && index == groupAnchor.Group.Alternatives.Count - 1;
        }

        return _draft.Alternative?.Anchor is ComboAlternativeSeedAnchor seedAnchor
            && groupIndex is null && index == seedAnchor.Location.Index && SameRequirement(requirement, seedAnchor.Seed);
    }

    private static bool SameRequirement(ComboAlternative left, ComboAlternative right) =>
        left.Kind == right.Kind && ReferenceEquals(left.Category, right.Category) && ReferenceEquals(left.Card, right.Card);

    private object FormOperandKey(int index) {
        while (_propertyKeys.Count <= index) { _propertyKeys.Add(new object()); }
        return _propertyKeys[index];
    }
    private bool EditingComposite => _draft.Editing?.Requirement.Category?.CategoryBases.Count > 1;
    private string? PropertyError => _propertyErrors.Values.FirstOrDefault();
    private CategoryBase? FindRequirementCategory(string? identity) {
        if (identity is not null && _numericCategories.TryGetValue(identity, out CategoryBase? numeric)) { return numeric; }
        if (identity?.StartsWith("metadata:numeric:", StringComparison.Ordinal) == true && NumericPropertyRange.TryFromIdentity(identity, out NumericPropertyRange? range)) { return range.ToCategory(); }
        return _availableCategories.FirstOrDefault(category => category.Identity == identity);
    }

    private void ChangeRequirementTarget(int index, RequirementPropertyPicker.Change change) {
        if (change.Error is not null) { _propertyErrors[index] = change.Error; return; }
        _propertyErrors.Remove(index);
        if (change.Category is { } category) { _numericCategories[category.Identity] = category; }
        if (EditingComposite) { ChangeProperty(_draft.Values.ActiveProperty, change.Value); }
        else { SelectTarget(change.Value); }
    }

    private void SelectTarget(string? value) {
        _draft.Values.Properties.Clear();
        if (value?.StartsWith("card:", StringComparison.Ordinal) == true) {
            OnSelectCard(value[5..]);
        }
        else {
            OnSelectCategory(value);
            if (FindRequirementCategory(value) is { } category) {
                _draft.Values.Properties.Add(category);
            }
        }
    }

    private void BeginEdit(ComboAlternative requirement, int index, int? groupIndex, int property = 0) {
        if (IsOpenRow(requirement, index, groupIndex) && _draft.Values.ActiveProperty == property) {
            _returnRowKey = RowKey(requirement, index, groupIndex);
            CancelScopedAuthoring();
            return;
        }

        if (_draft.Editing is not null || AuthoringScope) {
            CancelScopedAuthoring();
        }

        _formVersion++;
        _propertyKeys.Clear();
        _propertyErrors.Clear();
        _draft.BeginEdit(requirement, ComboDraftLocation.For(requirement, index, groupIndex), property);

        _error = null;
        _returnRowKey = null;
        _focusMinimum = false;
        _focusAlternativeEntry = true;
    }

    private void ChangeProperty(int index, string? identity) {
        CategoryBase? category = FindRequirementCategory(identity);
        if (category is not null && index >= 0 && index < _draft.Values.Properties.Count) {
            _draft.Values.Properties[index] = category;
            _draft.Values.Target = ComboDraftTarget.Category(_draft.Values.Properties[index].Identity);
        }
    }

    private ComboAlternative? CurrentEditingRequirement => _draft.Editing?.Location.Find(Combo);

    private bool TryEditedRequirement(out ComboAlternative? replacement) {
        replacement = null;
        if (PropertyError is not null) { _error = PropertyError; return false; }
        if (_draft.Editing is null || CurrentEditingRequirement is not { } current || !SameRequirement(current, _draft.Editing?.Requirement)) {
            CancelScopedAuthoring();
            return false;
        }

        if (!int.TryParse(_draft.Values.Minimum, out int minimum) || minimum < 0 || !TryMaximum(minimum, out int maximum)) {
            _error = UiText.Get("Ui.EnterValidMinimumAndMaximumCounts");
            return false;
        }

        RequirementMaximumMode previousMode = current.Category?.MaximumMode ?? current.Card!.MaximumMode;
        if (MaximumMode == RequirementMaximumMode.HandSize && previousMode == MaximumMode) {
            maximum = current.Category?.MaxCount ?? current.Card!.MaxCount;
        }

        if (_draft.Values.Target.Kind == ComboDraftTargetKind.Card && (Cards.Any(card => card.Id == _draft.Values.Target.CardId) || current.Card?.CardId == _draft.Values.Target.CardId)) {
            replacement = ComboAlternative.For(new ComboCard(_draft.Values.Target.CardId, minimum, maximum, MaximumMode));
        }
        else if (_draft.Values.Target.Kind == ComboDraftTargetKind.Category && _draft.Values.Properties.Count > 0) {
            replacement = ComboAlternative.For(ComboCategory.SameCard(_draft.Values.Properties, minimum, maximum, MaximumMode));
        }
        else {
            _error = UiText.Get("Ui.ChooseACardCategoryOrProperty.d85ff195");
            return false;
        }

        if (!EditingHasChanges) {
            replacement = current;
        }

        return true;
    }

    // Replaces the exact occurrence; equal selectors and duplicate card references
    // in legacy sessions must never redirect an edit to a different requirement.
    private Combo ReplaceEditedRequirement(ComboAlternative replacement) {
        if (SameRequirement(replacement, _draft.Editing!.Requirement)) {
            return Combo;
        }
        int index = _draft.Editing!.Location.Index;
        if (_draft.Editing?.Location.GroupIndex is int groupIndex) {
            return ComboRequirementEditing.ReplaceAlternative(Combo, groupIndex, index, replacement);
        }

        ComboRequirementKind kind = _draft.Editing!.Requirement.Category is not null ? ComboRequirementKind.Category : ComboRequirementKind.Card;

        return ComboRequirementEditing.Replace(Combo, new(kind, index), replacement);
    }

    private Task CommitEdit() {
        if (!TryEditedRequirement(out ComboAlternative? replacement)) {
            return Task.CompletedTask;
        }

        Combo updated = ReplaceEditedRequirement(replacement!);
        int index = _draft.Editing!.Location.Index;
        if (_draft.Editing?.Location.GroupIndex is null && replacement!.Kind != _draft.Editing!.Requirement.Kind) {
            index = replacement.Category is not null ? updated.Categories.Count - 1 : updated.Cards.Count - 1;
        }
        _returnRowKey = RowKey(replacement!, index, _draft.Editing?.Location.GroupIndex);
        CancelScopedAuthoring();
        return ReferenceEquals(updated, Combo) ? Task.CompletedTask : OnReplaceCombo.InvokeAsync((Index, updated));
    }

    private async Task ExtendWithAlternative() {
        if (!TryEditedRequirement(out ComboAlternative? replacement)) { return; }
        int index = _draft.Editing!.Location.Index;
        int? groupIndex = _draft.Editing?.Location.GroupIndex;
        bool changedKind = replacement!.Kind != _draft.Editing!.Requirement.Kind;
        Combo updated = ReplaceEditedRequirement(replacement);
        if (changedKind && groupIndex is null) {
            index = replacement.Category is not null ? updated.Categories.Count - 1 : updated.Cards.Count - 1;
        }
        _draft.ReleaseEditedRow();
        if (!ReferenceEquals(updated, Combo)) {
            await OnReplaceCombo.InvokeAsync((Index, updated));
        }
        ComboAlternativeAnchor anchor = groupIndex is int group
            ? new ComboAlternativeGroupAnchor(updated.AlternativeGroups[group], group)
            : new ComboAlternativeSeedAnchor(replacement, ComboDraftLocation.For(replacement, index, null));
        BeginAlternativeDraft(anchor);
        _draft.Alternative!.TermIdentity = TermIdentity(replacement, index, groupIndex);
        if (groupIndex is int selectedGroup) {
            (int start, int count) = AlternativeTerms(updated.AlternativeGroups[selectedGroup]).Single(term => index >= term.Start && index < term.Start + term.Count);
            _draft.Alternative.InsertIndex = start + count;
        }
    }

    private async Task ExtendWithProperty() {
        if (!TryEditedRequirement(out ComboAlternative? replacement) || replacement!.Category is not { } category) { return; }
        int index = _draft.Editing!.Location.Index;
        int? groupIndex = _draft.Editing?.Location.GroupIndex;
        bool changedKind = _draft.Editing!.Requirement.Kind != replacement.Kind;
        Combo updated = ReplaceEditedRequirement(replacement);
        if (changedKind && groupIndex is null) {
            index = updated.Categories.Count - 1;
        }
        _draft.ReleaseEditedRow();
        if (!ReferenceEquals(updated, Combo)) {
            await OnReplaceCombo.InvokeAsync((Index, updated));
        }
        BeginAndDraft(
            category,
            ComboDraftLocation.For(replacement, index, groupIndex),
            groupIndex is int group ? updated.AlternativeGroups[group] : null
        );
    }

    private Task DeleteEditingRequirement() {
        ComboAlternative? requirement = CurrentEditingRequirement;
        if (requirement is null || _draft.Editing is null || !SameRequirement(requirement, _draft.Editing?.Requirement)) {
            CancelScopedAuthoring();
            return Task.CompletedTask;
        }

        if (requirement.Category is { CategoryBases.Count: > 1 } categoryGroup) {
            ComboCategory remaining = ComboCategory.SameCard(categoryGroup.CategoryBases.Where((_, position) => position != _draft.Values.ActiveProperty),
                categoryGroup.MinCount, categoryGroup.MaxCount, categoryGroup.MaximumMode);
            Combo updated = ReplaceEditedRequirement(ComboAlternative.For(remaining));
            CancelRequirement();
            return OnReplaceCombo.InvokeAsync((Index, updated));
        }

        int index = _draft.Editing!.Location.Index;
        int? groupIndex = _draft.Editing?.Location.GroupIndex;
        CancelScopedAuthoring();
        if (groupIndex is int group) {
            return RemoveAlternative(Combo.AlternativeGroups[group], group, requirement, index);
        }

        return requirement.Category is { } category ? RemoveCategory(category, index) : RemoveCard(requirement.Card!, index);
    }

    private void CancelRequirement() {
        if (_draft.Editing?.Requirement is { } requirement && _draft.Editing?.Location.Index is int index) {
            _returnRowKey = RowKey(requirement, index, _draft.Editing?.Location.GroupIndex);
        }
        CancelScopedAuthoring();
    }

    private void ReconcileEditingRequirement() {
        if (_draft.Editing?.Requirement is not { } original) { return; }
        ComboAlternative? current = CurrentEditingRequirement;
        if (current is not null && SameRequirement(original, current)) { return; }

        // Category renames replace selectors while the parent preserves the combo.
        // Carry unsaved property edits forward only for that explicit rename.
        if (CategoryRename is { } rename && ReferenceEquals(_previousCombo, Combo)
            && original.Category is { } category && current?.Category is { } renamed
            && renamed.Selector.Equals(category.Selector.RenameCategory($"user:{rename.OldName}", $"user:{rename.NewName}"))
            && renamed.MinCount == category.MinCount && renamed.MaxCount == category.MaxCount && renamed.MaximumMode == category.MaximumMode) {
            for (int index = 0; index < _draft.Values.Properties.Count; index++) {
                if (_draft.Values.Properties[index].Identity == $"user:{rename.OldName}") {
                    _draft.Values.Properties[index] = _availableCategories.First(candidate => candidate.Identity == $"user:{rename.NewName}");
                }
            }
            _draft.Editing!.Requirement = current;
            return;
        }

        CancelScopedAuthoring();
    }
}
