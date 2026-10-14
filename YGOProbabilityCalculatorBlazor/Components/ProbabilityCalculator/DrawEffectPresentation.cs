using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

public readonly record struct DrawEffectPresentation(string? HeaderText, string? InactiveMessage) {
    public static DrawEffectPresentation ForCard(Card card, bool drawEffectsEnabled) {
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
        if (card.PreDrawDeckRemovalCount > 0) {
            headerText = $"Banish {card.PreDrawDeckRemovalCount} · {headerText}";
        }
        if (card.PreDrawRemovalCount > 0) {
            headerText = $"Cost {card.PreDrawRemovalCount} · {headerText}";
        }
        if (card.PostDrawDeckMillCount > 0) {
            headerText += $" · Mill {card.PostDrawDeckMillCount}";
        }
        if (card.PostDrawDiscardCount > 0) {
            headerText += $" · Remove {card.PostDrawDiscardCount}";
        }

        return new(headerText, null);
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
        if (plan.FirstActivationOnly) {
            steps.Add("first draw activation only");
        }
        if (plan.BlocksLaterDraws) {
            steps.Add("no further draws");
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
}
