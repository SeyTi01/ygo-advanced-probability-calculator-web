using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Services.Session;

public class LegacyCardMetadataEnricher(ICardInfoService cardInfoService) : ILegacyCardMetadataEnricher {
    public async Task EnrichAsync(SessionState session) {
        var candidates = session.Cards.Where(card => card.ExternalCardId is null &&
            !card.Categories.Any(category => category.Source == CategorySource.Metadata) &&
            !string.IsNullOrWhiteSpace(card.Name)).ToArray();
        if (candidates.Length == 0) return;

        IReadOnlyDictionary<string, CardInfo> resolved;
        try {
            resolved = await cardInfoService.GetCardInfoByExactNamesAsync(
                candidates.Select(card => card.Name!).Distinct(StringComparer.Ordinal));
        }
        catch {
            // Enrichment is opportunistic, independently of offline schema migration.
            return;
        }

        for (var i = 0; i < session.Cards.Count; i++) {
            var card = session.Cards[i];
            if (!candidates.Contains(card) || !resolved.TryGetValue(card.Name!, out var info) ||
                info.Id <= 0 || !string.Equals(info.Name, card.Name, StringComparison.Ordinal)) continue;
            var properties = CardPropertyProvider.GetCategories(info);
            if (properties.Count == 0) continue;
            var categories = card.Categories.ToList();
            var identities = categories.Select(category => category.Identity).ToHashSet(StringComparer.Ordinal);
            categories.AddRange(properties.Where(property => identities.Add(property.Identity)));
            session.Cards[i] = new Card(categories, card.Copies, card.Name, card.Active, card.Id, info.Id);
        }
    }
}
