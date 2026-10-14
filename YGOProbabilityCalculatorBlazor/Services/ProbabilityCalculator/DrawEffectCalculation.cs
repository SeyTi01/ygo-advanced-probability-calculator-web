using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

internal sealed class DrawEffectCalculation(
    List<Card> deck,
    int handSize,
    WorkBudget budget,
    bool conditionalEffectsEnabled = false,
    DrawActivationPriority? priority = null
) {
    internal ExactCalculationResult Calculate(List<Combo> combos, IReadOnlyList<ComboGroup>? groups, bool totalOnly = false) {
        if (conditionalEffectsEnabled && deck.Any(card => card.Copies > 0 && (card.PostDrawDiscardCount > 0 || card.PreDrawRemovalCount > 0))) {
            return new PostDrawCalculation(deck, handSize, budget, priority).Calculate(combos, groups, totalOnly);
        }

        List<Card> ordinary = [];
        List<Card> effects = [];
        int ordinaryCopies = 0;
        int deckCopies = 0;
        foreach (Card card in deck) {
            budget.Spend(1);
            ArgumentOutOfRangeException.ThrowIfNegative(card.Copies);
            deckCopies = checked(deckCopies + card.Copies);
            if (card.DrawCount is not null && card.Copies > 0) {
                effects.Add(card);
            }
            else {
                ordinary.Add(card);
                ordinaryCopies = checked(ordinaryCopies + card.Copies);
            }

            budget.CheckStorage(deck.Count, deck.Count + (long)effects.Count);
        }

        if (effects.Count == 0) {
            ProbabilityCalculation calculation = new(ordinary, handSize, budget);
            return totalOnly ? new(calculation.CalculateUnionExact(combos), [], []) : calculation.CalculateExactResults(combos, groups);
        }

        budget.Spend(effects.Count + 1L);
        if (!DrawEffectResolver.CanUseCountStateFastPath(effects, deckCopies, handSize, conditionalEffectsEnabled)) {
            List<Card> fixedDrawDeck = conditionalEffectsEnabled ? deck
                : deck.Select(card => card.WithPreDrawRemovalCount(0).WithPostDrawDiscardCount(0).WithPreDrawDeckRemovalCount(0).WithPostDrawDeckMillCount(0)).ToList();
            return new PostDrawCalculation(fixedDrawDeck, handSize, budget, priority).Calculate(combos, groups, totalOnly);
        }

        // The count-state proof bounds ALL possible activations collectively. With no
        // hand operations, every encountered source can resolve in any priority order;
        // its visible-draw contribution and OPT remainder are identical. Hidden positions
        // remain exchangeable. Priority therefore does not invalidate this fast path.
        bool ignoreDeckCosts = !conditionalEffectsEnabled && effects.Any(card => card.PreDrawDeckRemovalCount > 0 || card.PostDrawDeckMillCount > 0);
        List<Card> effectiveEffects = ignoreDeckCosts ? effects.Select(card => card.WithPreDrawDeckRemovalCount(0).WithPostDrawDeckMillCount(0)).ToList() : effects;
        DrawResolution resolution = DrawEffectResolver.Resolve(ordinaryCopies, effectiveEffects, handSize, budget);
        ExactProbability total = ExactProbability.Zero;
        ExactProbability[] individual = Enumerable.Repeat(ExactProbability.Zero, totalOnly ? 0 : combos.Count).ToArray();
        ExactProbability[] grouped = Enumerable.Repeat(ExactProbability.Zero, totalOnly ? 0 : groups?.Count ?? 0).ToArray();
        foreach ((DrawComposition composition, ExactProbability weight) in resolution.Scenarios) {
            budget.Spend(effects.Count + ordinary.Count + 1L);
            List<Card> retained = [];
            for (int i = 0; i < effects.Count; i++) {
                if (composition.EffectCounts[i] > 0) {
                    retained.Add(effects[i].WithCopies(composition.EffectCounts[i]));
                }
            }

            ProbabilityCalculation calculation = new(ordinary, composition.OrdinaryCount, budget, retained);
            ExactCalculationResult result = totalOnly
                ? new(calculation.CalculateUnionExact(combos), [], [])
                : calculation.CalculateExactResults(combos, groups);
            total = total.Add(weight.Multiply(result.Total, budget), budget);
            for (int i = 0; i < individual.Length; i++) {
                individual[i] = individual[i].Add(weight.Multiply(result.Combos[i], budget), budget);
            }

            for (int i = 0; i < grouped.Length; i++) {
                grouped[i] = grouped[i].Add(weight.Multiply(result.Groups[i], budget), budget);
            }

            long cells = total.Cells + individual.Sum(value => value.Cells) + grouped.Sum(value => value.Cells);
            budget.CheckStorage(1L + individual.Length + grouped.Length, cells);
        }

        return new(total, individual, grouped);
    }
}
