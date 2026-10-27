using Microsoft.AspNetCore.Components;
using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Localization;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

public partial class ProbabilityCalculatorComponent {
    [CascadingParameter] public LanguageWorkspace? LanguageWorkspace { get; set; }
    private bool _languageWorkspaceApplied;

    private void RecoveryInspected() {
        if (_languageWorkspaceApplied) {
            _languageWorkspaceApplied = false;
            LanguageWorkspace?.Accept();
        }
        StateHasChanged();
    }

    private sealed record LanguageState(
        string Session,
        CalculationStorageTier Storage,
        ProbabilityCalculationResult? Result,
        bool Stale,
        PinnedResultSnapshot? Accepted,
        PinnedResultSnapshot? Pin
    );

    private string CaptureLanguageWorkspace() {
        if (_isCalculating) { throw new InvalidOperationException(UiText.Get("Language.WaitForCalculation")); }
        if (!_sessionInitialized || _pendingLanguageLoad is not null || _sharedLoading || _ydkeImportRequest is not null) {
            throw new InvalidOperationException(UiText.Get("Language.WaitForLoad"));
        }
        string session = _sessionService.SerializeSession(CaptureSession());
        Task<SessionState> restored = _sessionService.LoadSessionAsync(session);
        if (!restored.IsCompletedSuccessfully || _sessionService.SerializeSession(restored.Result) != session) {
            throw new InvalidOperationException(UiText.Get("Language.CaptureFailed"));
        }
        LanguageState state = new(
            session,
            _storageTier,
            _calculationResult,
            _calculationResultIsStale,
            _acceptedResultSnapshot,
            _pinnedResult
        );
        return JsonSerializer.Serialize(state);
    }

    private async Task RestoreLanguageWorkspaceAsync(string checkpoint) {
        LanguageState state = JsonSerializer.Deserialize<LanguageState>(checkpoint)
            ?? throw new InvalidOperationException(UiText.Get("Language.CaptureFailed"));
        if (state.Session is null || !Enum.IsDefined(state.Storage)
            || state.Accepted?.IsValid == false || state.Pin?.IsValid == false) {
            throw new InvalidOperationException(UiText.Get("Language.CaptureFailed"));
        }
        // Offline validation precedes mutation. No catalog enrichment, defaults or share consumption.
        SessionState session = await _sessionService.LoadSessionAsync(state.Session);
        _cards.AddRange(session.Cards);
        _categoryBases.AddRange(session.Categories);
        _combos.AddRange(session.Combos);
        _comboGroups.AddRange(session.ComboGroups ?? []);
        foreach (KeyValuePair<string, int> color in session.CategoryColorIndices ?? []) { _categoryColorIndices.Add(color.Key, color.Value); }
        _handSize = session.HandSize;
        _selectedModel = session.CalculationModel;
        _fastStrategy = session.FastExecutionStrategy;
        _drawActivationPriority = session.DrawActivationPriority ?? DrawActivationPriority.Default;
        _storageTier = state.Storage;
        _calculationResult = state.Result;
        _calculationResultIsStale = state.Stale;
        _acceptedResultSnapshot = state.Accepted;
        _pinnedResult = state.Pin;
    }
}
