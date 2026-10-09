using Microsoft.AspNetCore.Components.Forms;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

// Every workspace replacement checks the same request, draft, and accepted-model boundary.
public partial class ProbabilityCalculatorComponent : IDisposable {
    private int _autosaveReplacementVersion;
    private long _sessionLoadVersion;
    private bool _sessionInitialized;
    private long _workspaceEditVersion;

    private void ObserveWorkspaceEdit() => _workspaceEditVersion++;

    private SessionState CaptureSession() => new() {
        Categories = _categoryBases,
        Cards = _cards,
        Combos = _combos,
        ComboGroups = _comboGroups,
        HandSize = _handSize,
        CategoryColorIndices = new(_categoryColorIndices, StringComparer.Ordinal)
    };

    private sealed record SessionLoadRequest(long Version, long EditVersion, string Before);

    private SessionLoadRequest? _ydkeImportRequest;

    private SessionLoadRequest BeginSessionLoad() => new(
        ++_sessionLoadVersion,
        _workspaceEditVersion,
        _sessionService.SerializeSession(CaptureSession())
    );

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

    private async Task<bool> ApplyRecoveryAsync(SessionState session) {
        if (_disposed) {
            return false;
        }

        SessionLoadRequest request = BeginSessionLoad();

        if (!await RestoreSessionDataAsync(session, request)) {
            return false;
        }

        StateHasChanged();

        return true;
    }

    private async Task<List<Card>> PrepareSessionCardsAsync(SessionState session) {
        if (session.Cards.Select(card => card.Id).Distinct(StringComparer.Ordinal).Count() != session.Cards.Count) {
            throw new InvalidOperationException("Session contains duplicate card IDs.");
        }

        // Metadata is best effort; a hung network must not gate recovery/startup.
        // Enrichment receives its own model so a late completion cannot mutate applied work.
        List<Card> preparedCards = session.Cards.ToList();
        SessionState enriched = await _sessionService.LoadSessionAsync(_sessionService.SerializeSession(session));
        Card[] originalEnrichmentCards = [.. enriched.Cards];

        try {
            await _legacyMetadataEnricher.EnrichAsync(enriched).WaitAsync(TimeSpan.FromSeconds(2));

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
        _activeCardIndex = _activeComboIndex = -1;
        _sessionVersion++;
        _autosaveReplacementVersion++;

        return true;
    }

    public void Dispose() {
        if (_disposed) {
            return;
        }

        SharingNavigation.LocationChanged -= SharingLocationChanged;
        _sessionLoadVersion++;
        _disposed = true;
        StopCalculation();
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
            _sessionLoadVersion++;
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
        SessionLoadRequest request = BeginSessionLoad();

        try {
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
            SessionLoadRequest request = BeginSessionLoad();

            try {
                _pendingSessionService.PendingSession = null;

                if (!await RestoreSessionDataAsync(session, request) && OwnsSessionLoad(request)) {
                    _errorMessage = "Current work changed while loading the example. Load the example again to replace it.";
                }
            }
            catch (Exception ex) {
                if (OwnsSessionLoad(request)) {
                    _errorMessage = $"Failed to load example session: {ex.Message}";
                }
            }
        }

        if (_disposed) {
            return;
        }

        _sessionInitialized = true;
        await base.OnInitializedAsync();
    }
}
