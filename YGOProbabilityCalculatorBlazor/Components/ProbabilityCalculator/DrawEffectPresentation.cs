using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

public readonly record struct DrawEffectPresentation(string? HeaderText, string? InactiveMessage) {
    public static DrawEffectPresentation ForCard(Card card, bool drawEffectsEnabled, bool conditionalEffectsEnabled) {
        if (card.EffectProfile.ExecutionModel != EffectExecutionModel.DrawSequenceV1) {
            return new("Unsupported effect", null);
        }

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
        if (conditionalEffectsEnabled && card.PostDrawDeckMillCount > 0) {
            headerText += $" · Mill {card.PostDrawDeckMillCount}";
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

    public static string DescribePlan(DrawEffectPlan plan, IReadOnlyList<CategoryBase> categories) {
        if (plan.DrawCount is null) {
            return "No draw effect";
        }

        List<string> steps = [];
        if (plan.HandCost > 0) {
            steps.Add($"Remove {plan.HandCost} from hand ({DescribeSelector(plan.HandCostSelector, categories)})");
        }
        if (plan.HiddenDeckCost > 0) {
            steps.Add($"Banish {plan.HiddenDeckCost} unseen deck cards");
        }
        steps.Add($"Draw {plan.DrawCount}");
        if (plan.HiddenPostDrawMill > 0) {
            steps.Add($"send {plan.HiddenPostDrawMill} unseen top deck cards to the GY");
        }
        if (plan.RemovalCount > 0) {
            steps.Add($"remove {plan.RemovalCount} from hand ({DescribeSelector(plan.RemovalSelector, categories)})");
        }
        if (plan.OncePerTurn) {
            steps.Add("once per turn");
        }

        return string.Join("; ", steps);
    }

    private static string DescribeSelector(CardSelector selector, IReadOnlyList<CategoryBase> categories) {
        if (selector.Kind == CardSelectorKind.Any) {
            return "any card";
        }

        return string.Join(" OR ", selector.Alternatives.Select(alternative => {
            string label = string.Join(" AND ", alternative.CategoryIdentities.Select(identity => {
                CategoryBase? category = categories.FirstOrDefault(candidate => candidate.Identity == identity);

                return category?.Name ?? Uri.UnescapeDataString(identity[(identity.IndexOf(':') + 1)..]);
            }));

            return alternative.Kind == CardSelectorKind.AllOf ? $"({label})" : label;
        }));
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
        card.PreDrawDeckRemovalCount > 0 || card.PostDrawDeckMillCount > 0 ||
        card.PreDrawRemovalCount > 0 ||
        card.PostDrawDiscardCount > 0 ||
        card.PostDrawSelector.Kind != CardSelectorKind.Any ||
        card.PostDrawFailureBehavior != PostDrawFailureBehavior.FailBranch;

    private static string CardCount(int count) => count == 1 ? "1 active card" : $"{count} active cards";
}
