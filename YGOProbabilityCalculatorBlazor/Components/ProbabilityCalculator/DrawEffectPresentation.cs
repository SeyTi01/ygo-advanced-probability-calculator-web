using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

public readonly record struct DrawEffectPresentation(string? HeaderText, string? InactiveMessage) {
    public static DrawEffectPresentation ForCard(Card card, bool drawEffectsEnabled) {
        if (card.EffectProfile.ExecutionModel != EffectExecutionModel.DrawSequenceV1) {
            return new("Unsupported effect", null);
        }

        if (card.Search is not null) {
            return new(drawEffectsEnabled ? "Search 1" : "Search configured · Off", null);
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
        if (plan.DrawCount is null && plan.Search is null) {
            return "No draw effect";
        }

        List<string> steps = [];
        if (plan.HandCost > 0) {
            steps.Add($"Remove {plan.HandCost} from hand ({DescribeSelector(plan.HandCostSelector, categories)})");
        }
        if (plan.HiddenDeckCost > 0) {
            steps.Add($"Banish {plan.HiddenDeckCost} unseen deck cards");
        }
        steps.Add(plan.Search is null ? $"Draw {plan.DrawCount}" : $"Search 1 ({DescribeSelector(plan.Search.Selector, categories)})");
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
        if (plan.MinimumHandSize > 0) {
            steps.Add($"at least {plan.MinimumHandSize} cards in hand before activation");
        }
        if (plan.Search?.Modes is { } modes) {
            steps.Add(string.Join("; ", modes.Select(mode => $"{DescribeSelector(mode.Selector, categories)}: {mode.ActivationLimit?.ToString() ?? "unlimited"} per entry")));
        }
        if (plan.ActivationLimit is int limit) {
            steps.Add($"up to {limit} activations per entry");
        }
        else if (plan.OncePerTurn) {
            steps.Add("once per turn");
        }

        return string.Join("; ", steps);
    }

    public static string DescribeSelector(CardSelector selector, IReadOnlyList<CategoryBase> categories) {
        if (selector.Kind == CardSelectorKind.Any) {
            return "any card";
        }

        if (selector.Kind is CardSelectorKind.Objective or CardSelectorKind.Not) {
            return (selector.Kind == CardSelectorKind.Objective ? "verified " : "except ") + DescribeSelector(selector.Children[0], categories);
        }
        if (selector.Kind == CardSelectorKind.LevelAtLeast) {
            return $"Level >= {selector.MinimumLevel}";
        }
        if (selector.Kind == CardSelectorKind.Category) {
            string identity = selector.CategoryIdentities[0];
            return categories.FirstOrDefault(candidate => candidate.Identity == identity)?.Name
                ?? Uri.UnescapeDataString(identity[(identity.IndexOf(':') + 1)..]);
        }
        string operation = selector.Kind == CardSelectorKind.AllOf ? " AND " : " OR ";
        return "(" + string.Join(operation, selector.Terms.Select(term => DescribeSelector(term, categories))) + ")";
    }
}
