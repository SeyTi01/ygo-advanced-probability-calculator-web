using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

// Feature-owned recovery/load ordering; compose Dispose with other lifecycle features.
public partial class ProbabilityCalculatorComponent : IDisposable {
    private int autosaveReplacementVersion;
    private long sessionLoadVersion;
    private bool sessionDisposed;
    private bool sessionInitialized;
    private long workspaceEditVersion;
    private void ObserveWorkspaceEdit() => workspaceEditVersion++;
    private SessionState CaptureSession() => new() {
        Categories = categoryBases,
        Cards = cards,
        Combos = combos,
        ComboGroups = comboGroups,
        HandSize = handSize,
        CategoryColorIndices = new(categoryColorIndices, StringComparer.Ordinal)
    };

    private bool CanApplySession(long request, string before, long edit) => !sessionDisposed && request == sessionLoadVersion && edit == workspaceEditVersion &&
        before == _sessionService.SerializeSession(CaptureSession());

    private async Task<bool> ApplyRecoveryAsync(SessionState session) {
        var request = ++sessionLoadVersion;
        var edit = workspaceEditVersion;
        var before = _sessionService.SerializeSession(CaptureSession());
        if (!await RestoreSessionDataAsync(session, request, before, edit)) return false;
        activeCardIndex = activeComboIndex = -1;
        InvalidateCalculation(clearPreviousResult: true);
        sessionVersion++;
        autosaveReplacementVersion++;
        StateHasChanged();
        return true;
    }

    private async Task<bool> RestoreSessionDataAsync(SessionState session, long request, string before, long edit) {
        if (session.Cards.Select(card => card.Id).Distinct(StringComparer.Ordinal).Count() != session.Cards.Count)
            throw new InvalidOperationException("Session contains duplicate card IDs.");
        // Metadata is best effort; a hung network must not gate recovery/startup.
        // Enrichment receives its own model so a late completion cannot mutate applied work.
        var enriched = await _sessionService.LoadSessionAsync(_sessionService.SerializeSession(session));
        var originalEnrichmentCards = enriched.Cards.ToArray();
        try {
            await _legacyMetadataEnricher.EnrichAsync(enriched).WaitAsync(TimeSpan.FromSeconds(2));
            for (var i = 0; i < enriched.Cards.Count; i++) {
                if (!ReferenceEquals(enriched.Cards[i], originalEnrichmentCards[i])) session.Cards[i] = enriched.Cards[i];
            }
        }
        catch { }
        if (!CanApplySession(request, before, edit)) return false;
        categoryBases.Clear();
        categoryBases.AddRange(session.Categories.Where(category => category.Source == CategorySource.User));

        cards.Clear();
        cards.AddRange(session.Cards);

        combos.Clear();
        combos.AddRange(session.Combos);
        comboGroups.Clear();
        comboGroups.AddRange(session.ComboGroups ?? []);

        RestoreCategoryColorIndices(session);
        handSize = session.HandSize;
        return true;
    }

    public void Dispose() { sessionDisposed = true; sessionLoadVersion++; }
}
