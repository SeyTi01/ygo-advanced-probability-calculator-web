using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

// Compile preimages of final-hand events under optional one-card substitutions.
// There is no draw history or remaining-deck vector here. Availability uses the
// original copies minus the observed hand, with one scalar physical capacity.
// Only originally held sources act; fetched effects never restart either phase.
internal sealed class SearchAccessPlan {
    private readonly List<Card> _deck;
    private readonly WorkBudget _budget;
    private readonly (int Source, int[] Targets)[] _sources;

    internal SearchAccessPlan(List<Card> deck, WorkBudget budget) {
        _deck = deck;
        _budget = budget;
        budget.CheckStorage(deck.Count, (long)deck.Count * deck.Count + 3L * deck.Count);
        budget.Spend(deck.Sum(card => card.Categories.Count + 1L)
            * deck.Sum(card => (card.Search?.Selector.CategoryIdentities.Count ?? 0) + 1L));
        _sources = deck.Select((card, row) => (Card: card, Row: row))
            .Where(entry => entry.Card.Copies > 0 && entry.Card.Search is not null)
            .Select(entry => (entry.Row, deck.Select((card, row) => (Card: card, Row: row))
                .Where(target => target.Row != entry.Row && entry.Card.Search!.Matches(target.Card))
                .Select(target => target.Row).ToArray())).ToArray();
    }

    internal bool ObservesIdentity(Card card) => _sources.Any(source => _deck[source.Source].Id == card.Id
        || source.Targets.Any(target => _deck[target].Id == card.Id));

    internal List<List<CompiledEvent?>> Compile(List<Combo> combos, int handSize, List<Card> rows, int capacity) {
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
                            valid &= Add(BigInteger.One << row, used[row], _deck[row].Copies - fetched[row]);
                        }
                    }
                    if (!valid) {
                        continue;
                    }
                    CompiledEvent result = new(bounds.Values.OrderBy(bound => bound.EligibleRows).ToArray());
                    _budget.Spend(result.Constraints.Length + 1L);
                    if (!projected[combo].Contains(result)) {
                        cells += (result.Constraints.Length + 1L) * (3 + rows.Count / 32);
                        _budget.CheckStorage(projected.Sum(events => (long)events.Count) + 1, cells);
                        projected[combo].Add(result);
                    }

                    bool Add(BigInteger mask, int minimum, int maximum) {
                        BigInteger mapped = BigInteger.Zero;
                        _budget.Spend(rows.Count * (WorkBudget.IntegerCells(mask) + 1));
                        for (int row = 0; row < rowMap.Length; row++) {
                            if ((rowMap[row] & mask) != 0) {
                                mapped |= BigInteger.One << row;
                            }
                        }
                        minimum = Math.Max(0, minimum);
                        maximum = Math.Min(handSize, maximum);
                        if (bounds.TryGetValue(mapped, out CountBound previous)) {
                            minimum = Math.Max(minimum, previous.MinCount);
                            maximum = Math.Min(maximum, previous.MaxCount);
                        }
                        if (minimum > maximum || (mapped.IsZero && minimum > 0)) {
                            return false;
                        }
                        if (!mapped.IsZero && (minimum != 0 || maximum != handSize)) {
                            bounds[mapped] = new(mapped, minimum, maximum);
                            _budget.CheckStorage(bounds.Count, (long)bounds.Count * (3 + rows.Count / 32));
                        }
                        return true;
                    }
                }
            }
        }
    }
}

// Uses exactly the ordinary conditional counter and union evaluator. The search
// compiler only changes the event predicates, not their random sample space.
internal sealed record SearchHandPool(List<Card> Cards, int Held);

internal sealed class SearchProbabilityCalculation(
    List<Card> random,
    int randomCount,
    List<Card> retained,
    SearchAccessPlan search,
    int capacity,
    WorkBudget budget,
    List<SearchHandPool>? pools = null
) {
    internal ExactCalculationResult Calculate(List<Combo> combos, IReadOnlyList<ComboGroup>? groups, bool totalOnly) {
        // Condition only the small search source/target partition. All other
        // identities stay in the existing combinatorial counter. These are
        // final-hand subsets, not additional draw states or physical histories.
        List<Card> special = random.Where(search.ObservesIdentity).ToList();
        List<Card> ordinary = random.Where(card => !search.ObservesIdentity(card)).ToList();
        int ordinaryCopies = ordinary.Sum(card => card.Copies);
        int size = randomCount + retained.Sum(card => card.Copies);
        List<Card> rows = [.. ordinary, .. special, .. retained];
        List<List<CompiledEvent?>> events = search.Compile(combos, size, rows, capacity);
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
        int[] suffix = new int[special.Count + 1];
        for (int i = special.Count - 1; i >= 0; i--) {
            suffix[i] = suffix[i + 1] + special[i].Copies;
        }
        int[] counts = new int[special.Count];
        BigInteger denominator = pools is null ? Choose(ordinaryCopies + suffix[0], randomCount)
            : pools.Aggregate(BigInteger.One, (mass, pool) => mass * Choose(pool.Cards.Sum(card => card.Copies), pool.Held));
        BigInteger[] wins = new BigInteger[1 + (totalOnly ? 0 : combos.Count + (groups?.Count ?? 0))];
        budget.CheckStorage(wins.Length + special.Count, wins.Length * (WorkBudget.IntegerCells(denominator) + 1) + 3L * special.Count);
        Visit(0, randomCount, BigInteger.One);
        ExactProbability[] result = wins.Select(win => new ExactProbability(win, denominator)).ToArray();

        return new(result[0], totalOnly ? [] : result.Skip(1).Take(combos.Count).ToArray(),
            totalOnly ? [] : result.Skip(1 + combos.Count).ToArray());

        void Visit(int row, int left, BigInteger weight) {
            budget.Spend(WorkBudget.IntegerCells(weight) + 1);
            if (row == special.Count) {
                Count(left, weight);
                return;
            }
            int minimum = Math.Max(0, left - ordinaryCopies - suffix[row + 1]);
            int maximum = Math.Min(left, special[row].Copies);
            for (int count = minimum; count <= maximum; count++) {
                counts[row] = count;
                Visit(row + 1, left - count, weight * Choose(special[row].Copies, count));
            }
        }

        void Count(int left, BigInteger weight) {
            List<Card> fixedCards = [.. special.Select((card, row) => card.WithCopies(counts[row])), .. retained];
            ScenarioConditioner conditioner = new(ordinary.Count, left, fixedCards, budget);
            List<List<CompiledEvent?>> conditional = events.Select(routes => Simplify(routes.Select(conditioner.Project))).ToList();
            ComboEventCompiler compiler = new(ordinary, left, budget);
            ExactHandCounter counter = new(ordinary, left, budget);
            EventUnionEvaluator union = new(ordinary.Count, left, budget, compiler, counter);
            List<ExactProbability> probabilities = [union.UnionExact(Simplify(conditional.SelectMany(routes => routes)))];
            if (!totalOnly) {
                probabilities.AddRange(conditional.Select(union.UnionExact));
                probabilities.AddRange((groups ?? []).Select(group => union.UnionExact(Simplify(conditional
                    .Where((_, index) => combos[index].GroupId == group.Id).SelectMany(routes => routes)))));
            }
            BigInteger mass = weight * Choose(ordinaryCopies, left);
            for (int i = 0; i < wins.Length; i++) {
                ExactProbability probability = probabilities[i];
                budget.Spend((WorkBudget.IntegerCells(mass) + probability.Cells + 1) * (probability.Cells + 1));
                BigInteger contribution = BigInteger.DivRem(mass * probability.Numerator, probability.Denominator, out BigInteger remainder);
                if (!remainder.IsZero) {
                    throw new InvalidOperationException("Conditional search hands must have integral subset mass.");
                }
                wins[i] += contribution;
            }
        }

        List<CompiledEvent?> Simplify(IEnumerable<CompiledEvent?> input) {
            List<CompiledEvent> unique = input.OfType<CompiledEvent>().Distinct().OrderBy(route => route.Constraints.Length).ToList();
            List<CompiledEvent?> kept = [];
            foreach (CompiledEvent candidate in unique) {
                bool subsumed = kept.Any(previous => previous!.Constraints.All(bound => {
                    budget.Spend(candidate.Constraints.Length + 1L);
                    return candidate.Constraints.Any(other => other.EligibleRows == bound.EligibleRows
                        && other.MinCount >= bound.MinCount && other.MaxCount <= bound.MaxCount);
                }));
                if (!subsumed) {
                    kept.Add(candidate);
                }
                budget.CheckStorage(kept.Count, kept.Sum(route => route!.Constraints.Length + 1L));
            }
            return kept;
        }

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
