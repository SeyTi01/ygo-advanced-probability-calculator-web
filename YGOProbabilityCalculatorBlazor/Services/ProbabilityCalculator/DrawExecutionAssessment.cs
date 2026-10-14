using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

public enum DrawExecutionPath { Ordinary, DrawCountState, ExchangeableConditional, OrderedFrontier, Unknown }

// Structural guidance only. Never compiles events, reduces the deck, or enumerates hands.
public readonly record struct DrawExecutionAssessment(DrawExecutionPath Path, bool ElevatedRisk) {
    internal static bool IsExchangeableEffect(Card effect, bool relevant, int copies, int handSize) =>
        !effect.EffectPlan.HasActivationRestrictions && effect.PreDrawRemovalCount == 0 && !relevant && !effect.DrawOncePerTurn
        && effect.DrawCount == effect.PostDrawDiscardCount + 1
        && effect.PostDrawSelector.Kind == CardSelectorKind.Any
        && (effect.DrawCount!.Value + effect.PreDrawDeckRemovalCount + effect.PostDrawDeckMillCount) * (long)effect.Copies <= copies - handSize;

    public static DrawExecutionAssessment Assess(
        IReadOnlyList<Card> cards,
        IReadOnlyList<Combo> combos,
        int handSize,
        bool drawsEnabled,
        bool conditionsEnabled
    ) {
        if (!drawsEnabled) {
            return new(DrawExecutionPath.Ordinary, false);
        }
        // Bound UI work even for unusually large imported models. Unknown is not a latency claim.
        if (cards.Count > 128 || combos.Count > 64 || combos.Sum(combo => (long)combo.RequirementCount) > 256
            || cards.Sum(card => (long)card.Categories.Count) > 2048
            || combos.Sum(combo => combo.AllCategories.Sum(requirement => (long)requirement.Selector.CategoryIdentities.Count)) > 2048) {
            return new(DrawExecutionPath.Unknown, false);
        }
        if (cards.Any(card => card.Active && card.Copies > 0
            && card.EffectProfile.ExecutionModel != EffectExecutionModel.DrawSequenceV1)) {
            return new(DrawExecutionPath.Unknown, false);
        }
        Card[] active = cards.Where(card => card.Active && card.Copies > 0).ToArray();
        Card[] effects = active.Where(card => card.DrawCount is not null).ToArray();
        long copies = active.Sum(card => (long)card.Copies);
        if (copies > int.MaxValue || handSize < 1 || handSize > copies) {
            return new(DrawExecutionPath.Unknown, false);
        }
        if (effects.Length == 0) {
            return new(DrawExecutionPath.Ordinary, false);
        }
        // The shared proof rejects timing/lock interactions before testing collective capacity.
        // Priority itself does not invalidate the remaining exchangeability proofs.
        bool countStateFits = DrawEffectResolver.CanUseCountStateFastPath(effects, (int)copies, handSize, conditionsEnabled);
        bool conditional = conditionsEnabled && effects.Any(card => card.PreDrawRemovalCount > 0 || card.PostDrawDiscardCount > 0
            || (card.EffectPlan.HasActivationRestrictions && !countStateFits)
            || ((card.PreDrawDeckRemovalCount > 0 || card.PostDrawDeckMillCount > 0) && !countStateFits));
        if (!conditional) {
            return new(countStateFits
                ? DrawExecutionPath.DrawCountState : DrawExecutionPath.OrderedFrontier, false);
        }
        Combo[] routes = combos.Where(combo => combo.Active).ToArray();
        HashSet<string> trackedCategories = routes.SelectMany(combo => combo.AllCategories)
            .SelectMany(requirement => requirement.Selector.CategoryIdentities).ToHashSet(StringComparer.Ordinal);
        string Signature(Card card) => Membership(card, trackedCategories);
        bool Relevant(Card card) => routes.Any(combo =>
            combo.AllCards.Any(requirement => requirement.MinCount > 0 && requirement.CardId == card.Id)
            || combo.AllCategories.Any(requirement => requirement.MinCount > 0 && requirement.Selector.Matches(card.Categories)));

        // A sufficient pre-reduction proof: identical category membership and no direct
        // references make all irrelevant ordinary entries one filler after ReduceDeck.
        // More elaborate eligible decks remain Unknown rather than running reduction here.
        Card[] filler = active.Where(card => card.DrawCount is null && !Relevant(card)).ToArray();
        bool oneFillerClass = !routes.Any(combo => combo.AllCards.Any(requirement => filler.Any(card => card.Id == requirement.CardId)))
            && filler.Select(Signature).Distinct(StringComparer.Ordinal).Take(2).Count() <= 1;
        if (effects.Length == 1 && oneFillerClass && IsExchangeableEffect(effects[0], Relevant(effects[0]), (int)copies, handSize)) {
            return new(DrawExecutionPath.ExchangeableConditional, false);
        }
        bool couldSpecialize = effects.Length == 1 && IsExchangeableEffect(effects[0], Relevant(effects[0]), (int)copies, handSize);
        DrawExecutionPath path = couldSpecialize ? DrawExecutionPath.Unknown : DrawExecutionPath.OrderedFrontier;
        // Initial conservative rule: many membership classes AND several active routes,
        // plus multiple effects or a filtered/cost effect. General alone is not risky.
        int classes = active.Select(card => Signature(card) + (routes.Any(combo => combo.AllCards.Any(requirement => requirement.CardId == card.Id))
            ? ":" + card.Id : "")).Distinct(StringComparer.Ordinal).Take(8).Count();
        bool detailedEffect = effects.Length > 1 || effects.Any(card => card.PreDrawRemovalCount > 0 || card.PreDrawDeckRemovalCount > 0 || card.PostDrawDeckMillCount > 0 || card.PostDrawSelector.Kind != CardSelectorKind.Any);
        bool elevated = !couldSpecialize && classes >= 8 && routes.Length >= 4 && detailedEffect;
        return new(path, elevated);
    }

    private static string Membership(Card card, HashSet<string> trackedCategories) => string.Concat(card.Categories.Select(category => category.Identity)
        .Where(trackedCategories.Contains)
        .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(identity => $"{identity.Length}:{identity}"));
}
