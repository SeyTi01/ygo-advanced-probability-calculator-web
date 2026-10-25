using Microsoft.AspNetCore.Components;
using YGOProbabilityCalculatorBlazor.Localization;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

public partial class ProbabilityCalculatorComponent {
    [CascadingParameter] public LanguageWorkspace? LanguageWorkspace { get; set; }

    private sealed record LanguageState(
        string Session,
        int ActiveCard,
        int ActiveCombo,
        bool ImportOpen,
        string ImportCode,
        CalculationStorageTier Storage,
        ProbabilityCalculationResult? Result,
        bool Stale,
        PinnedResultSnapshot? Accepted,
        PinnedResultSnapshot? Pin
    );

    private string CaptureLanguageDraft() {
        if (!_sessionInitialized || _loadingSessionVersion is not null || _sharedLoading || _ydkeImportRequest is not null) {
            throw new InvalidOperationException(UiText.Get("Language.WaitForLoad"));
        }
        string session = _sessionService.SerializeSession(CaptureSession());
        Task<SessionState> restored = _sessionService.LoadSessionAsync(session);
        if (!restored.IsCompletedSuccessfully || _sessionService.SerializeSession(restored.Result) != session) {
            throw new InvalidOperationException(UiText.Get("Language.CaptureFailed"));
        }
        return LanguageDraftState.Pack(new LanguageState(
            session,
            _activeCardIndex,
            _activeComboIndex,
            _isYdkeImportOpen,
            _ydkeCode,
            _storageTier,
            _calculationResult,
            _calculationResultIsStale,
            _acceptedResultSnapshot,
            _pinnedResult
        ));
    }

    private void RestoreLanguageDraft(string json) {
        LanguageState state = LanguageDraftState.Unpack<LanguageState>(json);
        // The session codec is offline and synchronous. Do not enrich or apply defaults.
        Task<SessionState> load = _sessionService.LoadSessionAsync(state.Session);
        if (!load.IsCompletedSuccessfully) { throw new InvalidOperationException(UiText.Get("Language.CaptureFailed")); }
        SessionState session = load.Result;
        _cards.Clear(); _cards.AddRange(session.Cards);
        _categoryBases.Clear(); _categoryBases.AddRange(session.Categories);
        _combos.Clear(); _combos.AddRange(session.Combos);
        _comboGroups.Clear(); _comboGroups.AddRange(session.ComboGroups ?? []);
        _categoryColorIndices.Clear();
        foreach (KeyValuePair<string, int> color in session.CategoryColorIndices ?? []) { _categoryColorIndices.Add(color.Key, color.Value); }
        _handSize = session.HandSize;
        _selectedModel = session.CalculationModel;
        _fastStrategy = session.FastExecutionStrategy;
        _drawActivationPriority = session.DrawActivationPriority ?? DrawActivationPriority.Default;
        _activeCardIndex = state.ActiveCard;
        _activeComboIndex = state.ActiveCombo;
        _isYdkeImportOpen = state.ImportOpen;
        _ydkeCode = state.ImportCode;
        _storageTier = state.Storage;
        _calculationResult = state.Result;
        _calculationResultIsStale = state.Stale;
        _acceptedResultSnapshot = state.Accepted;
        _pinnedResult = state.Pin;
    }
}
