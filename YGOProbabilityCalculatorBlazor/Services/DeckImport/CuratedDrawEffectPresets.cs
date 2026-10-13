using System.Collections.Frozen;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.DeckImport;

public sealed record CuratedDrawEffectPreset(
    int CardId,
    string Name,
    int DrawCount,
    bool DrawOncePerTurn,
    int PostDrawDiscardCount = 0,
    CardSelector? PostDrawSelector = null,
    int PreDrawRemovalCount = 0,
    CardSelector? PreDrawSelector = null,
    PostDrawFailureBehavior? FailureBehavior = null,
    int PreDrawDeckRemovalCount = 0
);

public static class CuratedDrawEffectPresets {
    public static IReadOnlyList<CategoryBase> FilterProperties { get; } = Array.AsReadOnly(new[] {
        CardPropertyProvider.GetCategories(new() { Type = "Effect Monster", Attribute = "DARK" })
            .Single(category => category.MetadataKey!.StartsWith("attribute:", StringComparison.Ordinal)),
        CardPropertyProvider.GetCategories(new() { Type = "Spell Card", Race = "Quick-Play" })
            .Single(category => category.MetadataKey!.StartsWith("spell-type:", StringComparison.Ordinal)),
        CardPropertyProvider.GetCategories(new() { Archetype = "Radiant Typhoon" }).Single(),
        new("Monster", CategorySource.Metadata, "kind:monster"),
        new("Level 8", CategorySource.Metadata, "level:8"),
        new("Archetype: Orcust", CategorySource.Metadata, "archetype:orcust"),
        new("Archetype: World Legacy", CategorySource.Metadata, "archetype:world%20legacy")
    });

    private static readonly CuratedDrawEffectPreset[] Presets = [
        new(38120068, "Trade-In", 2, false, PreDrawRemovalCount: 1,
            PreDrawSelector: CardSelector.For(FilterProperties[4])),
        // Only the hand cost is supported; there is no field-state simulation.
        new(26845680, "Orcustrated Return", 2, true, PreDrawRemovalCount: 1,
            PreDrawSelector: CardSelector.AnyOfSelectors([
                CardSelector.AllOf([FilterProperties[3].Identity, FilterProperties[5].Identity]),
                CardSelector.AllOf([FilterProperties[3].Identity, FilterProperties[6].Identity])])),
        new(55144522, "Pot of Greed", 2, false),
        new(20508881, "Radiant Typhoon Vision", 2, true, 1, CardSelector.AnyOf(FilterProperties.Skip(1).Take(2).Select(category => category.Identity))),
        new(35261759, "Pot of Desires", 2, true, PreDrawDeckRemovalCount: 10),
        new(70368879, "Upstart Goblin", 1, false),
        new(1475311, "Allure of Darkness", 2, false, 1, CardSelector.For(FilterProperties[0])),
        new(49238328, "Pot of Extravagance", 2, true),
        new(93946239, "Into the Void", 1, false),
        new(67616300, "Chicken Game", 1, false),
        new(74117290, "Dark World Dealings", 1, false, 1),
        new(56673480, "Contract with Don Thousand", 1, true),
        new(33782437, "One Day of Peace", 1, false),
        new(79571449, "Graceful Charity", 3, false, 2),
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
        if (card.PreDrawDeckRemovalCount != 0 || card.PreDrawRemovalCount != 0 || card.PreDrawSelector.Kind != CardSelectorKind.Any
            || card.DrawCount is not null || !card.DrawOncePerTurn || card.PostDrawDiscardCount != 0
            || card.PostDrawSelector.Kind != CardSelectorKind.Any
            || card.PostDrawFailureBehavior != PostDrawFailureBehavior.FailBranch) {
            return card;
        }

        return ApplyPreset(card, cardInfo);
    }

    public static Card ApplyForExplicitSelection(Card replacement, Card previousCard, CardInfo selectedCard) {
        if (HasSameCanonicalIdentity(previousCard, selectedCard)) {
            return replacement;
        }

        Card reset = replacement
            .WithDrawCount(null)
            .WithDrawOncePerTurn(true)
            .WithPostDrawFailureBehavior(PostDrawFailureBehavior.FailBranch);

        return ApplyPreset(reset, selectedCard);
    }

    private static bool HasSameCanonicalIdentity(Card card, CardInfo cardInfo) {
        if (card.ExternalCardId is not int externalCardId) {
            return false;
        }

        int canonicalCardId = cardInfo.CanonicalCardId is > 0
            ? cardInfo.CanonicalCardId.Value
            : cardInfo.Id;

        return externalCardId == cardInfo.Id
            || externalCardId == canonicalCardId
            || cardInfo.ArtworkImageIds.Contains(externalCardId);
    }

    private static Card ApplyPreset(Card card, CardInfo cardInfo) {
        CuratedDrawEffectPreset? preset = Find(cardInfo);

        if (preset is null) {
            return card;
        }

        return card
            .WithDrawCount(preset.DrawCount)
            .WithDrawOncePerTurn(preset.DrawOncePerTurn)
            .WithPostDrawDiscardCount(preset.PostDrawDiscardCount)
            .WithPostDrawSelector(preset.PostDrawSelector ?? CardSelector.Any)
            .WithPreDrawDeckRemovalCount(preset.PreDrawDeckRemovalCount)
            .WithPreDrawRemovalCount(preset.PreDrawRemovalCount)
            .WithPreDrawSelector(preset.PreDrawSelector ?? CardSelector.Any)
            .WithPostDrawFailureBehavior(preset.FailureBehavior ?? PostDrawFailureBehavior.FailBranch);
    }
}
