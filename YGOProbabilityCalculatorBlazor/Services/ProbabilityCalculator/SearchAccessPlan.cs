using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

// Compile preimages of final-hand events under optional one-card substitutions.
// The Fast projection uses original copies minus the observed hand and scalar
// Deck capacity. The physical projection substitutes a joint held-plus-hidden
// variable for each availability bound and subtracts known consumed sources.
// Only originally held sources act; fetched effects never restart either phase.
internal sealed class SearchAccessPlan {
    private readonly List<Card> _deck;
    private readonly WorkBudget _budget;
    private readonly (int Source, int[] Targets)[] _sources;

    internal SearchAccessPlan(List<Card> deck, WorkBudget budget) {
        using IDisposable? phase = budget.SearchDiagnostics?.Measure("eligibility");
        _deck = deck;
        _budget = budget;
        budget.CheckStorage(deck.Count, (long)deck.Count * deck.Count + 3L * deck.Count);
        budget.Spend(deck.Sum(card => card.Categories.Count + 1L)
            * deck.Sum(card => (card.Search?.Selector.EvaluationSize ?? 0) + 1L));
        _sources = deck.Select((card, row) => (Card: card, Row: row))
            .Where(entry => entry.Card.Copies > 0 && entry.Card.Search is not null)
            .Select(entry => (entry.Row, deck.Select((card, row) => (Card: card, Row: row))
                .Where(target => target.Row != entry.Row && entry.Card.Search!.Matches(target.Card))
                .Select(target => target.Row).ToArray())).ToArray();
    }

    // Reuse the very same ORIGINAL identity eligibility as search execution,
    // before ordered reduction/deferred pools can hide distinctions. The union
    // of possible losses is deliberately broader than any reachable trace.
    // If it misses every target, n_i - h_i equals physical r_i jointly for all
    // searchable identities. Thus all optional assignments and every row agree,
    // including maxima/zero requirements; no monotonicity assumption is needed.
    internal CapabilityReason TargetLossReason(List<Card> effects, int handSize) {
        _budget.Spend(_deck.Count + _sources.Length + 1L);
        if (_deck.Sum(card => card.Copies) == handSize) {
            // No operation replenishes Deck capacity, including a zero draw.
            return CapabilityReason.ZeroDeckCapacity;
        }
        if (!_sources.Any(source => source.Targets.Length > 0)) {
            return CapabilityReason.NoReachableSearchTarget;
        }
        foreach (Card effect in effects) {
            _budget.Spend(1);
            if (effect.PreDrawDeckRemovalCount > 0 || effect.PostDrawDeckMillCount > 0) {
                return CapabilityReason.HiddenDeckLossMayRemoveTarget;
            }
            foreach ((int _, int[] targets) in _sources) {
                _budget.Spend(1);
                foreach (int row in targets) {
                    _budget.Spend(1);
                    Card target = _deck[row];
                    if (target.Id == effect.Id) {
                        return CapabilityReason.ConsumedSearchTarget;
                    }
                    if (effect.PreDrawRemovalCount > 0 && Matches(effect.PreDrawSelector, target)) {
                        return CapabilityReason.HandCostMayRemoveTarget;
                    }
                    if (effect.PostDrawDiscardCount > 0 && Matches(effect.PostDrawSelector, target)) {
                        return CapabilityReason.HandRemovalMayRemoveTarget;
                    }
                }
            }
        }
        return CapabilityReason.NoReachableSearchTargetLoss;

        bool Matches(CardSelector selector, Card target) {
            // Cached full-tree counts bound nested/grouped/Level evaluation;
            // do not add an uncharged recursive proof traversal.
            _budget.Spend((1L + selector.NodeCount + selector.CategoryReferenceCount) * (target.Categories.Count + 1L));
            return selector.Matches(target);
        }
    }

    internal List<List<CompiledEvent?>> Compile(
        List<Combo> combos,
        int handSize,
        List<Card> rows,
        int capacity,
        int targetLoss = 0,
        PhysicalSearchScenario? physical = null
    ) {
        using IDisposable? phase = _budget.SearchDiagnostics?.Measure("assignment compilation");
        ComboEventCompiler compiler = new(_deck, handSize, _budget);
        List<List<CompiledEvent?>> finalEvents = combos.Select(compiler.CompileCombo).ToList();
        List<HashSet<CompiledEvent>> projected = combos.Select(_ => new HashSet<CompiledEvent>()).ToList();
        // Rows are original identities supplied by the final conditional hand
        // counter; draw pools are expanded here, never in the draw frontier.
        BigInteger[] rowMap = rows.Select(card => {
            int original = _deck.FindIndex(entry => entry.Id == card.Id);
            if (original < 0) {
                throw new InvalidOperationException("Search projection requires an original card identity.");
            }
            return BigInteger.One << original;
        }).ToArray();
        int[] used = new int[_deck.Count];
        int[] fetched = new int[_deck.Count];
        long cells = 0;
        int limit = Math.Min(handSize, Math.Max(0, capacity));
        VisitSource(0, 0);

        _budget.SearchDiagnostics?.Add("compiled predicates", projected.Sum(routes => routes.Count));
        return projected.Select(events => events.Cast<CompiledEvent?>().ToList()).ToList();

        void VisitSource(int sourceIndex, int total) {
            _budget.Spend(_deck.Count + 1L);
            if (sourceIndex == _sources.Length) {
                Project();
                return;
            }
            (int source, int[] targets) = _sources[sourceIndex];
            VisitSource(sourceIndex + 1, total);
            int allowance = _deck[source].OncePerTurn ? 1 : _deck[source].Copies;
            SelectTargets(0, Math.Min(allowance, limit - total), total);

            // Nondecreasing target indices enumerate each multiset once. Depth
            // follows activated copies, not the source-by-target Cartesian table.
            void SelectTargets(int start, int left, int activated) {
                if (left == 0) {
                    return;
                }
                for (int index = start; index < targets.Length; index++) {
                    _budget.Spend(1);
                    int target = targets[index];
                    if (fetched[target] == _deck[target].Copies) {
                        continue;
                    }
                    fetched[target]++;
                    used[source]++;
                    VisitSource(sourceIndex + 1, activated + 1);
                    SelectTargets(index, left - 1, activated + 1);
                    used[source]--;
                    fetched[target]--;
                }
            }
        }

        void Project() {
            _budget.SearchDiagnostics?.Add("assignments");
            for (int combo = 0; combo < finalEvents.Count; combo++) {
                foreach (CompiledEvent? route in finalEvents[combo]) {
                    if (route is null) {
                        continue;
                    }
                    Dictionary<BigInteger, CountBound> bounds = new();
                    bool valid = true;
                    foreach (CountBound bound in route.Constraints) {
                        int shift = 0;
                        _budget.Spend(_deck.Count * (WorkBudget.IntegerCells(bound.EligibleRows) + 1));
                        for (int row = 0; row < _deck.Count; row++) {
                            if ((bound.EligibleRows & (BigInteger.One << row)) != 0) {
                                shift += fetched[row] - used[row];
                            }
                        }
                        valid &= Add(bound.EligibleRows, bound.MinCount - shift, bound.MaxCount - shift);
                    }
                    for (int row = 0; row < _deck.Count && valid; row++) {
                        _budget.Spend(1);
                        if (used[row] > 0 || fetched[row] > 0) {
                            // A consumed source never replenishes a deck copy.
                            // For positive fetched counts only: f <= max(0, n-h-D)
                            // iff h <= n-D-f. Empty assignments must remain optional.
                            int loss = fetched[row] > 0 ? targetLoss : 0;
                            if (physical is null) {
                                valid &= Add(BigInteger.One << row, used[row], _deck[row].Copies - fetched[row] - loss);
                            }
                            else {
                                valid &= Add(BigInteger.One << row, used[row], handSize);
                                if (fetched[row] > 0) {
                                    int consumed = physical.Consumed(_deck[row].Id);
                                    valid &= Add(BigInteger.One << row, 0, _deck[row].Copies - consumed - fetched[row], unavailable: true);
                                }
                            }
                        }
                    }
                    if (!valid) {
                        continue;
                    }
                    CompiledEvent result = new(bounds.Values.OrderBy(bound => bound.EligibleRows).ToArray());
                    _budget.Spend(result.Constraints.Length + 1L);
                    if (!projected[combo].Contains(result)) {
                        cells += (result.Constraints.Length + 1L) * (3 + rows.Count / (physical is null ? 32 : 16));
                        _budget.CheckStorage(projected.Sum(events => (long)events.Count) + 1, cells);
                        projected[combo].Add(result);
                    }

                    bool Add(BigInteger mask, int minimum, int maximum, bool unavailable = false) {
                        BigInteger mapped = BigInteger.Zero;
                        _budget.Spend(rows.Count * (WorkBudget.IntegerCells(mask) + 1));
                        for (int row = 0; row < rowMap.Length; row++) {
                            if ((rowMap[row] & mask) != 0) {
                                int variable = physical is null ? row : 2 * row;
                                mapped |= BigInteger.One << (unavailable ? variable + 1 : variable);
                            }
                        }
                        minimum = Math.Max(0, minimum);
                        maximum = unavailable ? maximum : Math.Min(handSize, maximum);
                        if (bounds.TryGetValue(mapped, out CountBound previous)) {
                            minimum = Math.Max(minimum, previous.MinCount);
                            maximum = Math.Min(maximum, previous.MaxCount);
                        }
                        if (minimum > maximum || (mapped.IsZero && minimum > 0)) {
                            return false;
                        }
                        if (!mapped.IsZero && (minimum != 0 || maximum != handSize || unavailable)) {
                            bounds[mapped] = new(mapped, minimum, maximum);
                            _budget.CheckStorage(bounds.Count, (long)bounds.Count * (3 + rows.Count / (physical is null ? 32 : 16)));
                        }
                        return true;
                    }
                }
            }
        }
    }
}

// Conditional uniform final-hand pools supplied by the unchanged draw resolver.
internal sealed record SearchHandPool(List<Card> Cards, int Held);

internal sealed class SearchProbabilityCalculation(
    List<Card> random,
    int randomCount,
    List<Card> retained,
    SearchAccessPlan search,
    int capacity,
    WorkBudget budget,
    List<SearchHandPool>? pools = null,
    int targetLoss = 0
) {
    internal ExactCalculationResult Calculate(List<Combo> combos, IReadOnlyList<ComboGroup>? groups, bool totalOnly) {
        using IDisposable? scenario = budget.SearchDiagnostics?.Measure("search scenarios");
        budget.SearchDiagnostics?.Add("scenarios");
        // Source variables first let each assignment's activation conditions
        // collapse before exploring target/role bounds. This reorders only the
        // final uniform sample; it cannot change draw or removal priorities.
        List<Card> ordered = random.OrderByDescending(card => card.Search is not null).ToList();
        int size = randomCount + retained.Sum(card => card.Copies);
        List<Card> rows = [.. ordered, .. retained];
        List<List<CompiledEvent?>> events = search.Compile(combos, size, rows, capacity, targetLoss);
        if (pools is not null) {
            List<CountBound> conditions = [];
            foreach (SearchHandPool pool in pools) {
                BigInteger mask = BigInteger.Zero;
                for (int row = 0; row < rows.Count; row++) {
                    budget.Spend(pool.Cards.Count + 1L);
                    if (pool.Cards.Any(card => card.Id == rows[row].Id)) {
                        mask |= BigInteger.One << row;
                    }
                }
                conditions.Add(new(mask, pool.Held, pool.Held));
            }
            long entries = events.Sum(routes => (long)routes.Count);
            long bounds = events.Sum(routes => routes.Sum(route => (route?.Constraints.Length ?? 0L) + conditions.Count));
            budget.Spend(bounds + entries);
            budget.CheckStorage(entries, bounds * (3 + rows.Count / 32) + entries);
            events = events.Select(routes => routes.Select(route => route is null ? null
                : new CompiledEvent([.. route.Constraints, .. conditions])).ToList()).ToList();
        }
        using IDisposable? projection = budget.SearchDiagnostics?.Measure("fixed hand projection");
        ScenarioConditioner conditioner = new(ordered.Count, randomCount, retained, budget);
        List<List<CompiledEvent?>> projected = events.Select(routes => routes.Select(conditioner.Project).ToList()).ToList();
        projection?.Dispose();
        using IDisposable? construction = budget.SearchDiagnostics?.Measure("decision construction");
        SearchDecisionCounter counter = new(ordered, randomCount, budget);
        int[] roots = projected.Select(counter.Union).ToArray();
        int total = roots.Aggregate(0, counter.Or);
        int[] groupRoots = totalOnly ? [] : (groups ?? []).Select(group => roots.Where((_, index) => combos[index].GroupId == group.Id)
            .Aggregate(0, counter.Or)).ToArray();
        construction?.Dispose();
        using IDisposable? counting = budget.SearchDiagnostics?.Measure("weighted counting");
        BigInteger denominator = pools is null ? Choose(ordered.Sum(card => card.Copies), randomCount)
            : pools.Aggregate(BigInteger.One, (mass, pool) => mass * Choose(pool.Cards.Sum(card => card.Copies), pool.Held));
        budget.CheckStorage(roots.Length + groupRoots.Length + 1L,
            roots.Length + groupRoots.Length + WorkBudget.IntegerCells(denominator));
        ExactProbability Probability(int root) => new(counter.Count(root), denominator);
        ExactCalculationResult result = new(Probability(total), totalOnly ? [] : roots.Select(Probability).ToArray(),
            groupRoots.Select(Probability).ToArray());
        budget.CheckStorage(1L + result.Combos.Length + result.Groups.Length,
            result.Total.Cells + result.Combos.Sum(value => value.Cells) + result.Groups.Sum(value => value.Cells));

        return result;

        BigInteger Choose(int n, int k) {
            k = Math.Min(k, n - k);
            BigInteger value = BigInteger.One;
            for (int i = 1; i <= k; i++) {
                budget.Spend(WorkBudget.IntegerCells(value) * 2 + 1);
                value = value * (n - i + 1) / i;
            }
            return value;
        }
    }
}
