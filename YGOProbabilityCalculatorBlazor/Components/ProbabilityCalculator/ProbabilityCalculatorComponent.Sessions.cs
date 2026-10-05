using Microsoft.AspNetCore.Components.Forms;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

// Every workspace replacement checks the same request, draft, and accepted-model boundary.
public partial class ProbabilityCalculatorComponent : IDisposable
{
    private int autosaveReplacementVersion;
    private long sessionLoadVersion;
    private bool sessionInitialized;
    private long workspaceEditVersion;

    private void ObserveWorkspaceEdit() => workspaceEditVersion++;

    private SessionState CaptureSession() => new()
    {
        Categories = categoryBases,
        Cards = cards,
        Combos = combos,
        ComboGroups = comboGroups,
        HandSize = handSize,
        CategoryColorIndices = new(categoryColorIndices, StringComparer.Ordinal)
    };

    private sealed record SessionLoadRequest(long Version, long EditVersion, string Before);

    private SessionLoadRequest? ydkeImportRequest;

    private SessionLoadRequest BeginSessionLoad() => new(++sessionLoadVersion,
        workspaceEditVersion,
        _sessionService.SerializeSession(CaptureSession()));

    private bool OwnsSessionLoad(long version) => ! disposed && version == sessionLoadVersion;

    private bool OwnsSessionLoad(SessionLoadRequest request) => OwnsSessionLoad(request.Version);

    private bool CanApplySession(SessionLoadRequest request) =>
        OwnsSessionLoad(request) && request.EditVersion == workspaceEditVersion &&
        request.Before == _sessionService.SerializeSession(CaptureSession());

    private async Task<bool> ApplyRecoveryAsync(SessionState session)
    {
        if (disposed)
        {
            return false;
        }

        SessionLoadRequest request = BeginSessionLoad();

        if (! await RestoreSessionDataAsync(session, request))
        {
            return false;
        }

        StateHasChanged();

        return true;
    }

    private async Task<List<Card>> PrepareSessionCardsAsync(SessionState session)
    {
        if (session.Cards.Select(card => card.Id).Distinct(StringComparer.Ordinal).Count() != session.Cards.Count)
        {
            throw new InvalidOperationException("Session contains duplicate card IDs.");
        }

        // Metadata is best effort; a hung network must not gate recovery/startup.
        // Enrichment receives its own model so a late completion cannot mutate applied work.
        List<Card> preparedCards = [.. session.Cards];
        SessionState enriched = await _sessionService.LoadSessionAsync(_sessionService.SerializeSession(session));
        Card[] originalEnrichmentCards = [.. enriched.Cards];

        try
        {
            await _legacyMetadataEnricher.EnrichAsync(enriched).WaitAsync(TimeSpan.FromSeconds(2));

            for (int i = 0; i < enriched.Cards.Count; i++)
            {
                if (! ReferenceEquals(enriched.Cards[i], originalEnrichmentCards[i]))
                {
                    preparedCards[i] = enriched.Cards[i];
                }
            }
        }
        catch
        {
        }

        return preparedCards;
    }

    private async Task<bool> RestoreSessionDataAsync(SessionState session, SessionLoadRequest request)
    {
        if (! OwnsSessionLoad(request))
        {
            return false;
        }

        List<Card> preparedCards = await PrepareSessionCardsAsync(session);

        if (! CanApplySession(request))
        {
            return false;
        }

        ConsumeSharedFragment();
        InvalidateCalculation(clearPreviousResult: true);
        categoryBases.Clear();
        categoryBases.AddRange(session.Categories.Where(category => category.Source == CategorySource.User));

        cards.Clear();
        cards.AddRange(preparedCards);

        combos.Clear();
        combos.AddRange(session.Combos);
        comboGroups.Clear();
        comboGroups.AddRange(session.ComboGroups ?? []);

        RestoreCategoryColorIndices(session);
        handSize = session.HandSize;
        activeCardIndex = activeComboIndex = -1;
        sessionVersion++;
        autosaveReplacementVersion++;

        return true;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        SharingNavigation.LocationChanged -= SharingLocationChanged;
        sessionLoadVersion++;
        disposed = true;
        StopCalculation();
    }

    private async Task ImportDeckAsync(InputFileChangeEventArgs e)
    {
        SessionLoadRequest request = BeginSessionLoad();

        try
        {
            importError = null;
            List<Card> importedCards = await _deckImportService.ImportDeckFromYdkAsync(e.File);

            if (! OwnsSessionLoad(request))
            {
                return;
            }

            if (! CanApplySession(request))
            {
                importError = "Current work changed during import. Import again to replace it.";

                return;
            }

            ReplaceImportedDeck(importedCards);
        }
        catch (Exception ex)
        {
            if (OwnsSessionLoad(request))
            {
                importError = $"Failed to import deck: {ex.Message}";
            }
        }
    }

    private void OpenYdkeImport()
    {
        importError = null;
        ydkeCode = string.Empty;
        isYdkeImportOpen = true;
    }

    private void CancelYdkeImport()
    {
        // Revoke this form's request without invalidating a newer file/recovery load.
        if (ydkeImportRequest is { } request && OwnsSessionLoad(request))
        {
            sessionLoadVersion++;
        }

        ydkeImportRequest = null;
        importError = null;
        ydkeCode = string.Empty;
        isYdkeImportOpen = false;
    }

    private async Task ImportYdkeAsync()
    {
        SessionLoadRequest request = BeginSessionLoad();

        try
        {
            ydkeImportRequest = request;
            importError = null;
            List<Card> importedCards = await _deckImportService.ImportDeckFromYdkeAsync(ydkeCode);

            if (! OwnsSessionLoad(request))
            {
                return;
            }

            if (! CanApplySession(request))
            {
                importError = "Current work changed during import. Import again to replace it.";

                return;
            }

            ReplaceImportedDeck(importedCards);
            ydkeCode = string.Empty;
            isYdkeImportOpen = false;
        }
        catch (Exception ex)
        {
            if (OwnsSessionLoad(request))
            {
                importError = $"Failed to import deck: {ex.Message}";
            }
        }
        finally
        {
            if (ReferenceEquals(ydkeImportRequest, request))
            {
                ydkeImportRequest = null;
            }
        }
    }

    private void ReplaceImportedDeck(List<Card> importedCards)
    {
        ConsumeSharedFragment();
        cards.Clear();
        cards.AddRange(importedCards);
        InvalidateCalculation(clearPreviousResult: true);
        activeCardIndex = -1;
        autosaveReplacementVersion++;
    }

    private async Task SaveSession(string fileName)
    {
        try
        {
            await _sessionService.SaveSessionAsync(CaptureSession(), fileName);
        }
        catch (Exception ex)
        {
            errorMessage = $"Failed to save session: {ex.Message}";
        }
    }

    private async Task LoadSessionFile(InputFileChangeEventArgs e)
    {
        SessionLoadRequest request = BeginSessionLoad();

        try
        {
            IBrowserFile file = e.File;
            using StreamReader streamReader = new(file.OpenReadStream());
            string fileContent = await streamReader.ReadToEndAsync();

            SessionState session = await _sessionService.LoadSessionAsync(fileContent);

            if (! await RestoreSessionDataAsync(session, request))
            {
                if (OwnsSessionLoad(request))
                {
                    errorMessage = "Current work changed while loading. Load the session again to replace it.";
                }

                return;
            }
        }
        catch (Exception ex)
        {
            if (OwnsSessionLoad(request))
            {
                errorMessage = $"Failed to load session: {ex.Message}";
            }
        }
    }

    private async Task SaveCurrentSession()
    {
        string fileName = $"calculator_session_{DateTime.Now:yyyyMMdd_HHmmss}.json";
        await SaveSession(fileName);
    }

    protected override async Task OnInitializedAsync()
    {
        InitializeSharing();

        if (sharedFragment is not null)
        {
            _pendingSessionService.PendingSession = null;
        }

        if (sharedFragment is null && _pendingSessionService.PendingSession is { } session)
        {
            SessionLoadRequest request = BeginSessionLoad();

            try
            {
                _pendingSessionService.PendingSession = null;

                if (! await RestoreSessionDataAsync(session, request) && OwnsSessionLoad(request))
                {
                    errorMessage =
                        "Current work changed while loading the example. Load the example again to replace it.";
                }
            }
            catch (Exception ex)
            {
                if (OwnsSessionLoad(request))
                {
                    errorMessage = $"Failed to load example session: {ex.Message}";
                }
            }
        }

        if (disposed)
        {
            return;
        }

        sessionInitialized = true;
        await base.OnInitializedAsync();
    }
}
