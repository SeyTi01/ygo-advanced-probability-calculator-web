using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

// Search setup is isolated from every no-search counting path. Simple draws keep
// their original count-state distribution; hand operations retain deferred pools.
internal sealed class SearchAccessCalculation(
    List<Card> deck,
    int handSize,
    WorkBudget budget,
    DrawActivationPriority? priority,
    bool physicalSearch = false,
    bool adaptiveFast = false
) {
    private CalculationCapability? _capability;
    private CapabilityReason? _targetLossReason;
    private SolverRoute _route;
    private PostDrawCalculation? _ordered;
    private DrawResolution? _resolution;
    internal List<Card> Cards { get; private set; } = null!;
    internal List<Card> Effects { get; private set; } = null!;
    internal SearchAccessPlan Search { get; private set; } = null!;
    internal int HandSize => handSize;

    internal CalculationCapability DescribeCapability() => _capability ??=
        CalculationCapability.Search(_route, _targetLossReason ?? Search.TargetLossReason(Effects, handSize));

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
        if (cards.Any(card => card.EffectPlan.RequiresSearchResolution || (card.Search is not null && card.MinimumHandSize > 0))) {
            // Hand loss and mode allowances invalidate the substitution projection
            // and its survival certificate. Both modes use physical counting.
            PostDrawCalculation bounded = new(cards, handSize, budget, priority);
            ExactCalculationResult result = bounded.CalculateSearch(combos, groups, totalOnly, search, physical: true);
            _capability = new(bounded.Route, PhysicalSearchAccuracy.ProvenExact,
                CapabilityReason.ExplicitPhysicalSurvivors, CertificationSupport.ReusePrimary);

            return result;
        }
        if (effects.Count == 0) {
            _route = SolverRoute.SearchOnly;
            return new SearchProbabilityCalculation(cards, handSize, [], search, copies - handSize, budget)
                .Calculate(combos, groups, totalOnly);
        }
        bool countState = !effects.Any(card => card.EffectPlan.RequiresHandResolution)
            && DrawEffectResolver.CanUseCountStateFastPath(effects, copies, handSize);
        // Choose once, after the existing applicability proof and before either
        // complete solver. Structurally proven requests keep their old route.
        bool adaptiveCandidate = adaptiveFast && !countState
            && FastPhysicalRoute.IsCandidate(cards, effects, handSize, combos, groups, budget);
        if (physicalSearch || adaptiveCandidate) {
            CapabilityReason reason = search.TargetLossReason(effects, handSize);
            _targetLossReason = reason;
            if (!CalculationCapability.ProvesSearchSurvival(reason)) {
                if (!countState) {
                    PostDrawCalculation fallback = new(cards, handSize, budget, priority);
                    ExactCalculationResult enumerated = fallback.CalculateSearch(combos, groups, totalOnly, search, physical: true);
                    _capability = new(fallback.Route, PhysicalSearchAccuracy.ProvenExact,
                        fallback.Route == SolverRoute.SearchPhysicalDeferred ? CapabilityReason.ConditionalSelectiveSurvivors
                            : CapabilityReason.ExplicitPhysicalSurvivors, CertificationSupport.ReusePrimary);
                    return enumerated;
                }
                ExactCalculationResult result = new PhysicalSearchCalculation(cards, effects, handSize, search, budget)
                    .Calculate(combos, groups, totalOnly);
                _capability = new(SolverRoute.SearchPhysicalCountState, PhysicalSearchAccuracy.ProvenExact,
                    CapabilityReason.ConditionalPhysicalSurvivors, CertificationSupport.ReusePrimary);
                return result;
            }
        }
        if (!countState) {
            PostDrawCalculation calculation = new(cards, handSize, budget, priority);
            ExactCalculationResult result = calculation.CalculateSearch(combos, groups, totalOnly, search);
            _route = calculation.Route;
            _ordered = calculation;
            return result;
        }
        _route = SolverRoute.SearchCountState;
        DrawResolution resolution = DrawEffectResolver.Resolve<SearchComposition>(ordinary.Sum(card => card.Copies), effects, handSize, budget);
        _resolution = resolution;
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

    // Reuse completed primary state. A streamed lower pass may replay its
    // guarded partition, always with the same draw/retention policy.
    internal ExactCalculationResult CalculateLower(
        List<Combo> combos,
        IReadOnlyList<ComboGroup>? groups,
        bool declineSearch,
        ExactCalculationResult? improve = null
    ) {
        if (_ordered is not null) {
            return _ordered.CalculateSearchLower(combos, groups, declineSearch);
        }
        DrawResolution resolution = _resolution ?? throw new InvalidOperationException("Missing completed search distribution.");
        budget.Spend((long)Cards.Count * (Effects.Count + 1) + 1);
        List<Card> ordinary = Cards.Where(card => !Effects.Contains(card)).ToList();
        int copies = Cards.Sum(card => card.Copies);
        ExactProbability[] sums = improve is null
            ? Enumerable.Repeat(ExactProbability.Zero, 1 + combos.Count + (groups?.Count ?? 0)).ToArray()
            : [improve.Total, .. improve.Combos, .. improve.Groups];
        foreach ((DrawComposition composition, ExactProbability weight) in resolution.Scenarios) {
            budget.Spend(Cards.Count + sums.Length + Effects.Count + 1L);
            List<Card> retained = Effects.Select((card, row) => card.WithCopies(composition.EffectCounts[row]))
                .Where(card => card.Copies > 0).ToList();
            int held = composition.OrdinaryCount + retained.Sum(card => card.Copies);
            int remaining = composition.EffectCounts[^1];
            int lost = copies - held - remaining;
            if (remaining < 0 || lost < 0 || held < 0) {
                throw new InvalidOperationException("Count-state conservation failed.");
            }
            int[]? losses = improve is null ? null : CountStateLossBudget.For(Cards, Effects, composition, lost, budget);
            if (losses is not null && !Search.CanImproveLoss(lost, losses)) {
                continue;
            }
            ExactCalculationResult lower = Count(losses);
            ExactProbability[] values = [lower.Total, .. lower.Combos, .. lower.Groups];
            if (improve is not null) {
                // Replace only affected conditional contributions. Unaffected
                // scenarios reuse the completed elementary aggregate; no cache
                // or second draw distribution is retained. Publish only after
                // every delta completes, so limits retain the old certificate.
                ExactCalculationResult previous = Count(null);
                ExactProbability[] old = [previous.Total, .. previous.Combos, .. previous.Groups];
                for (int row = 0; row < values.Length; row++) {
                    values[row] = values[row].Add(new(-old[row].Numerator, old[row].Denominator), budget);
                }
            }
            for (int row = 0; row < sums.Length; row++) {
                sums[row] = sums[row].Add(weight.Multiply(values[row], budget), budget);
            }
            budget.CheckStorage(sums.Length, sums.Sum(value => value.Cells));

            ExactCalculationResult Count(int[]? lossBounds) => new SearchProbabilityCalculation(ordinary, composition.OrdinaryCount,
                retained, Search, declineSearch ? 0 : remaining, budget, targetLoss: lost, targetLosses: lossBounds)
                .Calculate(combos, groups, false);
        }

        return new(sums[0], sums.Skip(1).Take(combos.Count).ToArray(), sums.Skip(1 + combos.Count).ToArray());
    }

    internal ExactCalculationResult? TightenSurvivalUpper(List<Combo> combos, IReadOnlyList<ComboGroup>? groups, ExactCalculationResult upper) =>
        TargetSurvivalUpperBound.TryCalculate(Cards, Effects, _resolution, combos, groups, upper, budget);

    internal bool CanRefineKnownSourceLoss() {
        // Ordered/coalesced identities need a different observation proof.
        if (_resolution is null) {
            return false;
        }
        budget.Spend(Cards.Count + 1L);
        int copies = Cards.Sum(card => card.Copies);
        foreach (DrawComposition composition in _resolution.Scenarios.Keys) {
            budget.Spend(Effects.Count + 1L);
            int held = composition.OrdinaryCount + composition.EffectCounts.Take(Effects.Count).Sum();
            int lost = copies - held - composition.EffectCounts[^1];
            int[] losses = CountStateLossBudget.For(Cards, Effects, composition, lost, budget);
            if (Search.CanImproveLoss(lost, losses)) {
                return true;
            }
        }

        return false;
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
            int activations = Math.Min(effects[i].MaximumActivations, count);
            hidden += activations * (effects[i].PreDrawDeckRemovalCount + effects[i].PostDrawDeckMillCount);
        }

        return [.. retained, Math.Max(0, remaining - hidden)];
    }
}

// Keep the full encountered source vector, not only OPT remainders. Consumed
// copies are visible observations and must be excluded before sampling losses.
internal readonly struct PhysicalSearchComposition : IDrawTerminal {
    public static int[] Retained(int[] retained, int[]? encountered, List<Card> effects, int remaining) =>
        encountered is null ? effects.Select(effect => effect.Copies).ToArray() : (int[])encountered.Clone();
}
