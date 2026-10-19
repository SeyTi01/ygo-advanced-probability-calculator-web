using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

public partial class ComboEditor {
    private static string GroupKey(int groupIndex) => $"group:{groupIndex}";

    private static bool IsComposite(ComboAlternative alternative) => alternative.Category?.CategoryBases.Count > 1;

    private static IEnumerable<(int Start, int Count)> AlternativeTerms(ComboAlternativeGroup group) {
        for (int start = 0; start < group.Alternatives.Count;) {
            int end = start + 1;
            if (!IsComposite(group.Alternatives[start])) {
                while (end < group.Alternatives.Count && !IsComposite(group.Alternatives[end])) { end++; }
            }
            yield return (start, end - start);
            start = end;
        }
    }

    private void ToggleAlternativeTerm(ComboAlternativeGroup group, int groupIndex, int start, int count) {
        int open = Enumerable.Range(start, count).FirstOrDefault(index => IsOpenRow(group.Alternatives[index], index, groupIndex), -1);
        int selected = open < 0 ? start : open;
        BeginEdit(group.Alternatives[selected], selected, groupIndex);
    }

    private bool HasInlineAlternativeButton(string key) => Rows(Combo).Any(row => row.Group is not null
        && !IsComposite(row.Requirement) && RowKey(row.Requirement, row.Index, row.Group) == key);

    private string VisibleRowKey(string key) {
        foreach ((ComboAlternativeGroup group, int groupIndex) in Combo.AlternativeGroups.Select((group, index) => (group, index))) {
            foreach ((int start, int count) in AlternativeTerms(group)) {
                if (Enumerable.Range(start, count).Any(index => RowKey(group.Alternatives[index], index, groupIndex) == key)) {
                    return RowKey(group.Alternatives[start], start, groupIndex);
                }
            }
        }

        return key;
    }

    private static readonly string[] ComboOperators = ["+", "AND", "OR"];
    private readonly Dictionary<string, ExpressionSeparator> _connectors = [];
    private Dictionary<string, object> _rowIdentities = [];
    private string? _connectorErrorKey;
    private string? _connectorError;
    private string? _focusConnector;

    private string? ConnectorError(string key) => _connectorErrorKey == key ? _connectorError : null;

    private void RefuseInline(string key, string message) {
        _connectorErrorKey = key;
        _connectorError = message;
    }

    private ComboAlternative RequirementOperand(ComboRequirementPosition position) => position.Kind == ComboRequirementKind.Category
        ? ComboAlternative.For(Combo.Categories[position.Index]) : ComboAlternative.For(Combo.Cards[position.Index]);

    private ComboRequirementPosition? EditingPosition {
        get {
            if (_editingIndex is not int index || _editingRequirement is not { } editing) { return null; }
            ComboRequirementKind kind = editing.Category is not null ? ComboRequirementKind.Category : ComboRequirementKind.Card;
            if (_editingGroupIndex is not null) { kind = ComboRequirementKind.Group; }
            return new(kind, _editingGroupIndex ?? index);
        }
    }

    private bool CanApplyInline(IReadOnlyList<ComboRequirementPosition> positions, ComboGroupEdit edit) {
        bool editsAffected = EditingPosition is { } position && positions.Contains(position);
        return !AuthoringScope && !(editsAffected && EditingHasChanges) && ComboOperatorEditing.CanApply(Combo, positions, edit);
    }

    private IReadOnlyList<string> PeerOperators(ComboRequirementPosition right) {
        int index = Combo.RequirementOrder.ToList().IndexOf(right);
        if (index <= 0) { return ["+"]; }
        ComboRequirementPosition[] positions = [Combo.RequirementOrder[index - 1], right];
        ComboAlternative[] operands = positions.SelectMany(position => position.Kind == ComboRequirementKind.Group
            ? Combo.AlternativeGroups[position.Index].Alternatives : new[] { RequirementOperand(position) }).ToArray();
        bool hasGroup = positions.Any(position => position.Kind == ComboRequirementKind.Group);
        return ComboOperators.Where(operation => operation == "+"
            || (operation != "AND" || !hasGroup) && CanApplyInline(positions, new(operation, operands))).ToArray();
    }

    private IReadOnlyList<string> GroupOperators(int index) => CanApplyInline(
        [new(ComboRequirementKind.Group, index)], new("AND", Combo.AlternativeGroups[index].Alternatives)) ? ["AND", "OR"] : ["OR"];

    private IReadOnlyList<string> SameCardOperators(ComboAlternative requirement, int index, int? group) {
        ComboRequirementPosition position = group is int groupIndex
            ? new(ComboRequirementKind.Group, groupIndex) : new(ComboRequirementKind.Category, index);
        return CanApplyInline([position], new("OR", [requirement])) ? ["AND", "OR"] : ["AND"];
    }

    private Task BeginPeerConversion(ComboRequirementPosition right, string operation) {
        if (operation == "+" || !PeerOperators(right).Contains(operation)) { return Task.CompletedTask; }
        int index = Combo.RequirementOrder.ToList().IndexOf(right);
        if (index <= 0) { return Task.CompletedTask; }
        ComboRequirementPosition left = Combo.RequirementOrder[index - 1];
        string key = $"peer:{right}";
        if (operation == "AND" && (left.Kind == ComboRequirementKind.Group || right.Kind == ComboRequirementKind.Group)) {
            RefuseInline(key, "A nested OR cannot become same-card AND. Change its inner operator first.");
            return Task.CompletedTask;
        }
        ComboAlternative[] operands = new[] { left, right }.SelectMany(position => position.Kind == ComboRequirementKind.Group
            ? Combo.AlternativeGroups[position.Index].Alternatives : new[] { RequirementOperand(position) }).ToArray();
        return ApplyInline([left, right], new(operation, operands), key);
    }

    private Task ChangeGroupOperator(int index, string operation) => operation == "OR" || !GroupOperators(index).Contains(operation) ? Task.CompletedTask
        : ApplyInline([new(ComboRequirementKind.Group, index)], new(operation, Combo.AlternativeGroups[index].Alternatives), $"group:{index}");

    private Task ConvertSameCard(ComboAlternative requirement, int index, int? group, string operation) {
        if (operation == "AND" || requirement.Category is not { } category || !SameCardOperators(requirement, index, group).Contains(operation)) { return Task.CompletedTask; }
        ComboAlternative[] operands = category.CategoryBases.Select(property => ComboAlternative.For(
            new ComboCategory(property, category.MinCount, category.MaxCount, category.MaximumMode))).ToArray();
        if (group is int groupIndex) {
            List<ComboAlternative> alternatives = Combo.AlternativeGroups[groupIndex].Alternatives.ToList();
            alternatives.RemoveAt(index);
            alternatives.InsertRange(index, operands);
            return ApplyInline([new(ComboRequirementKind.Group, groupIndex)], new("OR", alternatives), $"same:{RowKey(requirement, index, group)}");
        }
        return ApplyInline([new(ComboRequirementKind.Category, index)], new(operation, operands), $"same:{RowKey(requirement, index, group)}");
    }

    private async Task ApplyInline(ComboRequirementPosition[] positions, ComboGroupEdit edit, string key) {
        _connectorErrorKey = null;
        _connectorError = null;
        bool editsAffected = EditingPosition is { } position && positions.Contains(position);
        if (AuthoringScope || editsAffected && EditingHasChanges) {
            RefuseInline(key, "Save or cancel the pending row before changing its grouping.");
            return;
        }
        try {
            Combo updated = ComboOperatorEditing.Apply(Combo, positions, edit);
            if (editsAffected) {
                CancelScopedAuthoring();
                _focusAlternativeEntry = false;
            }
            else {
                RemapEditingRow(updated);
            }
            int authoredIndex = positions.Min(position => Combo.RequirementOrder.ToList().IndexOf(position));
            ComboRequirementPosition result = updated.RequirementOrder[authoredIndex];
            _focusConnector = edit.Operator == "OR" ? $"group:{result.Index}" : null;
            if (_focusConnector is null) {
                _returnRowKey = result.Kind == ComboRequirementKind.Category ? $"category:{result.Index}" : $"card:{result.Index}";
                _focusAlternativeEntry = true;
            }
            TransferRowIdentities(updated);
            await OnReplaceCombo.InvokeAsync((Index, updated));
        }
        catch (ArgumentException exception) {
            RefuseInline(key, exception.Message);
        }
    }

    private static IEnumerable<(ComboAlternative Requirement, int Index, int? Group)> Rows(Combo combo) =>
        combo.Categories.Select((category, index) => (ComboAlternative.For(category), index, (int?)null))
            .Concat(combo.Cards.Select((card, index) => (ComboAlternative.For(card), index, (int?)null)))
            .Concat(combo.AlternativeGroups.SelectMany((group, groupIndex) => group.Alternatives.Select((leaf, index) => (leaf, index, (int?)groupIndex))));

    private object RowIdentity(ComboAlternative requirement, int index, int? group) {
        string key = RowKey(requirement, index, group);
        if (!_rowIdentities.TryGetValue(key, out object? identity)) {
            identity = new();
            _rowIdentities[key] = identity;
        }
        return identity;
    }

    private void TransferRowIdentities(Combo updated) {
        List<(ComboAlternative Requirement, object Identity)> previous = Rows(Combo)
            .Select(row => (row.Requirement, RowIdentity(row.Requirement, row.Index, row.Group))).ToList();
        Dictionary<string, object> identities = [];
        foreach ((ComboAlternative requirement, int index, int? group) in Rows(updated)) {
            int match = previous.FindIndex(row => SameRequirement(row.Requirement, requirement));
            if (match >= 0) {
                identities[RowKey(requirement, index, group)] = previous[match].Identity;
                previous.RemoveAt(match);
            }
        }
        foreach ((ComboAlternative requirement, int index, int? group) in Rows(updated)) {
            string key = RowKey(requirement, index, group);
            if (!identities.ContainsKey(key) && _rowIdentities.TryGetValue(key, out object? identity) && !identities.ContainsValue(identity)) {
                identities[key] = identity;
            }
        }
        _rowIdentities = identities;
    }

    private void RemapEditingRow(Combo updated) {
        if (_editingRequirement is not { } editing) { return; }
        // Track the occurrence too: legacy sessions may repeat the same object.
        int occurrence = Rows(Combo).TakeWhile(row => row.Index != _editingIndex || row.Group != _editingGroupIndex || !SameRequirement(row.Requirement, editing))
            .Count(row => SameRequirement(row.Requirement, editing));
        (ComboAlternative Requirement, int Index, int? Group)[] matches = Rows(updated).Where(row => SameRequirement(row.Requirement, editing)).ToArray();
        if (occurrence < matches.Length) {
            _editingIndex = matches[occurrence].Index;
            _editingGroupIndex = matches[occurrence].Group;
        }
    }

    private void ToggleNewRequirement() {
        if (_editingNewRequirement) { CancelRequirement(); return; }
        BeginTopLevelRequirement();
    }

    private void FinishNewRequirement(string key) {
        _editingNewRequirement = false;
        _hasNewRequirementRow = false;
        _returnRowKey = key;
        _focusAlternativeEntry = true;
    }

    private void BeginTopLevelRequirement() {
        if (_editingRequirement is not null || AuthoringScope) { CancelScopedAuthoring(); }
        _hasNewRequirementRow = true;
        _editingNewRequirement = true;
        _focusAlternativeEntry = true;
        _returnRowKey = null;
    }
}
