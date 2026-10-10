using System.Collections.Frozen;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.DeckImport;

public sealed record CuratedDrawEffectPreset(int CardId, string Name, int DrawCount, bool DrawOncePerTurn);

public static class CuratedDrawEffectPresets {
    private static readonly CuratedDrawEffectPreset[] Presets = [
        new(55144522, "Pot of Greed", 2, false),
        new(20508881, "Radiant Typhoon Vision", 2, true),
        new(35261759, "Pot of Desires", 2, true),
        new(70368879, "Upstart Goblin", 1, false),
        new(1475311, "Allure of Darkness", 2, false),
        new(49238328, "Pot of Extravagance", 2, true),
        new(93946239, "Into the Void", 1, false),
        new(67616300, "Chicken Game", 1, false),
        new(74117290, "Dark World Dealings", 1, false),
        new(56673480, "Contract with Don Thousand", 1, true),
        new(33782437, "One Day of Peace", 1, false),
        new(79571449, "Graceful Charity", 3, false),
        new(24940422, "Sekka's Light", 2, true),
        new(7925734, "Link Apple", 1, true)
    ];

    private static readonly IReadOnlyList<CuratedDrawEffectPreset> PresetList = Array.AsReadOnly(Presets);
    private static readonly FrozenDictionary<int, CuratedDrawEffectPreset> PresetsByCardId =
        Presets.ToFrozenDictionary(preset => preset.CardId);

    public static IReadOnlyList<CuratedDrawEffectPreset> All => PresetList;

    public static CuratedDrawEffectPreset? Find(CardInfo cardInfo) {
        int cardId = cardInfo.CanonicalCardId is int canonicalCardId && canonicalCardId > 0
            ? canonicalCardId
            : cardInfo.Id;

        return PresetsByCardId.GetValueOrDefault(cardId);
    }

    public static Card CreateImportedCard(CardInfo cardInfo, int copies, int importedCardId) {
        Card card = new(
            CardPropertyProvider.GetCategories(cardInfo),
            copies,
            cardInfo.Name,
            externalCardId: importedCardId);

        return ApplyPreset(card, cardInfo);
    }

    public static Card ApplyToSelectedCardIfPristine(Card card, CardInfo cardInfo) {
        if (card.DrawCount is not null || !card.DrawOncePerTurn) {
            return card;
        }

        return ApplyPreset(card, cardInfo);
    }

    private static Card ApplyPreset(Card card, CardInfo cardInfo) {
        CuratedDrawEffectPreset? preset = Find(cardInfo);

        if (preset is null) {
            return card;
        }

        return card
            .WithDrawCount(preset.DrawCount)
            .WithDrawOncePerTurn(preset.DrawOncePerTurn);
    }
}
