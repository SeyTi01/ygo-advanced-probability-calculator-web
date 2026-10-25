using YGOProbabilityCalculatorBlazor.Localization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

public partial class SelectorExpressionEditor {
    private sealed record LanguageOperand(CardSelector Original, string Identity, string? Error, CardSelector? Group);
    private sealed record LanguageRow(CardSelector Value, CardSelector[] Terms);
    private sealed record LanguageState(
        bool Editing,
        string Mode,
        CardSelectorKind Operation,
        CardSelector? Seed,
        int? NestedIndex,
        CardSelector? NestedTarget,
        string? Error,
        string? OperatorError,
        LanguageOperand[] Operands,
        LanguageRow[] Rows
    );

    private string CaptureLanguageDraft() => LanguageDraftState.Pack(new LanguageState(
        IsEditing,
        _mode,
        _operation,
        _seed,
        _nestedIndex,
        _nestedTarget,
        _error,
        _operatorError,
        _operands.Select(operand => new LanguageOperand(operand.Original, operand.Identity, operand.Error, operand.Group)).ToArray(),
        Rows.Select(row => new LanguageRow(row.Value, row.Terms)).ToArray()
    ));

    private void RestoreLanguageDraft(string json) {
        LanguageState state = LanguageDraftState.Unpack<LanguageState>(json);
        _mode = state.Mode;
        _operation = state.Operation;
        _seed = state.Seed;
        _nestedIndex = state.NestedIndex;
        _nestedTarget = state.NestedTarget;
        _error = state.Error;
        _operatorError = state.OperatorError;
        _operands.Clear();
        _operands.AddRange(state.Operands.Select(operand => new OperandDraft(operand.Original) {
            Identity = operand.Identity,
            Error = operand.Error,
            Group = operand.Group
        }));
        Rows = state.Rows.Select(row => new SelectorRow(row.Value, row.Terms)).ToArray();
        foreach (SelectorRow row in Rows) { _termKeys.TryAdd(row.Value.Signature, new object()); }
        if (state.Editing) {
            _validationOwner = Selector;
            EditorSession.ActiveNode = _node;
            EditorSession.ActiveSelector = Selector;
            EditorSession.HasChanges = HasChanges;
            EditorSession.CancelActive = () => InvokeAsync(() => Close(false));
        }
    }
}
