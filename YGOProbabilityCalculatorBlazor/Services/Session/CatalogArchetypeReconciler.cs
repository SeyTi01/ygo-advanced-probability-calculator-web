using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;

namespace YGOProbabilityCalculatorBlazor.Services.Session;

/// <summary>Repairs only source-owned archetypes using verified bundled identities.</summary>
public static class CatalogArchetypeReconciler {
    public static void Reconcile(List<Card> cards, IReadOnlyList<CardInfo> catalog) {
        if (cards.Count == 0 || catalog.Count == 0) {
            return;
        }

        // Only index evidence this session can use. Canonical IDs take precedence,
        // so ordinary identified sessions need neither artwork nor name indexes.
        HashSet<int> externalIds = cards.Where(card => card.ExternalCardId is not null)
            .Select(card => card.ExternalCardId!.Value).ToHashSet();
        HashSet<string> exactNames = cards.Where(card => card.ExternalCardId is null && card.Name is not null)
            .Select(card => card.Name!).ToHashSet(StringComparer.Ordinal);
        Dictionary<int, CardInfo> byId = catalog.Where(info => externalIds.Contains(info.Id)).ToDictionary(info => info.Id);
        externalIds.ExceptWith(byId.Keys);
        Dictionary<int, CardInfo[]> byArtwork = externalIds.Count == 0 ? [] : catalog
            .SelectMany(info => info.ArtworkImageIds.Distinct().Where(externalIds.Contains).Select(id => (Id: id, Info: info)))
            .GroupBy(entry => entry.Id)
            .ToDictionary(group => group.Key, group => group.Select(entry => entry.Info).ToArray());
        Dictionary<string, CardInfo[]> byName = exactNames.Count == 0 ? new(StringComparer.Ordinal) : catalog
            .Where(info => exactNames.Contains(info.Name))
            .GroupBy(info => info.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        for (int index = 0; index < cards.Count; index++) {
            Card card = cards[index];
            CardInfo? info = null;
            if (card.ExternalCardId is int externalId) {
                if (!byId.TryGetValue(externalId, out info)
                    && byArtwork.TryGetValue(externalId, out CardInfo[] artwork) && artwork.Length == 1) {
                    info = artwork[0];
                }
            }
            else if (card.Name is not null && byName.TryGetValue(card.Name, out CardInfo[] names) && names.Length == 1) {
                info = names[0];
            }

            if (info?.ResolvedArchetypes is null) {
                continue;
            }

            List<CategoryBase> effective = card.Categories
                .Where(category => !IsArchetype(category) || card.ManualMetadataCategoryKeys.Contains(category.MetadataKey!))
                .Concat(CardPropertyProvider.GetCategories(info).Where(IsArchetype))
                .DistinctBy(category => category.Identity)
                .ToList();
            if (card.Categories.Select(category => category.Identity).ToHashSet(StringComparer.Ordinal)
                .SetEquals(effective.Select(category => category.Identity))) {
                continue;
            }

            // WithCategories retains stored manual keys, identity, and the exact effect snapshot.
            // Older automatic memberships have no recoverable manual provenance to invent.
            cards[index] = card.WithCategories(effective);
        }
    }

    private static bool IsArchetype(CategoryBase category) => category.Source == CategorySource.Metadata
        && category.MetadataKey!.StartsWith("archetype:", StringComparison.Ordinal);
}
