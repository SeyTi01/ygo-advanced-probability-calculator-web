using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

internal sealed record SelectorRow(CardSelector Value, CardSelector[] Terms);

internal sealed record SelectorOperatorChange(CardSelector Updated, CardSelector Restored);

internal sealed class SelectorOperandDraft(CardSelector selector) {
    public CardSelector Original { get; set; } = selector;
    public string Identity { get; set; } = selector.Kind is CardSelectorKind.LevelAtLeast or CardSelectorKind.NumericRange
        && NumericPropertyRange.TryFromSelector(selector, out NumericPropertyRange? range)
            ? range.Identity : selector.CategoryIdentities.FirstOrDefault() ?? "";
    public string? Error { get; set; }
    public CardSelector? Group { get; set; } = selector.Kind is CardSelectorKind.AllOf or CardSelectorKind.AnyOf ? selector : null;

    public CardSelector Build() {
        if (Group is not null) { return Group; }
        if ((Original.Kind == CardSelectorKind.Category || SelectorExpressionEditing.IsCatalogIdentity(Original))
            && Identity == Original.CategoryIdentities[0]
            || NumericPropertyRange.TryFromSelector(Original, out NumericPropertyRange? originalRange) && Identity == originalRange.Identity) {
            return Original;
        }
        if (Identity.StartsWith("metadata:catalog-id:", StringComparison.Ordinal)) {
            return CardSelector.Objective(new(CardSelectorKind.Category, [Identity]));
        }

        return Identity.StartsWith("metadata:numeric:", StringComparison.Ordinal)
            && NumericPropertyRange.TryFromIdentity(Identity, out NumericPropertyRange? range)
                ? range.ToSelector() : new(CardSelectorKind.Category, [Identity]);
    }

    public void RenameCategory(string oldIdentity, string newIdentity) {
        if (Identity == oldIdentity) { Identity = newIdentity; }
        Group = Group?.RenameCategory(oldIdentity, newIdentity);
        Original = Original.RenameCategory(oldIdentity, newIdentity);
    }
}

// These operations only transform selector models and their authored row/draft
// projections. Active editor ownership, keyed components, validation and focus
// remain the responsibility of SelectorExpressionEditor.
internal static class SelectorExpressionEditing {
    public static bool IsCatalogIdentity(CardSelector selector) => selector.Kind == CardSelectorKind.Objective
        && selector.Children[0].Kind == CardSelectorKind.Category
        && selector.Children[0].CategoryIdentities[0].StartsWith("metadata:catalog-id:", StringComparison.Ordinal);

    public static CardSelector Combine(CardSelectorKind kind, IEnumerable<CardSelector> terms) => kind == CardSelectorKind.AllOf
        ? CardSelector.AllOfSelectors(terms) : CardSelector.AnyOfSelectors(terms);

    // A legacy row is a maximal adjacent run of direct operands. Nested groups
    // remain independent terms; projection never changes the stored Boolean tree.
    public static SelectorRow[] ProjectRows(CardSelectorKind operation, CardSelector[] terms) {
        List<SelectorRow> rows = [];
        List<CardSelector> run = [];
        foreach (CardSelector term in terms) {
            if (term.Kind is CardSelectorKind.AllOf or CardSelectorKind.AnyOf) {
                FlushRun();
                rows.Add(new(term, [term]));
            }
            else {
                run.Add(term);
            }
        }
        FlushRun();

        return rows.ToArray();

        void FlushRun() {
            if (run.Count == 0) { return; }
            CardSelector[] operands = run.ToArray();
            rows.Add(new(Combine(operation, operands), operands));
            run.Clear();
        }
    }

    public static SelectorRow[] RetainRows(CardSelectorKind operation, CardSelector[] terms, SelectorRow[]? retained) {
        if (retained is null) { return ProjectRows(operation, terms); }
        List<SelectorRow> rows = [];
        HashSet<CardSelector> assigned = [];
        foreach (SelectorRow row in retained) {
            CardSelector[] members = row.Terms.SelectMany(term => term.Kind == operation ? term.AuthoredTerms : [term])
                .Where(term => terms.Contains(term) && assigned.Add(term)).ToArray();
            if (members.Length > 0) { rows.Add(new(Combine(operation, members), members)); }
        }
        rows.AddRange(ProjectRows(operation, terms.Where(term => !assigned.Contains(term)).ToArray()));

        return rows.ToArray();
    }

    public static SelectorRow[] ReconcileRows(CardSelector selector, SelectorRow[] previous, SelectorRow[]? retained) {
        if (selector.Kind is not (CardSelectorKind.AllOf or CardSelectorKind.AnyOf)) { return []; }
        CardSelector[] terms = selector.AuthoredTerms.ToArray();
        bool previousRowsCoverTerms = terms.All(term => previous.Any(row => row.Terms.Any(previousTerm =>
            previousTerm.Equals(term) || previousTerm.Kind == selector.Kind && previousTerm.AuthoredTerms.Contains(term))));

        return RetainRows(selector.Kind, terms, retained ?? (previousRowsCoverTerms ? previous : null));
    }

    public static SelectorRow[] RenameRows(SelectorRow[] rows, string oldIdentity, string newIdentity) => rows
        .Select(row => new SelectorRow(row.Value.RenameCategory(oldIdentity, newIdentity),
            row.Terms.Select(term => term.RenameCategory(oldIdentity, newIdentity)).ToArray())).ToArray();

    public static SelectorOperandDraft[] ReconcileOperands(
        IReadOnlyList<SelectorOperandDraft> drafts,
        CardSelector[] previousTerms,
        CardSelector[] terms
    ) {
        // Keep local edits and incorporate new siblings in authored order, so a
        // save cannot erase a concurrent addition. Locally removed terms stay gone.
        List<SelectorOperandDraft> remaining = drafts.ToList();
        List<SelectorOperandDraft> refreshed = [];
        foreach (CardSelector term in terms) {
            int index = remaining.FindIndex(operand => operand.Original.Equals(term));
            if (index >= 0) {
                refreshed.Add(remaining[index]);
                remaining.RemoveAt(index);
            }
            else if (!previousTerms.Contains(term)) {
                refreshed.Add(new(term));
            }
        }

        return refreshed.ToArray();
    }

    public static SelectorOperatorChange? ChangeOperator(CardSelector selector, SelectorRow[] rows, CardSelectorKind kind) {
        if (selector.Kind is not (CardSelectorKind.AllOf or CardSelectorKind.AnyOf)) { return null; }
        try {
            CardSelector updated = Combine(kind, rows.Length > 1 ? rows.Select(row => row.Value) : selector.AuthoredTerms);
            if (updated.Kind is not (CardSelectorKind.AllOf or CardSelectorKind.AnyOf)) { return null; }
            SelectorRow[] updatedRows = RetainRows(updated.Kind, updated.AuthoredTerms.ToArray(), rows);
            CardSelector restored = Combine(selector.Kind, updatedRows.Length > 1 ? updatedRows.Select(row => row.Value) : updated.AuthoredTerms);
            SelectorRow[] restoredRows = RetainRows(restored.Kind, restored.AuthoredTerms.ToArray(), updatedRows);

            return OperatorReversibility.SameExpression(selector, restored) && SameRows(rows, restoredRows)
                ? new(updated, restored) : null;
        }
        catch (ArgumentException) { return null; }
    }

    public static SelectorOperatorChange? ReplaceRowOperator(
        CardSelector selector,
        SelectorRow[] rows,
        int position,
        CardSelector updated,
        CardSelector restored
    ) {
        try {
            SelectorRow[] replaced = WithRow(rows, position, updated);
            CardSelector forward = Combine(selector.Kind, replaced.Select(row => row.Value));
            SelectorRow[] forwardRows = RetainRows(forward.Kind, forward.AuthoredTerms.ToArray(), replaced);
            int target = Array.FindIndex(forwardRows, row => OperatorReversibility.SameExpression(row.Value, updated));
            if (target < 0) { return null; }
            SelectorRow[] reversed = WithRow(forwardRows, target, restored);
            CardSelector backward = Combine(selector.Kind, reversed.Select(row => row.Value));
            SelectorRow[] backwardRows = RetainRows(backward.Kind, backward.AuthoredTerms.ToArray(), reversed);

            return OperatorReversibility.SameExpression(selector, backward) && SameRows(rows, backwardRows)
                ? new(forward, backward) : null;
        }
        catch (ArgumentException) { return null; }
    }

    public static SelectorRow[] WithRow(SelectorRow[] rows, int position, CardSelector value) => rows
        .Select((row, index) => index == position ? new SelectorRow(value, [value]) : row).ToArray();

    public static bool ContainsSelector(CardSelector parent, CardSelector target) => parent.Equals(target)
        || parent.Kind is CardSelectorKind.AllOf or CardSelectorKind.AnyOf && parent.AuthoredTerms.Any(child => ContainsSelector(child, target));

    private static bool SameRows(SelectorRow[] original, SelectorRow[] restored) => original.Length == restored.Length
        && original.Zip(restored).All(pair => OperatorReversibility.SameExpression(pair.First.Value, pair.Second.Value));
}
