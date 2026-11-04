using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

public enum DrawExecutionPath { Ordinary, DrawCountState, ExchangeableConditional, OrderedFrontier, Unknown, DeferredConditional }

// Structural guidance only. Never compiles events, reduces the deck, or enumerates hands.
public readonly record struct DrawExecutionAssessment(DrawExecutionPath Path, bool ElevatedRisk) {
    internal static bool IsExchangeableEffect(Card effect, bool relevant, int copies, int handSize) =>
        !effect.EffectPlan.HasActivationRestrictions && effect.PreDrawRemovalCount == 0 && !relevant && effect.MaximumActivations == int.MaxValue
        && effect.DrawCount == effect.PostDrawDiscardCount + 1
        && effect.PostDrawSelector.Kind == CardSelectorKind.Any
        && (effect.DrawCount!.Value + effect.PreDrawDeckRemovalCount + effect.PostDrawDeckMillCount) * (long)effect.Copies <= copies - handSize;

    internal static bool CanDeferIdentities(Card effect) =>
        (effect.PreDrawRemovalCount == 0 || effect.PreDrawSelector.Kind != CardSelectorKind.Any)
        && (effect.PostDrawDiscardCount == 0 || effect.PostDrawSelector.Kind != CardSelectorKind.Any)
        && !effect.EffectPlan.HasActivationRestrictions;

    // Every hand operation must exclude the pool. Hidden deck operations depend
    // only on physical capacity, which the ordered resolver already tracks.
    private static bool IsRemovalMaterial(Card effect, Card card) =>
        (effect.PreDrawRemovalCount > 0 && effect.PreDrawSelector.Matches(card))
        || (effect.PostDrawDiscardCount > 0 && effect.PostDrawSelector.Matches(card));

    public static DrawExecutionAssessment Assess(
        IReadOnlyList<Card> cards,
        IReadOnlyList<Combo> combos,
        int handSize,
        bool drawsEnabled
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

        Card[] active = [.. cards.Where(card => card.Active && card.Copies > 0).Select(card => card.ForCalculation())];

        if (active.Any(card => card.EffectProfile.ExecutionModel != EffectExecutionModel.DrawSequenceV1)) {
            return new(DrawExecutionPath.Unknown, false);
        }

        long copies = active.Sum(card => (long)card.Copies);

        if (copies > int.MaxValue || handSize < 1 || handSize > copies) {
            return new(DrawExecutionPath.Unknown, false);
        }

        HashSet<Card>? inert = DrawEffectFeasibility.FindInert(active, (int)copies, handSize);
        Card[] effects = [.. active.Where(card => card.DrawCount is not null && (inert is null || !inert.Contains(card)))];
        Combo[] routes = [.. combos.Where(combo => combo.Active)];

        // Excavation resolves physical target availability even when the draws alone
        // fit count-state processing. Hidden losses plus searches made this shape
        // expensive in the excavation benchmark; retain the existing size thresholds.
        bool excavationWithHiddenSearch = active.Any(card => card.Excavation is { } excavation && excavation.Count <= copies - handSize)
            && effects.Any(card => card.PreDrawDeckRemovalCount > 0 || card.PostDrawDeckMillCount > 0)
            && active.Any(card => card.Search is not null);
        if (excavationWithHiddenSearch && active.Length >= 8 && routes.Length >= 4) {
            return new(DrawExecutionPath.OrderedFrontier, true);
        }

        if (effects.Length == 0) {
            return new(DrawExecutionPath.Ordinary, false);
        }

        // The shared proof rejects timing/lock interactions before testing collective capacity.
        // Priority itself does not invalidate the remaining exchangeability proofs.
        bool countStateFits = DrawEffectResolver.CanUseCountStateFastPath(effects, (int)copies, handSize);
        bool conditional = effects.Any(card => card.PreDrawRemovalCount > 0 || card.PostDrawDiscardCount > 0
            || (card.EffectPlan.HasActivationRestrictions && !countStateFits)
            || ((card.PreDrawDeckRemovalCount > 0 || card.PostDrawDeckMillCount > 0) && !countStateFits));

        if (!conditional) {
            return new(countStateFits ? DrawExecutionPath.DrawCountState : DrawExecutionPath.OrderedFrontier, false);
        }

        if (active.Any(card => card.Search is not null)) {
            // Search hand operations use the ordered resolver, possibly with a
            // smaller deferred pool. Do not promise the no-search exchangeable
            // specialization without proving search-policy equivalence.
            return new(DrawExecutionPath.OrderedFrontier, active.Length >= 8 && routes.Length >= 4);
        }

        HashSet<string> trackedCategories = routes
            .SelectMany(combo => combo.AllCategories)
            .SelectMany(requirement => requirement.Selector.CategoryIdentities)
            .ToHashSet(StringComparer.Ordinal);

        HashSet<string> directIds = routes
            .SelectMany(combo => combo.AllCards)
            .Select(requirement => requirement.CardId)
            .ToHashSet(StringComparer.Ordinal);

        string Signature(Card card) => Membership(card, trackedCategories);
        string PolicySignature(Card card) => Signature(card) + (directIds.Contains(card.Id) ? ":" + card.Id : "");

        bool Relevant(Card card) => routes.Any(combo =>
            combo.AllCards.Any(requirement => requirement.MinCount > 0 && requirement.CardId == card.Id)
            || combo.AllCategories.Any(requirement => requirement.MinCount > 0 && requirement.Selector.Matches(card)));

        // A sufficient pre-reduction proof: identical category membership and no direct
        // references make all irrelevant ordinary entries one filler after ReduceDeck.
        // More elaborate eligible decks remain Unknown rather than running reduction here.
        Card[] filler = [.. active.Where(card => card.DrawCount is null && !Relevant(card))];
        bool oneFillerClass = !routes.Any(combo => combo.AllCards.Any(requirement => filler.Any(card => card.Id == requirement.CardId)))
            && filler.Select(Signature).Distinct(StringComparer.Ordinal).Take(2).Count() <= 1;

        if (effects.Length == 1 && oneFillerClass && IsExchangeableEffect(effects[0], Relevant(effects[0]), (int)copies, handSize)) {
            return new(DrawExecutionPath.ExchangeableConditional, false);
        }

        // The resolver retains even preflight-inert sources. Use the same complete
        // source set for this proof; do not forget a selector or merge source IDs.
        Card[] allSources = [.. active.Where(card => card.DrawCount is not null)];
        bool Material(Card card) => allSources.Any(effect => IsRemovalMaterial(effect, card));

        if (allSources.All(CanDeferIdentities)
            && allSources.Any(effect => effect.PreDrawRemovalCount > 0 || effect.PostDrawDiscardCount > 0)
            && active
                .Where(card => card.DrawCount is null && !Material(card))
                .Select(PolicySignature).Distinct(StringComparer.Ordinal).Take(2).Count() == 2) {

            int observedClasses = allSources.Length + active.Where(card => card.DrawCount is null && Material(card))
                .Select(PolicySignature)
                .Distinct(StringComparer.Ordinal)
                .Take(8)
                .Count();

            return new(DrawExecutionPath.DeferredConditional, observedClasses >= 8 && routes.Length >= 4);
        }

        bool couldSpecialize = effects.Length == 1 && IsExchangeableEffect(effects[0], Relevant(effects[0]), (int)copies, handSize);
        DrawExecutionPath path = couldSpecialize ? DrawExecutionPath.Unknown : DrawExecutionPath.OrderedFrontier;
        // Initial conservative rule: many membership classes AND several active routes,
        // plus multiple effects or a filtered/cost effect. General alone is not risky.
        int classes = active.Select(PolicySignature).Distinct(StringComparer.Ordinal).Take(8).Count();
        bool detailedEffect = effects.Length > 1 || effects.Any(card => card.PreDrawRemovalCount > 0 || card.PreDrawDeckRemovalCount > 0 || card.PostDrawDeckMillCount > 0 || card.PostDrawSelector.Kind != CardSelectorKind.Any);
        bool elevated = !couldSpecialize && classes >= 8 && routes.Length >= 4 && detailedEffect;

        return new(path, elevated);
    }

    private static string Membership(Card card, HashSet<string> trackedCategories) => string.Concat(card.Categories.Select(category => category.Identity)
        .Where(trackedCategories.Contains)
        .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(identity => $"{identity.Length}:{identity}"));
}
