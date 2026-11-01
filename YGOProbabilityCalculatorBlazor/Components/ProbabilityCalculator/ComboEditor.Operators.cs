using YGOProbabilityCalculatorBlazor.Localization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

public partial class ComboEditor {
    private static string GroupKey(int groupIndex) => $"group:{groupIndex}";
    private static string PeerKey(ComboRequirementPosition right) => $"peer:{right}";

    private static bool IsComposite(ComboAlternative alternative) => alternative.Category?.CategoryBases.Count > 1;

    private IEnumerable<(int Start, int Count)> AlternativeTerms(ComboAlternativeGroup group) {
        int groupIndex = Combo.AlternativeGroups.IndexOf(group);
        for (int start = 0; start < group.Alternatives.Count;) {
            int end = start + 1;
            object term = TermIdentity(group.Alternatives[start], start, groupIndex);
            while (end < group.Alternatives.Count
                && ReferenceEquals(term, TermIdentity(group.Alternatives[end], end, groupIndex))) { end++; }
            yield return (start, end - start);
            start = end;
        }
    }

    private void ToggleAlternativeTerm(ComboAlternativeGroup group, int groupIndex, int start, int count) {
        int open = Enumerable.Range(start, count).FirstOrDefault(index => IsOpenRow(group.Alternatives[index], index, groupIndex), -1);
        int selected = open < 0 ? start : open;
        BeginEdit(group.Alternatives[selected], selected, groupIndex);
    }

    private bool HasInlineAlternativeButton(string key) {
        foreach ((ComboAlternativeGroup group, int groupIndex) in Combo.AlternativeGroups.Select((group, index) => (group, index))) {
            foreach ((int start, int count) in AlternativeTerms(group)) {
                if (count == 1 && IsComposite(group.Alternatives[start])) { continue; }
                if (Enumerable.Range(start, count).Any(index => RowKey(group.Alternatives[index], index, groupIndex) == key)) {
                    return true;
                }
            }
        }
        return false;
    }

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
    // A visible row can contain several independently editable requirements.
    // Keep that presentation identity when the solver flattens OR groups, or
    // when removing a member changes the first requirement in an existing row.
    private readonly Dictionary<object, object> _termIdentities = [];
    private Combo? _identityCombo;
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
        return ComboOperators.Where(operation => operation == "+"
            || operation == "OR" && CanApplyInline(positions, new(operation, operands))).ToArray();
    }

    private IReadOnlyList<string> GroupOperators(int index) => AlternativeTerms(Combo.AlternativeGroups[index]).Count() == 1 && CanApplyInline(
        [new(ComboRequirementKind.Group, index)], new("AND", Combo.AlternativeGroups[index].Alternatives)) ? ["AND", "OR"] : ["OR"];

    private IReadOnlyList<string> AlternativeRowOperators(int groupIndex, int? boundary) => boundary is null
        ? GroupOperators(groupIndex) : new[] { "+" }.Concat(GroupOperators(groupIndex)).ToArray();

    private Task ChangeAlternativeRowOperator(int groupIndex, int? boundary, string operation) => operation == "+" && boundary is int split
        ? SplitAlternativeRows(groupIndex, split) : ChangeGroupOperator(groupIndex, operation);

    private IReadOnlyList<string> SameCardOperators(ComboAlternative requirement, int index, int? group) {
        if (group is not null) { return ["AND"]; }
        ComboRequirementPosition position = new(ComboRequirementKind.Category, index);
        return CanApplyInline([position], new("OR", [requirement])) ? ["AND", "OR"] : ["AND"];
    }

    private Task BeginPeerConversion(ComboRequirementPosition right, string operation) {
        if (operation == "+" || !PeerOperators(right).Contains(operation)) { return Task.CompletedTask; }
        int index = Combo.RequirementOrder.ToList().IndexOf(right);
        if (index <= 0) { return Task.CompletedTask; }
        ComboRequirementPosition left = Combo.RequirementOrder[index - 1];
        string key = $"peer:{right}";
        ComboAlternative[] operands = new[] { left, right }.SelectMany(position => position.Kind == ComboRequirementKind.Group
            ? Combo.AlternativeGroups[position.Index].Alternatives : new[] { RequirementOperand(position) }).ToArray();
        return ApplyInline([left, right], new(operation, operands), key);
    }

    private Task ChangeGroupOperator(int index, string operation) => operation == "OR" || !GroupOperators(index).Contains(operation) ? Task.CompletedTask
        : ApplyInline([new(ComboRequirementKind.Group, index)], new(operation, Combo.AlternativeGroups[index].Alternatives), $"group:{index}");

    private async Task SplitAlternativeRows(int groupIndex, int boundary) {
        ComboRequirementPosition position = new(ComboRequirementKind.Group, groupIndex);
        string key = GroupKey(groupIndex);
        bool editsAffected = EditingPosition == position;
        if (AuthoringScope || editsAffected && EditingHasChanges) {
            RefuseInline(key, UiText.Get("Ui.SaveOrCancelThePendingRowBeforeChangingItsGrouping"));
            return;
        }
        Combo updated = ComboOperatorEditing.SplitAlternatives(Combo, groupIndex, boundary);
        if (editsAffected) {
            CancelScopedAuthoring();
            _focusAlternativeEntry = false;
        }
        else { RemapEditingRow(updated); }
        int authoredIndex = Combo.RequirementOrder.ToList().IndexOf(position);
        _focusConnector = PeerKey(updated.RequirementOrder[authoredIndex + 1]);
        TransferRowIdentities(updated);
        _connectorErrorKey = null;
        _connectorError = null;
        await OnReplaceCombo.InvokeAsync((Index, updated));
    }

    private Task ConvertSameCard(ComboAlternative requirement, int index, int? group, string operation) {
        if (operation == "AND" || requirement.Category is not { } category || !SameCardOperators(requirement, index, group).Contains(operation)) { return Task.CompletedTask; }
        ComboAlternative[] operands = category.CategoryBases.Select(property => ComboAlternative.For(
            new ComboCategory(property, category.MinCount, category.MaxCount, category.MaximumMode))).ToArray();
        return ApplyInline([new(ComboRequirementKind.Category, index)], new(operation, operands), $"same:{RowKey(requirement, index, group)}");
    }

    private async Task ApplyInline(ComboRequirementPosition[] positions, ComboGroupEdit edit, string key) {
        _connectorErrorKey = null;
        _connectorError = null;
        bool editsAffected = EditingPosition is { } position && positions.Contains(position);
        if (AuthoringScope || editsAffected && EditingHasChanges) {
            RefuseInline(key, UiText.Get("Ui.SaveOrCancelThePendingRowBeforeChangingItsGrouping"));
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

    private object TermIdentity(ComboAlternative requirement, int index, int? group) {
        object member = RowIdentity(requirement, index, group);
        if (!_termIdentities.TryGetValue(member, out object? term)) {
            term = new object();
            _termIdentities[member] = term;
        }
        return term;
    }

    private void ReconcileTermIdentities() {
        if (_identityCombo is not null && !ReferenceEquals(_identityCombo, Combo)) {
            TransferRowIdentities(Combo);
        }
        _identityCombo = Combo;
        foreach ((ComboAlternativeGroup group, int groupIndex) in Combo.AlternativeGroups.Select((group, index) => (group, index))) {
            object? previousTerm = null;
            foreach ((ComboAlternative leaf, int index) in group.Alternatives.Select((leaf, index) => (leaf, index))) {
                object member = RowIdentity(leaf, index, groupIndex);
                bool composite = IsComposite(leaf);
                if (!_termIdentities.ContainsKey(member)) {
                    _termIdentities[member] = composite || previousTerm is null ? new object() : previousTerm;
                }
                previousTerm = composite ? null : _termIdentities[member];
            }
        }
        foreach ((ComboAlternative requirement, int index, int? group) in Rows(Combo)) {
            TermIdentity(requirement, index, group);
        }
        HashSet<object> members = _rowIdentities.Values.ToHashSet();
        foreach (object removed in _termIdentities.Keys.Where(member => !members.Contains(member)).ToArray()) {
            _termIdentities.Remove(removed);
        }
    }

    private void TransferRowIdentities(Combo updated) {
        List<(ComboAlternative Requirement, object Identity)> previous = Rows(_identityCombo ?? Combo)
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
        _identityCombo = updated;
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

    private void FinishNewRequirement(string key) {
        _editingNewRequirement = false;
        _returnRowKey = key;
        _focusAlternativeEntry = true;
    }

    private void BeginTopLevelRequirement() {
        if (_editingRequirement is not null || AuthoringScope) { CancelScopedAuthoring(); }
        _editingNewRequirement = true;
        _focusAlternativeEntry = true;
        _returnRowKey = null;
    }
}
