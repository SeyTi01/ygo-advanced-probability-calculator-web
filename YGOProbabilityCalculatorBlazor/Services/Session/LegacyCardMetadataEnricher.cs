using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Services.Session;

public class LegacyCardMetadataEnricher(ICardInfoService cardInfoService) : ILegacyCardMetadataEnricher {
    public async Task EnrichAsync(SessionState session) {
        Card[] candidates = session.Cards.Where(card => {
            if (card.ExternalCardId is not null) {
                return false;
            }

            bool hasImportedMetadataCategory = card.Categories.Any(category =>
                category.Source == CategorySource.Metadata &&
                !card.ManualMetadataCategoryKeys.Contains(category.MetadataKey!));

            return !hasImportedMetadataCategory && !string.IsNullOrWhiteSpace(card.Name);
        }).ToArray();

        if (candidates.Length == 0) {
            return;
        }

        IReadOnlyDictionary<string, CardInfo> resolved;

        try {
            resolved = await cardInfoService.GetCardInfoByExactNamesAsync(candidates.Select(static card => card.Name!).Distinct(StringComparer.Ordinal));
        }
        catch {
            // Enrichment is opportunistic, independently of offline schema migration.
            return;
        }

        for (int index = 0; index < session.Cards.Count; index++) {
            Card card = session.Cards[index];

            if (!candidates.Contains(card)) {
                continue;
            }

            if (!resolved.TryGetValue(card.Name!, out CardInfo info) ||
                info.Id <= 0 || !string.Equals(info.Name, card.Name, StringComparison.Ordinal)) {

                continue;
            }

            IReadOnlyList<CategoryBase> properties = CardPropertyProvider.GetCategories(info);

            if (properties.Count == 0) {
                continue;
            }

            session.Cards[index] = card.WithObjectiveMetadata(properties, info.Id);
        }
    }
}
