using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Session;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

// Every workspace replacement checks the same request, draft, and accepted-model boundary.
public partial class ProbabilityCalculatorComponent : IAsyncDisposable {
    private int _autosaveReplacementVersion;
    private long _sessionLoadVersion;
    private bool _sessionInitialized;
    private long _workspaceEditVersion;
    private long? _loadingSessionVersion;

    private void ObserveWorkspaceEdit() {
        _workspaceEditVersion++;
        _loadingSessionVersion = null;
    }

    private void InvalidateSessionLoad() {
        _sessionLoadVersion++;
        _loadingSessionVersion = null;
        _sharedLoading = false;
    }

    // Initialization and workspace events render at their first incomplete await.
    private async Task BeforeSessionWorkAsync() =>
        await SessionLoadingJS.InvokeVoidAsync("sessionLoading.beforeWork");

    private void EndSessionLoad(SessionLoadRequest request) {
        if (_loadingSessionVersion == request.Version) {
            _loadingSessionVersion = null;
        }
    }

    private bool HasMeaningfulWorkspaceContent =>
        _cards.Count > 0
        || _categoryBases.Count > 0
        || _combos.Count > 0
        || _comboGroups.Count > 0
        || _handSize != 5
        || _calculationResult is not null
        || _pinnedResult is not null
        || _isYdkeImportOpen
        || _ydkeCode.Length > 0
        || _ydkeImportRequest is not null
        || _sharedFragment is not null
        || _sharingRecovery?.HasRecovery == true
        || _pendingSessionService.PendingSession is not null;

    private void RequestClearWorkspace() {
        if (HasMeaningfulWorkspaceContent) {
            _isClearWorkspaceConfirmationOpen = true;
            return;
        }

        ClearWorkspace();
    }

    private void CancelClearWorkspace() => _isClearWorkspaceConfirmationOpen = false;

    private void ConfirmClearWorkspace() => ClearWorkspace();

    private void ClearWorkspace() {
        _isClearWorkspaceConfirmationOpen = false;
        InvalidateSessionLoad();
        _ydkeImportRequest = null;
        _pendingSessionService.PendingSession = null;
        ConsumeSharedFragment();
        _sharedFragment = _sharedJson = _sharedError = null;
        _sharedLoading = false;

        InvalidateCalculation(clearPreviousResult: true);
        _categoryBases.Clear();
        _drawActivationPriority = DrawActivationPriority.Default;
        _cards.Clear();
        _combos.Clear();
        _comboGroups.Clear();
        _categoryColorIndices.Clear();
        _pinnedResult = null;
        _categoryRename = null;
        _categoryRenameVersion++;
        _handSize = 5;
        _activeCardIndex = _activeComboIndex = -1;
        _isYdkeImportOpen = false;
        _ydkeCode = string.Empty;
        _importError = null;
        _sessionVersion++;
        _autosaveReplacementVersion++;
    }

    private SessionState CaptureSession() => new() {
        Categories = _categoryBases,
        Cards = _cards,
        Combos = _combos,
        ComboGroups = _comboGroups,
        HandSize = _handSize,
        DrawEffectsEnabled = _drawEffectsEnabled,
        DrawActivationPriority = _drawActivationPriority,
        CategoryColorIndices = new(_categoryColorIndices, StringComparer.Ordinal)
    };

    private sealed record SessionLoadRequest(long Version, long EditVersion, string Before);

    private SessionLoadRequest? _ydkeImportRequest;

    private SessionLoadRequest BeginSessionLoad(bool fullSession = false) {
        _isClearWorkspaceConfirmationOpen = false;
        InvalidateSessionLoad();
        _loadingSessionVersion = fullSession ? _sessionLoadVersion : null;

        return new(
            _sessionLoadVersion,
            _workspaceEditVersion,
            _sessionService.SerializeSession(CaptureSession())
        );
    }

    private bool OwnsSessionLoad(long version) => !_disposed && version == _sessionLoadVersion;

    private bool OwnsSessionLoad(SessionLoadRequest request) => OwnsSessionLoad(request.Version);

    private bool CanApplySession(SessionLoadRequest request) {
        if (!OwnsSessionLoad(request)) {
            return false;
        }

        if (request.EditVersion != _workspaceEditVersion) {
            return false;
        }

        string currentSession = _sessionService.SerializeSession(CaptureSession());

        return request.Before == currentSession;
    }

    private Task<bool> ApplyRecoveryAsync(SessionState session) => RestoreRecoveryAsync(() => Task.FromResult(session));

    private Task<bool> ApplyRecoveryPayloadAsync(string json) => RestoreRecoveryAsync(() => _sessionService.LoadSessionAsync(json));

    private async Task<bool> RestoreRecoveryAsync(Func<Task<SessionState>> load) {
        if (_disposed) {
            return false;
        }

        SessionLoadRequest request = BeginSessionLoad(fullSession: true);

        try {
            // This callback belongs to the recovery child, so render its owner here.
            StateHasChanged();
            await BeforeSessionWorkAsync();
            SessionState session = await load();

            return await RestoreSessionDataAsync(session, request);
        }
        finally {
            EndSessionLoad(request);
            if (!_disposed) {
                StateHasChanged();
            }
        }
    }

    private async Task<List<Card>> PrepareSessionCardsAsync(SessionState session) {
        if (session.Cards.Select(card => card.Id).Distinct(StringComparer.Ordinal).Count() != session.Cards.Count) {
            throw new InvalidOperationException("Session contains duplicate card IDs.");
        }

        // Resolve the same-origin bundle before applying saved objective memberships.
        List<Card> preparedCards = session.Cards.ToList();
        IReadOnlyList<CardInfo> catalog = [];
        try {
            if (preparedCards.Any(card => card.ExternalCardId is not null || !string.IsNullOrWhiteSpace(card.Name))) {
                catalog = await _cardInfoService.GetCatalogCardsAsync() ?? [];
                CatalogArchetypeReconciler.Reconcile(preparedCards, catalog);
            }
        }
        catch {
            // An unavailable bundled catalog is not evidence about an unknown card.
        }

        // Check the supplied snapshot, just as the legacy enricher does. Catalog
        // reconciliation still repairs automatic archetypes on the fast path.
        if (!session.Cards.Any(LegacyCardMetadataEnricher.NeedsNameEnrichment)) {
            return preparedCards;
        }

        // Optional legacy network enrichment is bounded and receives its own model,
        // so a late completion cannot mutate the already reconciled workspace.
        SessionState enriched = await _sessionService.LoadSessionAsync(_sessionService.SerializeSession(session));
        Card[] originalEnrichmentCards = [.. enriched.Cards];

        try {
            await _legacyMetadataEnricher.EnrichAsync(enriched).WaitAsync(TimeSpan.FromSeconds(2));
            CatalogArchetypeReconciler.Reconcile(enriched.Cards, catalog);

            for (int i = 0; i < enriched.Cards.Count; i++) {
                if (!ReferenceEquals(enriched.Cards[i], originalEnrichmentCards[i])) {
                    preparedCards[i] = enriched.Cards[i];
                }
            }
        }
        catch {
        }

        return preparedCards;
    }

    private async Task<bool> RestoreSessionDataAsync(SessionState session, SessionLoadRequest request) {
        if (!OwnsSessionLoad(request)) {
            return false;
        }

        List<Card> preparedCards = await PrepareSessionCardsAsync(session);

        if (!CanApplySession(request)) {
            return false;
        }

        ConsumeSharedFragment();
        _isClearWorkspaceConfirmationOpen = false;
        InvalidateCalculation(clearPreviousResult: true);
        _categoryBases.Clear();
        _categoryBases.AddRange(session.Categories.Where(category => category.Source == CategorySource.User));

        _cards.Clear();
        _cards.AddRange(preparedCards);

        _combos.Clear();
        _combos.AddRange(session.Combos);
        _comboGroups.Clear();
        _comboGroups.AddRange(session.ComboGroups ?? []);

        RestoreCategoryColorIndices(session);
        _handSize = session.HandSize;
        _drawActivationPriority = (session.DrawActivationPriority ?? DrawActivationPriority.Default).ForWorkspace(_cards);
        _drawEffectsEnabled = session.DrawEffectsEnabled;
        _conditionalEffectsEnabled = _drawEffectsEnabled;

        _activeCardIndex = _activeComboIndex = -1;
        _sessionVersion++;
        _autosaveReplacementVersion++;

        return true;
    }

    public async ValueTask DisposeAsync() {
        if (_disposed) {
            return;
        }

        SharingNavigation.LocationChanged -= SharingLocationChanged;
        InvalidateSessionLoad();
        _disposed = true;
        StopCalculation();
        await _backgroundCalculator.DisposeAsync();
    }

    private async Task ImportDeckAsync(InputFileChangeEventArgs e) {
        SessionLoadRequest request = BeginSessionLoad();

        try {
            _importError = null;
            List<Card> importedCards = await _deckImportService.ImportDeckFromYdkAsync(e.File);

            if (!OwnsSessionLoad(request)) {
                return;
            }

            if (!CanApplySession(request)) {
                _importError = "Current work changed during import. Import again to replace it.";
                return;
            }

            ReplaceImportedDeck(importedCards);
        }
        catch (Exception ex) {
            if (OwnsSessionLoad(request)) {
                _importError = $"Failed to import deck: {ex.Message}";
            }
        }
    }

    private void OpenYdkeImport() {
        _importError = null;
        _ydkeCode = string.Empty;
        _isYdkeImportOpen = true;
    }

    private void CancelYdkeImport() {
        // Revoke this form's request without invalidating a newer file/recovery load.
        if (_ydkeImportRequest is { } request && OwnsSessionLoad(request)) {
            InvalidateSessionLoad();
        }

        _ydkeImportRequest = null;
        _importError = null;
        _ydkeCode = string.Empty;
        _isYdkeImportOpen = false;
    }

    private async Task ImportYdkeAsync() {
        SessionLoadRequest request = BeginSessionLoad();

        try {
            _ydkeImportRequest = request;
            _importError = null;
            List<Card> importedCards = await _deckImportService.ImportDeckFromYdkeAsync(_ydkeCode);

            if (!OwnsSessionLoad(request)) {
                return;
            }

            if (!CanApplySession(request)) {
                _importError = "Current work changed during import. Import again to replace it.";
                return;
            }

            ReplaceImportedDeck(importedCards);
            _ydkeCode = string.Empty;
            _isYdkeImportOpen = false;
        }
        catch (Exception ex) {
            if (OwnsSessionLoad(request)) {
                _importError = $"Failed to import deck: {ex.Message}";
            }
        }
        finally {
            if (ReferenceEquals(_ydkeImportRequest, request)) {
                _ydkeImportRequest = null;
            }
        }
    }

    private void ReplaceImportedDeck(List<Card> importedCards) {
        ConsumeSharedFragment();
        _isClearWorkspaceConfirmationOpen = false;
        _drawActivationPriority = DrawActivationPriority.Default;
        _cards.Clear();
        _cards.AddRange(importedCards);
        InvalidateCalculation(clearPreviousResult: true);
        _activeCardIndex = -1;
        _autosaveReplacementVersion++;
    }

    private async Task SaveSession(string fileName) {
        try {
            await _sessionService.SaveSessionAsync(CaptureSession(), fileName);
        }
        catch (Exception ex) {
            _errorMessage = $"Failed to save session: {ex.Message}";
        }
    }

    private async Task LoadSessionFile(InputFileChangeEventArgs e) {
        SessionLoadRequest request = BeginSessionLoad(fullSession: true);

        try {
            await BeforeSessionWorkAsync();
            IBrowserFile file = e.File;
            using StreamReader streamReader = new(file.OpenReadStream());
            string fileContent = await streamReader.ReadToEndAsync();

            SessionState session = await _sessionService.LoadSessionAsync(fileContent);

            if (!await RestoreSessionDataAsync(session, request)) {
                if (OwnsSessionLoad(request)) {
                    _errorMessage = "Current work changed while loading. Load the session again to replace it.";
                }
            }
        }
        catch (Exception ex) {
            if (OwnsSessionLoad(request)) {
                _errorMessage = $"Failed to load session: {ex.Message}";
            }
        }
        finally {
            EndSessionLoad(request);
        }
    }

    private async Task SaveCurrentSession() {
        string fileName = $"calculator_session_{DateTime.Now:yyyyMMdd_HHmmss}.json";
        await SaveSession(fileName);
    }

    protected override async Task OnInitializedAsync() {
        InitializeSharing();

        if (_sharedFragment is not null) {
            _pendingSessionService.PendingSession = null;
        }

        if (_sharedFragment is null && _pendingSessionService.PendingSession is { } session) {
            SessionLoadRequest request = BeginSessionLoad(fullSession: true);

            try {
                _pendingSessionService.PendingSession = null;
                await BeforeSessionWorkAsync();

                if (!await RestoreSessionDataAsync(session, request) && OwnsSessionLoad(request)) {
                    _errorMessage = "Current work changed while loading the example. Load the example again to replace it.";
                }
            }
            catch (Exception ex) {
                if (OwnsSessionLoad(request)) {
                    _errorMessage = $"Failed to load example session: {ex.Message}";
                }
            }
            finally {
                EndSessionLoad(request);
            }
        }

        if (_disposed) {
            return;
        }

        _sessionInitialized = true;
        await base.OnInitializedAsync();
    }
}
