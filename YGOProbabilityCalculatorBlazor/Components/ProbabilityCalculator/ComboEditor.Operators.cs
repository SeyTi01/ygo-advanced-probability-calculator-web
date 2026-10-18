using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

public partial class ComboEditor {
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

    private Task BeginPeerConversion(ComboRequirementPosition right, string operation) {
        if (operation == "+") { return Task.CompletedTask; }
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

    private Task ChangeGroupOperator(int index, string operation) => operation == "OR" ? Task.CompletedTask
        : ApplyInline([new(ComboRequirementKind.Group, index)], new(operation, Combo.AlternativeGroups[index].Alternatives), $"group:{index}");

    private Task ConvertSameCard(ComboAlternative requirement, int index, int? group, string operation) {
        if (operation == "AND" || requirement.Category is not { } category) { return Task.CompletedTask; }
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
        ComboRequirementPosition? editingPosition = null;
        if (_editingIndex is int editingIndex && _editingRequirement is { } editing) {
            ComboRequirementKind kind = editing.Category is not null ? ComboRequirementKind.Category : ComboRequirementKind.Card;
            if (_editingGroupIndex is not null) { kind = ComboRequirementKind.Group; }
            editingPosition = new(kind, _editingGroupIndex ?? editingIndex);
        }
        bool editsAffected = editingPosition is { } position && positions.Contains(position);
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

    private async Task InsertGroupMember(int groupIndex, string? target) {
        if (!TryInlineTarget(target, out ComboAlternative? leaf)) { return; }
        if (_alternativeGroupIndex == groupIndex || _andGroupIndex == groupIndex) {
            RefuseInline($"group:{groupIndex}", "Finish the pending addition before inserting another condition.");
            return;
        }
        List<ComboAlternativeGroup> groups = Combo.AlternativeGroups.ToList();
        groups[groupIndex] = new(groups[groupIndex].Alternatives.Append(leaf!).ToArray());
        _connectorErrorKey = null;
        Combo updated = Combo.WithAlternativeGroups(groups);
        TransferRowIdentities(updated);
        await OnReplaceCombo.InvokeAsync((Index, updated));
    }

    private Task InsertRowMember(ComboAlternative requirement, int index, int? groupIndex, string target) {
        if (target.StartsWith("AND|", StringComparison.Ordinal)) {
            return InsertProperty(requirement, index, groupIndex, target[4..]);
        }
        if (groupIndex is int group) { return InsertGroupMember(group, target); }
        if (!TryInlineTarget(target, out ComboAlternative? leaf)) { return Task.CompletedTask; }
        return ApplyInline([new(requirement.Category is not null ? ComboRequirementKind.Category : ComboRequirementKind.Card, index)],
            new("OR", [requirement, leaf!]), RowKey(requirement, index, groupIndex));
    }

    private async Task InsertProperty(ComboAlternative requirement, int index, int? groupIndex, string? target) {
        if (requirement.Category is not { } category || _availableCategories.FirstOrDefault(candidate => candidate.Identity == target) is not { } property) { return; }
        if (_editingRequirement is not null && SameRequirement(_editingRequirement, requirement)
            && _editingIndex == index && _editingGroupIndex == groupIndex) {
            if (_selectedKind != "Category") {
                RefuseInline(RowKey(requirement, index, groupIndex), "Save or cancel the target change first.");
                return;
            }
            if (!_draftProperties.Any(candidate => candidate.Identity == property.Identity)) { _draftProperties.Add(property); }
            return;
        }
        if (AuthoringScope) {
            RefuseInline(RowKey(requirement, index, groupIndex), "Finish the pending addition first.");
            return;
        }
        ComboCategory replacement;
        try {
            replacement = ComboCategory.SameCard(category.CategoryBases.Append(property), category.MinCount, category.MaxCount, category.MaximumMode);
        }
        catch (ArgumentException exception) {
            RefuseInline(RowKey(requirement, index, groupIndex), exception.Message);
            return;
        }
        Combo updated;
        if (groupIndex is int group) {
            List<ComboAlternativeGroup> groups = Combo.AlternativeGroups.ToList();
            List<ComboAlternative> leaves = groups[group].Alternatives.ToList();
            leaves[index] = ComboAlternative.For(replacement);
            groups[group] = new(leaves);
            updated = Combo.WithAlternativeGroups(groups);
        }
        else {
            List<ComboCategory> categories = Combo.Categories.ToList();
            categories[index] = replacement;
            updated = Combo.WithCategories(categories);
        }
        _connectorErrorKey = null;
        TransferRowIdentities(updated);
        await OnReplaceCombo.InvokeAsync((Index, updated));
    }

    private bool TryInlineTarget(string? target, out ComboAlternative? requirement) {
        requirement = null;
        if (target?.StartsWith("card:", StringComparison.Ordinal) == true && Cards.Any(card => card.Id == target[5..])) {
            requirement = ComboAlternative.For(new ComboCard(target[5..], 1, HandSize, RequirementMaximumMode.HandSize));
        }
        else if (_availableCategories.FirstOrDefault(category => category.Identity == target) is { } category) {
            requirement = ComboAlternative.For(new ComboCategory(category, 1, HandSize, RequirementMaximumMode.HandSize));
        }
        return requirement is not null;
    }

    private void BeginTopLevelRequirement() {
        if (_editingRequirement is not null || AuthoringScope) { CancelScopedAuthoring(); }
        _focusAlternativeEntry = true;
        _returnRowKey = null;
    }
}
