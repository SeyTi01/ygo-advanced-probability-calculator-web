using YGOProbabilityCalculatorBlazor.Localization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

public partial class ComboEditor {
    private sealed record LanguageState(
        string Kind,
        string Category,
        string Card,
        string Minimum,
        string Maximum,
        string? EditingKind,
        int? EditingIndex,
        int? EditingGroup,
        bool EditingNew,
        bool NewRow,
        int? AlternativeGroup,
        int? AlternativeInsert,
        string? SeedKind,
        int? SeedIndex,
        int? AndIndex,
        int? AndGroup,
        int? AndAlternative,
        (string Kind, string Category, string Card, string Min, string Max)? Suspended,
        CategoryBase[] Properties,
        KeyValuePair<string, CategoryBase>[] Numeric,
        KeyValuePair<int, string>[] Errors,
        string? Error,
        KeyValuePair<string, int>[] Terms,
        int? AlternativeTerm
    );

    private string CaptureLanguageDraft() {
        List<object> terms = [];
        KeyValuePair<string, int>[] termGroups = _rowIdentities.Select(entry => {
            object term = _termIdentities.GetValueOrDefault(entry.Value, entry.Value);
            if (!terms.Contains(term)) { terms.Add(term); }
            return new KeyValuePair<string, int>(entry.Key, terms.IndexOf(term));
        }).ToArray();
        int activeTerm = _alternativeTermIdentity is null ? -1 : terms.IndexOf(_alternativeTermIdentity);
        return LanguageDraftState.Pack(new LanguageState(
            _selectedKind,
            _selectedCategory,
            _selectedCardId,
            _minCount,
            _maxCount,
            _editingRequirement?.Kind,
            _editingIndex,
            _editingGroupIndex,
            _editingNewRequirement,
            _hasNewRequirementRow,
            _alternativeGroupIndex,
            _alternativeInsertIndex,
            _alternativeSeed?.Kind,
            _alternativeSeedIndex,
            _andSeedIndex,
            _andGroupIndex,
            _andAlternativeIndex,
            _suspendedDraft,
            _draftProperties.ToArray(),
            _numericCategories.ToArray(),
            _propertyErrors.ToArray(),
            _error,
            termGroups,
            activeTerm < 0 ? null : activeTerm
        ));
    }

    private void RestoreLanguageDraft(string json) {
        LanguageState state = LanguageDraftState.Unpack<LanguageState>(json);
        _selectedKind = state.Kind; _selectedCategory = state.Category; _selectedCardId = state.Card;
        _minCount = state.Minimum; _maxCount = state.Maximum;
        _editingIndex = state.EditingIndex; _editingGroupIndex = state.EditingGroup;
        _editingRequirement = LanguageRequirement(state.EditingKind, state.EditingIndex, state.EditingGroup);
        _editingNewRequirement = state.EditingNew; _hasNewRequirementRow = state.NewRow;
        _alternativeGroupIndex = state.AlternativeGroup; _alternativeInsertIndex = state.AlternativeInsert;
        _alternativeGroup = state.AlternativeGroup is int group ? Combo.AlternativeGroups[group] : null;
        _alternativeSeedIndex = state.SeedIndex;
        _alternativeSeed = LanguageRequirement(state.SeedKind, state.SeedIndex, null);
        _andSeedIndex = state.AndIndex; _andGroupIndex = state.AndGroup; _andAlternativeIndex = state.AndAlternative;
        _andGroup = state.AndGroup is int andGroup ? Combo.AlternativeGroups[andGroup] : null;
        _andSeed = _andGroup is not null && state.AndAlternative is int alternative
            ? _andGroup.Alternatives[alternative].Category
            : state.AndIndex is int andIndex ? Combo.Categories[andIndex] : null;
        _suspendedDraft = state.Suspended;
        _draftProperties.Clear(); _draftProperties.AddRange(state.Properties);
        _numericCategories.Clear(); foreach (KeyValuePair<string, CategoryBase> category in state.Numeric) { _numericCategories.Add(category.Key, category.Value); }
        _propertyErrors.Clear(); foreach (KeyValuePair<int, string> error in state.Errors) { _propertyErrors.Add(error.Key, error.Value); }
        _error = state.Error;
        Dictionary<int, object> terms = state.Terms.Select(entry => entry.Value).Distinct().ToDictionary(index => index, _ => new object());
        if (state.Terms.Length != _rowIdentities.Count || state.Terms.Any(entry => !_rowIdentities.ContainsKey(entry.Key))) {
            throw new InvalidOperationException(UiText.Get("Language.CaptureFailed"));
        }
        foreach (KeyValuePair<string, int> member in state.Terms) { _termIdentities[_rowIdentities[member.Key]] = terms[member.Value]; }
        _alternativeTermIdentity = state.AlternativeTerm is int activeTerm ? terms[activeTerm] : null;
        _previousCombo = Combo;
    }

    private ComboAlternative? LanguageRequirement(string? kind, int? index, int? group) {
        if (kind is null || index is not int position) { return null; }
        if (group is int groupIndex) { return Combo.AlternativeGroups[groupIndex].Alternatives[position]; }
        return kind == "Card" ? ComboAlternative.For(Combo.Cards[position]) : ComboAlternative.For(Combo.Categories[position]);
    }
}
