using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

public partial class ComboEditor {
    private ComboAlternative? _editingRequirement;
    private int? _editingIndex;
    private int? _editingGroupIndex;
    private readonly List<CategoryBase> _draftProperties = [];
    private readonly Dictionary<string, ExpressionRow> _rowButtons = [];
    private readonly Dictionary<string, ExpressionMember> _alternativeButtons = [];
    private bool _hasNewRequirementRow;
    private bool _editingNewRequirement;
    private ExpressionRow? _newRequirementRow;
    private ElementReference _newRequirementButton;
    private bool ShowNewRequirementRow => ConstraintCount == 0 || _hasNewRequirementRow;
    private ElementReference _minimumInput;
    private bool _focusMinimum;
    private string? _returnRowKey;

    private bool RowExists(string key) => Combo.Categories.Select((category, index) => RowKey(ComboAlternative.For(category), index, null))
        .Concat(Combo.Cards.Select((card, index) => RowKey(ComboAlternative.For(card), index, null)))
        .Concat(Combo.AlternativeGroups.SelectMany((group, groupIndex) => group.Alternatives.Select((leaf, index) => RowKey(leaf, index, groupIndex))))
        .Contains(key, StringComparer.Ordinal);

    private bool EditingHasChanges => _editingRequirement is { } original && (
        _selectedKind != original.Kind
        || _minCount != (original.Category?.MinCount ?? original.Card!.MinCount).ToString()
        || _maxCount != ((original.Category?.MaximumMode ?? original.Card!.MaximumMode) == RequirementMaximumMode.HandSize
            ? string.Empty : (original.Category?.MaxCount ?? original.Card!.MaxCount).ToString())
        || (_selectedKind == "Card" ? _selectedCardId != original.Card?.CardId
            : original.Category is null || !_draftProperties.Select(category => category.Identity).SequenceEqual(original.Category.CategoryBases.Select(category => category.Identity))));

    private string SelectedTarget => _selectedKind == "Card" && _selectedCardId.Length > 0
        ? $"card:{_selectedCardId}"
        : _selectedCategory;

    private static string RowKey(ComboAlternative requirement, int index, int? groupIndex) => groupIndex is int group
        ? $"alternative:{group}:{index}"
        : $"{requirement.Kind.ToLowerInvariant()}:{index}";

    private bool IsOpenRow(ComboAlternative requirement, int index, int? groupIndex) {
        if (_editingRequirement is not null) {
            return _editingIndex == index && _editingGroupIndex == groupIndex && SameRequirement(_editingRequirement, requirement);
        }

        if (AndScope) {
            return groupIndex == _andGroupIndex && index == (_andAlternativeIndex ?? _andSeedIndex)
                && ReferenceEquals(requirement.Category, _andSeed);
        }

        if (_alternativeGroupIndex is int alternativeGroup) {
            return groupIndex == alternativeGroup && index == Combo.AlternativeGroups[alternativeGroup].Alternatives.Count - 1;
        }

        return groupIndex is null && index == _alternativeSeedIndex
            && _alternativeSeed is not null && SameRequirement(requirement, _alternativeSeed);
    }

    private static bool SameRequirement(ComboAlternative left, ComboAlternative right) =>
        left.Kind == right.Kind && ReferenceEquals(left.Category, right.Category) && ReferenceEquals(left.Card, right.Card);

    private object FormOperandKey(int index) {
        while (_propertyKeys.Count <= index) { _propertyKeys.Add(new object()); }
        return _propertyKeys[index];
    }
    private int FormOperandCount => _editingRequirement is not null && _draftProperties.Count > 1 ? _draftProperties.Count : 1;
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
        if (FormOperandCount > 1) { ChangeProperty(index, change.Value); }
        else { SelectTarget(change.Value); }
    }

    private void SelectTarget(string? value) {
        _draftProperties.Clear();
        if (value?.StartsWith("card:", StringComparison.Ordinal) == true) {
            _selectedKind = "Card";
            _selectedCategory = string.Empty;
            OnSelectCard(value[5..]);
        }
        else {
            _selectedKind = "Category";
            _selectedCardId = string.Empty;
            OnSelectCategory(value);
            if (FindRequirementCategory(value) is { } category) {
                _draftProperties.Add(category);
            }
        }
    }

    private void BeginEdit(ComboAlternative requirement, int index, int? groupIndex) {
        if (IsOpenRow(requirement, index, groupIndex)) {
            _returnRowKey = RowKey(requirement, index, groupIndex);
            CancelScopedAuthoring();
            return;
        }

        if (_editingRequirement is not null || AuthoringScope) {
            CancelScopedAuthoring();
        }

        _formVersion++;
        _propertyKeys.Clear();
        _propertyErrors.Clear();
        _editingNewRequirement = false;
        _suspendedDraft = (_selectedKind, _selectedCategory, _selectedCardId, _minCount, _maxCount);
        _editingRequirement = requirement;
        _editingIndex = index;
        _editingGroupIndex = groupIndex;
        _selectedKind = requirement.Kind;
        _selectedCategory = requirement.Category?.BaseCategory.Identity ?? string.Empty;
        _selectedCardId = requirement.Card?.CardId ?? string.Empty;
        _minCount = (requirement.Category?.MinCount ?? requirement.Card!.MinCount).ToString();
        RequirementMaximumMode mode = requirement.Category?.MaximumMode ?? requirement.Card!.MaximumMode;
        _maxCount = mode == RequirementMaximumMode.HandSize ? string.Empty
            : (requirement.Category?.MaxCount ?? requirement.Card!.MaxCount).ToString();
        _draftProperties.Clear();
        if (requirement.Category is { } category) {
            _draftProperties.AddRange(category.CategoryBases);
        }

        _error = null;
        _returnRowKey = null;
        _focusMinimum = _draftProperties.Count > 1;
        _focusAlternativeEntry = _draftProperties.Count <= 1;
    }

    private void ChangeProperty(int index, string? identity) {
        CategoryBase? category = FindRequirementCategory(identity);
        if (category is not null && index >= 0 && index < _draftProperties.Count) {
            _draftProperties[index] = category;
            _selectedCategory = _draftProperties[0].Identity;
        }
    }

    private void RemoveDraftProperty(int index) {
        if (index >= 0 && index < _draftProperties.Count && _draftProperties.Count > 1) {
            _draftProperties.RemoveAt(index);
            Dictionary<int, string> remainingErrors = _propertyErrors.Where(pair => pair.Key != index).ToDictionary(pair => pair.Key > index ? pair.Key - 1 : pair.Key, pair => pair.Value);
            _propertyErrors.Clear();
            foreach (KeyValuePair<int, string> error in remainingErrors) { _propertyErrors[error.Key] = error.Value; }
            if (index < _propertyKeys.Count) { _propertyKeys.RemoveAt(index); }
            _selectedCategory = _draftProperties[0].Identity;
            _focusAlternativeEntry = _draftProperties.Count == 1;
        }
    }

    private ComboAlternative? CurrentEditingRequirement {
        get {
            if (_editingIndex is not int index) {
                return null;
            }
            if (_editingGroupIndex is int group) {
                return Combo.AlternativeGroups.ElementAtOrDefault(group)?.Alternatives.ElementAtOrDefault(index);
            }
            if (_editingRequirement?.Category is not null && Combo.Categories.ElementAtOrDefault(index) is { } category) {
                return ComboAlternative.For(category);
            }
            if (_editingRequirement?.Card is not null && Combo.Cards.ElementAtOrDefault(index) is { } card) {
                return ComboAlternative.For(card);
            }

            return null;
        }
    }

    private bool TryEditedRequirement(out ComboAlternative? replacement) {
        replacement = null;
        if (PropertyError is not null) { _error = PropertyError; return false; }
        if (_editingRequirement is null || CurrentEditingRequirement is not { } current || !SameRequirement(current, _editingRequirement)) {
            CancelScopedAuthoring();
            return false;
        }

        if (!int.TryParse(_minCount, out int minimum) || minimum < 0 || !TryMaximum(minimum, out int maximum)) {
            _error = "Enter valid minimum and maximum counts.";
            return false;
        }

        RequirementMaximumMode previousMode = current.Category?.MaximumMode ?? current.Card!.MaximumMode;
        if (MaximumMode == RequirementMaximumMode.HandSize && previousMode == MaximumMode) {
            maximum = current.Category?.MaxCount ?? current.Card!.MaxCount;
        }

        if (_selectedKind == "Card" && (Cards.Any(card => card.Id == _selectedCardId) || current.Card?.CardId == _selectedCardId)) {
            replacement = ComboAlternative.For(new ComboCard(_selectedCardId, minimum, maximum, MaximumMode));
        }
        else if (_selectedKind == "Category" && _draftProperties.Count > 0) {
            replacement = ComboAlternative.For(ComboCategory.SameCard(_draftProperties, minimum, maximum, MaximumMode));
        }
        else {
            _error = "Choose a card, category or property.";
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
        if (SameRequirement(replacement, _editingRequirement!)) {
            return Combo;
        }
        int index = _editingIndex!.Value;
        if (_editingGroupIndex is int groupIndex) {
            List<ComboAlternativeGroup> groups = Combo.AlternativeGroups.ToList();
            List<ComboAlternative> leaves = groups[groupIndex].Alternatives.ToList();
            leaves[index] = replacement;
            groups[groupIndex] = new(leaves);
            return Combo.WithAlternativeGroups(groups);
        }

        List<ComboCategory> categories = Combo.Categories.ToList();
        List<ComboCard> cards = Combo.Cards.ToList();
        if (_editingRequirement!.Category is not null) {
            if (replacement.Category is { } category) { categories[index] = category; }
            else { categories.RemoveAt(index); cards.Add(replacement.Card!); }
        }
        else {
            if (replacement.Card is { } card) { cards[index] = card; }
            else { cards.RemoveAt(index); categories.Add(replacement.Category!); }
        }

        Combo updated = Combo.WithCategories(categories).WithCards(cards);
        ComboRequirementKind previousKind = _editingRequirement.Category is not null ? ComboRequirementKind.Category : ComboRequirementKind.Card;
        ComboRequirementPosition previous = new(previousKind, index);
        ComboRequirementKind nextKind = replacement.Category is not null ? ComboRequirementKind.Category : ComboRequirementKind.Card;
        int nextIndex = previousKind == nextKind ? index : replacement.Category is not null ? categories.Count - 1 : cards.Count - 1;
        if (previousKind != nextKind) {
            updated = Combo.WithoutRequirement(previous);
            updated = replacement.Category is { } nextCategory ? updated.WithCategories(updated.Categories.Append(nextCategory))
                : updated.WithCards(updated.Cards.Append(replacement.Card!));
        }
        return updated.WithRequirementAt(new(nextKind, nextIndex), Combo.RequirementOrder.ToList().IndexOf(previous));
    }

    private Task CommitEdit() {
        if (!TryEditedRequirement(out ComboAlternative? replacement)) {
            return Task.CompletedTask;
        }

        Combo updated = ReplaceEditedRequirement(replacement!);
        CancelScopedAuthoring();
        return ReferenceEquals(updated, Combo) ? Task.CompletedTask : OnReplaceCombo.InvokeAsync((Index, updated));
    }

    private async Task ExtendWithAlternative() {
        if (!TryEditedRequirement(out ComboAlternative? replacement)) { return; }
        int index = _editingIndex!.Value;
        int? groupIndex = _editingGroupIndex;
        bool changedKind = replacement!.Kind != _editingRequirement!.Kind;
        Combo updated = ReplaceEditedRequirement(replacement);
        if (changedKind && groupIndex is null) {
            index = replacement.Category is not null ? updated.Categories.Count - 1 : updated.Cards.Count - 1;
        }
        _editingRequirement = null;
        _draftProperties.Clear();
        if (!ReferenceEquals(updated, Combo)) {
            await OnReplaceCombo.InvokeAsync((Index, updated));
        }
        BeginAlternativeDraft(
            groupIndex is null ? replacement : null,
            groupIndex is null ? index : null,
            groupIndex is int group ? updated.AlternativeGroups[group] : null,
            groupIndex
        );
        _alternativeTermIdentity = TermIdentity(replacement, index, groupIndex);
        if (groupIndex is int selectedGroup) {
            (int start, int count) = AlternativeTerms(updated.AlternativeGroups[selectedGroup]).Single(term => index >= term.Start && index < term.Start + term.Count);
            _alternativeInsertIndex = start + count;
        }
    }

    private async Task ExtendWithProperty() {
        if (!TryEditedRequirement(out ComboAlternative? replacement) || replacement!.Category is not { } category) { return; }
        int index = _editingIndex!.Value;
        int? groupIndex = _editingGroupIndex;
        bool changedKind = _editingRequirement!.Kind != replacement.Kind;
        Combo updated = ReplaceEditedRequirement(replacement);
        if (changedKind && groupIndex is null) {
            index = updated.Categories.Count - 1;
        }
        _editingRequirement = null;
        _draftProperties.Clear();
        if (!ReferenceEquals(updated, Combo)) {
            await OnReplaceCombo.InvokeAsync((Index, updated));
        }
        BeginAndDraft(
            category,
            groupIndex is null ? index : null,
            groupIndex is int group ? updated.AlternativeGroups[group] : null,
            groupIndex,
            groupIndex is null ? null : index
        );
    }

    private Task DeleteEditingRequirement() {
        ComboAlternative? requirement = CurrentEditingRequirement;
        if (requirement is null || _editingRequirement is null || !SameRequirement(requirement, _editingRequirement)) {
            CancelScopedAuthoring();
            return Task.CompletedTask;
        }

        int index = _editingIndex!.Value;
        int? groupIndex = _editingGroupIndex;
        CancelScopedAuthoring();
        if (groupIndex is int group) {
            return RemoveAlternative(Combo.AlternativeGroups[group], group, requirement, index);
        }

        return requirement.Category is { } category ? RemoveCategory(category, index) : RemoveCard(requirement.Card!, index);
    }

    private Task HandleRequirementKeyDown(KeyboardEventArgs args) {
        if (args.Key == "Escape") {
            CancelRequirement();
        }

        return Task.CompletedTask;
    }

    private void CancelRequirement() {
        if (_editingNewRequirement) { _hasNewRequirementRow = false; }
        if (_editingRequirement is { } requirement && _editingIndex is int index) {
            _returnRowKey = RowKey(requirement, index, _editingGroupIndex);
        }
        CancelScopedAuthoring();
    }

    private Task HandleCountKeyDown(KeyboardEventArgs args) => args.Key == "Enter"
        ? AddConstraint()
        : HandleRequirementKeyDown(args);

    private void ReconcileEditingRequirement() {
        if (_editingRequirement is not { } original) { return; }
        ComboAlternative? current = CurrentEditingRequirement;
        if (current is not null && SameRequirement(original, current)) { return; }

        // Category renames replace selectors while the parent preserves the combo.
        // Carry unsaved property edits forward only for that explicit rename.
        if (CategoryRename is { } rename && ReferenceEquals(_previousCombo, Combo)
            && original.Category is { } category && current?.Category is { } renamed
            && renamed.Selector.Equals(category.Selector.RenameCategory($"user:{rename.OldName}", $"user:{rename.NewName}"))
            && renamed.MinCount == category.MinCount && renamed.MaxCount == category.MaxCount && renamed.MaximumMode == category.MaximumMode) {
            for (int index = 0; index < _draftProperties.Count; index++) {
                if (_draftProperties[index].Identity == $"user:{rename.OldName}") {
                    _draftProperties[index] = _availableCategories.First(candidate => candidate.Identity == $"user:{rename.NewName}");
                }
            }
            _editingRequirement = current;
            return;
        }

        CancelScopedAuthoring();
    }
}
