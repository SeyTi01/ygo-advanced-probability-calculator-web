using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

// Search setup is isolated from every no-search counting path. Simple draws keep
// their original count-state distribution; hand operations retain deferred pools.
internal sealed class SearchAccessCalculation(
    List<Card> deck,
    int handSize,
    WorkBudget budget,
    DrawActivationPriority? priority
) {
    private CalculationCapability? _capability;
    private SolverRoute _route;
    internal List<Card> Cards { get; private set; } = null!;
    internal List<Card> Effects { get; private set; } = null!;
    internal SearchAccessPlan Search { get; private set; } = null!;
    internal int HandSize => handSize;

    internal CalculationCapability DescribeCapability() => _capability ??=
        CalculationCapability.Search(_route, Search.TargetLossReason(Effects, handSize));

    internal ExactCalculationResult Calculate(List<Combo> combos, IReadOnlyList<ComboGroup>? groups, bool totalOnly) {
        List<Card> cards = deck.Where(card => card.Active && card.Copies > 0).ToList();
        int copies = cards.Sum(card => card.Copies);
        if (handSize < 0 || handSize > copies || deck.Any(card => card.Copies < 0)) {
            throw new ArgumentOutOfRangeException(nameof(handSize));
        }
        SearchAccessPlan search = new(cards, budget);
        HashSet<Card>? inert = DrawEffectFeasibility.FindInert(cards, copies, handSize, budget);
        List<Card> effects = cards.Where(card => card.DrawCount is not null && inert?.Contains(card) != true).ToList();
        Cards = cards;
        Effects = effects;
        Search = search;
        List<Card> ordinary = cards.Where(card => !effects.Contains(card)).ToList();
        if (effects.Count == 0) {
            _route = SolverRoute.SearchOnly;
            return new SearchProbabilityCalculation(cards, handSize, [], search, copies - handSize, budget)
                .Calculate(combos, groups, totalOnly);
        }
        if (effects.Any(card => card.EffectPlan.RequiresHandResolution)
            || !DrawEffectResolver.CanUseCountStateFastPath(effects, copies, handSize)) {
            PostDrawCalculation calculation = new(cards, handSize, budget, priority);
            ExactCalculationResult result = calculation.CalculateSearch(combos, groups, totalOnly, search);
            _route = calculation.Route;
            return result;
        }
        _route = SolverRoute.SearchCountState;
        DrawResolution resolution = DrawEffectResolver.Resolve<SearchComposition>(ordinary.Sum(card => card.Copies), effects, handSize, budget);
        ExactProbability total = ExactProbability.Zero;
        ExactProbability[] individual = Enumerable.Repeat(ExactProbability.Zero, totalOnly ? 0 : combos.Count).ToArray();
        ExactProbability[] grouped = Enumerable.Repeat(ExactProbability.Zero, totalOnly ? 0 : groups?.Count ?? 0).ToArray();
        foreach ((DrawComposition composition, ExactProbability weight) in resolution.Scenarios) {
            List<Card> retained = effects.Select((card, row) => card.WithCopies(composition.EffectCounts[row]))
                .Where(card => card.Copies > 0).ToList();
            ExactCalculationResult result = new SearchProbabilityCalculation(ordinary, composition.OrdinaryCount, retained,
                search, composition.EffectCounts[^1], budget).Calculate(combos, groups, totalOnly);
            total = total.Add(weight.Multiply(result.Total, budget), budget);
            for (int i = 0; i < individual.Length; i++) {
                individual[i] = individual[i].Add(weight.Multiply(result.Combos[i], budget), budget);
            }
            for (int i = 0; i < grouped.Length; i++) {
                grouped[i] = grouped[i].Add(weight.Multiply(result.Groups[i], budget), budget);
            }
            budget.CheckStorage(1L + individual.Length + grouped.Length,
                total.Cells + individual.Sum(value => value.Cells) + grouped.Sum(value => value.Cells));
        }

        return new(total, individual, grouped);
    }
}

internal interface IDrawTerminal {
    static abstract int[] Retained(int[] retained, int[]? encountered, List<Card> effects, int remaining);
}
internal readonly struct VisibleComposition : IDrawTerminal {
    public static int[] Retained(int[] retained, int[]? encountered, List<Card> effects, int remaining) => retained;
}
internal readonly struct SearchComposition : IDrawTerminal {
    public static int[] Retained(int[] retained, int[]? encountered, List<Card> effects, int remaining) {
        int hidden = 0;
        for (int i = 0; i < effects.Count; i++) {
            int count = encountered?[i] ?? effects[i].Copies;
            int activations = effects[i].DrawOncePerTurn ? Math.Min(1, count) : count;
            hidden += activations * (effects[i].PreDrawDeckRemovalCount + effects[i].PostDrawDeckMillCount);
        }

        return [.. retained, Math.Max(0, remaining - hidden)];
    }
}
