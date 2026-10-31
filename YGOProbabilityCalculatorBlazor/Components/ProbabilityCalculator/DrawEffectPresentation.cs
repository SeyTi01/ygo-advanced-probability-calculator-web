using YGOProbabilityCalculatorBlazor.Localization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

public static class DrawEffectPresentation {
    public static string? HeaderForCard(Card card, bool drawEffectsEnabled) {
        drawEffectsEnabled &= card.SimulationEnabled;
        if (card.EffectProfile.ExecutionModel != EffectExecutionModel.DrawSequenceV1) {
            return UiText.Get("Ui.UnsupportedEffect");
        }

        if (card.IsDangerReveal) {
            return UiText.Get(drawEffectsEnabled ? "Effect.DangerReveal" : "Effect.DangerRevealOff");
        }

        if (card.Search is not null) {
            return drawEffectsEnabled ? UiText.Get("Ui.Search1") : UiText.Get("Ui.SearchConfiguredOff");
        }
        if (card.DrawCount is not { } drawCount) {
            return null;
        }

        if (!drawEffectsEnabled) {
            return UiText.Get("Ui.DrawConfiguredOff");
        }

        string headerText = UiText.Format("Ui.Draw0", drawCount);
        if (card.PreDrawDeckRemovalCount > 0) {
            headerText = UiText.Format("Ui.Banish01", card.PreDrawDeckRemovalCount, headerText);
        }
        if (card.PreDrawRemovalCount > 0) {
            headerText = UiText.Format("Ui.Cost01", card.PreDrawRemovalCount, headerText);
        }
        if (card.PostDrawDeckMillCount > 0) {
            headerText += UiText.Format("Ui.Mill0", card.PostDrawDeckMillCount);
        }
        if (card.PostDrawDiscardCount > 0) {
            headerText += UiText.Format("Ui.Remove0", card.PostDrawDiscardCount);
        }

        return headerText;
    }

    public static string DescribePlan(DrawEffectPlan plan, IReadOnlyList<CategoryBase> categories) {
        if (plan.Operation == DrawOperation.DangerReveal) {
            return UiText.Get("Effect.DangerRevealSummary");
        }

        if (plan.DrawCount is null && plan.Search is null) {
            return UiText.Get("Ui.NoDrawEffect");
        }

        List<string> steps = [];
        if (plan.HandCost > 0) {
            steps.Add(UiText.Format("Ui.Remove0FromHand1", plan.HandCost, DescribeSelector(plan.HandCostSelector, categories)));
        }
        if (plan.HiddenDeckCost > 0) {
            steps.Add(UiText.Format("Ui.Banish0UnseenDeckCards", plan.HiddenDeckCost));
        }
        steps.Add(plan.Search is null ? UiText.Format("Ui.Draw0", plan.DrawCount) : UiText.Format("Ui.Search10", DescribeSelector(plan.Search.Selector, categories)));
        if (plan.HiddenPostDrawMill > 0) {
            steps.Add(UiText.Format("Ui.Send0UnseenTopDeckCardsToTheGY", plan.HiddenPostDrawMill));
        }
        if (plan.RemovalCount > 0) {
            steps.Add(UiText.Format("Ui.Remove0FromHand1.af3ec21f", plan.RemovalCount, DescribeSelector(plan.RemovalSelector, categories)));
        }
        if (plan.FirstActivationOnly) {
            steps.Add(UiText.Get("Ui.FirstDrawActivationOnly"));
        }
        if (plan.BlocksLaterDraws) {
            steps.Add(UiText.Get("Ui.NoFurtherDraws"));
        }
        if (plan.MinimumHandSize > 0) {
            steps.Add(UiText.Format("Ui.AtLeast0CardsInHandBeforeActivation", plan.MinimumHandSize));
        }
        if (plan.Search?.Modes is { } modes) {
            steps.Add(string.Join("; ", modes.Select(mode => UiText.Format("Ui.01PerEntry", DescribeSelector(mode.Selector, categories), mode.ActivationLimit?.ToString() ?? UiText.Get("Ui.Unlimited")))));
        }
        if (plan.ActivationLimit is int limit) {
            steps.Add(UiText.Format("Ui.UpTo0ActivationsPerEntry", limit));
        }
        else if (plan.OncePerTurn) {
            steps.Add(UiText.Get("Ui.OncePerTurn.e31d0a49"));
        }

        return string.Join("; ", steps);
    }

    public static string DescribeSelector(CardSelector selector, IReadOnlyList<CategoryBase> categories) {
        if (selector.Kind == CardSelectorKind.Not && selector.Children[0].Kind == CardSelectorKind.Any) {
            return UiText.Get("Ui.NoEligibleCards");
        }
        if (selector.Kind == CardSelectorKind.Any) {
            return UiText.Get("Ui.AnyCard");
        }

        if (selector.Kind is CardSelectorKind.Objective or CardSelectorKind.Not) {
            return UiText.Format(selector.Kind == CardSelectorKind.Objective ? "Selector.Verified" : "Selector.Except", DescribeSelector(selector.Children[0], categories));
        }
        if (selector.NumericRange is { } range) { return DisplayLabels.NumericRange(range); }
        if (selector.Kind == CardSelectorKind.LevelAtLeast) {
            return UiText.Format("Ui.Level0", selector.MinimumLevel);
        }
        if (selector.Kind == CardSelectorKind.Category) {
            string identity = selector.CategoryIdentities[0];
            return DisplayLabels.CategoryOrNull(categories.FirstOrDefault(candidate => candidate.Identity == identity))
                ?? Uri.UnescapeDataString(identity[(identity.IndexOf(':') + 1)..]);
        }
        string operation = selector.Kind == CardSelectorKind.AllOf ? UiText.Get("Expression.AndSeparator") : UiText.Get("Expression.OrSeparator");
        return "(" + string.Join(operation, selector.AuthoredTerms.Select(term => DescribeSelector(term, categories))) + ")";
    }
}
