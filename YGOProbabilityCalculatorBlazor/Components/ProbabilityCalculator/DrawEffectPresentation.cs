using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

public readonly record struct DrawEffectPresentation(string? HeaderText, string? InactiveMessage) {
    public static DrawEffectPresentation ForCard(Card card, bool drawEffectsEnabled, bool conditionalEffectsEnabled) {
        if (card.DrawCount is not { } drawCount) {
            return default;
        }

        if (!drawEffectsEnabled) {
            return new(
                "Draw configured · Off",
                "Draw simulation is off — this configured draw effect is currently ignored.");
        }

        string headerText = $"Draw {drawCount}";
        if (conditionalEffectsEnabled && card.PreDrawDeckRemovalCount > 0) {
            headerText = $"Banish {card.PreDrawDeckRemovalCount} · {headerText}";
        }
        if (conditionalEffectsEnabled && card.PreDrawRemovalCount > 0) {
            headerText = $"Cost {card.PreDrawRemovalCount} · {headerText}";
        }
        if (conditionalEffectsEnabled && card.PostDrawDiscardCount > 0) {
            headerText += $" · Remove {card.PostDrawDiscardCount}";
        }

        string? inactiveMessage = null;
        if (!conditionalEffectsEnabled && HasAdvancedConfiguration(card)) {
            inactiveMessage = card.PreDrawRemovalCount > 0 || card.PreDrawDeckRemovalCount > 0
                ? "Simplified draws ignore both pre-draw costs and post-draw removals; this card may draw unconditionally."
                : "Simplified draws ignore both pre-draw costs and post-draw removals.";
        }

        return new(headerText, inactiveMessage);
    }

    public static int CountActiveDrawConfiguredCards(IEnumerable<Card> cards) =>
        cards.Count(card => card.Active && card.DrawCount is not null);

    public static int CountActiveAdvancedConfiguredCards(IEnumerable<Card> cards) =>
        cards.Count(card => card.Active && HasAdvancedConfiguration(card));

    public static string? SettingsNote(
        bool drawEffectsEnabled,
        bool conditionalEffectsEnabled,
        int activeDrawConfiguredCardCount,
        int activeAdvancedConfiguredCardCount
    ) {
        if (!drawEffectsEnabled) {
            return activeDrawConfiguredCardCount == 0
                ? null
                : $"Draw simulation is off; configured draw effects on {CardCount(activeDrawConfiguredCardCount)} are currently ignored.";
        }

        if (conditionalEffectsEnabled || activeAdvancedConfiguredCardCount == 0) {
            return null;
        }

        return $"Simplified draws ignore both pre-draw costs and post-draw removals configured on {CardCount(activeAdvancedConfiguredCardCount)}.";
    }

    private static bool HasAdvancedConfiguration(Card card) =>
        card.PreDrawDeckRemovalCount > 0 ||
        card.PreDrawRemovalCount > 0 ||
        card.PostDrawDiscardCount > 0 ||
        card.PostDrawSelector.Kind != CardSelectorKind.Any ||
        card.PostDrawFailureBehavior != PostDrawFailureBehavior.FailBranch;

    private static string CardCount(int count) => count == 1 ? "1 active card" : $"{count} active cards";
}
