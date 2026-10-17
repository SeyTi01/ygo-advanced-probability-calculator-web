using Microsoft.AspNetCore.Components;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

public partial class ComboEditor {
    private static readonly string[] ComboOperators = ["+", "AND", "OR"];
    private ComboRequirementPosition[]? _boundedPositions;
    private object[] _boundedSources = [];
    private IReadOnlyList<ComboAlternative> _boundedOperands = [];
    private string _boundedOperator = "OR";
    private string? _boundedError;
    private object? _boundedKey;
    private int _boundedRenameVersion;
    private (string Kind, string Category, string Card, string Min, string Max)? _boundedSuspendedDraft;

    private RenderFragment BoundedGroupForm() => builder => {
        builder.OpenComponent<ComboConditionGroupEditor>(0);
        builder.SetKey(_boundedKey);
        builder.AddAttribute(1, "Operands", _boundedOperands);
        builder.AddAttribute(2, "Operator", _boundedOperator);
        builder.AddAttribute(3, "CategoryBases", _availableCategories);
        builder.AddAttribute(4, "Cards", Cards);
        builder.AddAttribute(5, "CategoryRename", CategoryRename);
        builder.AddAttribute(6, "Error", _boundedError);
        builder.AddAttribute(7, "OnSave", EventCallback.Factory.Create<ComboGroupEdit>(this, SaveBoundedGroup));
        builder.AddAttribute(8, "OnCancel", EventCallback.Factory.Create(this, CloseBoundedGroup));
        builder.CloseComponent();
    };

    private object RequirementObject(ComboRequirementPosition position) => position.Kind switch {
        ComboRequirementKind.Category => Combo.Categories[position.Index],
        ComboRequirementKind.Card => Combo.Cards[position.Index],
        _ => Combo.AlternativeGroups[position.Index]
    };

    private ComboAlternative RequirementOperand(ComboRequirementPosition position) => position.Kind == ComboRequirementKind.Category
        ? ComboAlternative.For(Combo.Categories[position.Index]) : ComboAlternative.For(Combo.Cards[position.Index]);

    private void BeginPeerConversion(ComboRequirementPosition right, string operation) {
        if (operation == "+") { return; }
        int index = Combo.RequirementOrder.ToList().IndexOf(right);
        if (index <= 0) { return; }
        ComboRequirementPosition left = Combo.RequirementOrder[index - 1];
        if (operation == "AND" && (left.Kind == ComboRequirementKind.Group || right.Kind == ComboRequirementKind.Group)) {
            _error = "Edit the grouped condition first. Combining a nested OR with same-card AND cannot be represented by this combo model.";
            return;
        }
        ComboAlternative[] operands = new[] { left, right }.SelectMany(position => position.Kind == ComboRequirementKind.Group
            ? Combo.AlternativeGroups[position.Index].Alternatives : new[] { RequirementOperand(position) }).ToArray();
        OpenBoundedGroup([left, right], operands, operation);
    }

    private void BeginBoundedGroup(int index, string operation = "OR") => OpenBoundedGroup(
        [new(ComboRequirementKind.Group, index)], Combo.AlternativeGroups[index].Alternatives, operation);

    private void BeginSameCardConversion(string operation) {
        if (operation == "AND" || _editingGroupIndex is not null) {
            if (_editingGroupIndex is not null) { _error = "Edit the outer OR group to change this grouping."; }
            return;
        }
        if (!TryEditedRequirement(out ComboAlternative? requirement) || requirement?.Category is not { } category) { return; }
        ComboAlternative[] operands = category.CategoryBases.Select(property => ComboAlternative.For(new ComboCategory(property, category.MinCount, category.MaxCount, category.MaximumMode))).ToArray();
        OpenBoundedGroup([new(ComboRequirementKind.Category, _editingIndex!.Value)], operands, operation);
    }

    private void OpenBoundedGroup(ComboRequirementPosition[] positions, IReadOnlyList<ComboAlternative> operands, string operation) {
        (string Kind, string Category, string Card, string Min, string Max) draft = _boundedSuspendedDraft ?? _suspendedDraft
            ?? (_selectedKind, _selectedCategory, _selectedCardId, _minCount, _maxCount);
        CancelScopedAuthoring();
        _boundedSuspendedDraft = draft;
        _focusAlternativeEntry = false;
        _focusMinimum = false;
        _boundedPositions = positions;
        _boundedSources = positions.Select(RequirementObject).ToArray();
        _boundedOperands = operands;
        _boundedOperator = operation;
        _boundedError = null;
        _boundedKey = new();
        _boundedRenameVersion = CategoryRename?.Version ?? 0;
    }

    private void CloseBoundedGroup() {
        if (_boundedPositions?.FirstOrDefault() is { } first && Combo.RequirementOrder.Contains(first)) {
            _returnRowKey = first.Kind == ComboRequirementKind.Group ? $"alternative:{first.Index}:0"
                : $"{first.Kind.ToString().ToLowerInvariant()}:{first.Index}";
        }
        _boundedPositions = null;
        _boundedError = null;
        if (_boundedSuspendedDraft is { } draft) {
            (_selectedKind, _selectedCategory, _selectedCardId, _minCount, _maxCount) = draft;
            _boundedSuspendedDraft = null;
        }
        _focusAlternativeEntry = true;
    }

    private async Task SaveBoundedGroup(ComboGroupEdit edit) {
        if (!ReconcileBoundedGroup()) { return; }
        try {
            Combo updated = ComboOperatorEditing.Apply(Combo, _boundedPositions!, edit);
            CloseBoundedGroup();
            await OnReplaceCombo.InvokeAsync((Index, updated));
        }
        catch (ArgumentException exception) { _boundedError = exception.Message; }
    }

    private bool ReconcileBoundedGroup() {
        if (_boundedPositions is null) { return false; }
        int first = Combo.RequirementOrder.ToList().IndexOf(_boundedPositions[0]);
        if (first < 0 || !Combo.RequirementOrder.Skip(first).Take(_boundedPositions.Length).SequenceEqual(_boundedPositions)) {
            CloseBoundedGroup();
            return false;
        }
        bool rename = CategoryRename is { } notice && notice.Version != _boundedRenameVersion;
        if (rename && CategoryRename is { } categoryRename && _boundedSuspendedDraft is { } draft && draft.Category == $"user:{categoryRename.OldName}") {
            _boundedSuspendedDraft = draft with { Category = $"user:{categoryRename.NewName}" };
        }
        for (int index = 0; index < _boundedPositions.Length; index++) {
            ComboRequirementPosition position = _boundedPositions[index];
            if (!Combo.RequirementOrder.Contains(position)) { CloseBoundedGroup(); return false; }
            object current = RequirementObject(position);
            if (!ReferenceEquals(current, _boundedSources[index]) && !(rename && RenamedRequirement(_boundedSources[index], current))) {
                CloseBoundedGroup();
                return false;
            }
            _boundedSources[index] = current;
        }
        _boundedRenameVersion = CategoryRename?.Version ?? 0;
        return true;
    }

    private bool RenamedRequirement(object previous, object current) {
        if (previous is ComboCategory before && current is ComboCategory after && CategoryRename is { } rename) {
            return before.Selector.RenameCategory($"user:{rename.OldName}", $"user:{rename.NewName}").Equals(after.Selector)
                && before.MinCount == after.MinCount && before.MaxCount == after.MaxCount && before.MaximumMode == after.MaximumMode;
        }
        if (previous is ComboAlternativeGroup beforeGroup && current is ComboAlternativeGroup afterGroup && beforeGroup.Alternatives.Count == afterGroup.Alternatives.Count) {
            return beforeGroup.Alternatives.Zip(afterGroup.Alternatives).All(pair =>
                ReferenceEquals(pair.First, pair.Second) || pair.First.Category is { } beforeCategory && pair.Second.Category is { } afterCategory && RenamedRequirement(beforeCategory, afterCategory));
        }
        return false;
    }
}
