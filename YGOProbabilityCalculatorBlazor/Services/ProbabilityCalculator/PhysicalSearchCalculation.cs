using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

// Conditional on a count-state terminal: ordinary hands are uniform k-subsets;
// the hidden positions are one uniform H-subset of the STILL UNOBSERVED pool.
// Seen sources (held or consumed) never enter that pool. For ordinary entry i,
// the joint weight is C(n_i,h_i) C(n_i-h_i,l_i); for an effect it is C(n_i-e_i,l_i).
// The common denominator is C(O,k) C(N-k-sum(e),H). Interleaved hand/unavailable
// variables preserve correlations, while Boolean union merges search witnesses
// BEFORE weighting. This is valid only under the existing count-state guard,
// with no identity-dependent hand removals or observations of hidden positions.
internal sealed class PhysicalSearchScenario {
    internal List<Card> Cards { get; }
    internal int[] Seen { get; }
    internal int[] Held { get; }
    internal int OrdinaryCount { get; }
    internal int HiddenCount { get; }
    internal int Capacity { get; }
    internal int HandSize { get; }
    internal int[] Domains { get; }
    private readonly Dictionary<string, int> _consumed;

    internal PhysicalSearchScenario(List<Card> cards, List<Card> effects, DrawComposition composition, WorkBudget budget) {
        budget.Spend(cards.Count * (effects.Count + 1L));
        budget.CheckStorage(cards.Count, 8L * cards.Count);
        Cards = cards.OrderByDescending(card => card.Search is not null).ToList();
        Seen = new int[cards.Count];
        Held = new int[cards.Count];
        Domains = new int[2 * cards.Count];
        _consumed = new(StringComparer.Ordinal);
        OrdinaryCount = composition.OrdinaryCount;
        for (int row = 0; row < Cards.Count; row++) {
            Card card = Cards[row];
            int effect = effects.FindIndex(entry => entry.Id == card.Id);
            int seen = effect < 0 ? -1 : composition.EffectCounts[effect];
            int activated = seen < 0 ? 0 : card.OncePerTurn ? Math.Min(1, seen) : seen;
            Seen[row] = seen;
            Held[row] = seen < 0 ? -1 : seen - activated;
            _consumed.Add(card.Id, activated);
            HiddenCount += activated * (card.PreDrawDeckRemovalCount + card.PostDrawDeckMillCount);
        }
        HandSize = OrdinaryCount + Held.Where(value => value >= 0).Sum();
        Capacity = cards.Sum(card => card.Copies) - OrdinaryCount - Seen.Where(value => value >= 0).Sum() - HiddenCount;
        for (int row = 0; row < Cards.Count; row++) {
            Domains[2 * row] = Held[row] >= 0 ? Held[row] : Math.Min(Cards[row].Copies, OrdinaryCount);
            // The second variable is unavailable = held + hidden loss.
            // Availability bounds then involve just one variable, while the
            // weighted counter still jointly chooses held and lost copies.
            Domains[2 * row + 1] = Math.Min(HiddenCount + Domains[2 * row], Cards[row].Copies - Consumed(Cards[row].Id));
        }
    }

    internal int Consumed(string id) => _consumed[id];
}

internal sealed class PhysicalSearchCalculation(
    List<Card> cards,
    List<Card> effects,
    int handSize,
    SearchAccessPlan search,
    WorkBudget budget
) {
    internal ExactCalculationResult Calculate(List<Combo> combos, IReadOnlyList<ComboGroup>? groups, bool totalOnly) {
        int ordinary = cards.Where(card => !effects.Contains(card)).Sum(card => card.Copies);
        DrawResolution resolution = DrawEffectResolver.Resolve<PhysicalSearchComposition>(ordinary, effects, handSize, budget);
        int rowCount = 1 + (totalOnly ? 0 : combos.Count + (groups?.Count ?? 0));
        budget.CheckStorage(rowCount, 2L * rowCount);
        ExactProbability[] totals = Enumerable.Repeat(ExactProbability.Zero, rowCount).ToArray();
        foreach ((DrawComposition composition, ExactProbability weight) in resolution.Scenarios) {
            PhysicalSearchScenario scenario = new(cards, effects, composition, budget);
            budget.SearchDiagnostics?.Add("physical scenarios");
            List<List<CompiledEvent?>> events = search.Compile(combos, scenario.HandSize, scenario.Cards,
                scenario.Capacity, physical: scenario);
            SearchDecisionCounter diagram = new(scenario.Domains, scenario.Domains.DefaultIfEmpty(0).Max(), budget, singleSample: false);
            int[] roots = events.Select(diagram.Union).ToArray();
            int union = roots.Aggregate(0, diagram.Or);
            int[] groupRoots = totalOnly ? [] : (groups ?? []).Select(group => roots.Where((_, index) => combos[index].GroupId == group.Id)
                .Aggregate(0, diagram.Or)).ToArray();
            SearchDecisionCounter.SurvivorCounter counter = new(diagram, scenario);
            BigInteger denominator = counter.Count(1);
            int[] resultRoots = totalOnly ? [union] : [union, .. roots, .. groupRoots];
            budget.CheckStorage(resultRoots.Length, 2L * resultRoots.Length + WorkBudget.IntegerCells(denominator));
            for (int row = 0; row < totals.Length; row++) {
                ExactProbability probability = new(counter.Count(resultRoots[row]), denominator);
                totals[row] = totals[row].Add(weight.Multiply(probability, budget), budget);
            }
            budget.CheckStorage(totals.Length, totals.Sum(value => value.Cells));
        }
        return new(totals[0], totalOnly ? [] : totals.Skip(1).Take(combos.Count).ToArray(),
            totalOnly ? [] : totals.Skip(1 + combos.Count).ToArray());
    }
}
