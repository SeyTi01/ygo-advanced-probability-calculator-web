using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using DrawResolutionResult = YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.DrawResolution.DrawResolution;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Compilation;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Counting;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Search;

// Conditional on a count-state terminal: ordinary hands are uniform k-subsets;
// the hidden positions are one uniform H-subset of the STILL UNOBSERVED pool.
// Seen sources (held or consumed) never enter that pool. For ordinary entry i,
// the joint weight is C(n_i,h_i) C(n_i-h_i,l_i); for an effect it is C(n_i-e_i,l_i).
// The common denominator is C(O,k) C(N-k-sum(e),H). Interleaved hand/unavailable
// variables preserve correlations, while Boolean union merges search witnesses
// BEFORE weighting. Count-state draws and the selective deferred resolver each
// prove that the ordinary pool is unobserved by all hand operations. Explicit
// rows may be removed: both their observed and retained counts must be known.
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
            int activated = seen < 0 ? 0 : Math.Min(card.MaximumActivations, seen);
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

    // Selective hand operations observe explicit rows only. The resolver retains
    // their seen/held counts and one unremovable exchangeable pool. Hidden labels
    // remain unobserved even when they precede later visible draws.
    internal PhysicalSearchScenario(
        List<Card> cards,
        List<Card> explicitRows,
        int[] remainingAndHeld,
        int ordinaryCount,
        int hiddenCount,
        WorkBudget budget
    ) {
        budget.Spend(cards.Count * (explicitRows.Count + 1L));
        budget.CheckStorage(cards.Count, 8L * cards.Count);
        Cards = cards.OrderByDescending(card => card.Search is not null).ToList();
        Seen = new int[cards.Count];
        Held = new int[cards.Count];
        Domains = new int[2 * cards.Count];
        _consumed = new(StringComparer.Ordinal);
        OrdinaryCount = ordinaryCount;
        HiddenCount = hiddenCount;
        int width = explicitRows.Count + 1;
        for (int row = 0; row < Cards.Count; row++) {
            Card card = Cards[row];
            int index = explicitRows.FindIndex(entry => entry.Id == card.Id);
            Seen[row] = index < 0 ? -1 : card.Copies - remainingAndHeld[index];
            Held[row] = index < 0 ? -1 : remainingAndHeld[width + index];
            _consumed.Add(card.Id, index < 0 ? 0 : Seen[row] - Held[row]);
            Domains[2 * row] = index < 0 ? Math.Min(card.Copies, ordinaryCount) : Held[row];
            Domains[2 * row + 1] = Math.Min(hiddenCount + Domains[2 * row], card.Copies - Consumed(card.Id));
        }
        HandSize = ordinaryCount + Held.Where(value => value >= 0).Sum();
        Capacity = cards.Sum(card => card.Copies) - ordinaryCount - Seen.Where(value => value >= 0).Sum() - hiddenCount;
    }
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
        DrawResolutionResult resolution = DrawEffectResolver.Resolve<PhysicalSearchComposition>(ordinary, effects, handSize, budget);
        int rowCount = 1 + (totalOnly ? 0 : combos.Count + (groups?.Count ?? 0));
        budget.CheckStorage(rowCount, 2L * rowCount);
        ExactProbability[] totals = Enumerable.Repeat(ExactProbability.Zero, rowCount).ToArray();
        foreach ((DrawComposition composition, ExactProbability weight) in resolution.Scenarios) {
            PhysicalSearchScenario scenario = new(cards, effects, composition, budget);
            ExactProbability[] probabilities = CountScenario(scenario, search, combos, groups, totalOnly, budget);
            for (int row = 0; row < totals.Length; row++) {
                totals[row] = totals[row].Add(weight.Multiply(probabilities[row], budget), budget);
            }
            budget.CheckStorage(totals.Length, totals.Sum(value => value.Cells));
        }
        return new(totals[0], totalOnly ? [] : totals.Skip(1).Take(combos.Count).ToArray(),
            totalOnly ? [] : totals.Skip(1 + combos.Count).ToArray());
    }

    internal static ExactProbability[] CountScenario(
        PhysicalSearchScenario scenario,
        SearchAccessPlan search,
        List<Combo> combos,
        IReadOnlyList<ComboGroup>? groups,
        bool totalOnly,
        WorkBudget budget,
        bool conditionObserved = false
    ) {
        using IDisposable? stage = budget.SearchDiagnostics?.Stage("physical");
        budget.SearchDiagnostics?.BeginScenario();
        budget.SearchDiagnostics?.Add("physical scenarios");
        List<List<CompiledEvent?>> events = search.Compile(combos, scenario.HandSize, scenario.Cards,
            scenario.Capacity, physical: scenario);
        using IDisposable? conditioning = budget.SearchDiagnostics?.Measure("fixed hand projection");
        if (conditionObserved) {
            foreach (List<CompiledEvent?> routes in events) {
                for (int route = 0; route < routes.Count; route++) {
                    routes[route] = Condition(routes[route], scenario, budget);
                }
            }
        }
        conditioning?.Dispose();
        budget.SearchDiagnostics?.Predicates(events.SelectMany(routes => routes));
        using IDisposable? construction = budget.SearchDiagnostics?.Measure("decision construction");
        SearchDecisionCounter diagram = new(scenario.Domains, scenario.Domains.DefaultIfEmpty(0).Max(), budget, singleSample: false);
        int[] roots = events.Select(diagram.Union).ToArray();
        int union = roots.Aggregate(0, diagram.Or);
        int[] groupRoots = totalOnly ? [] : (groups ?? []).Select(group => roots.Where((_, index) => combos[index].GroupId == group.Id)
            .Aggregate(0, diagram.Or)).ToArray();
        construction?.Dispose();
        using IDisposable? counting = budget.SearchDiagnostics?.Measure("weighted counting");
        SearchDecisionCounter.SurvivorCounter counter = new(diagram, scenario);
        BigInteger denominator = counter.Count(1);
        int[] resultRoots = totalOnly ? [union] : [union, .. roots, .. groupRoots];
        budget.CheckStorage(resultRoots.Length, 2L * resultRoots.Length + WorkBudget.IntegerCells(denominator));
        ExactProbability[] probabilities = resultRoots.Select(root => new ExactProbability(counter.Count(root), denominator)).ToArray();
        budget.CheckStorage(probabilities.Length, probabilities.Sum(value => value.Cells));
        return probabilities;
    }

    private static CompiledEvent? Condition(CompiledEvent? predicate, PhysicalSearchScenario scenario, WorkBudget budget) {
        if (predicate is null) {
            return null;
        }
        List<CountBound> bounds = [];
        // Projection bounds contain either held variables or a single unavailable
        // variable. Substitute known held values before building the diagram;
        // otherwise impossible values of explicit material enlarge every union.
        // With no hidden loss, unavailable equals held, including pooled rows.
        foreach (CountBound bound in predicate.Constraints) {
            BigInteger mask = BigInteger.Zero;
            int fixedCount = 0;
            budget.Spend((2L * scenario.Cards.Count + 1) * (WorkBudget.IntegerCells(bound.EligibleRows) + 1));
            for (int row = 0; row < scenario.Cards.Count; row++) {
                for (int kind = 0; kind < 2; kind++) {
                    if ((bound.EligibleRows & (BigInteger.One << (2 * row + kind))) == 0) {
                        continue;
                    }
                    bool fixedValue = scenario.Held[row] >= 0 && (kind == 0 || scenario.HiddenCount == 0
                        || scenario.Seen[row] == scenario.Cards[row].Copies);
                    if (fixedValue) {
                        fixedCount += scenario.Held[row];
                    }
                    else {
                        mask |= BigInteger.One << (2 * row + (scenario.HiddenCount == 0 ? 0 : kind));
                    }
                }
            }
            int minimum = Math.Max(0, bound.MinCount - fixedCount);
            int maximum = bound.MaxCount - fixedCount;
            if (maximum < minimum || (mask.IsZero && minimum > 0)) {
                return null;
            }
            if (!mask.IsZero) {
                bounds.Add(new(mask, minimum, maximum));
            }
        }
        budget.CheckStorage(bounds.Count, (long)bounds.Count * (3 + scenario.Cards.Count / 16));
        return new(bounds.ToArray());
    }
}
