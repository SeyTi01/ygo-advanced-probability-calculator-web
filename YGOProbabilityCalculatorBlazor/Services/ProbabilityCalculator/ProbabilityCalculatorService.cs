using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

public class ProbabilityCalculatorService : IProbabilityCalculatorService {
    public double CalculateProbabilityForCombos(List<Card> deck, List<Combo> combos, int handSize) =>
        CalculateProbabilityForCombos(deck, combos, handSize, CalculationWorkPolicy.Default);

    public double CalculateProbabilityForCombos(List<Card> deck, List<Combo> combos, int handSize, CalculationWorkPolicy workPolicy) {
        ArgumentNullException.ThrowIfNull(workPolicy);
        ValidateComboCount(combos);
        ValidateCardIds(deck);
        var calculation = new Calculation(deck, handSize, workPolicy);
        return calculation.Union(combos.SelectMany(combo => calculation.CompileCombo(combo)).ToList());
    }

    public ProbabilityCalculationResult CalculateProbabilityResults(
        List<Card> deck, List<Combo> combos, int handSize, IReadOnlyList<ComboGroup>? groups = null) =>
        CalculateProbabilityResults(deck, combos, handSize, groups, CalculationWorkPolicy.Default);

    public ProbabilityCalculationResult CalculateProbabilityResults(
        List<Card> deck, List<Combo> combos, int handSize, IReadOnlyList<ComboGroup>? groups, CalculationWorkPolicy workPolicy) {
        ArgumentNullException.ThrowIfNull(workPolicy);
        ValidateComboCount(combos);
        ValidateCardIds(deck);
        // Cache and work budget belong to this request, never to a service instance.
        var calculation = new Calculation(deck, handSize, workPolicy);
        var ownedEvents = combos.Select(combo => calculation.CompileCombo(combo)).ToList();
        var events = ownedEvents.SelectMany(e => e).ToList();
        var totalProbability = calculation.Union(events);
        var comboProbabilities = combos.Select((combo, index) =>
            new ComboProbabilityResult(index, combo.Name, calculation.Union(ownedEvents[index]), combo.GroupId)).ToList();
        var groupProbabilities = (groups ?? []).Select(group => {
            var members = ownedEvents.Where((_, index) => combos[index].GroupId == group.Id).ToList();
            return new GroupProbabilityResult(group.Id, group.Name, calculation.Union(members.SelectMany(e => e).ToList()), members.Count);
        }).ToList();

        return new ProbabilityCalculationResult(
            totalProbability, comboProbabilities.AsReadOnly(), groupProbabilities.AsReadOnly());
    }

    private static void ValidateComboCount(List<Combo> combos) {
        if (combos.Count > IProbabilityCalculatorService.MaxComboCount)
            throw new ArgumentOutOfRangeException(nameof(combos),
                $"Calculation supports at most {IProbabilityCalculatorService.MaxComboCount} combos.");
    }

    private static void ValidateCardIds(List<Card> deck) {
        if (deck.Select(card => card.Id).Distinct(StringComparer.Ordinal).Count() != deck.Count)
            throw new ArgumentException("Deck card IDs must be unique.", nameof(deck));
    }

    private sealed class Calculation(List<Card> deck, int handSize, CalculationWorkPolicy workPolicy) {
        private readonly WorkBudget budget = new(workPolicy);
        private readonly Dictionary<Event, BigInteger> counts = new();
        private int cachedConstraints;
        private long cachedIntegerCells;
        private BigInteger? totalWays;

        private long compiledEntries;
        private long compiledCells;

        public List<Event?> CompileCombo(Combo combo) {
            // Check the compact input and potential expansion before route allocation.
            // This request-wide bound is independent of the 30 USER combo policy.
            long routes = 1;
            budget.Spend(combo.RequirementCount + 1L);
            foreach (var group in combo.AlternativeGroups) {
                budget.Spend(group.Alternatives.Count + 1L);
                if (routes > 32768 / group.Alternatives.Count)
                    throw new ProbabilityCalculationLimitException(ProbabilityCalculationLimitReason.Storage);
                routes *= group.Alternatives.Count;
            }
            WorkBudget.CheckStorage(compiledEntries + routes,
                combo.Categories.Count + (long)combo.Cards.Count + combo.AlternativeGroups.Count);
            var choices = new int[combo.AlternativeGroups.Count];
            var unique = new HashSet<Event>();
            for (long route = 0; route < routes; route++) {
                budget.Spend(combo.Categories.Count + (long)combo.Cards.Count + choices.Length + 1);
                var categories = combo.Categories.ToList();
                var cards = combo.Cards.ToList();
                for (var g = 0; g < choices.Length; g++) {
                    var alternative = combo.AlternativeGroups[g].Alternatives[choices[g]];
                    if (alternative.Category is { } category) categories.Add(category);
                    else cards.Add(alternative.Card!);
                }
                var compiled = Canonicalize(new Combo(categories, cards: cards));
                if (compiled is not null) {
                    budget.Spend(compiled.Constraints.Length + 1L);
                    if (!unique.Contains(compiled)) {
                        var cells = (long)(compiled.Constraints.Length + (compiled.Roles?.Length ?? 0)) * (3 + deck.Count / 32);
                        WorkBudget.CheckStorage(compiledEntries + 1, compiledCells + cells);
                        compiledEntries++;
                        compiledCells += cells;
                        unique.Add(compiled);
                    }
                }
                for (var g = choices.Length - 1; g >= 0; g--) {
                    if (++choices[g] < combo.AlternativeGroups[g].Alternatives.Count) break;
                    choices[g] = 0;
                }
            }
            return unique.Cast<Event?>().ToList();
        }

        private Event? Canonicalize(Combo combo) {
            budget.Spend(combo.Categories.Count + combo.Cards.Count + 1L);
            var constraints = new List<Requirement>();
            foreach (var group in combo.Categories.GroupBy(c => c.BaseCategory.Identity, StringComparer.Ordinal)) {
                var min = group.Max(c => c.MinCount);
                var max = Math.Min(handSize, group.Min(c => c.GetEffectiveMaximum(handSize)));
                if (min > max) return null;
                // Every category count is in [0, handSize], regardless of overlap.
                if (min == 0 && max == handSize) continue;
                constraints.Add(new Requirement(new ConstraintKey(false, group.Key), min, max));
            }
            foreach (var group in combo.Cards.GroupBy(c => c.CardId, StringComparer.Ordinal)) {
                var min = group.Max(c => c.MinCount);
                var max = Math.Min(handSize, group.Min(c => c.GetEffectiveMaximum(handSize)));
                if (min > max) return null;
                if (min == 0 && max == handSize) continue;
                constraints.Add(new Requirement(new ConstraintKey(true, group.Key), min, max));
            }
            if (constraints.Sum(c => (long)c.MinCount) > handSize) return null;

            var roles = new List<CountBound>();
            foreach (var constraint in constraints) {
                var eligible = BigInteger.Zero;
                for (var i = 0; i < deck.Count; i++) {
                    budget.Spend(deck[i].Categories.Count + 1L);
                    if (deck[i].Copies > 0 && (constraint.Key.IsCard
                        ? deck[i].Id == constraint.Key.Value
                        : deck[i].Categories.Any(c => c.Identity == constraint.Key.Value)))
                        eligible |= BigInteger.One << i;
                }
                roles.Add(new CountBound(eligible, constraint.MinCount, constraint.MaxCount));
            }
            return Compile(roles.ToArray());
        }

        private Event? Compile(CountBound[] roles) {
            // Hall's matching condition with repeated slots: every subset of roles
            // needs at least the sum of its minima in the UNION of eligible copies.
            // Whole roles suffice: partial slots have the same neighbors and no
            // greater demand. Upper bounds still count the entire hand.
            var bounds = new Dictionary<BigInteger, (int Min, int Max)>();
            var demands = new Dictionary<BigInteger, int> { [BigInteger.Zero] = 0 };
            foreach (var constraint in roles) {
                var eligible = constraint.EligibleRows;
                if (!AddBound(eligible, constraint.MinCount, constraint.MaxCount)) return null;
                if (constraint.MinCount == 0) continue;
                foreach (var (subset, demand) in demands.ToArray()) {
                    budget.Spend(1 + eligible.GetBitLength() / 32);
                    var union = subset | eligible;
                    var minimum = demand + constraint.MinCount;
                    demands[union] = Math.Max(demands.GetValueOrDefault(union), minimum);
                    WorkBudget.CheckStorage(demands.Count, (long)demands.Count * (1 + deck.Count / 32));
                }
            }
            foreach (var (eligible, minimum) in demands)
                if (!AddBound(eligible, minimum, handSize)) return null;
            return new Event(bounds.OrderBy(pair => pair.Key)
                .Select(pair => new CountBound(pair.Key, pair.Value.Min, pair.Value.Max)).ToArray(),
                roles.All(r => r.MaxCount == handSize) ? roles : null);

            bool AddBound(BigInteger eligible, int min, int max) {
                budget.Spend(1 + eligible.GetBitLength() / 32);
                if (eligible.IsZero) return min == 0;
                if (bounds.TryGetValue(eligible, out var previous)) {
                    min = Math.Max(min, previous.Min);
                    max = Math.Min(max, previous.Max);
                }
                if (min > max) return false;
                bounds[eligible] = (min, max);
                WorkBudget.CheckStorage(bounds.Count, (long)bounds.Count * (1 + deck.Count / 32));
                return true;
            }
        }

        public double Union(List<Event?> events) {
            budget.Spend(events.Count + 1L);
            var universal = events.FirstOrDefault(e => e is { Constraints.Length: 0 });
            if (universal is not null) return Probability(universal);
            var terms = new Dictionary<Event, BigInteger>();
            long storedConstraints = 0;
            foreach (var current in FactorAlternatives(events)) {
                // Indicator identity: U OR E = U + E - U*E. Equal intersections
                // combine integer coefficients before any floating-point evaluation.
                // Nested events cancel here too; no independence assumption is used.
                var changes = new Dictionary<Event, BigInteger>();
                long changedConstraints = 0;
                Add(changes, current, 1, ref changedConstraints);
                foreach (var (existing, coefficient) in terms) {
                    var intersection = Intersect(existing, current);
                    if (intersection is not null)
                        Add(changes, intersection, -coefficient, ref changedConstraints);
                }
                foreach (var (intersection, coefficient) in changes)
                    Add(terms, intersection, coefficient, ref storedConstraints);
            }

            if (terms.Count == 0) return 0;
            BigInteger successes = 0;
            foreach (var (intersection, coefficient) in terms) {
                var count = Count(intersection);
                budget.Spend(1 + count.GetBitLength() / 64 + successes.GetBitLength() / 64 + coefficient.GetBitLength() / 64);
                successes += coefficient * count;
                WorkBudget.CheckStorage(1, WorkBudget.IntegerCells(successes));
            }
            // Cancel signed inclusion-exclusion terms exactly, before conversion.
            return ToProbability(successes);
        }

        private List<Event> FactorAlternatives(List<Event?> events) {
            foreach (var item in events) budget.Spend((item?.Constraints.Length ?? 0) + 1L);
            var alternatives = events.OfType<Event>().Distinct().ToList();
            for (var i = 0; i < alternatives.Count; i++) {
                for (var j = i + 1; j < alternatives.Count; j++) {
                    budget.Spend(1);
                    var first = alternatives[i].Roles;
                    var second = alternatives[j].Roles;
                    if (first is null || second is null || first.Length != second.Length) continue;
                    // Multiset subtraction preserves separate slots even when a
                    // direct card and a category have identical eligibility.
                    var unmatched = second.ToList();
                    var common = new List<CountBound>();
                    CountBound? different = null;
                    foreach (var role in first) {
                        budget.Spend((long)(unmatched.Count + 1) * (1 + deck.Count / 32));
                        var match = unmatched.IndexOf(role);
                        if (match >= 0) { common.Add(role); unmatched.RemoveAt(match); }
                        else if (different is null) different = role;
                        else break;
                    }
                    if (common.Count != first.Length - 1 || unmatched.Count != 1 ||
                        different is not { MinCount: 1 } left || unmatched[0].MinCount != 1) continue;

                    // With no restrictive hand-wide maxima, C+A OR C+B is
                    // C+(A union B) for ONE alternative slot: its assigned copy
                    // belongs to at least one branch, with the same assignment
                    // for C. The converse is immediate. This fails for demand>1
                    // (copies could split across branches) or branch maxima.
                    common.Add(new CountBound(left.EligibleRows | unmatched[0].EligibleRows, 1, handSize));
                    alternatives[i] = Compile(common.ToArray())!;
                    alternatives.RemoveAt(j);
                    // A merge can expose another common-role pair anywhere.
                    i = -1;
                    break;
                }
            }
            return alternatives;
        }

        private void Add(Dictionary<Event, BigInteger> terms, Event key, BigInteger coefficient, ref long storedConstraints) {
            budget.Spend(key.Constraints.Length + 1L + coefficient.GetBitLength() / 32);
            if (terms.TryGetValue(key, out var previous)) {
                budget.Spend(1 + previous.GetBitLength() / 32);
                var updated = previous + coefficient;
                var cells = storedConstraints - WorkBudget.IntegerCells(previous) + WorkBudget.IntegerCells(updated);
                WorkBudget.CheckStorage(terms.Count, cells);
                if (updated == 0) {
                    terms.Remove(key);
                    storedConstraints -= key.Constraints.Length + WorkBudget.IntegerCells(previous);
                } else { terms[key] = updated; storedConstraints = cells; }
            } else if (coefficient != 0) {
                WorkBudget.CheckStorage(terms.Count + 1, storedConstraints + key.Constraints.Length + WorkBudget.IntegerCells(coefficient));
                terms.Add(key, coefficient);
                storedConstraints += key.Constraints.Length + WorkBudget.IntegerCells(coefficient);
            }
        }

        private Event? Intersect(Event first, Event second) {
            // These are hand-wide count bounds already compiled separately for
            // each combo. Conjoin them; never create Hall subsets across combos,
            // since each combo may reuse the same drawn copies independently.
            budget.Spend(first.Constraints.Length + second.Constraints.Length + 1L);
            var constraints = new List<CountBound>();
            int i = 0, j = 0;
            while (i < first.Constraints.Length || j < second.Constraints.Length) {
                if (i == first.Constraints.Length) { constraints.Add(second.Constraints[j++]); continue; }
                if (j == second.Constraints.Length) { constraints.Add(first.Constraints[i++]); continue; }
                var left = first.Constraints[i];
                var right = second.Constraints[j];
                var order = left.EligibleRows.CompareTo(right.EligibleRows);
                if (order < 0) { constraints.Add(left); i++; }
                else if (order > 0) { constraints.Add(right); j++; }
                else {
                    var min = Math.Max(left.MinCount, right.MinCount);
                    var max = Math.Min(left.MaxCount, right.MaxCount);
                    if (min > max) return null;
                    constraints.Add(new CountBound(left.EligibleRows, min, max));
                    i++; j++;
                }
            }
            // A disjoint collection of eligible row sets consumes at least the
            // sum of its hand-wide minima. A greedy collection is sufficient
            // for a sound rejection; it need not find every impossible event.
            // This is NOT matching roles across combos: intersected bounds
            // already describe counts in the same hand.
            var occupied = BigInteger.Zero;
            long required = 0;
            foreach (var bound in constraints) {
                budget.Spend(1 + bound.EligibleRows.GetBitLength() / 32);
                if (bound.MinCount == 0 || (occupied & bound.EligibleRows) != 0) continue;
                occupied |= bound.EligibleRows;
                required += bound.MinCount;
                if (required > handSize) return null;
            }
            return new Event(constraints.ToArray());
        }

        public double Probability(Event? predicate) {
            if (predicate is null) return 0;
            // Preserve the cheap universal event, including decks whose
            // binomial denominator would exceed floating-point range.
            if (predicate.Constraints.Length == 0) {
                var deckSize = 0;
                foreach (var card in deck) {
                    budget.Spend(1);
                    if (card.Copies < 0) throw new ArgumentOutOfRangeException(nameof(deck), "Copies cannot be negative.");
                    deckSize = checked(deckSize + card.Copies);
                }
                return handSize < 0 || handSize > deckSize ? double.NaN : 1;
            }
            var successes = Count(predicate);
            return ToProbability(successes);
        }

        private double ToProbability(BigInteger successes) {
            var denominator = totalWays!.Value;
            var doubleDenominator = (double)denominator;
            // Preserve ordinary-sized conversion, including the existing invalid
            // empty sample-space behavior. Valid counts satisfy 0 <= successes <= denominator.
            if (double.IsFinite(doubleDenominator)) return (double)successes / doubleDenominator;
            if (successes.IsZero) return 0;

            // Neither infinity/infinity nor finite/infinity represents the exact
            // ratio. Locate its binary exponent using integers, then round once
            // to a 53-bit significand (or the fixed 2^-1074 subnormal grid).
            var numeratorBits = successes.GetBitLength();
            var denominatorBits = denominator.GetBitLength();
            budget.Spend(1 + (numeratorBits + denominatorBits) / 32);
            var exponent = numeratorBits - denominatorBits;
            if (exponent < -1075) return 0;
            if ((successes << (int)-exponent) < denominator) exponent--;
            var shift = (int)Math.Min(1074, 52 - exponent);
            WorkBudget.CheckStorage(1, (numeratorBits + shift + 31) / 32);
            var significand = BigInteger.DivRem(successes << shift, denominator, out var remainder);
            var rounding = (remainder << 1).CompareTo(denominator);
            if (rounding > 0 || (rounding == 0 && !significand.IsEven)) significand++;
            return Math.ScaleB((double)significand, -shift);
        }

        private BigInteger Count(Event predicate) {
            if (counts.TryGetValue(predicate, out var cached)) return cached;
            var categories = predicate.Constraints;
            // Build one mask per card entry, not per physical copy. Adding Copies
            // preserves multiplicities while avoiding an expanded deck allocation.
            var cardMasks = new Dictionary<BigInteger, int>();
            var deckSize = 0;
            for (var row = 0; row < deck.Count; row++) {
                var card = deck[row];
                if (card.Copies < 0) throw new ArgumentOutOfRangeException(nameof(deck), "Copies cannot be negative.");
                if (card.Copies == 0) continue;
                deckSize = checked(deckSize + card.Copies);
                budget.Spend((long)(categories.Length + 1) * (card.Categories.Count + 1));
                var mask = BigInteger.Zero;
                for (var i = 0; i < categories.Length; i++)
                    if ((categories[i].EligibleRows & (BigInteger.One << row)) != 0)
                        mask |= BigInteger.One << i;
                cardMasks[mask] = cardMasks.GetValueOrDefault(mask) + card.Copies;
                WorkBudget.CheckStorage(cardMasks.Count, (long)cardMasks.Count * (categories.Length + 1));
            }

            if (totalWays is null) {
                budget.Spend(Math.Max(0, Math.Min(handSize, deckSize - handSize)) + 1L);
                totalWays = ComputeBinomial(deckSize, handSize, budget);
            }
            BigInteger successes = 0;
            if (categories.Length == 0) {
                successes = totalWays.Value;
            } else {
                // Keep combinatorial weights integral, including beyond 2^53.
                var states = new Dictionary<StateKey, BigInteger> { [new(0, new int[categories.Length])] = 1 };
                var remaining = new int[categories.Length];
                foreach (var (mask, count) in cardMasks)
                    for (var i = 0; i < remaining.Length; i++)
                        if ((mask & (BigInteger.One << i)) != 0) remaining[i] += count;
                var remainingCards = deckSize;
                foreach (var (mask, count) in cardMasks) {
                    remainingCards -= count;
                    for (var i = 0; i < remaining.Length; i++)
                        if ((mask & (BigInteger.One << i)) != 0) remaining[i] -= count;
                    states = Convolve(states, mask, count, categories, remaining, remainingCards);
                }
                foreach (var (state, ways) in states) {
                    budget.Spend(categories.Length + 1L);
                    if (state.DrawnCards == handSize &&
                        !categories.Where((category, i) => state.CategoryCounts[i] < category.MinCount).Any())
                        successes += ways;
                }
            }
            // A full cache only stops retaining entries; it never changes the result.
            var integerCells = WorkBudget.IntegerCells(successes);
            if (counts.Count < 1024 && cachedConstraints + categories.Length <= 16384 &&
                cachedIntegerCells + integerCells <= 262144) {
                counts.Add(predicate, successes);
                cachedConstraints += categories.Length;
                cachedIntegerCells += integerCells;
            }
            return successes;
        }

        private Dictionary<StateKey, BigInteger> Convolve(
            Dictionary<StateKey, BigInteger> states, BigInteger pattern, int groupSize, CountBound[] categories,
            int[] remaining, int remainingCards) {
            var next = new Dictionary<StateKey, BigInteger>();
            var indices = Enumerable.Range(0, categories.Length)
                .Where(i => (pattern & (BigInteger.One << i)) != 0).ToArray();
            var maxDraw = Math.Min(groupSize, handSize);
            // The same binomial row is used by every state in this convolution.
            budget.Spend(maxDraw + 1L);
            // Check the length in long arithmetic before int addition/allocation.
            WorkBudget.CheckStorage(maxDraw + 1L, maxDraw + 1L);
            var binomials = new BigInteger[maxDraw + 1];
            BigInteger binomial = 1;
            long binomialCells = maxDraw + 1L;
            for (var draw = 0; draw <= maxDraw; draw++) {
                budget.Spend(1 + binomial.GetBitLength() / 64);
                binomialCells += WorkBudget.IntegerCells(binomial);
                WorkBudget.CheckStorage(maxDraw + 1L, binomialCells);
                binomials[draw] = binomial;
                binomial = binomial * (groupSize - draw) / (draw + 1);
            }
            long nextIntegerCells = 0;
            foreach (var (state, ways) in states) {
                var limit = Math.Min(maxDraw, handSize - state.DrawnCards);
                foreach (var index in indices)
                    limit = Math.Min(limit, categories[index].MaxCount - state.CategoryCounts[index]);
                var minimumDraw = Math.Max(0, handSize - state.DrawnCards - remainingCards);
                for (var draw = minimumDraw; draw <= limit; draw++) {
                    budget.Spend(categories.Length + 1L);
                    var counts = (int[])state.CategoryCounts.Clone();
                    foreach (var index in indices) {
                        var count = counts[index] + draw;
                        // With no restrictive maximum, counts above the minimum
                        // are equivalent for all future transitions.
                        counts[index] = categories[index].MaxCount == handSize
                            ? Math.Min(count, categories[index].MinCount) : count;
                    }
                    // Drop states whose minima cannot be reached by the remaining
                    // eligible copies or remaining hand slots. Hall constraints
                    // add dimensions, so early feasibility pruning matters.
                    var slotsLeft = handSize - state.DrawnCards - draw;
                    var feasible = true;
                    for (var i = 0; i < counts.Length; i++)
                        if (counts[i] + Math.Min(slotsLeft, remaining[i]) < categories[i].MinCount) {
                            feasible = false;
                            break;
                        }
                    if (!feasible) continue;
                    var key = new StateKey(state.DrawnCards + draw, counts);
                    budget.Spend(1 + ways.GetBitLength() / 64 + binomials[draw].GetBitLength() / 64);
                    var increment = ways * binomials[draw];
                    var existed = next.TryGetValue(key, out var previous);
                    var updated = previous + increment;
                    nextIntegerCells += WorkBudget.IntegerCells(updated) - (existed ? WorkBudget.IntegerCells(previous) : 0);
                    var entries = next.Count + (existed ? 0 : 1);
                    WorkBudget.CheckStorage(entries, (long)entries * categories.Length + nextIntegerCells);
                    next[key] = updated;
                }
            }
            return next;
        }
    }

    // Bound retained entries/count-vector cells and cumulative work, rather than
    // pretending the 30-combo compatibility ceiling is a runtime guarantee.
    // Each live map: <= 32K entries and 256K cells (about 1 MiB of int payload).
    // Old/new DP maps, Hall subsets and union/change maps can coexist.
    // DP/binomial integer payload is also checked; object overhead is extra.
    // This is not a total-memory ceiling. Arithmetic work is charged separately.
    private sealed class WorkBudget(CalculationWorkPolicy policy) {
        private long remaining = policy.WorkUnits;
        public void Spend(long units) {
            if (units < 0) throw new ArgumentOutOfRangeException(nameof(units));
            if (units > remaining) throw new ProbabilityCalculationLimitException(ProbabilityCalculationLimitReason.Work);
            remaining -= units;
        }
        public static void CheckStorage(long entries, long cells) {
            if (entries > 32768 || cells > 262144) throw new ProbabilityCalculationLimitException(ProbabilityCalculationLimitReason.Storage);
        }
        // Count retained BigInteger payload in 32-bit cells as well as count vectors.
        // This is still a per-structure bound, not a total process-byte ceiling.
        public static long IntegerCells(BigInteger value) => (BigInteger.Abs(value).GetBitLength() + 31) / 32;
    }

    private static BigInteger ComputeBinomial(int n, int k, WorkBudget budget) {
        if (k < 0 || k > n) return 0;
        k = Math.Min(k, n - k);
        BigInteger result = 1;
        for (var i = 1; i <= k; i++) {
            budget.Spend(1 + result.GetBitLength() / 64);
            result = result * (n - (k - i)) / i;
            WorkBudget.CheckStorage(1, WorkBudget.IntegerCells(result));
        }
        return result;
    }

    private readonly record struct ConstraintKey(bool IsCard, string Value);
    private readonly record struct Requirement(ConstraintKey Key, int MinCount, int MaxCount);

    private readonly record struct CountBound(BigInteger EligibleRows, int MinCount, int MaxCount);

    // Roles are optional compilation metadata for safe OR factoring; event
    // equality and cached counts depend only on the compiled hand-wide bounds.
    private sealed record Event(CountBound[] Constraints, CountBound[]? Roles = null) {
        public bool Equals(Event? other) => other is not null &&
            Constraints.AsSpan().SequenceEqual(other.Constraints);
        public override int GetHashCode() {
            var hash = new HashCode();
            foreach (var constraint in Constraints) hash.Add(constraint);
            return hash.ToHashCode();
        }
    }

    // Counts are immutable after insertion; preserve structural state equality.
    private sealed record StateKey(int DrawnCards, int[] CategoryCounts) {
        public bool Equals(StateKey? other) => other is not null &&
            DrawnCards == other.DrawnCards && CategoryCounts.AsSpan().SequenceEqual(other.CategoryCounts);
        public override int GetHashCode() {
            var hash = new HashCode();
            hash.Add(DrawnCards);
            foreach (var count in CategoryCounts) hash.Add(count);
            return hash.ToHashCode();
        }
    }
}
