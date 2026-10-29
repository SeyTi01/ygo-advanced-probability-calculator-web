using YGOProbabilityCalculatorBlazor.Localization;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Services.Session;

// Prepares a separate card list; callers retain ownership of accepting or discarding it.
internal static class SessionCardPreparation {
    public static async Task<List<Card>> PrepareAsync(
        SessionState session,
        ICardInfoService cardInfoService,
        ISessionService sessionService,
        ILegacyCardMetadataEnricher metadataEnricher
    ) {
        if (session.Cards.Select(card => card.Id).Distinct(StringComparer.Ordinal).Count() != session.Cards.Count) {
            throw new InvalidOperationException(UiText.Get("Ui.SessionContainsDuplicateCardIDs"));
        }

        // Resolve the same-origin bundle before applying saved objective memberships.
        List<Card> preparedCards = session.Cards.ToList();
        IReadOnlyList<CardInfo> catalog = [];
        try {
            if (preparedCards.Any(card => card.ExternalCardId is not null || !string.IsNullOrWhiteSpace(card.Name))) {
                catalog = await cardInfoService.GetCatalogCardsAsync() ?? [];
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
        SessionState enriched = await sessionService.LoadSessionAsync(sessionService.SerializeSession(session));
        Card[] originalEnrichmentCards = [.. enriched.Cards];

        try {
            await metadataEnricher.EnrichAsync(enriched).WaitAsync(TimeSpan.FromSeconds(2));
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
}
