using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

internal sealed class DrawEffectCalculation(List<Card> deck, int handSize, WorkBudget budget) {
    internal ExactCalculationResult Calculate(List<Combo> combos, IReadOnlyList<ComboGroup>? groups, bool totalOnly = false) {
        List<Card> ordinary = [];
        List<Card> effects = [];
        int ordinaryCopies = 0;
        foreach (Card card in deck) {
            budget.Spend(1);
            ArgumentOutOfRangeException.ThrowIfNegative(card.Copies);
            if (card.DrawCount is not null && card.Copies > 0) {
                effects.Add(card);
            }
            else {
                ordinary.Add(card);
                ordinaryCopies = checked(ordinaryCopies + card.Copies);
            }

            WorkBudget.CheckStorage(deck.Count, deck.Count + (long)effects.Count);
        }

        if (effects.Count == 0) {
            ProbabilityCalculation calculation = new(ordinary, handSize, budget);
            return totalOnly ? new(calculation.CalculateUnionExact(combos), [], []) : calculation.CalculateExactResults(combos, groups);
        }

        DrawResolution resolution = DrawEffectResolver.Resolve(ordinaryCopies, effects, handSize, budget);
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
            WorkBudget.CheckStorage(1L + individual.Length + grouped.Length, cells);
        }

        return new(total, individual, grouped);
    }
}
