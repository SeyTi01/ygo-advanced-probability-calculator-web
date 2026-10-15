using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;

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
    int PreDrawDeckRemovalCount = 0,
    int PostDrawDeckMillCount = 0,
    bool FirstActivationOnly = false,
    bool BlocksLaterDraws = false
);

// Compatibility view for existing callers. Definitions live only in the registry.
public static class CuratedDrawEffectPresets {
    public static IReadOnlyList<CategoryBase> FilterProperties => CardEffectRegistry.Default.FilterProperties;
    public static IReadOnlyList<CuratedDrawEffectPreset> All { get; } = Array.AsReadOnly(
        CardEffectRegistry.Default.All.Where(effect => effect.Plan.DrawCount is not null).Select(ToPreset).ToArray());

    public static CuratedDrawEffectPreset? Find(CardInfo info) => CardEffectRegistry.Default.Find(info) is { Plan.DrawCount: not null } effect
        ? ToPreset(effect) : null;

    public static Card CreateImportedCard(CardInfo info, int copies, int importedId) =>
        CardEffectRegistry.Default.CreateImportedCard(info, copies, importedId);

    public static Card ApplyForExplicitSelection(Card replacement, Card previous, CardInfo selected) =>
        CardEffectRegistry.Default.ApplyForExplicitSelection(replacement, previous, selected);

    public static Card ApplyToSelectedCardIfPristine(Card card, CardInfo info) {
        if (card.EffectProfile.Source != EffectSource.LegacySnapshot || card.PreDrawDeckRemovalCount != 0 || card.PostDrawDeckMillCount != 0
            || card.PreDrawRemovalCount != 0 || card.PreDrawSelector.Kind != CardSelectorKind.Any
            || card.HasEffect || !card.DrawOncePerTurn || card.PostDrawDiscardCount != 0
            || card.PostDrawSelector.Kind != CardSelectorKind.Any
            || card.PostDrawFailureBehavior != PostDrawFailureBehavior.FailBranch) {
            return card;
        }

        return CardEffectRegistry.Default.ApplyCurrent(card, info);
    }

    private static CuratedDrawEffectPreset ToPreset(MaintainedCardEffect effect) => new(
        effect.CanonicalCardId, effect.Name, effect.Plan.DrawCount!.Value, effect.Plan.OncePerTurn,
        effect.Plan.RemovalCount, effect.Plan.RemovalSelector, effect.Plan.HandCost,
        effect.Plan.HandCostSelector, effect.Plan.FailureBehavior, effect.Plan.HiddenDeckCost, effect.Plan.HiddenPostDrawMill, effect.Plan.FirstActivationOnly, effect.Plan.BlocksLaterDraws);
}
